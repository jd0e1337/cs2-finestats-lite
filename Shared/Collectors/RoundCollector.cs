using Finestats.Events;
using Finestats.Helpers;
using Finestats.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace Finestats.Collectors;

public sealed class RoundCollector(ISwiftlyCore core, CollectionContext context)
{
    public void Subscribe(GameEventSubscriptions hooks)
    {
        hooks.Post<EventBeginNewMatch>(_ =>
        {
            context.BeginMatch();
            context.Emit("match_start", new LifecycleEvent("begin_new_match"));
        });
        hooks.Post<EventRoundStart>(_ =>
        {
            var rules = core.EntitySystem.GetGameRules();
            int? round = rules is not null && rules.TotalRoundsPlayed >= 0 ? rules.TotalRoundsPlayed + 1 : null;
            context.BeginRound(round, rules?.WarmupPeriod);
            var (ct, t) = Scores();
            context.Emit("round_start", new RoundEvent(null, null, ct, t, "round_start_callback"));
        });
        hooks.Post<EventRoundEnd>(e =>
        {
            var (ct, t) = Scores();
            context.Emit("round_end", new RoundEvent(e.Winner, e.Reason, ct, t,
                "round_end_callback", WeaponHelper.Limit(e.Message, 256)));
        });
        hooks.Post<EventRoundOfficiallyEnded>(_ =>
        {
            var (ct, t) = Scores();
            context.Emit("round_officially_ended", new RoundEvent(null, null, ct, t, "round_officially_ended_callback"));
        });
    }

    private (int? Ct, int? T) Scores()
    {
        int? ct = null, t = null;
        foreach (var team in core.EntitySystem.GetAllEntitiesByClass<CCSTeam>())
        {
            if (!team.IsValid) continue;
            if (team.TeamNum == 3) ct = team.Score;
            else if (team.TeamNum == 2) t = team.Score;
        }
        return (ct, t);
    }
}
