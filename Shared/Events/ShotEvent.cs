namespace Finestats.Events;

public sealed record ShotEvent(PlayerIdentity? Shooter, string? Weapon, bool Silenced) : EventData
{
    public string? ShooterSteamid => Shooter?.Steamid;
}
