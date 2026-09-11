# Deployment

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
