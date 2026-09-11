using System.Text.Json;

namespace Finestats.Config;

public sealed record RankingOptions
{
    public bool Enabled { get; init; } = true;
    public double StartingPoints { get; init; } = 1000;
    public double MinimumPoints { get; init; } = 0;
    public double KillBase { get; init; } = 5;
    public double DeathBase { get; init; } = 3;
    public double AssistBase { get; init; } = 2;
    public double HeadshotBonus { get; init; } = 2;
    public bool RoundPointsToIntegers { get; init; } = true;
    public double TeamkillPenalty { get; init; } = 10;
    public double SuicidePenalty { get; init; } = 5;
    public double BombPlantPoints { get; init; } = 2;
    public double BombDefusePoints { get; init; } = 4;
    public double MvpPoints { get; init; } = 1;
    public double HostageRescuePoints { get; init; } = 3;
    public double StrengthScale { get; init; } = 1000;
    public double MinimumMultiplier { get; init; } = .25;
    public double MaximumMultiplier { get; init; } = 4;
    public bool ExcludeWarmup { get; init; } = true;
    public bool ExcludeBots { get; init; } = true;
    public bool ExcludeTeamDamage { get; init; } = true;
    public int MinimumKillsForLeaderboard { get; init; } = 10;
    public int AlgorithmVersion { get; init; } = 1;
    public string Json => JsonSerializer.Serialize(this, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });

    public void Validate()
    {
        if (RoundPointsToIntegers && (StartingPoints != Math.Truncate(StartingPoints) || MinimumPoints != Math.Truncate(MinimumPoints)))
            throw new InvalidOperationException("Whole-point ranking requires integer StartingPoints and MinimumPoints.");
        if (AlgorithmVersion != 1 || StrengthScale <= 0 || MinimumMultiplier <= 0 || MaximumMultiplier < MinimumMultiplier || MinimumPoints < 0 || StartingPoints < MinimumPoints || MinimumKillsForLeaderboard < 0)
            throw new InvalidOperationException("Invalid ranking configuration");
        foreach (var p in GetType().GetProperties().Where(p => p.PropertyType == typeof(double)))
            if (p.GetValue(this)is double n && (!double.IsFinite(n) || n < 0 || n > 1e9))
                throw new InvalidOperationException("Invalid ranking constant");
    }
}
