using System.Globalization;
using System.Text.Json;
using Finestats.Events;
using Finestats.Config;

namespace Finestats.Services;

public sealed record ScoreReceipt(Guid EventId, string Steamid, Guid CollectorId, string SessionId, double Delta, double PointsAfter, string Reason, long? KillsAfter = null, int? MinimumKills = null, string? OpponentName = null, bool? Headshot = null);

public sealed class ScoreReceiptFilter(StatsConfig? config = null)
{
    private readonly HashSet<(Guid, string, string)> _seen = [];
    private readonly Queue<(Guid, string, string)> _order = [];
    public ScoreReceipt[] Read(JsonElement root, StatsEvent[] batch, long now)
    {
        if (!root.TryGetProperty("scoreNotices", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > batch.Length * 3) return [];
        var events = batch.GroupBy(e => e.EventId).ToDictionary(g => g.Key, g => g.First());
        var results = new List<ScoreReceipt>();
        foreach (var row in rows.EnumerateArray())
        {
            var receipt = JsonSerializer.Deserialize<ScoreReceipt>(row, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (receipt is null || !events.TryGetValue(receipt.EventId, out var e) || e.CollectorId != receipt.CollectorId || (now - e.Timestamp < -5000 || now - e.Timestamp > (config?.ScoreNotificationMaxAgeSeconds ?? 30) * 1000L) || !double.IsFinite(receipt.Delta) || !double.IsFinite(receipt.PointsAfter)) continue;
            if (!ShowScore(receipt, config) && !ShowProgress(receipt, config)) continue;
            PlayerIdentity? actor = (e.Data, receipt.Reason) switch
            {
                (KillEvent k, "kill" or "teamkill") => k.Attacker,
                (KillEvent k, "death") => k.Victim,
                (KillEvent k, "suicide") => k.Victim ?? k.Attacker,
                (KillEvent k, "assist") => k.Assister,
                (ObjectiveEvent o, "bomb_planted" or "bomb_defused" or "hostage_rescued" or "mvp") when e.EventType == receipt.Reason => o.Player,
                _ => null
            };
            if (actor is null || actor.Steamid != receipt.Steamid || actor.SessionId != receipt.SessionId || !actor.Authenticated) continue;
            var key = (receipt.EventId, receipt.Steamid, receipt.Reason);
            if (!_seen.Add(key)) continue;
            _order.Enqueue(key);
            while (_order.Count > 8192) _seen.Remove(_order.Dequeue());
            results.Add(receipt);
        }
        return results.ToArray();
    }
    public static string Message(ScoreReceipt r, StatsConfig? config = null)
    {
        if (!ShowScore(r, config)) return "";
        var messages = new ChatMessages(config ?? new StatsConfig());
        if (r.Reason == "kill" && r.Delta > 0 && r.Headshot is bool headshot)
            return messages.Get(headshot ? "ScoreHeadshotKill" : "ScoreKill", ("amount", messages.Number(r.Delta)),
                ("points", messages.Number(r.PointsAfter)), ("opponent", StatsCommandClient.Safe(r.OpponentName ?? messages.Get("Unknown"), config?.DisplayNameMaxLength ?? 48)));
        return messages.Get(r.Delta > 0 ? "ScoreReceived" : "ScoreLost", ("amount", messages.Number(Math.Abs(r.Delta))),
            ("points", messages.Number(r.PointsAfter)), ("reason", messages.Get("Reason_" + r.Reason)));
    }
    private static bool ShowScore(ScoreReceipt r, StatsConfig? config)
    {
        if (r.Delta == 0 || !(config?.ScoreNotificationsEnabled ?? true)
            || config is not null && !config.ScoreNotificationReasons.Contains(r.Reason)) return false;

        // Kill points are still recorded before qualification, but their chat
        // notice starts with the qualifying kill. Progress notices remain separate.
        int minimumKills = config?.Ranking.MinimumKillsForLeaderboard ?? r.MinimumKills ?? 0;
        return r.Reason != "kill" || minimumKills <= 0 || r.KillsAfter >= minimumKills;
    }
    private static bool ShowProgress(ScoreReceipt r, StatsConfig? config) => (config?.RankingProgressNotificationsEnabled ?? true)
        && r.Reason == "kill" && r.MinimumKills is > 0 && r.KillsAfter is > 0 && r.KillsAfter <= r.MinimumKills;
    public static string ProgressMessage(ScoreReceipt r, StatsConfig? config = null)
    {
        if (!ShowProgress(r, config)) return "";
        return new ChatMessages(config ?? new StatsConfig()).Get(r.KillsAfter == r.MinimumKills ? "RankingQualified" : "RankingProgress",
            ("remaining", r.MinimumKills - r.KillsAfter), ("kills", r.KillsAfter), ("required", r.MinimumKills));
    }
}
