using Finestats.Collectors;
using Finestats.Config;
using Finestats.Storage;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;

namespace Finestats.Services;

// Native state is accessed only on the game thread. Delayed reads carry immutable
// identity values and recheck both native and collector sessions before delivery.
public sealed class ConnectChat(ISwiftlyCore core, PlayerCollector players, LiteStore store, StatsConfig config) : IDisposable
{
    private readonly ConnectionGate _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    public void Subscribe()
    {
        core.Event.OnClientPutInServer += Ready;
        core.Event.OnClientSteamAuthorize += Authorized;
        core.Event.OnClientDisconnected += Disconnected;
    }

    private void Ready(IOnClientPutInServerEvent e)
    {
        if (!players.World.IsReady) return;
        var p = core.PlayerManager.GetPlayer(e.PlayerId);
        if (p is null || p.IsFakeClient)
            return;
        _gate.Ready(e.PlayerId, p.SessionId);
        Begin(e.PlayerId);
    }

    private void Authorized(IOnClientSteamAuthorizeEvent e) => Begin(e.PlayerId);
    private void Disconnected(IOnClientDisconnectedEvent e) => _gate.Remove(e.PlayerId);
    public void ResetForMap() => _gate.Clear();
    private void Begin(int slot)
    {
        if (!players.World.IsReady || Volatile.Read(ref _disposed) != 0)
            return;
        var p = core.PlayerManager.GetPlayer(slot);
        if (p is null || p.IsFakeClient || !p.IsAuthorized)
            return;
        var identity = players.Resolve(p);
        if (identity is not { Authenticated: true, Steamid: not null })
            return;
        ulong native = p.SessionId;
        if (!_gate.TryBegin(slot, native, true))
            return;
        long mapGeneration = players.World.Generation;
        var token = _stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(config.ConnectMessageDelaySeconds), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Resolve again after the delay so a country database that loaded during
                // connection can contribute this session's country (never a historical IP).
                core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!players.World.IsCurrent(mapGeneration) || Volatile.Read(ref _disposed) != 0)
                        return;
                    var current = core.PlayerManager.GetPlayer(slot);
                    if (current is null || current.SessionId != native || !current.IsAuthorized)
                        return;
                    var snapshot = players.Resolve(current);
                    if (snapshot?.SessionId != identity.SessionId || snapshot.Steamid != identity.Steamid)
                        return;
                    _ = ReadAndDeliver(snapshot.Name ?? "—", snapshot.Geoip?.CountryCode);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        });
        async Task ReadAndDeliver(string name, string? country)
        {
            try
            {
                var message = await Task.Run(async () =>
                {
                    var profile = await store.ReadAsync("players/" + Uri.EscapeDataString(identity.Steamid), token).ConfigureAwait(false);
                    long? rank = profile is { } row && row.TryGetProperty("rank", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
                    return config.ConnectionMessages.Format(name, country, rank, config);
                }, token).ConfigureAwait(false);
                if (message.Length == 0 || token.IsCancellationRequested)
                    return;
                core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!players.World.IsCurrent(mapGeneration) || Volatile.Read(ref _disposed) != 0)
                        return;
                    var current = core.PlayerManager.GetPlayer(slot);
                    if (current is null || !current.IsAuthorized || current.SessionId != native)
                        return;
                    var snapshot = players.Resolve(current);
                    if (snapshot?.SessionId != identity.SessionId || snapshot.Steamid != identity.Steamid)
                        return;
                    var line = StatsCommandClient.ChatLine(message, config);
                    if (config.PublicConnectMessages)
                        core.PlayerManager.SendChat(line);
                    else
                        current.SendChat(line);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        core.Event.OnClientPutInServer -= Ready;
        core.Event.OnClientSteamAuthorize -= Authorized;
        core.Event.OnClientDisconnected -= Disconnected;
        _stop.Cancel();
        _gate.Clear();
    }
}
