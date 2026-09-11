using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Finestats.Storage;

public sealed partial class LiteStore
{
    private static List<Dictionary<string, object?>> Rows(SqliteConnection connection, string sql, params object?[] args)
    {
        using var command = Command(connection, sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private const string Profiles = """
        WITH totals AS (SELECT player,
            sum(CASE WHEN metric='kills' THEN value ELSE 0 END) kills,
            sum(CASE WHEN metric='deaths' THEN value ELSE 0 END) deaths,
            sum(CASE WHEN metric='assists' THEN value ELSE 0 END) assists,
            sum(CASE WHEN metric='headshot_kills' THEN value ELSE 0 END) headshots
            FROM counters WHERE dimension='overall' AND key='' GROUP BY player),
        base AS (SELECT p.id,p.steam steamid,p.name,p.points,coalesce(t.kills,0) kills,coalesce(t.deaths,0) deaths,
            coalesce(t.assists,0) assists,coalesce(t.headshots,0) headshots,
            coalesce(t.kills,0)*1.0/nullif(t.deaths,0) kd,coalesce(t.headshots,0)*100.0/nullif(t.kills,0) headshot_percentage,
            (SELECT coalesce(sum(max(0,coalesce(ended,last_at)-CASE WHEN partial=0 THEN coalesce(started,first_at) ELSE first_at END)),0)/1000.0 FROM sessions WHERE player=p.id) playtime_seconds,
            (SELECT country FROM sessions WHERE player=p.id AND country IS NOT NULL ORDER BY country_at DESC,id DESC LIMIT 1) country_code
            FROM players p LEFT JOIN totals t ON t.player=p.id WHERE p.steam IS NOT NULL),
        ranked AS (SELECT id,row_number() OVER(ORDER BY points DESC,steamid) rank FROM base WHERE kills>=@p0),
        profiles AS (SELECT base.*,ranked.rank FROM base LEFT JOIN ranked USING(id))
        """;
    private static Dictionary<string, long> Counters(SqliteConnection connection, string id, string dimension, string key) => Rows(connection, "SELECT metric,value FROM counters WHERE player=@p0 AND dimension=@p1 AND key=@p2", id, dimension, key).ToDictionary(row => (string)row["metric"]!, row => Convert.ToInt64(row["value"]));
    public async Task<JsonElement?> ReadAsync(string route, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.CommandTimeoutSeconds));
        await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            using var connection = Open();
            Initialize(connection);
            using var transaction = connection.BeginTransaction(deferred: true);
            var parts = route.Split('?', 2);
            var query = parts.Length == 1 ? new Dictionary<string, string>() : parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => x.Length == 2 ? Uri.UnescapeDataString(x[1]) : "");
            int Number(string key, int fallback, int max) => query.TryGetValue(key, out var text) && int.TryParse(text, out var n) ? Math.Clamp(n, 0, max) : fallback;
            int limit = Number("limit", 5, 100), offset = Number("offset", 0, 100000);
            var path = parts[0].Split('/');
            object Page(List<Dictionary<string, object?>> rows) => new
            {
                items = rows.Take(limit),
                has_more = rows.Count > limit
            };
            Dictionary<string, object?> Server() => new()
            {
                ["server_id"] = config.ServerId,
                ["name"] = config.ServerId,
                ["map"] = Scalar(connection, "SELECT value FROM metadata WHERE key='map'"),
                ["event_count"] = Convert.ToInt64(Scalar(connection, "SELECT value FROM metadata WHERE key='event_count'") ?? 0),
                ["last_received"] = Scalar(connection, "SELECT value FROM metadata WHERE key='last_received'"),
                ["ingestion_health"] = DateTimeOffset.TryParse(Scalar(connection, "SELECT value FROM metadata WHERE key='last_received'") as string, out var received) && DateTimeOffset.UtcNow - received < TimeSpan.FromMinutes(5) ? "recently_observed" : "stale"
            };
            object? result = null;
            if (path[0] == "servers")
                result = path.Length == 1 ? new
                {
                    items = offset == 0 ? new[]
                    {
                        Server()
                    }

                    : [],
                    has_more = false
                }

                : Server();
            else if (path[0] == "rankings")
                result = Page(Rows(connection, Profiles + " SELECT * FROM profiles WHERE rank IS NOT NULL ORDER BY rank LIMIT @p1 OFFSET @p2", config.Ranking.MinimumKillsForLeaderboard, limit + 1, offset));
            else if (path[0] == "players" && path.Length >= 2)
            {
                var steam = Uri.UnescapeDataString(path[1]);
                var id = "steam:" + steam;
                if (path.Length == 2)
                {
                    var rows = Rows(connection, Profiles + " SELECT * FROM profiles WHERE steamid=@p1", config.Ranking.MinimumKillsForLeaderboard, steam);
                    if (rows.Count > 0)
                    {
                        rows[0]["counters"] = Counters(connection, id, "overall", "");
                        if (query.ContainsKey("active_session") && query.ContainsKey("active_collector"))
                            rows[0]["playtime_seconds"] = Scalar(connection, """
                                SELECT coalesce(sum(max(0,CASE WHEN ended IS NOT NULL THEN ended
                                    WHEN collector=@p1 AND session=@p2 THEN @p3 ELSE last_at END
                                    - CASE WHEN partial=0 THEN coalesce(started,first_at) ELSE first_at END)),0)/1000.0
                                FROM sessions WHERE player=@p0
                                """, id, query["active_collector"], query["active_session"], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        result = rows[0];
                    }
                }
                else if (path[2] == "next")
                    result = new
                    {
                        items = Rows(connection, Profiles + " SELECT * FROM profiles WHERE rank<(SELECT rank FROM profiles WHERE steamid=@p1) ORDER BY rank DESC LIMIT 3", config.Ranking.MinimumKillsForLeaderboard, steam),
                        has_more = false
                    };
                else if (path[2] == "sessions")
                {
                    var rows = Rows(connection, """
                        SELECT id,map,ended ended_at,partial,started,first_at,last_at,country country_code,
                        max(0,last_at-first_at)/1000.0 observed_span_seconds,
                        max(0,coalesce(ended,@p3)-CASE WHEN partial=0 THEN coalesce(started,first_at) ELSE first_at END)/1000.0 playtime_seconds
                        FROM sessions WHERE player=@p0 AND collector=@p1 AND session=@p2 LIMIT 1
                        """, id, query.GetValueOrDefault("collector"), query.GetValueOrDefault("session"), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    foreach (var row in rows)
                    {
                        row["counters"] = Counters(connection, id, "session", (string)row["id"]!);
                        row["counters_complete"] = true;
                        row["completeness"] = Convert.ToInt64(row["partial"]) == 0 && row["ended_at"] is not null ? "complete" : "partial";
                    }

                    result = new
                    {
                        items = rows,
                        has_more = false
                    };
                }
                else if (path[2] is "weapons" or "hitgroups")
                {
                    var dimension = path[2] == "weapons" ? "weapon" : "hitgroup";
                    var rows = Rows(connection, "SELECT key FROM counters WHERE player=@p0 AND dimension=@p1 GROUP BY key ORDER BY key LIMIT @p2 OFFSET @p3", id, dimension, limit + 1, offset);
                    foreach (var row in rows)
                        row["counters"] = Counters(connection, id, dimension, (string)row["key"]!);
                    result = Page(rows);
                }
            }

            deadline.Token.ThrowIfCancellationRequested();
            return result is null ? null : JsonSerializer.SerializeToElement(result);
        }
        finally
        {
            _gate.Release();
        }
    }
}
