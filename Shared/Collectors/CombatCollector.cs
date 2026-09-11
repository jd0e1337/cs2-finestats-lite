using Finestats.Events;
using Finestats.Helpers;
using Finestats.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;

namespace Finestats.Collectors;

public sealed class CombatCollector(ISwiftlyCore core, CollectionContext context, PlayerCollector players)
{
    public void Subscribe(GameEventSubscriptions hooks)
    {
        hooks.Post<EventPlayerHurt>(Hit);
        hooks.Post<EventPlayerDeath>(Kill);
        hooks.Post<EventWeaponFire>(e => context.Emit("shot", new ShotEvent(
            players.Resolve(e.UserIdPlayer), WeaponHelper.Normalize(e.Weapon), e.Silenced)));
    }

    private void Hit(EventPlayerHurt e)
    {
        var attacker = players.Resolve(e.AttackerPlayer);
        var victim = players.Resolve(e.UserIdPlayer);
        long hitgroup = (uint)e.ActualHitGroup;
        context.Emit("hit", new HitEvent(attacker, victim, WeaponHelper.Normalize(e.Weapon),
            e.ActualDmgHealth, e.ActualDmgArmor, e.ActualHealth, e.ActualArmor,
            hitgroup, HitGroupHelper.Normalize(hitgroup), IdentityRules.IsTeamkill(attacker, victim),
            IdentityRules.IsSuicide(attacker, victim)));
    }

    private void Kill(EventPlayerDeath e)
    {
        var attacker = players.Resolve(e.AttackerPlayer);
        var victimPlayer = e.UserIdPlayer;
        var victim = players.Resolve(victimPlayer);
        var assister = players.Resolve(e.AssisterPlayer);
        context.Emit("kill", new KillEvent(attacker, victim, assister, WeaponHelper.Normalize(e.Weapon),
            e.Headshot, e.Penetrated, e.ThruSmoke, e.NoScope,
            float.IsFinite(e.Distance) && e.Distance >= 0 ? e.Distance : null,
            e.AttackerBlind, PlayerHelper.IsBlind(victimPlayer, core), e.Revenge != 0, e.Dominated != 0,
            IdentityRules.IsTeamkill(attacker, victim), IdentityRules.IsSuicide(attacker, victim),
            e.AssistedFlash, e.AttackerInAir));
    }
}
