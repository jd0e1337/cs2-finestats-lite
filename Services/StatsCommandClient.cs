using System.Globalization;
using System.Text.Json;
using Finestats.Config;

namespace Finestats.Services;

public sealed record CommandPlayer(string Steamid, string SessionId, Guid CollectorId);
// Managed-only chat formatting over local SQLite reads; there is no HTTP client.
public sealed class StatsCommandClient : IDisposable
{
    public static readonly string[] Names = ["rank", "skill", "points", "place", "top5", "top10", "top20", "next", "statsme", "kpd", "kdratio", "kdeath", "kills", "kill", "player_kills", "session", "session_data", "weapons", "weapon", "targets", "target", "accuracy", "servers", "status", "load", "hlx_menu", "hlx_help"];
    private readonly Finestats.Storage.LiteStore _store;
    private readonly string _server;
    private readonly StatsConfig _config;
    private readonly ChatMessages _messages;
    private string T(string key, params (string Key, object? Value)[] args) => _messages.Get(key, args);
    public StatsCommandClient(StatsConfig config, Finestats.Storage.LiteStore store)
    {
        _config = config;
        _messages = new(config);
        _store = store;
        _server = Uri.EscapeDataString(config.ServerId);
    }

    public static string Safe(string? text, int maxLength = 48) => new(ChatColors.Plain(text ?? "—").Where(c => !char.IsControl(c) && c is not '<' and not '>' and not '{' and not '}' and not '[' and not ']' && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(maxLength).ToArray());
    public static string ChatLine(string text, StatsConfig? config = null)
    {
        return ChatColors.Limit((config?.ChatPrefix ?? "[blue][fs][/] ") + text, config?.ChatMaxBytes ?? 230);
    }

    private string Text(JsonElement row, string key) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? Safe(v.GetString(), key is "name" or "key" ? _config.DisplayNameMaxLength : 128) : T("Unknown");
    private static double? Value(JsonElement row, string key, bool counter = false)
    {
        if (row.TryGetProperty(key, out var v))
            return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        if (counter && row.TryGetProperty("counters", out var c) && c.ValueKind == JsonValueKind.Object)
            return c.TryGetProperty(key, out var x) ? x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var n) && double.IsFinite(n) ? n : null : 0;
        return null;
    }

    private string N(double? v) => _messages.Number(v);
    private string N(JsonElement row, string key, bool counter = false) => N(Value(row, key, counter));
    private Task<JsonElement?> Read(string path, CancellationToken ct) => _store.ReadAsync(path, ct);
    public async Task<string[]> Execute(string command, string[] args, CommandPlayer player, CancellationToken ct, Action? rankReady = null)
    {
        if (command is "hlx_menu" or "hlx_help")
            return[T("HelpCommands"), T("HelpLists"), T("HelpObservations", ("page_size", _config.CommandPageSize))];
        if (command == "accuracy")
            return[T("Accuracy")];
        if (args.Length > 1 || args.Any(a => a.Length > 64))
            return[T("InvalidArguments")];
        var paged = command is "top5" or "top10" or "top20" or "weapons" or "weapon" or "targets" or "target" or "servers";
        int page = 1;
        if (paged && args.Length == 1 && (!int.TryParse(args[0], out page) || page < 1 || page > 100000 / _config.CommandPageSize + 1))
            return[T("InvalidPage")];
        if (!paged && args.Length != 0)
            return[T("NoArguments")];
        int size = _config.CommandPageSize;
        int top = command == "top5" ? 5 : command == "top10" ? 10 : 20;
        int pages = (top + size - 1) / size;
        if (command.StartsWith("top") && page > pages)
            return[T("PageRange", ("command", command), ("pages", pages))];
        string scope = "server=" + _server;
        string who = "players/" + Uri.EscapeDataString(player.Steamid);
        string path = command switch
        {
            "top5" or "top10" or "top20" => $"rankings?{scope}&limit={Math.Min(size, top - (page - 1) * size)}&offset={(page - 1) * size}",
            "next" => $"{who}/next?{scope}",
            "session" or "session_data" => $"{who}/sessions?{scope}&session={Uri.EscapeDataString(player.SessionId)}&collector={player.CollectorId:D}&limit=1",
            "weapons" or "weapon" => $"{who}/weapons?{scope}&limit={size}&offset={(page - 1) * size}",
            "targets" or "target" => $"{who}/hitgroups?{scope}&limit=100",
            "servers" => $"servers?limit={size}&offset={(page - 1) * size}",
            "status" => "servers/" + _server,
            "load" => who + "?" + LiveSession(),
            _ => $"{who}?{scope}&{LiveSession()}"};
        string LiveSession() => $"active_session={Uri.EscapeDataString(player.SessionId)}&active_collector={player.CollectorId:D}";
        var result = await Read(path, ct).ConfigureAwait(false);
        if (result is null)
            return[T("NoObservations")];
        var data = result.Value;
        if (command is "session" or "session_data")
        {
            var rows = data.GetProperty("items");
            if (rows.GetArrayLength() == 0)
                return[T("NoSession")];
            var row = rows[0];
            bool open = row.GetProperty("ended_at").ValueKind == JsonValueKind.Null;
            var kills = Value(row, "kills", true);
            var deaths = Value(row, "deaths", true);
            return[T("SessionHeader", ("map", Text(row, "map")), ("status", open ? T("SessionOpen") : Text(row, "completeness") == "complete" ? T("SessionComplete") : T("SessionPartial"))), T("Playtime", ("duration", PlaytimeFormatter.Format(Value(row, "playtime_seconds")))), T("SessionCombat", ("kills", N(kills)), ("deaths", N(deaths)), ("assists", N(row, "assists", true)), ("kd", N(deaths is> 0 ? kills / deaths : null)), ("headshots", N(row, "headshot_kills", true)), ("damage", N(row, "damage", true))), T("SessionEvents", ("shots", N(row, "shot_events", true)), ("hits", N(row, "hits", true)), ("teamkills", N(row, "teamkills", true)), ("suicides", N(row, "suicides", true))), row.TryGetProperty("counters_complete", out var complete) && complete.ValueKind == JsonValueKind.True ? T("SessionCounted") : T("SessionUnavailable")];
        }

        if (command is "targets" or "target")
        {
            var rows = data.GetProperty("items").EnumerateArray().ToArray();
            if (rows.Length == 0)
                return[T("NoHits")];
            var groups = new[]
            {
                "head",
                "chest",
                "stomach",
                "left_arm",
                "right_arm",
                "left_leg",
                "right_leg",
                "neck",
                "gear",
                "generic",
                "unknown"
            };
            int groupPages = (groups.Length + size - 1) / size;
            if (page > groupPages)
                return[T("PageRange", ("command", "targets"), ("pages", groupPages))];
            double total = rows.Sum(r => Value(r, "hits", true) ?? 0);
            var lines = new List<string>
            {
                T("HitgroupHeader", ("page", page), ("pages", groupPages))
            };
            foreach (var group in groups.Skip((page - 1) * size).Take(size))
            {
                var row = rows.FirstOrDefault(r => r.GetProperty("key").GetString() == group);
                double? count = row.ValueKind == JsonValueKind.Undefined ? 0 : Value(row, "hits", true);
                var damage = row.ValueKind == JsonValueKind.Undefined ? "0" : N(row, "damage", true);
                lines.Add(T("HitgroupRow", ("group", T("Hitgroup_" + group)), ("hits", N(count)), ("percentage", N(total > 0 ? count * 100 / total : null)), ("damage", damage)));
            }

            return lines.ToArray();
        }

        if (data.TryGetProperty("items", out var items))
        {
            if (items.GetArrayLength() == 0)
                return[command == "next" ? T("NoPredecessors") : T("EmptyPage")];
            var lines = new List<string>
            {
                T("ListHeader", ("command", command), ("page", page))
            };
            foreach (var row in items.EnumerateArray().Take(command.StartsWith("top") ? Math.Min(size, top - (page - 1) * size) : size))
            {
                if (command is "weapons" or "weapon")
                    lines.Add(T("WeaponRow", ("weapon", Text(row, "key")), ("kills", N(row, "kills", true)), ("headshots", N(row, "headshot_kills", true)), ("damage", N(row, "damage", true)), ("shots", N(row, "shot_events", true)), ("hits", N(row, "hits", true))));
                else if (command == "servers")
                    lines.Add(T("ServerRow", ("name", Text(row, "name")), ("server", Text(row, "server_id")), ("map", Text(row, "map")), ("status", Observed(row))));
                else
                    lines.Add(T("RankingRow", ("rank", N(row, "rank")), ("name", Text(row, "name")), ("points", N(row, "points"))));
            }

            if (data.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True && (!command.StartsWith("top") || page < pages))
                lines.Add(T("More", ("command", command), ("page", page + 1)));
            return lines.ToArray();
        }

        if (command == "status")
            return[T("ServerHeader", ("name", Text(data, "name")), ("map", Text(data, "map")), ("status", Observed(data))), T("ServerEvents", ("events", N(data, "event_count")), ("last_received", Text(data, "last_received")))];
        var rank = Value(data, "rank")is double r ? T("RankPosition", ("rank", N(r))) : T("Unranked");
        var summary = T("RankSummary", ("scope", command == "load" ? T("ScopeAll") : T("ScopeServer")), ("rank", rank), ("points", N(data, "points")));
        if (command is "rank" or "skill" or "points" or "place")
        {
            if (command == "rank" && Value(data, "points")is not null)
                rankReady?.Invoke();
            return[T("RankReply", ("name", Text(data, "name")), ("summary", summary))];
        }

        return[summary, T("ProfileCombat", ("kills", N(data, "kills")), ("deaths", N(data, "deaths")), ("assists", N(data, "assists")), ("kd", N(data, "kd")), ("percentage", N(data, "headshot_percentage"))), T("ProfilePlaytime", ("damage", N(data, "damage", true)), ("hits", N(data, "hits", true)), ("duration", PlaytimeFormatter.Format(Value(data, "playtime_seconds"))))];
    }

    private string Observed(JsonElement row) => row.TryGetProperty("ingestion_health", out var v) && v.GetString() == "recently_observed" ? T("RecentlyObserved") : T("Stale");
    public void Dispose()
    {
    }
}
