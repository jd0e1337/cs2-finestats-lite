using Finestats.Events;
using Finestats.Helpers;
using SwiftlyS2.Shared;

namespace Finestats.Services;

// Game-thread-owned state. Once emitted, an envelope cannot change during a map transition.
public sealed class CollectionContext(ISwiftlyCore core, string serverId, EventQueue queue, Diagnostics log)
{
    private long _sequence;
    public Guid CollectorId { get; } = Guid.NewGuid();
    public Guid MapInstanceId { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; } = Guid.NewGuid();
    public Guid? RoundId { get; private set; }
    public string? Map { get; private set; }
    public int? Round { get; private set; }
    public bool? Warmup { get; private set; }

    public void SetMap(string? map)
    {
        Map = WeaponHelper.Limit(map, 128);
        MapInstanceId = Guid.NewGuid();
        BeginMatch();
    }

    public void BeginMatch() { MatchId = Guid.NewGuid(); Round = null; RoundId = null; Warmup = null; }
    public void BeginRound(int? number, bool? warmup) { Round = number; Warmup = warmup; RoundId = Guid.NewGuid(); }

    public void Emit(string type, EventData data)
    {
        // Round-start can precede the warmup transition, and loading mid-round
        // may miss it entirely. Classify this event from the current game rules.
        // Unknown state stays unknown so ExcludeWarmup continues to fail closed.
        try { Warmup = core.EntitySystem.GetGameRules()?.WarmupPeriod; }
        catch (InvalidOperationException) { Warmup = null; }
        int? tick;
        try { tick = core.Engine.GlobalVars.TickCount; }
        catch (InvalidOperationException) { tick = null; }
        var value = new StatsEvent(type, 1, serverId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Map, Round, data, Guid.NewGuid(), CollectorId, ++_sequence, MapInstanceId, MatchId, RoundId, tick, Warmup);
        if (!queue.TryEnqueue(value)) log.Warn($"finestats: event queue full or closed; dropped total={queue.Dropped}, capacity pressure at depth={queue.Count}.");
    }
}
