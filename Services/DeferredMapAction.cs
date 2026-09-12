namespace Finestats.Services;

// Used on the game thread. Invalidating a pending action avoids touching players
// from an old map when another transition or plugin unload happens first.
public sealed class DeferredMapAction
{
    private long _generation;

    public void Schedule(Action<Action> nextWorldUpdate, Action action)
    {
        long generation = ++_generation;
        nextWorldUpdate(() =>
        {
            if (generation != _generation) return;
            ++_generation;
            action();
        });
    }

    public void Cancel() => ++_generation;
}
