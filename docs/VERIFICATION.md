# Verification

The standalone repository was checked on Windows with the .NET 10 SDK:

- Release plugin build completed successfully.
- All 211 checks passed against temporary real SQLite databases.
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
