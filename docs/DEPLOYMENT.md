# Deployment

## Upgrading SwiftlyS2 and finestats-lite together

For SwiftlyS2 1.4.10, use the corresponding `swiftly1.4.10` plugin package from
[finestats-lite releases](https://github.com/jd0e1337/cs2-finestats-lite/releases/latest).
On Linux, the [framework package with runtimes](https://github.com/swiftly-solution/swiftlys2/releases/download/v1.4.10/swiftlys2-linux-v1.4.10-with-runtimes.zip)
includes the required .NET runtime.

Stop the server before replacing native framework libraries. Back up the existing
framework binaries, gamedata, translations, plugins, configuration, and data.
The framework ZIP has an outer package directory: copy from its contained
`addons/swiftlys2` directory into the existing installation. Do not create a
second `swiftlys2` directory inside the current one. Preserve existing config
files, custom plugins, and statistics. Keep executable permissions on the bundled
`dotnet` host. Install the matching finestats-lite package, then start the server
and check framework/plugin versions, SQLite integrity, and a map transition.

## Plugin installation

Build the package for your server OS and installed SwiftlyS2 version as described
in the README. The default build targets SwiftlyS2 1.4.10.

1. Back up the existing plugin configuration and statistics before an update.
2. Stop the game server or unload the plugin.
3. Extract the package into `addons/swiftlys2/plugins/`, keeping the included
   `finestats-lite` folder and all managed/native SQLite dependencies together.
4. Keep configuration under `addons/swiftlys2/configs/plugins/finestats-lite/`
   and data under `addons/swiftlys2/data/finestats-lite/` across plugin updates.
5. Disable overlapping statistics/chat commands from other plugins.
6. Start the server or load the plugin. Check initialization, database creation,
   player commands, combat statistics and reconnect behavior.

To roll back, unload the plugin and restore the previous plugin package and
compatible configuration. Preserve the statistics database and its backups.
See [configuration and backups](CONFIGURATION.md) and [verification](VERIFICATION.md).
