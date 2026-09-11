using System.Globalization;
using System.Text.RegularExpressions;
namespace Finestats.Config;

public sealed class ChatMessages(StatsConfig config)
{
    private static readonly Regex Tokens = new(@"\{([a-z_]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Dictionary<string, string> BuiltIn = new()
    {
        ["Unknown"] = "—",
        ["ScoreKill"] = "Received [green]{amount}[/] Points for Killing [yellow]{opponent}[/] [green]({points})[/]",
        ["ScoreHeadshotKill"] = "Received [green]{amount}[/] Points for Killing [yellow]{opponent}[/] with [gold]Headshot[/] [green]({points})[/]",
        ["Playtime"] = "Playtime: [yellow]{duration}[/]",
        ["ProfilePlaytime"] = "Damage [yellow]{damage}[/] | Damage events [yellow]{hits}[/] | Playtime: [yellow]{duration}[/]",
        ["RankingProgress"] = "You still need [green]{remaining}[/] kills to start ranking ([yellow]{kills}[/]/[yellow]{required}[/]).",
        ["RankingQualified"] = "You have reached [green]{required}[/] kills and are now ranked!",
        ["PlayerOnly"] = "This command is only available in game.",
        ["AuthenticationPending"] = "Steam authentication has not completed yet.",
        ["Cooldown"] = "Please wait before requesting statistics again.",
        ["Busy"] = "Statistics requests are busy. Please try again later.",
        ["Timeout"] = "Statistics request cancelled or the backend took too long to respond.",
        ["Unavailable"] = "Statistics are currently unavailable. Please try again later.",
        ["HelpCommands"] = "Commands: !rank, !top10 [page], !next, !statsme, !session",
        ["HelpLists"] = "!weapons [page], !targets [page], !servers [page], !status, !load",
        ["HelpObservations"] = "Observed data only; lists show up to [yellow]{page_size}[/] entries per page.",
        ["Accuracy"] = "Exact accuracy and misses are unavailable. !weapons shows separate fire and damage events.",
        ["InvalidArguments"] = "Too many or overly long arguments. Help: !hlx_help",
        ["InvalidPage"] = "Please enter a valid page number, e.g. !weapons 2.",
        ["NoArguments"] = "This command takes no arguments.",
        ["PageRange"] = "[yellow]{command}[/]: pages 1 to [yellow]{pages}[/].",
        ["NoObservations"] = "No backend observations yet. Please try again later.",
        ["NoSession"] = "The current session has not been observed by the backend yet.",
        ["SessionHeader"] = "Current session: [yellow]{map}[/] | [yellow]{status}[/]",
        ["SessionTime"] = "Observed time span: [yellow]{seconds}[/] s. Not exact connection time.",
        ["SessionCombat"] = "K/D/A [green]{kills}[/]/[red]{deaths}[/]/[green]{assists}[/] | K/D [yellow]{kd}[/] | HS [yellow]{headshots}[/] | Damage [yellow]{damage}[/]",
        ["SessionEvents"] = "Fire [yellow]{shots}[/] / damage events [yellow]{hits}[/] | TK [red]{teamkills}[/] | Suicides [red]{suicides}[/]",
        ["SessionCounted"] = "Counters: received events from this session.",
        ["SessionUnavailable"] = "Historical session counters unavailable; a complete raw replay is required.",
        ["SessionOpen"] = "open",
        ["SessionComplete"] = "complete",
        ["SessionPartial"] = "partial",
        ["NoHits"] = "No damage events observed yet.",
        ["HitgroupHeader"] = "Hitgroups · Page [yellow]{page}[/]/[yellow]{pages}[/] · Damage events, not accuracy",
        ["HitgroupRow"] = "[yellow]{group}[/]: [yellow]{hits}[/] events | [yellow]{percentage}[/] % | [yellow]{damage}[/] damage",
        ["NoPredecessors"] = "No players ahead of you: rank 1, still unranked, or no data.",
        ["EmptyPage"] = "No entries on this page.",
        ["ListHeader"] = "[yellow]{command}[/] · Page [yellow]{page}[/] · Received observations",
        ["WeaponRow"] = "[yellow]{weapon}[/]: [green]{kills}[/] K | [yellow]{headshots}[/] HS | [yellow]{damage}[/] damage | Fire [yellow]{shots}[/] / damage events [yellow]{hits}[/]",
        ["ServerRow"] = "[green]{name}[/] ([yellow]{server}[/]): [yellow]{map}[/] | [yellow]{status}[/]",
        ["RankingRow"] = "#[yellow]{rank}[/] [green]{name}[/] | [green]{points}[/] points",
        ["More"] = "More entries: ![yellow]{command}[/] [yellow]{page}[/]",
        ["ServerHeader"] = "[green]{name}[/] | [yellow]{map}[/] | [yellow]{status}[/]",
        ["ServerEvents"] = "Events: [yellow]{events}[/] | Last received: [yellow]{last_received}[/]",
        ["RankPosition"] = "[yellow]#{rank}[/]",
        ["Unranked"] = "unranked (minimum activity)",
        ["ScopeAll"] = "All servers",
        ["ScopeServer"] = "This server",
        ["RankSummary"] = "[yellow]{scope}[/]: Rank {rank} | [green]{points}[/] points",
        ["RankReply"] = "[green]{name}[/]: {summary}",
        ["ProfileCombat"] = "K/D/A: [green]{kills}[/]/[red]{deaths}[/]/[green]{assists}[/] | K/D [yellow]{kd}[/] | HS [yellow]{percentage}[/] %",
        ["ProfileEvents"] = "Damage [yellow]{damage}[/] | Damage events [yellow]{hits}[/] | Completed observed time [yellow]{seconds}[/] s",
        ["RecentlyObserved"] = "recently observed (not live status)",
        ["Stale"] = "no recent observation",
        ["ScoreReceived"] = "Received [green]{amount}[/] points for [yellow]{reason}[/] ([green]{points}[/] points).",
        ["ScoreLost"] = "Lost [red]{amount}[/] points for [yellow]{reason}[/] ([green]{points}[/] points).",
        ["Reason_kill"] = "kill",
        ["Reason_death"] = "death",
        ["Reason_assist"] = "assist",
        ["Reason_teamkill"] = "teamkill",
        ["Reason_suicide"] = "suicide",
        ["Reason_bomb_planted"] = "planting the bomb",
        ["Reason_bomb_defused"] = "defusing the bomb",
        ["Reason_hostage_rescued"] = "rescuing a hostage",
        ["Reason_mvp"] = "MVP",
        ["Hitgroup_head"] = "head",
        ["Hitgroup_chest"] = "chest",
        ["Hitgroup_stomach"] = "stomach",
        ["Hitgroup_left_arm"] = "left arm",
        ["Hitgroup_right_arm"] = "right arm",
        ["Hitgroup_left_leg"] = "left leg",
        ["Hitgroup_right_leg"] = "right leg",
        ["Hitgroup_neck"] = "neck",
        ["Hitgroup_gear"] = "gear",
        ["Hitgroup_generic"] = "generic",
        ["Hitgroup_unknown"] = "unknown",
    };
    public static Dictionary<string, string> Defaults() => new(BuiltIn);
    public string Get(string key, params (string Key, object? Value)[] args)
    {
        var template = config.Messages.GetValueOrDefault(key) ?? BuiltIn[key];
        var values = args.ToDictionary(x => x.Key, x => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? "");
        // One pass: values cannot expand into further placeholders or change the template.
        return Tokens.Replace(template, m => values.GetValueOrDefault(m.Groups[1].Value, m.Value));
    }
    public string Number(double? value) => value?.ToString(config.NumberDecimalPlaces == 0 ? "0" : "0." + new string('#', config.NumberDecimalPlaces), CultureInfo.InvariantCulture) ?? Get("Unknown");
    public static void Validate(Dictionary<string, string>? messages)
    {
        if (messages is null) throw new InvalidDataException("Messages must be an object.");
        foreach (var (key, text) in messages)
        {
            if (!BuiltIn.TryGetValue(key, out var original)) throw new InvalidDataException("Unknown message key: " + key);
            if (text is null || text.Length > 1024 || text.Any(char.IsControl)) throw new InvalidDataException("Message must be plain text up to 1024 characters: " + key);
            var allowed = Tokens.Matches(original).Select(x => x.Groups[1].Value).ToHashSet();
            if (Tokens.Matches(text).Any(x => !allowed.Contains(x.Groups[1].Value)) || Tokens.Replace(text, "").IndexOfAny(['{', '}']) >= 0)
                throw new InvalidDataException("Unknown or malformed placeholder in message: " + key);
        }
    }
}
