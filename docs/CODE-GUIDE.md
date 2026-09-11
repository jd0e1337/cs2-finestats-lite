# Understanding the finestats-lite code

`finestats-lite` collects game events from a CS2 server, calculates statistics and
points, and stores them locally in SQLite. Players can query these records using
chat commands. No additional web server runs alongside the plugin.

## Where to start

| File | Responsibility |
|---|---|
| `FinestatsPlugin.cs` | Entry point: read configuration, connect services, subscribe to events, and unsubscribe on unload. |
| `Config/StatsConfig.cs` | Read settings and reject invalid combinations. |
| `Config/RankingOptions.cs` | Define scoring, filtering, and leaderboard qualification rules. |
| `Services/EventDispatcher.cs` | Process the queue in bounded batches on a background worker. |
| `Services/SqliteStatsClient.cs` | Request storage, retry database lock failures within a limit, and then deliver score notices. |
| `Storage/LiteStore.cs` | Initialize the database, record events, resolve identities, calculate points, and create backups. |
| `Storage/LiteQueries.cs` | The other part of the same `LiteStore` class: read profiles, rankings, and detailed statistics. |
| `Services/StatsChatCommands.cs` | Register chat commands, limit requests, and deliver replies to the correct player session. |
| `Services/StatsCommandClient.cs` | Translate commands into local queries and format results as chat text. |
| `Services/ConnectChat.cs` / `ConnectionGate.cs` | Trigger connection announcements once the player is ready and authenticated, at most once per native session. |
| `tests/Program.cs` | Executable checks using real, temporary SQLite databases. |

All paths in this table are relative to the repository root.

## Example: a player gets a kill

1. `CombatCollector` captures the game event. `CollectionContext` adds information
   such as the server, map, collector, and timestamp, then puts the event in `EventQueue`.
2. `EventDispatcher` takes events from the queue in the background. It writes a
   batch when the batch is full or its flush deadline expires.
3. `SqliteStatsClient` passes the batch to `LiteStore.AppendAsync`. A temporarily
   locked database can trigger another attempt.
4. `AppendAsync` opens a transaction. Previously processed event IDs return their
   saved receipts; new events are passed to `Apply`.
5. `Apply` resolves the players involved, applies warmup and bot filters, increments
   the relevant counters, and awards points where appropriate. Suicides, teamkills,
   and regular kills are handled separately.
6. Statistics and event receipts are committed together. Optional chat notices
   are processed afterwards. A chat failure must not turn a successful database
   write into a failed attempt.

## Why the queue and background worker exist

Game callbacks must return quickly, so database access runs in the background.
The queue has a fixed capacity: overload can drop events instead of consuming
unlimited memory.

On unload, `RequestStop` stops accepting new events and sets a deadline for
processing the remaining queue. It does not wait synchronously on the game thread.
After the deadline, the dispatcher counts unsaved events as failures.

Chat commands copy only the identity information needed by the background work.
The actual reply returns to the game through `NextWorldUpdate`. Before sending it,
the session is checked again: a reply must not reach a new occupant of the same
player slot after a reconnect.

## What the database contains

| Table | Contents |
|---|---|
| `players` | Player identity, name, and current points. |
| `sessions` | Observed sessions, their time boundaries, and optional country. |
| `counters` | Counters by player, dimension, key, and metric, such as weapon damage or total kills. |
| `receipts` | Processed event IDs and their score receipts, preventing duplicate accounting on retries. |
| `rounds` | Previously observed round/player combinations. |
| `metadata` | Server ownership and general processing information. |

An authenticated Steam identity is persistent. Before authentication, records can
belong to a provisional session identity. `Resolve` merges those records when
authentication arrives. A later observation with incomplete identity information
must not downgrade the authenticated identity.

`Count` increments a single counter. `Apply` uses local helpers: `Add` distributes
counters across overall, map, session, and, where appropriate, weapon dimensions;
`Award` records permitted point changes, including rounding and the points floor.
A counted kill can advance leaderboard qualification even when the opponent is
not eligible for regular scoring.

## Where the shared classes live

`Shared/` contains source files originally shared with the full version. These
include collectors, event types, `CollectionContext`, `EventQueue`, diagnostics,
GeoIP, and chat helpers. They are compiled directly into this plugin; this
repository builds without the full version.

Names such as `SendAsync`, `StatsCommandClient`, and the path-shaped queries come
from that shared structure. In Lite, they lead to local SQLite access. The JSON
envelope around score receipts also exists to reuse the shared filter; it does
not represent a network call here.

## Where common changes belong

- **Change scoring:** defaults in `RankingOptions`, accounting in `LiteStore.Apply`.
- **Change chat text:** templates in `resources/config.jsonc`, formatting in
  `StatsCommandClient`, and connection announcements in `ConnectChat`.
- **Query a statistic:** data access in `LiteQueries`, output in
  `StatsCommandClient`, and command registration in `StatsChatCommands`.
- **Investigate storage problems:** `EventDispatcher` for queues and losses,
  `SqliteStatsClient` for retries, and `LiteStore` for transactions.

Run the existing checks from the repository root:

```powershell
dotnet run --project tests/finestats-lite.Tests.csproj -c Release
```

They cover scoring, filters, retries, identity changes, backups, and concurrent
SQLite access, among other behavior. Native game callbacks and rendered chat
also need testing on a CS2 server. See [verification](VERIFICATION.md) for the
current dependency warning affecting the default test command.
