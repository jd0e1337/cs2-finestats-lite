using Finestats.Events;

namespace Finestats.Helpers;

public static class IdentityRules
{
    public static bool? IsSuicide(PlayerIdentity? attacker, PlayerIdentity? victim)
        => victim is null ? null : attacker is null ? false : attacker.SessionId == victim.SessionId;

    public static bool? IsTeamkill(PlayerIdentity? attacker, PlayerIdentity? victim)
    {
        if (victim is null) return null;
        if (attacker is null || IsSuicide(attacker, victim) == true) return false;
        if (attacker.Team is null || victim.Team is null) return null;
        return attacker.Team is 2 or 3 && attacker.Team == victim.Team;
    }
}
