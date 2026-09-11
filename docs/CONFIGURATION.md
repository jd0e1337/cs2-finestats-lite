# Configuration and storage

The generated JSONC template lists every default. Missing keys use defaults.
Change settings and reload the plugin. The configuration has no API credentials.

| Area | Settings |
|---|---|
| General | `Enabled`, `ServerId` (1–128 characters; bound to this database on creation) |
| SQLite | `DatabaseFile` (filename only inside plugin data), `DatabaseBusyTimeoutSeconds` (1–10) |
| Backups | `BackupIntervalMinutes` (0 disables; otherwise 1–10080), `BackupKeepCount` (1–100) |
| Queue | `QueueCapacity` (1–100000), `BatchSize` (1–1000, no larger than queue), `FlushIntervalMs` (100–60000) |
| Shutdown/retry | `RetryCount` (0–8; only SQLite busy/locked), `ShutdownDrainMs` (0–30000) |
| Logging | `WarningIntervalSeconds` (5–300), `LogDebugEvents` |
| GeoIP | `GeoIpEnabled`, `GeoIpCountryCsvPath` (absolute local DB-IP Country Lite CSV/CSV.gz) |
| Commands | `ChatCommandsEnabled`, `EnabledCommands`, `BareChatCommandsEnabled`, `PublicRankReplies` |
| Queries | `CommandCooldownSeconds` (1–30), `CommandPageSize` (1–10), `CommandMaxLines` (5–20 and at least page size + 2), `CommandMaxConcurrentRequests` (1–16), `CommandTimeoutSeconds` (1–30) |
| Appearance | `ChatPrefix` (48 UTF-8 template bytes), `ChatMaxBytes` (64–230 rendered bytes), `DisplayNameMaxLength` (1–128), `NumberDecimalPlaces` (0–6), `Messages` |
| Notices | `ScoreNotificationsEnabled`, `RankingProgressNotificationsEnabled`, `ScoreNotificationReasons`, `ScoreNotificationMaxAgeSeconds` (1–120), `ScoreNotificationQueueMaxAgeSeconds` (1–10), `ScoreNotificationMaxPerBatch` (1–20 lines/player/batch) |

`Ranking` contains enablement, initial/minimum points, kill/death/assist points,
headshot bonus, teamkill/suicide penalties, plant/defuse/hostage/MVP points,
strength scale and multiplier bounds, `ExcludeBots`, `ExcludeWarmup`,
`ExcludeTeamDamage`, `MinimumKillsForLeaderboard`, and `AlgorithmVersion` (1 only).
The strength multiplier is clamped `1 + (victimPoints - attackerPoints) / StrengthScale`.
Headshot bonus is added after scaling. Bots use `StartingPoints` as strength.
Both true and unknown warmup are excluded when `ExcludeWarmup=true`.
Disabling ranking leaves statistics active but suppresses score/progress notices.
Changing the minimum affects rank queries immediately after reload. Scoring changes
affect future events; there is no retrospective recalculation.

Progress uses saved per-event kill counts, including zero-point counted kills.
The threshold kill produces a qualification notice. Notice retries are deduplicated;
stale sessions/collectors cannot receive them. No notice is promised after disconnect,
queue overflow or a crash. Score and progress switches are independent.

## Messages

English templates use named placeholders and SwiftlyS2 colors, e.g.
`[green]{name}[/]`, `[red]{amount}[/]`, `[yellow]{required}[/]`. `[/]` resets to the
default color, not an enclosing color. All message keys and their allowed placeholders
appear in the template. Unknown keys/placeholders, malformed braces, control
characters or templates longer than 1024 characters are rejected. Empty messages
suppress their line. Names are sanitized; truncation preserves Unicode and whole tags.

The formatter and message vocabulary are shared with full finestats. Lite overrides
the defaults that referred to backend availability or cross-server scope. Keep these
Lite messages when customizing a partial Messages object.

## Database and durability

SQLite uses WAL and FULL synchronous mode. Writes, aggregation, identity merges and
receipt insertion share one transaction. A single worker drains the bounded queue;
queries and backup operations run off the game thread and share a per-instance gate.
SQLite itself also protects overlapping reloads. Busy retries are bounded.
CommandTimeoutSeconds bounds waiting for the gate and rejects overdue results;
an already executing synchronous SQLite operation may finish before cancellation.

SteamID is the persistent human key. Unauthenticated observations use a provisional
session key; later authorization merges counters atomically without awarding past
unrated kills. Session IDs include the collector UUID, so map/reload/reconnect
sessions stay separate. Country lookup happens locally; IP addresses are never
persisted. DB-IP database files are not included; preserve DB-IP attribution/license
when obtaining and using Country Lite data.

Schema version 1 is created transactionally and marked with an application ID.
Foreign databases and unsupported newer versions are rejected. Future migrations
must increment the version; never replace the database with a new empty file during
an upgrade. Event IDs and original notices are retained for durable deduplication.
There is no automatic pruning: database size grows with observations and sessions,
although full raw payloads are not stored. Monitor disk space and backup size.

## Backup and restore

With defaults, the first committed write creates a backup, then the first write
after each 60-minute interval creates another. Up to five completed snapshots remain
under the data directory's `backups/`. SQLite's online backup API produces a consistent
snapshot including WAL data. The snapshot is renamed into place only after completion.
Backup failure logs a warning and does not invalidate committed statistics. A large
backup can temporarily delay queued work; tune the interval for server size.

To restore: stop CS2, preserve the entire existing data directory as a rollback copy,
then restore a chosen snapshot under `DatabaseFile` in a clean data directory with
the correct game-server ownership. Do not reuse the old `-wal`/`-shm` files with a
restored database. Start CS2 and check logs and `!statsme`. The ServerId must match.
For manual offline backups, stop CS2 and copy the entire data directory, including
any WAL/SHM files. Copying only the live `.db` file is not a consistent backup method.

## Connection announcement (1.0.1)

`ConnectMessagesEnabled=true` enables a single announcement per human connection,
after both joining and Steam authentication. `PublicConnectMessages=true` broadcasts
it to everyone; false sends only to the joining player. `ConnectMessageDelaySeconds`
(default 3, range 0–15) allows the local country lookup to become available.
`ConnectCountryNames=true` shows English country names and codes; false shows codes.
Only this connection's country is used; missing lookup results never reuse a historical
country. Existing players at plugin load are not announced. Slot/session changes,
disconnects and unloads invalidate delayed output. Map/team changes do not add a
second announcement for the same native session.

```json
"ConnectionMessages": {
  "Message": "Player [green]{name}[/] connected from [yellow]{country}[/] - {rank}",
  "Ranked": "Rank [gold]#{rank}[/]",
  "Unranked": "[silver]Unranked[/]",
  "UnknownCountry": "Unknown country"
}
```

Message placeholders: `{name}`, `{country}`, `{country_code}`, `{rank}`.
`Ranked` accepts `{rank}`; fallback texts accept no placeholders. Names are sanitized.
Set Message to an empty string to suppress output. Rank comes from the current local
SQLite leaderboard. Database/query failure skips this optional announcement.

## Kill messages and whole-point scoring (1.0.2)

`Ranking.HeadshotBonus` now defaults to 2. With `Ranking.RoundPointsToIntegers=true`
(default), the final per-event delta is rounded to the nearest integer, with halves
away from zero, before applying the points floor and saving. At equal strength the
normal kill is 5 points; a headshot is 7. Strength scaling remains enabled.
This also applies to deaths, assists, teamkills, suicides and objectives.
StartingPoints and MinimumPoints must be integers in this mode. Setting the flag to
false explicitly restores fractional scoring.

On entering whole-point mode, existing fractional player balances are rounded to
whole points in the initialization transaction. Stored event receipts are not
rewritten. Existing balances can change by up to half a point; the deployment takes
an online database backup first. K/D and other ratios retain their normal precision.

```json
"Messages": {
  "ScoreKill": "Received [green]{amount}[/] Points for Killing [yellow]{opponent}[/]",
  "ScoreHeadshotKill": "Received [green]{amount}[/] Points for Killing [yellow]{opponent}[/] with [gold]Headshot[/]"
}
```

Both accept `{amount}` and `{opponent}`. A kill has one score line selected by its
recorded headshot flag; qualification progress may appear separately. Opponent name
and headshot status are stored with the committed receipt, so retries do not use a
later name or another event. Names are sanitized before display. Old receipts without
these optional fields keep the generic score message. Other score reasons keep their
existing templates. No replay of historical kills is performed.

## 1.0.3 — total points, playtime and prefix

ScoreKill and ScoreHeadshotKill now end in ` [green]({points})[/]`, using the committed
new total. Their configurable placeholders are amount, opponent and points.
The default prefix is `[blue][fs][/] `, including the trailing space.

Lite uses Messages.Playtime (`Playtime: [yellow]{duration}[/]`) for the session and
Messages.ProfilePlaytime for the profile. Duration is formatted in whole days,
hours and minutes, omitting zero units and using singular/plural English labels.
Under one minute is `0 minutes`; unknown values remain unknown.

The profile combines stored session durations and the currently validated player's
live session up to query time. Other unfinished sessions are capped at their last
observation, so crashes/offline time do not grow playtime. Partial sessions begin at
their first observation. Old missing events cannot be reconstructed. These calculations
change read output only; stored events/counters are not rewritten. The old SessionTime
and ProfileEvents templates remain accepted for compatibility but are not used by
Lite's current playtime display.
