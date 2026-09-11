namespace Finestats.Services;

// Game-thread-only lifecycle gate. Authentication and ready can arrive in either
// order; only a ready, authorized native session may announce once.
public sealed class ConnectionGate
{
    private readonly Dictionary<int, ulong> _ready = [];
    private readonly Dictionary<int, ulong> _started = [];
    public void Ready(int slot, ulong native) => _ready[slot] = native;
    public bool TryBegin(int slot, ulong native, bool authorized)
    {
        if (!authorized || !_ready.TryGetValue(slot, out var ready) || ready != native || (_started.TryGetValue(slot, out var started) && started == native))
            return false;
        _started[slot] = native;
        return true;
    }

    public void Remove(int slot)
    {
        _ready.Remove(slot);
        _started.Remove(slot);
    }

    public void Clear()
    {
        _ready.Clear();
        _started.Clear();
    }
}
