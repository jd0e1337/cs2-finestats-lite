using System.Text.Json;

namespace Finestats.Config;

public sealed record StatsConfig
{
    public string DatabaseFile { get; init; } = "finestats-lite.db";
    public int DatabaseBusyTimeoutSeconds { get; init; } = 2;
    public int BackupIntervalMinutes { get; init; } = 60;
    public int BackupKeepCount { get; init; } = 5;
    public RankingOptions Ranking { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public bool ConnectMessagesEnabled { get; init; } = true;
    public bool PublicConnectMessages { get; init; } = true;
    public int ConnectMessageDelaySeconds { get; init; } = 3;
    public bool ConnectCountryNames { get; init; } = true;
    public ConnectionMessages ConnectionMessages { get; init; } = new();
    public string ServerId { get; init; } = "cs2-01";
    public int BatchSize { get; init; } = 100;
    public int FlushIntervalMs { get; init; } = 1000;
    public int QueueCapacity { get; init; } = 10000;
    public int RetryCount { get; init; } = 3;
    public int ShutdownDrainMs { get; init; } = 5000;
    public int WarningIntervalSeconds { get; init; } = 30;
    public bool LogDebugEvents { get; init; }
    public bool ChatCommandsEnabled { get; init; } = true;
    public int CommandCooldownSeconds { get; init; } = 3;
    public bool ScoreNotificationsEnabled { get; init; } = true;
    public bool RankingProgressNotificationsEnabled { get; init; } = true;
    public bool GeoIpEnabled { get; init; }
    public string GeoIpCountryCsvPath { get; init; } = "";
    public bool PublicRankReplies { get; init; } = true;
    public bool BareChatCommandsEnabled { get; init; } = true;
    public string[] EnabledCommands { get; init; } = Finestats.Services.StatsCommandClient.Names.ToArray();
    public int CommandPageSize { get; init; } = 5;
    public int CommandMaxLines { get; init; } = 7;
    public int CommandMaxConcurrentRequests { get; init; } = 4;
    public int CommandTimeoutSeconds { get; init; } = 5;
    public string ChatPrefix { get; init; } = "[blue][fs][/] ";
    public int ChatMaxBytes { get; init; } = 230;
    public int DisplayNameMaxLength { get; init; } = 48;
    public int NumberDecimalPlaces { get; init; } = 2;
    public int ScoreNotificationMaxAgeSeconds { get; init; } = 30;
    public int ScoreNotificationQueueMaxAgeSeconds { get; init; } = 5;
    public int ScoreNotificationMaxPerBatch { get; init; } = 8;
    public string[] ScoreNotificationReasons { get; init; } = ["kill", "death", "assist", "teamkill", "suicide", "bomb_planted", "bomb_defused", "hostage_rescued", "mvp"];
    public Dictionary<string, string> Messages { get; init; } = DefaultMessages();

    public static Dictionary<string, string> DefaultMessages()
    {
        var messages = ChatMessages.Defaults();
        messages["Timeout"] = "Statistics request cancelled or the local database took too long to respond.";
        messages["NoObservations"] = "No local statistics recorded yet. Please try again later.";
        messages["NoSession"] = "The current session has not been saved locally yet.";
        messages["SessionUnavailable"] = "Session counters are unavailable.";
        messages["ScopeAll"] = "This server";
        return messages;
    }

    public static StatsConfig Read(string path)
    {
        var config = JsonSerializer.Deserialize<StatsConfig>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? throw new InvalidDataException("Config must contain an object.");
        ChatMessages.Validate(config.Messages);
        var messages = DefaultMessages();
        foreach (var pair in config.Messages)
            messages[pair.Key] = pair.Value;
        return config with
        {
            Messages = messages
        };
    }

    public void Validate()
    {
        Range(ConnectMessageDelaySeconds, 0, 15, nameof(ConnectMessageDelaySeconds));
        if (ConnectionMessages is null)
            throw new InvalidDataException("ConnectionMessages is required.");
        ConnectionMessages.Validate();
        if (string.IsNullOrWhiteSpace(DatabaseFile) || Path.GetFileName(DatabaseFile) != DatabaseFile || DatabaseFile.IndexOfAny(['/', '\\', ':']) >= 0 || DatabaseFile is "." or "..")
            throw new InvalidDataException("DatabaseFile must be a filename inside plugin data directory.");
        Range(DatabaseBusyTimeoutSeconds, 1, 10, nameof(DatabaseBusyTimeoutSeconds));
        Range(BackupIntervalMinutes, 0, 10080, nameof(BackupIntervalMinutes));
        Range(BackupKeepCount, 1, 100, nameof(BackupKeepCount));
        if (Ranking is null)
            throw new InvalidDataException("Ranking is required.");
        Ranking.Validate();
        Range(CommandPageSize, 1, 10, nameof(CommandPageSize));
        Range(CommandMaxLines, Math.Max(5, CommandPageSize + 2), 20, nameof(CommandMaxLines));
        Range(CommandMaxConcurrentRequests, 1, 16, nameof(CommandMaxConcurrentRequests));
        Range(CommandTimeoutSeconds, 1, 30, nameof(CommandTimeoutSeconds));
        Range(ChatMaxBytes, 64, 230, nameof(ChatMaxBytes));
        Range(DisplayNameMaxLength, 1, 128, nameof(DisplayNameMaxLength));
        Range(NumberDecimalPlaces, 0, 6, nameof(NumberDecimalPlaces));
        Range(ScoreNotificationMaxAgeSeconds, 1, 120, nameof(ScoreNotificationMaxAgeSeconds));
        Range(ScoreNotificationQueueMaxAgeSeconds, 1, 10, nameof(ScoreNotificationQueueMaxAgeSeconds));
        Range(ScoreNotificationMaxPerBatch, 1, 20, nameof(ScoreNotificationMaxPerBatch));
        if (ChatPrefix is null || ChatPrefix.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(ChatPrefix) > 48)
            throw new InvalidDataException("ChatPrefix must be plain text up to 48 UTF-8 bytes.");
        if (EnabledCommands is null || EnabledCommands.Distinct().Count() != EnabledCommands.Length || EnabledCommands.Any(x => !Finestats.Services.StatsCommandClient.Names.Contains(x)))
            throw new InvalidDataException("EnabledCommands contains an unknown or duplicate command.");
        var reasons = new[]
        {
            "kill",
            "death",
            "assist",
            "teamkill",
            "suicide",
            "bomb_planted",
            "bomb_defused",
            "hostage_rescued",
            "mvp"
        };
        if (ScoreNotificationReasons is null || ScoreNotificationReasons.Any(x => !reasons.Contains(x)) || ScoreNotificationReasons.Distinct().Count() != ScoreNotificationReasons.Length)
            throw new InvalidDataException("ScoreNotificationReasons contains an unknown or duplicate reason.");
        ChatMessages.Validate(Messages);
        if (GeoIpEnabled && (string.IsNullOrWhiteSpace(GeoIpCountryCsvPath) || !Path.IsPathFullyQualified(GeoIpCountryCsvPath)))
            throw new InvalidDataException("GeoIpCountryCsvPath must be an absolute local CSV or CSV.gz path when enabled.");
        if (string.IsNullOrWhiteSpace(ServerId) || ServerId.Length > 128)
            throw new InvalidDataException("ServerId must contain 1–128 characters.");
        Range(QueueCapacity, 1, 100000, nameof(QueueCapacity));
        Range(BatchSize, 1, Math.Min(QueueCapacity, 1000), nameof(BatchSize));
        Range(FlushIntervalMs, 100, 60000, nameof(FlushIntervalMs));
        Range(RetryCount, 0, 8, nameof(RetryCount));
        Range(ShutdownDrainMs, 0, 30000, nameof(ShutdownDrainMs));
        Range(WarningIntervalSeconds, 5, 300, nameof(WarningIntervalSeconds));
        Range(CommandCooldownSeconds, 1, 30, nameof(CommandCooldownSeconds));
    }

    private static void Range(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new InvalidDataException($"{name} must be between {min} and {max}.");
    }
}
