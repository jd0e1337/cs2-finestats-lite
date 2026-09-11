namespace Finestats.Events;

public sealed record KillEvent(PlayerIdentity? Attacker, PlayerIdentity? Victim, PlayerIdentity? Assister,
    string? Weapon, bool Headshot, int ObjectsPenetrated, bool ThroughSmoke, bool NoScope,
    double? Distance, bool AttackerBlind, bool? VictimBlind, bool Revenge, bool Domination,
    bool? IsTeamkill, bool? IsSuicide, bool AssistedFlash, bool AttackerInAir) : EventData
{
    public string? AttackerSteamid => Attacker?.Steamid;
    public string? VictimSteamid => Victim?.Steamid;
    public string? AssisterSteamid => Assister?.Steamid;
    public bool Penetrated => ObjectsPenetrated > 0;
    public string DistanceUnit => "meters";
}
