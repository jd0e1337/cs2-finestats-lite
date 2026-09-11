using Finestats.Events;
using Finestats.Services;
using SwiftlyS2.Shared.GameEventDefinitions;

namespace Finestats.Collectors;

public sealed class ObjectiveCollector(CollectionContext context, PlayerCollector players)
{
    public void Subscribe(GameEventSubscriptions hooks)
    {
        hooks.Post<EventBombPlanted>(e => context.Emit("bomb_planted", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site, e.C4)));
        hooks.Post<EventBombDefused>(e => context.Emit("bomb_defused", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site, e.C4)));
        hooks.Post<EventBombExploded>(e => context.Emit("bomb_exploded", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site, e.C4)));
        hooks.Post<EventBombDropped>(e => context.Emit("bomb_dropped", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), EntityIndex: e.EntIndex)));
        hooks.Post<EventBombPickup>(e => context.Emit("bomb_pickup", new ObjectiveEvent(players.Resolve(e.UserIdPlayer))));
        hooks.Post<EventBombBeginplant>(e => context.Emit("bomb_beginplant", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site)));
        hooks.Post<EventBombAbortplant>(e => context.Emit("bomb_abortplant", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site)));
        hooks.Post<EventBombBegindefuse>(e => context.Emit("bomb_begindefuse", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), HasKit: e.HasKit)));
        hooks.Post<EventBombAbortdefuse>(e => context.Emit("bomb_abortdefuse", new ObjectiveEvent(players.Resolve(e.UserIdPlayer))));
        hooks.Post<EventHostageRescued>(e => context.Emit("hostage_rescued", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), e.Site, e.Hostage)));
        hooks.Post<EventHostageFollows>(e => context.Emit("hostage_follows", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), EntityIndex: e.Hostage)));
        hooks.Post<EventHostageStopsFollowing>(e => context.Emit("hostage_stops_following", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), EntityIndex: e.Hostage)));
        hooks.Post<EventHostageHurt>(e => context.Emit("hostage_hurt", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), EntityIndex: e.Hostage)));
        hooks.Post<EventHostageKilled>(e => context.Emit("hostage_killed", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), EntityIndex: e.Hostage)));
        hooks.Post<EventHostageRescuedAll>(_ => context.Emit("hostage_rescued_all", new ObjectiveEvent(null)));
        hooks.Post<EventRoundMvp>(e => context.Emit("mvp", new ObjectiveEvent(players.Resolve(e.UserIdPlayer), Reason: e.Reason, Value: e.Value)));
    }
}
