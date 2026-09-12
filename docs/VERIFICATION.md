# Verification

The standalone repository was checked on Windows with the .NET 10 SDK:

- Release plugin build completed successfully.
- All 241 checks passed against temporary real SQLite databases.
- The tests do not connect to a production game server.

Coverage includes local chat commands, scoring and qualification, bot/warmup
filters, session identity merging, persistent event deduplication, transaction
rollback, backup/restore and retention, concurrent database access, shutdown
drain, connection gates and message formatting.

Run from the repository root:

```powershell
dotnet build finestats-lite.csproj -c Release
dotnet run --project tests/finestats-lite.Tests.csproj -c Release
```

The existing SQLitePCLRaw.lib.e_sqlite3 2.1.11 dependency triggers NuGet warning
NU1903. Because warnings are errors, the verification run used `-p:NuGetAudit=false`
on the command line. Project audit settings were not changed; a normal build can
remain blocked until this dependency warning is addressed.

## Native smoke checks

The automated checks do not validate native game callbacks or rendered chat.
On a test CS2 server, check plugin loading, configuration/database creation,
rank and detail commands, combat scoring, connection messages, reconnects,
map changes, hot reload and persistence after restart. Confirm that delayed
replies cannot reach a different player after a slot is reused.

## 1.0.4 public statsme replies

Successful statsme replies are public regardless of PublicRankReplies. Regression
checks cover all command names with that option enabled and disabled, plus private
argument errors and missing-profile replies. Native rendered chat still requires
a CS2 smoke test.

## 1.0.5 kill notice qualification

Kill point notices respect MinimumKillsForLeaderboard. Tests cover kills 1 through
11 with a minimum of 10, retained scoring and progress, delivery with progress
disabled, and immediate notices with a zero minimum.

## 1.0.6 first-kill investigation

Five checks verify a first kill without previous session events, its progress
message and delivery filter, statsme output, and late authentication merging.
The collector now reads warmup from current game rules for each event instead
of relying on a round-start snapshot. Native map/server startup and warmup
transitions still require in-game verification; the reported missing first
message was not reproduced on a live server during this investigation.

## 1.0.7 map transition safety

Player bootstrap is deferred from plugin/map load to the next world update.
Pending bootstrap work is cancelled on map/plugin unload and superseded by newer
map loads. Five regression checks cover deferred execution, cancellation, rapid
map changes and reload. Lifecycle/session events no longer refresh native game
rules during teardown. Live map-change validation is still required.

## 1.0.8 map lifecycle isolation

Lifecycle and session events never read native globals. A shared map generation
gates game callbacks and invalidates queued score, connection and command replies
before any player access. Disconnects use managed identities only. Player discovery
is lazy through ready/auth/game callbacks; map/plugin load no longer enumerates
players. Tests exercise the real lifecycle emitter with no available engine and
cover map activation, stale work and unload. Native validation is reported in the
release notes after deployment testing.
