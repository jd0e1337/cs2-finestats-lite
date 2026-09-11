namespace Finestats.Events;

// Only immutable managed values leave the game thread. Never retain IPlayer or an entity.
public sealed record CountryObservation(int Version, string CountryCode);
public sealed record PlayerIdentity(int Slot, string SessionId, string? Steamid, string? Name,
    int? Team, bool? IsBot, bool Authenticated,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CountryObservation? Geoip = null);

public sealed record PlayerEvent(PlayerIdentity Player, long SessionStartedAt,
    string Reason, bool PartialSession = false, int? DisconnectReason = null,
    int? OldTeam = null, int? NewTeam = null, string? OldName = null) : EventData;
