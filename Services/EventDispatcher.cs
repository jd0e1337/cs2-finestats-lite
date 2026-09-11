using Finestats.Config;
using Finestats.Events;
using System.Threading.Channels;

namespace Finestats.Services;

public sealed class EventDispatcher
{
    private readonly EventQueue _queue;
    private readonly SqliteStatsClient _client;
    private readonly StatsConfig _config;
    private readonly Diagnostics _log;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private int _stopping;
    private long _sent;
    private long _failed;
    public long Sent => Interlocked.Read(ref _sent);
    public long Failed => Interlocked.Read(ref _failed);
    public Task Completion => _worker ?? Task.CompletedTask;

    public EventDispatcher(EventQueue queue, SqliteStatsClient client, StatsConfig config, Diagnostics log) => (_queue, _client, _config, _log) = (queue, client, config, log);
    public void Start()
    {
        if (_worker is not null)
            throw new InvalidOperationException("Dispatcher already started.");
        _worker = Task.Run(RunAsync);
    }

    // Unload is synchronous in SwiftlyS2. Complete/cancel without blocking its game thread.
    public Task RequestStop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _queue.Complete();
            try
            {
                _stop.CancelAfter(_config.ShutdownDrainMs);
            }
            catch (ObjectDisposedException)
            { /* Worker already terminated and released its resources. */
            }
        }

        return Completion;
    }

    private async Task RunAsync()
    {
        var batch = new List<StatsEvent>(_config.BatchSize);
        try
        {
            var reader = _queue.Reader;
            while (await reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                await CollectBatchAsync(reader, batch).ConfigureAwait(false);
                if (batch.Count == 0)
                    continue;
                var result = await _client.SendAsync(batch.ToArray(), _stop.Token).ConfigureAwait(false);
                if (result.Accepted)
                    Interlocked.Add(ref _sent, batch.Count);
                else
                {
                    Interlocked.Add(ref _failed, batch.Count);
                    _log.Warn($"finestats: dropped {batch.Count} events after {result.Attempts} SQLite attempts (status={result.StatusCode?.ToString() ?? "storage"}); failed total={Failed}, queue drops={_queue.Dropped}.");
                }

                if (_queue.Dropped > 0)
                    _log.Warn($"finestats: bounded queue dropped {_queue.Dropped} events in total; depth={_queue.Count}, sent={Sent}, failed={Failed}.");
                if (_config.LogDebugEvents)
                    _log.Sample($"finestats: batch={batch.Count}, sample type={batch[0].EventType}, sent={Sent}, failed={Failed}, queue drops={_queue.Dropped}.");
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not log exception messages: Database exceptions can include paths or user data.
            _log.Warn($"finestats: dispatcher stopped unexpectedly ({ex.GetType().Name}); pending events are counted as lost.");
        }
        finally
        {
            _queue.Complete();
            Interlocked.Add(ref _failed, batch.Count);
            while (_queue.Reader.TryRead(out _))
                Interlocked.Increment(ref _failed);
            _client.Dispose();
            _stop.Dispose();
        }
    }

    private async Task CollectBatchAsync(ChannelReader<StatsEvent> reader, List<StatsEvent> batch)
    {
        // Start one deadline per batch. Resetting it for every event could delay
        // writes indefinitely while events keep arriving below the batch limit.
        using var flushDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        flushDeadline.CancelAfter(_config.FlushIntervalMs);
        do
        {
            while (batch.Count < _config.BatchSize && reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count >= _config.BatchSize)
            {
                break;
            }

            try
            {
                if (!await reader.WaitToReadAsync(flushDeadline.Token).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
            {
                // A flush timeout writes the partial batch; a shutdown timeout
                // propagates to RunAsync, which accounts for unsaved events.
                break;
            }
        }
        while (!flushDeadline.IsCancellationRequested);
    }
}
