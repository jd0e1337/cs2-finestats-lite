using System.Collections.Concurrent;
using System.Text.Json;
using Finestats.Collectors;
using Finestats.Config;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;

namespace Finestats.Services;

public sealed class StatsChatCommands : IDisposable
{
    private readonly ISwiftlyCore _core;
    private readonly PlayerCollector _players;
    private readonly CollectionContext _context;
    private readonly StatsCommandClient _client;
    private readonly int _cooldown;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _requests;
    private readonly StatsConfig _config;
    private readonly ChatMessages _messages;
    private readonly List<Guid> _commands = [];
    private readonly HashSet<string> _owned = [];
    private readonly ConcurrentDictionary<int, long> _pending = new();
    private readonly Dictionary<int, (ulong Session, long At)> _last = [];
    private Guid? _chatHook;
    private long _requestId;
    private int _disposed;
    public StatsChatCommands(ISwiftlyCore core, PlayerCollector players, CollectionContext context, StatsConfig config, Finestats.Storage.LiteStore store)
    {
        _core = core;
        _players = players;
        _context = context;
        _client = new(config, store);
        _cooldown = config.CommandCooldownSeconds;
        _config = config;
        _messages = new(config);
        _requests = new(config.CommandMaxConcurrentRequests);
    }

    public void Register()
    {
        foreach (var name in _config.EnabledCommands)
        {
            if (_core.Command.IsCommandRegistered("sw_" + name))
                continue;
            _commands.Add(_core.Command.RegisterCommand(name, c =>
            {
                if (!_context.World.IsReady) return;
                if (!c.IsSentByPlayer || c.Sender is null)
                {
                    var message = _messages.Get("PlayerOnly");
                    if (message.Length > 0)
                        c.Reply(StatsCommandClient.ChatLine(message, _config));
                    return;
                }

                Begin(c.Sender.PlayerID, name, c.Args);
            }));
            _owned.Add(name);
        }

        // Plain HLstatsX-style chat words; prefixed commands are handled by SwiftlyS2.
        if (!_config.BareChatCommandsEnabled)
            return;
        _chatHook = _core.Command.HookClientChat((slot, text, teamOnly) =>
        {
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !_owned.Contains(parts[0].ToLowerInvariant()))
                return HookResult.Continue;
            Begin(slot, parts[0].ToLowerInvariant(), parts.Skip(1).ToArray());
            return HookResult.Stop;
        });
    }

    private void Begin(int slot, string command, string[] args)
    {
        if (!_context.World.IsReady || Volatile.Read(ref _disposed) != 0 || slot is < 0 or > 255)
            return;
        var player = _core.PlayerManager.GetPlayer(slot);
        if (player is null || player.IsFakeClient)
            return;
        var identity = _players.Resolve(player);
        if (identity?.Steamid is not string steam || !identity.Authenticated)
        {
            SendMessage(player, "AuthenticationPending");
            return;
        }

        ulong native = player.SessionId;
        long now = Environment.TickCount64;
        if (_pending.ContainsKey(slot) || (_last.TryGetValue(slot, out var last) && last.Session == native && now - last.At < _cooldown * 1000))
        {
            SendMessage(player, "Cooldown");
            return;
        }

        if (!_requests.Wait(0))
        {
            SendMessage(player, "Busy");
            return;
        }

        long request = Interlocked.Increment(ref _requestId);
        _pending[slot] = request;
        _last[slot] = (native, now);
        var snapshot = new CommandPlayer(steam, identity.SessionId, _context.CollectorId);
        long mapGeneration = _context.World.Generation;
        var arguments = args.ToArray();
        // No IPlayer/entity or ICommandContext leaves this callback.
        _ = Task.Run(async () =>
        {
            string[] lines;
            bool publicReply = false;
            try
            {
                lines = await _client.Execute(command, arguments, snapshot, _stop.Token, () => publicReply = true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                publicReply = false;
                lines = [_messages.Get("Timeout")];
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                publicReply = false;
                lines = [_messages.Get("Unavailable")];
            }
            finally
            {
                _requests.Release();
            }

            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    try
                    {
                        if (!_context.World.IsCurrent(mapGeneration) || Volatile.Read(ref _disposed) != 0 || !_pending.TryGetValue(slot, out var active) || active != request)
                            return;
                        _pending.TryRemove(slot, out _);
                        var current = _core.PlayerManager.GetPlayer(slot);
                        // Slot reuse, reconnect, map/session changes and hot reload must not receive old replies.
                        if (current is null || current.SessionId != native || !current.IsAuthorized || current.SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture) != steam || _players.Resolve(current)?.SessionId != snapshot.SessionId)
                            return;
                        foreach (var line in lines.Where(line => !string.IsNullOrEmpty(line)).Take(_config.CommandMaxLines))
                            if (publicReply)
                                _core.PlayerManager.SendChat(StatsCommandClient.ChatLine(line, _config));
                            else
                                current.SendChat(StatsCommandClient.ChatLine(line, _config));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        _pending.TryRemove(slot, out _);
                    }
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _pending.TryRemove(slot, out _);
            }
        });
    }

    private void SendMessage(SwiftlyS2.Shared.Players.IPlayer player, string key)
    {
        var message = _messages.Get(key);
        if (message.Length > 0)
            player.SendChat(StatsCommandClient.ChatLine(message, _config));
    }

    public void ResetForMap()
    {
        _pending.Clear();
        _last.Clear();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        if (_chatHook is Guid hook)
            _core.Command.UnhookClientChat(hook);
        foreach (var command in _commands)
            _core.Command.UnregisterCommand(command);
        _commands.Clear();
        _owned.Clear();
        _pending.Clear();
        _last.Clear();
        _client.Dispose();
    }
}
