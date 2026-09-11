using Finestats.Collectors;
using Finestats.Config;
using Finestats.Events;
using Finestats.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Plugins;

namespace Finestats;

[PluginMetadata(Id = "finestats-lite", Version = "1.0.5", Name = "finestats-lite", Author = "finestats-lite", Description = "Standalone SQLite CS2 statistics")]
public sealed class FinestatsPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private Diagnostics? _log;
    private CollectionContext? _context;
    private PlayerCollector? _players;
    private GameEventSubscriptions? _hooks;
    private EventDispatcher? _dispatcher;
    private EventQueue? _queue;
    private bool _mapSubscribed;
    private StatsChatCommands? _chatCommands;
    private ScoreChat? _scoreChat;
    private ConnectChat? _connectChat;
    private CancellationTokenSource? _countryStop;
    public override void Load(bool hotReload)
    {
        try
        {
            Core.Configuration.InitializeWithTemplate("config.jsonc", "config.jsonc");
            var config = StatsConfig.Read(Core.Configuration.GetConfigPath("config.jsonc"));
            config.Validate();
            if (!config.Enabled)
            {
                Core.Logger.LogInformation("finestats is disabled in config.");
                return;
            }

            _log = new Diagnostics(message => Core.Logger.LogWarning("{Message}", message), config.LogDebugEvents ? message => Core.Logger.LogInformation("{Message}", message) : null, config.WarningIntervalSeconds);
            _queue = new EventQueue(config.QueueCapacity);
            _context = new CollectionContext(Core, config.ServerId, _queue, _log);
            // A hot reload cannot reconstruct a round/session start it never observed.
            try
            {
                _context.SetMap(Core.Engine.GlobalVars.MapName.Value);
            }
            catch (InvalidOperationException)
            {
                _context.SetMap(null);
            }

            _hooks = new GameEventSubscriptions(Core, _log);
            Task<CountryLookup?>? countryTask = null;
            _players = new PlayerCollector(Core, _context, _log, () => countryTask is { IsCompletedSuccessfully: true } ? countryTask.Result : null);
            if (config.GeoIpEnabled)
            {
                _countryStop = new CancellationTokenSource();
                var token = _countryStop.Token;
                var log = _log;
                countryTask = Task.Run<CountryLookup?>(() =>
                {
                    try
                    {
                        return CountryLookup.Load(config.GeoIpCountryCsvPath, token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        log.Warn("GeoIP disabled: local country database could not be loaded. Check GeoIpCountryCsvPath and CSV format.");
                    }

                    return null;
                });
            }

            var store = new Finestats.Storage.LiteStore(Path.Combine(Core.PluginDataDirectory, config.DatabaseFile), config);
            _scoreChat = new ScoreChat(Core, _players, _context.CollectorId, config);
            _dispatcher = new EventDispatcher(_queue, new SqliteStatsClient(config, store, _scoreChat.Deliver, _log.Warn), config, _log);
            _dispatcher.Start();
            _players.Subscribe(_hooks);
            if (config.ConnectMessagesEnabled)
            {
                _connectChat = new ConnectChat(Core, _players, store, config);
                _connectChat.Subscribe();
            }

            new RoundCollector(Core, _context).Subscribe(_hooks);
            new CombatCollector(Core, _context, _players).Subscribe(_hooks);
            new ObjectiveCollector(_context, _players).Subscribe(_hooks);
            Core.Event.OnMapLoad += MapLoaded;
            Core.Event.OnMapUnload += MapUnloaded;
            _mapSubscribed = true;
            _context.Emit("collector_start", new LifecycleEvent("load", hotReload));
            _players.Bootstrap();
            if (config.ChatCommandsEnabled)
            {
                _chatCommands = new StatsChatCommands(Core, _players, _context, config, store);
                _chatCommands.Register();
            }

            Core.Logger.LogInformation("finestats-lite loaded: bounded queue={Capacity}, batch={BatchSize}, hot reload={HotReload}.", config.QueueCapacity, config.BatchSize, hotReload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Validation messages are ours; other exceptions may contain config secrets.
            string detail = ex is InvalidDataException ? ex.Message : ex.GetType().Name;
            Core.Logger.LogError("finestats did not start: {Detail}. Check config and framework version.", detail);
            Unload();
        }
    }

    private void MapLoaded(IOnMapLoadEvent e)
    {
        try
        {
            _context!.SetMap(e.MapName);
            _context.Emit("map_start", new LifecycleEvent("map_load"));
            _players!.Bootstrap();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log?.CollectorError("map_load");
        }
    }

    private void MapUnloaded(IOnMapUnloadEvent e)
    {
        try
        {
            _players!.EndAll("map_change");
            _context!.Emit("map_end", new LifecycleEvent("map_unload"));
            _context.SetMap(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log?.CollectorError("map_unload");
        }
    }

    public override void Unload()
    {
        try
        {
            _chatCommands?.Dispose();
            _connectChat?.Dispose();
            _connectChat = null;
            _scoreChat?.Dispose();
            _scoreChat = null;
            _countryStop?.Cancel();
            _chatCommands = null;
            if (_mapSubscribed)
            {
                Core.Event.OnMapLoad -= MapLoaded;
                Core.Event.OnMapUnload -= MapUnloaded;
                _mapSubscribed = false;
            }

            _hooks?.Dispose();
            _players?.Dispose();
            _players?.EndAll("collector_unload");
            _context?.Emit("collector_stop", new LifecycleEvent("unload"));
        }
        finally
        {
            if (_dispatcher is not null)
            {
                Core.Logger.LogInformation("finestats-lite stopping: sent={Sent}, failed={Failed}, queue drops={Dropped}, queued={Queued}. Bounded background drain requested.", _dispatcher.Sent, _dispatcher.Failed, _queue?.Dropped, _queue?.Count);
                _log?.Detach();
                _ = _dispatcher.RequestStop(); // Observed internally; never Wait/GetResult on the game thread.
            }

            _hooks = null;
            _players = null;
            _context = null;
            _dispatcher = null;
            _queue = null;
            _log = null;
        }
    }
}
