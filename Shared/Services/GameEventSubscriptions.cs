using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace Finestats.Services;

public sealed class GameEventSubscriptions(ISwiftlyCore core, Diagnostics log) : IDisposable
{
    private readonly List<Guid> _hooks = [];
    public void Post<T>(Action<T> handler) where T : IGameEvent<T>
    {
        _hooks.Add(core.GameEvent.HookPost<T>(value =>
        {
            try { handler(value); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { log.CollectorError(typeof(T).Name); }
            return HookResult.Continue;
        }));
    }

    public void Dispose()
    {
        foreach (var hook in _hooks) core.GameEvent.Unhook(hook);
        _hooks.Clear();
    }
}
