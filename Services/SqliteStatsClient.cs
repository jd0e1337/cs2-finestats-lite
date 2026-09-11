using System.Text.Json;
using Finestats.Config;
using Finestats.Events;
using Finestats.Storage;
using Microsoft.Data.Sqlite;

namespace Finestats.Services;

public readonly record struct SendResult(bool Accepted, int Attempts, int? StatusCode);
public sealed class SqliteStatsClient(StatsConfig config, LiteStore store, Action<ScoreReceipt[]>? notices = null, Action<string>? warning = null) : IDisposable
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private readonly ScoreReceiptFilter _filter = new(config);
    public async Task<SendResult> SendAsync(StatsEvent[] batch, CancellationToken ct)
    {
        // RetryCount counts retries in addition to the initial write attempt.
        int maximumAttempts = config.RetryCount + 1;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                var receipts = await store.AppendAsync(batch, ct).ConfigureAwait(false);
                DeliverCommittedNotices(receipts, batch);
                return new SendResult(true, attempt, null);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked && attempt <= config.RetryCount)
            {
                int delayMilliseconds = Math.Min(1000, attempt * 100);
                await Task.Delay(delayMilliseconds, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new SendResult(false, attempt, null);
            }
        }

        return new SendResult(false, maximumAttempts, null);
    }

    private void DeliverCommittedNotices(ScoreReceipt[] receipts, StatsEvent[] batch)
    {
        // The transaction has committed. Optional chat/backup notifications must
        // never turn this success into another database write attempt.
        try
        {
            if (store.BackupError is string error)
            {
                warning?.Invoke("finestats-lite: automatic backup failed (" + error + "). Committed statistics are intact.");
            }

            // The shared receipt filter expects the full plugin's JSON envelope.
            var envelope = JsonSerializer.SerializeToElement(new { scoreNotices = receipts });
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var filteredReceipts = _filter.Read(envelope, batch, now);
            if (filteredReceipts.Length > 0)
            {
                notices?.Invoke(filteredReceipts);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        // Statistics remain saved even if an optional notification fails.
        }
    }

    public void Dispose()
    {
    }
}
