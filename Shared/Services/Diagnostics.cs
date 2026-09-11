namespace Finestats.Services;

public sealed class Diagnostics(Action<string>? warning, Action<string>? debug, int intervalSeconds)
{
    private readonly object _gate = new();
    private long _nextWarning;
    private long _nextDebug;
    private Action<string>? _warning = warning;
    private Action<string>? _debug = debug;
    private long _collectorErrors;
    public long CollectorErrors => Interlocked.Read(ref _collectorErrors);

    public void CollectorError(string collector)
    {
        Interlocked.Increment(ref _collectorErrors);
        Warn($"finestats: {collector} observation failed; collector errors total={CollectorErrors}. Native values were not queued.");
    }

    public void Warn(string message)
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (now < _nextWarning) return;
            _nextWarning = now + intervalSeconds * 1000L;
            _warning?.Invoke(message);
        }
    }

    public void Sample(string message)
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (now < _nextDebug) return;
            _nextDebug = now + intervalSeconds * 1000L;
            _debug?.Invoke(message);
        }
    }

    // After Unload returns, the background task must not touch plugin-owned logging services.
    public void Detach() { lock (_gate) { _warning = null; _debug = null; } }
}
