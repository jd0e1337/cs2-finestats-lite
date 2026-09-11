using System.Text.Json;
using Finestats.Config;
using Finestats.Events;
using Finestats.Services;
using Microsoft.Data.Sqlite;

namespace Finestats.Storage;

// Every caller runs on a worker. One gate serializes connections belonging to this
// instance; SQLite transactions also protect against overlapping plugin reloads.
public sealed partial class LiteStore(string path, StatsConfig config)
{
    private readonly SemaphoreSlim _gate = new(1);
    private bool _ready;
    private DateTime _lastBackup = DateTime.MinValue;
    public string DatabasePath { get; } = Path.GetFullPath(path);

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, DefaultTimeout = config.DatabaseBusyTimeoutSeconds }.ToString());
        try
        {
            connection.Open();
            Execute(connection, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params object?[] args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (int i = 0; i < args.Length; i++)
            command.Parameters.AddWithValue("@p" + i, args[i] ?? DBNull.Value);
        return command;
    }

    private static int Execute(SqliteConnection connection, string sql, params object?[] args)
    {
        using var command = Command(connection, sql, args);
        return command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, params object?[] args)
    {
        using var command = Command(connection, sql, args);
        var result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private void Initialize(SqliteConnection connection)
    {
        if (_ready)
            return;
        var application = Convert.ToInt64(Scalar(connection, "PRAGMA application_id"));
        var version = Convert.ToInt64(Scalar(connection, "PRAGMA user_version"));
        if (application != 0 && application != 1179864148 || version > 1)
            throw new InvalidDataException("Unsupported finestats-lite database version.");
        if (application == 0 && Convert.ToInt64(Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0)
            throw new InvalidDataException("Database is not a finestats-lite database.");
        Execute(connection, "PRAGMA journal_mode=WAL;");
        using var transaction = connection.BeginTransaction();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS players(id TEXT PRIMARY KEY,steam TEXT UNIQUE,name TEXT NOT NULL,points REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY,player TEXT NOT NULL,collector TEXT NOT NULL,session TEXT NOT NULL,
                map TEXT,first_at INTEGER NOT NULL,last_at INTEGER NOT NULL,started INTEGER,ended INTEGER,partial INTEGER NOT NULL DEFAULT 1,
                country TEXT,country_at INTEGER);
            CREATE INDEX IF NOT EXISTS session_player ON sessions(player);
            CREATE TABLE IF NOT EXISTS counters(player TEXT NOT NULL,dimension TEXT NOT NULL,key TEXT NOT NULL,metric TEXT NOT NULL,value INTEGER NOT NULL,
                PRIMARY KEY(player,dimension,key,metric));
            CREATE INDEX IF NOT EXISTS overall_counters ON counters(dimension,key,metric,player);
            CREATE TABLE IF NOT EXISTS receipts(id TEXT PRIMARY KEY,created INTEGER NOT NULL,notices TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS rounds(id TEXT NOT NULL,player TEXT NOT NULL,PRIMARY KEY(id,player));
            INSERT OR IGNORE INTO metadata VALUES('event_count','0');
            PRAGMA application_id=1179864148;
            PRAGMA user_version=1;
            """);
        var server = Scalar(connection, "SELECT value FROM metadata WHERE key='server'") as string;
        if (server is not null && server != config.ServerId)
            throw new InvalidDataException("Database belongs to a different ServerId.");
        Execute(connection, "INSERT OR IGNORE INTO metadata VALUES('server',@p0)", config.ServerId);
        // Convert legacy balances on entry into whole-point mode. Original event
        // receipts remain untouched, so retries still return their original amounts.
        if (config.Ranking.RoundPointsToIntegers)
            Execute(connection, "UPDATE players SET points=round(points,0) WHERE points<>round(points,0)");
        transaction.Commit();
        _ready = true;
    }

    public async Task<ScoreReceipt[]> AppendAsync(StatsEvent[] events, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var connection = Open();
            Initialize(connection);
            var notices = new List<ScoreReceipt>();
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var statsEvent in events)
                {
                    ct.ThrowIfCancellationRequested();
                    if (statsEvent.ServerId != config.ServerId)
                        throw new InvalidDataException("Event server does not match database.");
                    var previous = Scalar(connection, "SELECT notices FROM receipts WHERE id=@p0", statsEvent.EventId.ToString()) as string;
                    if (previous is not null)
                    {
                        notices.AddRange(JsonSerializer.Deserialize<ScoreReceipt[]>(previous)!);
                        continue;
                    }

                    var current = Apply(connection, statsEvent);
                    Execute(connection, "INSERT INTO receipts VALUES(@p0,@p1,@p2)", statsEvent.EventId.ToString(), statsEvent.Timestamp, JsonSerializer.Serialize(current));
                    Execute(connection, "UPDATE metadata SET value=CAST(value AS INTEGER)+1 WHERE key='event_count'");
                    Execute(connection, "INSERT INTO metadata VALUES('last_received',@p0) ON CONFLICT(key) DO UPDATE SET value=excluded.value", DateTimeOffset.UtcNow.ToString("O"));
                    Execute(connection, "INSERT INTO metadata VALUES('map',@p0) ON CONFLICT(key) DO UPDATE SET value=excluded.value", statsEvent.Map ?? "—");
                    notices.AddRange(current);
                }

                transaction.Commit();
            }

            // Backup failure must never turn a committed batch into a lost batch.
            if (config.BackupIntervalMinutes > 0 && DateTime.UtcNow - _lastBackup >= TimeSpan.FromMinutes(config.BackupIntervalMinutes))
            {
                try
                {
                    Backup(connection);
                    BackupError = null;
                }
                catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException)
                {
                    BackupError = ex.GetType().Name;
                }

                _lastBackup = DateTime.UtcNow;
            }

            return notices.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public string? BackupError { get; private set; }

    public async Task<string> BackupAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var connection = Open();
            Initialize(connection);
            return Backup(connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Backup(SqliteConnection source)
    {
        var directory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups");
        Directory.CreateDirectory(directory);
        var prefix = Path.GetFileName(DatabasePath) + ".";
        var target = Path.Combine(directory, prefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".db");
        var temporary = target + ".tmp";
        try
        {
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        foreach (var old in Directory.GetFiles(directory, prefix + "*.db").OrderByDescending(x => x, StringComparer.Ordinal).Skip(config.BackupKeepCount))
            File.Delete(old);
        return target;
    }

    private static string Session(StatsEvent statsEvent, PlayerIdentity p) => statsEvent.CollectorId + "/" + p.SessionId;
    private string Resolve(SqliteConnection connection, StatsEvent statsEvent, PlayerIdentity p)
    {
        var session = Session(statsEvent, p);
        var human = p.Authenticated && p.IsBot == false && !string.IsNullOrEmpty(p.Steamid);
        var id = human ? "steam:" + p.Steamid : "session:" + session;
        var previous = Scalar(connection, "SELECT player FROM sessions WHERE id=@p0", session) as string;
        // Once authenticated, a sparse later identity must not demote the session.
        if (!human && previous?.StartsWith("steam:", StringComparison.Ordinal) == true)
            id = previous;
        Execute(connection, "INSERT INTO players VALUES(@p0,@p1,@p2,@p3) ON CONFLICT(id) DO UPDATE SET name=excluded.name", id, human ? p.Steamid : null, p.Name ?? "—", config.Ranking.StartingPoints);
        if (previous is not null && previous != id)
        {
            if (previous.StartsWith("steam:", StringComparison.Ordinal))
                throw new InvalidDataException("Session identity changed.");
            Execute(connection, """
                INSERT INTO counters SELECT @p1,dimension,key,metric,value FROM counters WHERE player=@p0
                ON CONFLICT(player,dimension,key,metric) DO UPDATE SET value=value+excluded.value;
                DELETE FROM counters WHERE player=@p0;
                UPDATE sessions SET player=@p1 WHERE player=@p0;
                INSERT OR IGNORE INTO rounds SELECT id,@p1 FROM rounds WHERE player=@p0;
                DELETE FROM rounds WHERE player=@p0;
                DELETE FROM players WHERE id=@p0;
                """, previous, id);
        }

        Execute(connection, """
            INSERT INTO sessions(id,player,collector,session,map,first_at,last_at) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p5)
            ON CONFLICT(id) DO UPDATE SET last_at=max(last_at,excluded.last_at),first_at=min(first_at,excluded.first_at),player=excluded.player
            """, session, id, statsEvent.CollectorId.ToString(), p.SessionId, statsEvent.Map, statsEvent.Timestamp);
        if (p.Geoip is { Version: 1, CountryCode.Length: 2 } country && country.CountryCode.All(ch => ch is >= 'A' and <= 'Z'))
            Execute(connection, "UPDATE sessions SET country=@p1,country_at=@p2 WHERE id=@p0 AND (country_at IS NULL OR country_at<=@p2)", session, country.CountryCode, statsEvent.Timestamp);
        return id;
    }

    private static void Count(SqliteConnection connection, string id, string dimension, string key, string metric, long value) => Execute(connection, "INSERT INTO counters VALUES(@p0,@p1,@p2,@p3,@p4) ON CONFLICT(player,dimension,key,metric) DO UPDATE SET value=value+excluded.value", id, dimension, key, metric, value);
    private static long Total(SqliteConnection connection, string id, string metric) => Convert.ToInt64(Scalar(connection, "SELECT value FROM counters WHERE player=@p0 AND dimension='overall' AND key='' AND metric=@p1", id, metric) ?? 0L);
    private ScoreReceipt[] Apply(SqliteConnection connection, StatsEvent statsEvent)
    {
        PlayerIdentity?[] actors = statsEvent.Data switch
        {
            PlayerEvent p => [p.Player],
            KillEvent death => [death.Attacker, death.Victim, death.Assister],
            HitEvent h => [h.Attacker, h.Victim],
            ShotEvent s => [s.Shooter],
            ObjectiveEvent o => [o.Player],
            _ => []
        };
        var ids = actors.OfType<PlayerIdentity>().DistinctBy(p => p.SessionId).ToDictionary(p => p.SessionId, p => Resolve(connection, statsEvent, p));
        if (statsEvent.Data is PlayerEvent pe)
        {
            var ended = statsEvent.EventType is "session_end" or "player_disconnect";
            Execute(connection, """
                UPDATE sessions SET started=CASE WHEN started IS NULL THEN @p1 ELSE min(started,@p1) END,
                  ended=CASE WHEN @p2 THEN max(coalesce(ended,@p3),@p3) ELSE ended END,
                  partial=CASE WHEN @p4 THEN @p5 ELSE partial END WHERE id=@p0
                """, Session(statsEvent, pe.Player), pe.SessionStartedAt, ended, statsEvent.Timestamp, statsEvent.EventType is "session_start" or "player_connect", pe.PartialSession);
        }

        var options = config.Ranking;
        if (options.ExcludeWarmup && statsEvent.Warmup != false)
            return[];
        bool Eligible(PlayerIdentity? p) => p is not null && (!options.ExcludeBots || p.IsBot == false);
        var notices = new List<ScoreReceipt>();
        string weapon = statsEvent.Data switch
        {
            KillEvent death => death.Weapon ?? "unknown",
            HitEvent h => h.Weapon ?? "unknown",
            ShotEvent s => s.Weapon ?? "unknown",
            _ => "unknown"
        };
        void Add(PlayerIdentity? p, string metric, long value = 1, bool withWeapon = true)
        {
            if (!Eligible(p))
                return;
            var id = ids[p!.SessionId];
            Count(connection, id, "overall", "", metric, value);
            Count(connection, id, "map", statsEvent.Map ?? "unknown", metric, value);
            Count(connection, id, "session", Session(statsEvent, p), metric, value);
            if (withWeapon)
                Count(connection, id, "weapon", weapon, metric, value);
        }

        double Points(PlayerIdentity p) => p.IsBot == true ? options.StartingPoints : Convert.ToDouble(Scalar(connection, "SELECT points FROM players WHERE id=@p0", ids[p.SessionId]));
        void Award(PlayerIdentity? p, double delta, string reason)
        {
            if (!options.Enabled || !Eligible(p) || !p!.Authenticated || p.IsBot != false || p.Steamid is null)
                return;
            if (options.RoundPointsToIntegers)
                delta = Math.Round(delta, 0, MidpointRounding.AwayFromZero);
            var before = Points(p);
            var after = Math.Max(options.MinimumPoints, before + delta);
            Execute(connection, "UPDATE players SET points=@p1 WHERE id=@p0", ids[p.SessionId], after);
            notices.Add(new(statsEvent.EventId, p.Steamid, statsEvent.CollectorId, p.SessionId, after - before, after, reason, reason == "kill" ? Total(connection, ids[p.SessionId], "kills") : null, reason == "kill" ? options.MinimumKillsForLeaderboard : null, reason == "kill" && statsEvent.Data is KillEvent kill ? kill.Victim?.Name : null, reason == "kill" && statsEvent.Data is KillEvent death ? death.Headshot : null));
        }

        if (statsEvent.RoundId is Guid round)
            foreach (var p in actors.OfType<PlayerIdentity>().DistinctBy(p => p.SessionId).Where(Eligible))
                if (Execute(connection, "INSERT OR IGNORE INTO rounds VALUES(@p0,@p1)", statsEvent.CollectorId + "/" + round, ids[p.SessionId]) > 0)
                    Add(p, "observed_rounds", withWeapon: false);
        if (statsEvent.Data is KillEvent k)
        {
            if (options.ExcludeBots && (k.Attacker?.IsBot == true || k.Victim?.IsBot == true))
                return[];
            bool suicide = k.IsSuicide == true || k.Attacker is not null && k.Attacker.SessionId == k.Victim?.SessionId;
            bool team = !suicide && (k.IsTeamkill == true || k.Attacker?.Team is 2 or 3 && k.Attacker.Team == k.Victim?.Team);
            Add(k.Victim, "deaths");
            if (suicide)
            {
                Add(k.Victim ?? k.Attacker, "suicides");
                Award(k.Victim ?? k.Attacker, -options.SuicidePenalty, "suicide");
            }
            else if (team)
            {
                Add(k.Attacker, "teamkills");
                Award(k.Attacker, -options.TeamkillPenalty, "teamkill");
            }
            else
            {
                Add(k.Attacker, "kills");
                foreach (var(flag, metric)in new[]
                {
                    (k.Headshot, "headshot_kills"),
                    (k.NoScope, "no_scope_kills"),
                    (k.ThroughSmoke, "through_smoke_kills"),
                    (k.AttackerBlind, "blind_kills"),
                    (k.ObjectsPenetrated > 0, "wallbang_kills"),
                    (weapon.Contains("knife") || weapon == "bayonet", "knife_kills"),
                    (weapon is "hegrenade" or "inferno" or "molotov" or "incgrenade", "grenade_kills")
                }

                )
                    if (flag)
                        Add(k.Attacker, metric);
                var assist = k.Assister?.SessionId != k.Attacker?.SessionId && k.Assister?.SessionId != k.Victim?.SessionId ? k.Assister : null;
                Add(assist, "assists", withWeapon: false);
                if (Eligible(k.Attacker) && Eligible(k.Victim) && (k.Attacker!.Authenticated || k.Attacker.IsBot == true) && (k.Victim!.Authenticated || k.Victim.IsBot == true))
                {
                    var multiplier = Math.Clamp(1 + (Points(k.Victim) - Points(k.Attacker)) / options.StrengthScale, options.MinimumMultiplier, options.MaximumMultiplier);
                    Award(k.Attacker, options.KillBase * multiplier + (k.Headshot ? options.HeadshotBonus : 0), "kill");
                    Award(k.Victim, -options.DeathBase * multiplier, "death");
                    Award(assist, options.AssistBase, "assist");
                }
                else
                    Award(k.Attacker, 0, "kill"); // A counted kill can advance qualification without a rated opponent.
            }
        }
        else if (statsEvent.Data is HitEvent hit)
        {
            if (options.ExcludeBots && (hit.Attacker?.IsBot == true || hit.Victim?.IsBot == true))
                return[];
            bool suicide = hit.IsSuicide == true || hit.Attacker is not null && hit.Attacker.SessionId == hit.Victim?.SessionId;
            bool team = hit.IsTeamkill == true || hit.Attacker?.Team is 2 or 3 && hit.Attacker.Team == hit.Victim?.Team;
            if (!suicide && (!team || !options.ExcludeTeamDamage))
            {
                Add(hit.Attacker, "hits");
                Add(hit.Attacker, "damage", hit.DamageHealth);
                if (Eligible(hit.Attacker))
                {
                    Count(connection, ids[hit.Attacker!.SessionId], "hitgroup", hit.HitLocation, "hits", 1);
                    Count(connection, ids[hit.Attacker.SessionId], "hitgroup", hit.HitLocation, "damage", hit.DamageHealth);
                }
            }

            Add(hit.Victim, "damage_taken", hit.DamageHealth);
        }
        else if (statsEvent.Data is ShotEvent shot)
            Add(shot.Shooter, "shot_events");
        else if (statsEvent.Data is ObjectiveEvent objective)
        {
            Add(objective.Player, statsEvent.EventType, withWeapon: false);
            Award(objective.Player, statsEvent.EventType switch
            {
                "bomb_planted" => options.BombPlantPoints,
                "bomb_defused" => options.BombDefusePoints,
                "hostage_rescued" => options.HostageRescuePoints,
                "mvp" => options.MvpPoints,
                _ => 0
            }, statsEvent.EventType);
        }

        return notices.ToArray();
    }
}
