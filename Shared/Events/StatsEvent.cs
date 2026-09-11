using System.Text.Json.Serialization;

namespace Finestats.Events;

public sealed record StatsEvent(
    string EventType, int EventVersion, string ServerId, long Timestamp,
    string? Map, int? Round, EventData Data, Guid EventId, Guid CollectorId,
    long Sequence, Guid MapInstanceId, Guid MatchId, Guid? RoundId, int? Tick, bool? Warmup);

[JsonDerivedType(typeof(PlayerEvent))]
[JsonDerivedType(typeof(HitEvent))]
[JsonDerivedType(typeof(KillEvent))]
[JsonDerivedType(typeof(RoundEvent))]
[JsonDerivedType(typeof(ObjectiveEvent))]
[JsonDerivedType(typeof(ShotEvent))]
[JsonDerivedType(typeof(LifecycleEvent))]
public abstract record EventData;

public sealed record LifecycleEvent(string Reason, bool? HotReload = null) : EventData;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(StatsEvent[]))]
public partial class StatsJsonContext : JsonSerializerContext;
