using Finestats.Events;
using Finestats.Services;

static class NativeLifecycleChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = new MapLifecycle();
        check(!world.IsReady, "native access starts disabled");
        long firstMap = world.Suspend();
        world.Activate(firstMap);
        check(world.IsCurrent(firstMap), "world update enables the current map");
        var callbacks = new Queue<Action>();
        int nativeCalls = 0;
        foreach (var kind in new[] { "score", "connect", "command" })
        {
            callbacks.Enqueue(() => { if (world.IsCurrent(firstMap)) nativeCalls++; });
        }
        long secondMap = world.Suspend();
        while (callbacks.TryDequeue(out var callback)) callback();
        check(nativeCalls == 0, "queued chat work never accesses native objects after map unload");
        world.Activate(firstMap);
        check(!world.IsReady, "old world update cannot activate a newer map");
        world.Activate(secondMap);
        check(world.IsReady && !world.IsCurrent(firstMap), "old chat work stays invalid after new map activation");
        world.Suspend();
        world.Activate(secondMap);
        check(!world.IsReady, "plugin unload invalidates pending activation");

        // A null core deliberately makes any accidental engine/GameRules access
        // fail. Exercise the real emitter throughout map load/unload, not a mock.
        var queue = new EventQueue(32);
        var context = new CollectionContext(null!, "test", queue, new Diagnostics(_ => { }, null, 30));
        var player = new PlayerIdentity(1, "session", "76561198000000001", "Player", 2, false, true);
        foreach (bool ready in new[] { false, true })
        {
            long generation = context.World.Suspend();
            context.SetMap(ready ? "de_mirage" : null);
            if (ready) context.World.Activate(generation);
            foreach (string type in new[] { "collector_start", "map_start", "map_end", "collector_stop" })
            {
                context.Emit(type, new LifecycleEvent(type));
                check(queue.Reader.TryRead(out var value) && value.Tick is null,
                    $"{type} with ready={ready} never reads native engine state");
            }
            foreach (string type in new[] { "session_start", "session_end", "player_disconnect" })
            {
                context.Emit(type, new PlayerEvent(player, 1, type));
                check(queue.Reader.TryRead(out var value) && value.Tick is null,
                    $"{type} with ready={ready} only uses managed snapshots");
            }
        }
    }
}
