namespace Finestats.Events;

public sealed record ObjectiveEvent(PlayerIdentity? Player, int? Site = null,
    int? EntityIndex = null, bool? HasKit = null, int? Reason = null, int? Value = null) : EventData
{
    public string? PlayerSteamid => Player?.Steamid;
}
