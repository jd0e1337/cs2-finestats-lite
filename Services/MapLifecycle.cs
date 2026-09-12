namespace Finestats.Services;

// Transitions and native work run on the game thread. Background work may only
// capture/read the generation, never activate a map or access native objects.
public sealed class MapLifecycle
{
    private long _generation;
    private int _ready;
    public bool IsReady => Volatile.Read(ref _ready) != 0;
    public long Generation => Interlocked.Read(ref _generation);

    public long Suspend()
    {
        Volatile.Write(ref _ready, 0);
        return Interlocked.Increment(ref _generation);
    }

    public void Activate(long generation)
    {
        if (generation == Generation) Volatile.Write(ref _ready, 1);
    }

    public bool IsCurrent(long generation) => IsReady && generation == Generation;
}
