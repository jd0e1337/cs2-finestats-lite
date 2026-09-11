namespace Finestats.Events;

public sealed record RoundEvent(int? WinningTeam, int? Reason, int? CtScore, int? TScore,
    string ScoreTiming, string? Message = null) : EventData;
