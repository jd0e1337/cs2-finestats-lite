using Finestats.Collectors;
using Finestats.Config;
using SwiftlyS2.Shared;

namespace Finestats.Services;

public sealed class ScoreChat(ISwiftlyCore core, PlayerCollector players, Guid collectorId, StatsConfig config) : IDisposable
{
    private int _disposed;
    public void Deliver(ScoreReceipt[] receipts)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        long scheduledAt = Environment.TickCount64;
        core.Scheduler.NextWorldUpdate(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || Environment.TickCount64 - scheduledAt > config.ScoreNotificationQueueMaxAgeSeconds * 1000L) return;
            foreach (var player in core.PlayerManager.GetAllPlayers())
            {
                try
                {
                    if (player.IsFakeClient || !player.IsAuthorized) continue;
                    var identity = players.Resolve(player);
                    if (identity is null || !identity.Authenticated) continue;
                    var messages = receipts.Where(r => r.CollectorId == collectorId && r.Steamid == identity.Steamid && r.SessionId == identity.SessionId)
                        .SelectMany(r => new[] { ScoreReceiptFilter.ProgressMessage(r, config), ScoreReceiptFilter.Message(r, config) })
                        .Where(message => !string.IsNullOrEmpty(message)).Take(config.ScoreNotificationMaxPerBatch);
                    foreach (var message in messages)
                    {
                        player.SendChat(StatsCommandClient.ChatLine(message, config));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        });
    }
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
