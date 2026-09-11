namespace Finestats.Events;

public sealed record HitEvent(PlayerIdentity? Attacker, PlayerIdentity? Victim, string? Weapon,
    int DamageHealth, int DamageArmor, int HealthRemaining, int ArmorRemaining,
    long Hitgroup, string HitLocation, bool? IsTeamkill, bool? IsSuicide,
    double? Distance = null, bool? Penetrated = null, int? ObjectsPenetrated = null) : EventData
{
    public string? AttackerSteamid => Attacker?.Steamid;
    public string? VictimSteamid => Victim?.Steamid;
    public int? AttackerTeam => Attacker?.Team;
    public int? VictimTeam => Victim?.Team;
}
