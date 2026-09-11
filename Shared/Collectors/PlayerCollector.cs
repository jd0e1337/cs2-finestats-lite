using Finestats.Events;
using Finestats.Helpers;
using Finestats.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Players;

namespace Finestats.Collectors;

public sealed class PlayerCollector(ISwiftlyCore core, CollectionContext context, Diagnostics log, Func<CountryLookup?>? countryLookup = null) : IDisposable
{
    private readonly SessionRegistry _sessions = new();

    public void Subscribe(GameEventSubscriptions hooks)
    {
        core.Event.OnClientConnected += Connected;
        core.Event.OnClientPutInServer += PutInServer;
        core.Event.OnClientSteamAuthorize += Authorized;
        core.Event.OnClientDisconnected += Disconnected;
        hooks.Post<EventPlayerTeam>(e =>
        {
            var player = Resolve(e.UserIdPlayer);
            if (player is null) return;
            var state = _sessions.Find(player.Slot)!;
            state.Player = player with { Team = e.Team };
            Emit("player_team", state, e.Disconnect ? "disconnect" : "team_change", oldTeam: e.OldTeam, newTeam: e.Team);
        });
        hooks.Post<EventPlayerChangename>(e =>
        {
            var player = Resolve(e.UserIdPlayer);
            if (player is null) return;
            var state = _sessions.Find(player.Slot)!;
            state.Player = player with { Name = WeaponHelper.Limit(e.NewName, 128) };
            Emit("player_name", state, "name_change", oldName: WeaponHelper.Limit(e.OldName, 128));
        });
    }

    private void Guard(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { log.CollectorError(name); }
    }

    private bool ValidSlot(int slot) => slot >= 0 && slot < core.PlayerManager.PlayerCap;

    private void Connected(IOnClientConnectedEvent e) => Guard("connect", () =>
    {
        if (!ValidSlot(e.PlayerId)) return;
        End(e.PlayerId, "slot_reused");
        var state = _sessions.Start(e.PlayerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
        Apply(state, PlayerHelper.Observe(core.PlayerManager.GetPlayer(e.PlayerId)));
        Emit("player_connect", state, "connect_attempt");
        Emit("session_start", state, "connect_attempt");
        ReportAuthentication(state);
    });

    private void PutInServer(IOnClientPutInServerEvent e) => Guard("put_in_server", () =>
    {
        var player = ResolveSlot(e.PlayerId);
        if (player is not null) Emit("player_ready", _sessions.Find(e.PlayerId)!, "put_in_server");
    });

    private void Authorized(IOnClientSteamAuthorizeEvent e) => Guard("steam_authorize", () => ResolveSlot(e.PlayerId));

    private void Disconnected(IOnClientDisconnectedEvent e) => Guard("disconnect", () =>
    {
        // Prefer the last managed snapshot if teardown has already invalidated native state.
        var state = _sessions.Find(e.PlayerId);
        if (state is null) return;
        var observed = PlayerHelper.Observe(core.PlayerManager.GetPlayer(e.PlayerId));
        if (observed is not null && state.NativeSessionId == observed.NativeSessionId) Apply(state, observed);
        Emit("player_disconnect", state, "disconnect", (int)e.Reason);
        End(e.PlayerId, "disconnect", (int)e.Reason);
    });

    public PlayerIdentity? ResolveSlot(int slot) => ValidSlot(slot) ? Resolve(core.PlayerManager.GetPlayer(slot)) : null;

    public PlayerIdentity? Resolve(IPlayer? player)
    {
        var observed = PlayerHelper.Observe(player);
        if (observed is null || !ValidSlot(observed.Slot)) return null;
        var state = _sessions.Find(observed.Slot);
        if (state?.NativeSessionId is ulong previous && previous != observed.NativeSessionId)
        {
            End(observed.Slot, "slot_reused");
            state = null;
        }
        bool created = state is null;
        state ??= _sessions.Start(observed.Slot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
        Apply(state, observed);
        ObserveCountry(state, player);
        if (created) Emit("session_start", state, "observed_mid_session");
        ReportAuthentication(state);
        return state.Player;
    }

    private static void Apply(SessionState state, ObservedPlayer? observed)
    {
        if (observed is null) return;
        state.NativeSessionId = observed.NativeSessionId;
        state.Player = new(observed.Slot, state.Player.SessionId, observed.Steamid, observed.Name,
            observed.Team, observed.IsBot, observed.Authenticated, state.Player.Geoip);
    }

    private void ObserveCountry(SessionState state, IPlayer? player)
    {
        if (state.CountryChecked || player is null || !state.Player.Authenticated || countryLookup?.Invoke() is not CountryLookup lookup) return;
        var address = player.IPAddress;
        if (string.IsNullOrWhiteSpace(address)) return;
        state.CountryChecked = true;
        if (lookup.Find(address) is not string country) return;
        state.Player = state.Player with { Geoip = new CountryObservation(1, country) };
        Emit("player_country", state, "local_country_lookup");
    }

    private void ReportAuthentication(SessionState state)
    {
        if (!state.Player.Authenticated || state.AuthReported) return;
        state.AuthReported = true;
        Emit("player_authenticated", state, "steam_authorized");
    }

    public void Bootstrap()
    {
        foreach (var player in core.PlayerManager.GetAllPlayers()) Resolve(player);
    }

    private void End(int slot, string reason, int? disconnectReason = null)
    {
        var state = _sessions.Find(slot);
        if (state is null) return;
        Emit("session_end", state, reason, disconnectReason);
        _sessions.Remove(slot);
    }

    public void EndAll(string reason)
    {
        foreach (var state in _sessions.Values) Emit("session_end", state, reason);
        _sessions.Clear();
    }

    private void Emit(string type, SessionState state, string reason, int? disconnectReason = null,
        int? oldTeam = null, int? newTeam = null, string? oldName = null)
        => context.Emit(type, new PlayerEvent(state.Player, state.StartedAt, reason, state.Partial,
            disconnectReason, oldTeam, newTeam, oldName));

    public void Dispose()
    {
        core.Event.OnClientConnected -= Connected;
        core.Event.OnClientPutInServer -= PutInServer;
        core.Event.OnClientSteamAuthorize -= Authorized;
        core.Event.OnClientDisconnected -= Disconnected;
    }
}
