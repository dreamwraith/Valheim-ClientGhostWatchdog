# Changelog

All notable changes to **ClientGhostWatchdog** will be documented in this file.

## [1.1.1] - 2026-10-08

### Changed & Improved
- Added detection and coexistence logging for `NetworkPerformanceSystem` (`MidnightsFX.NetworkPerformanceSystem`).
- Modernized project packaging and release automation tooling.
- Readme Cleanup

## [1.1.0] - 2026-09-17

### Fixed & Improved
- **My Very Lame Peer Check**: I had a dumb `if (serverPeer == null) continue;` check thinking it would just wait for the peer to hook up. Turns out, when a mod like `ServerCharacters` abruptly yanks the cord on your connection, `GetServerPeer()` becomes `null`, which meant my watchdog was happily spinning in an infinite `continue;` loop doing nothing while players were stranded in ghost limbo. I misread the underlying behavior on this one. If you're spawned in the world and the server peer vanishes, it now catches it, saves your character, and pulls you back to the menu.
- **Unswallowing Native Disconnects**: I was being a bit too eager with my custom popup and accidentally bulldozing native's actual error messages. If an admin kicked or banned you, or the server shut down, my code was just crapping all over it to tell you it saved you from a ghost connection instead of letting you know the actual disconnect reason. Now checks if the game already has a legitimate error status before putting my message over it.
- **Cleaned Up Connection Status Checks**: Cleaned up my previously messy checks. Instead of silently dying in the background if the connection dropped, it now distinguishes between pre-spawn loading and in-game disconnects.
- **Teleport / Portal Hang Guard**: If a portal handshake hangs or desyncs, the watchdog shouldn't stay muted forever. Added a `TeleportTimeoutSeconds` config (defaults to 30s) that resumes health checks if you get stuck in an infinite portal transition.
- **Disconnect Notice Delay**: Added `DisconnectDelaySeconds` (defaults to 5s, range 0 to 30s) so the on-screen center notice actually stays up long enough to read what happened before getting booted to the main menu. Set to 0 for instantaneous logout.
- **Debug Peer Severance Testing**: Added `DebugSimulateSeverKey` keybind and `cgw_simulatesever` console command (requires `EnableDebugLogs`) to simulate badly behaved mod peer drops for testing without needing another mod installed.
- **Consolidated Disconnect Logic**: Cleaned up the disconnect code into a single shared helper routine instead of repeating HUD notices and logout calls all over the place.

## [1.0.0] - 2026-09-17

### Initial Release
- **Dedicated Watchdog Coroutine**: Background heartbeat monitoring independent of `Game.Update` to prevent frame hitching.
- **Singleplayer / Host Safety**: Automatically inactive on singleplayer sessions or when hosting local servers (`ZNet.instance.IsServer()`).
- **Connection Loss Detection**: Monitors `ZRpc` socket peer state and detects server disconnection, timeout silence, and socket closure.
- **Midpoint Warning Notice**: Displays a customizable on-screen warning alert (TopCenter hud notice) before forcing disconnection.
- **Clean Disconnect**: Triggers graceful return to main menu (`FejdStartup`) preserving player save integrity and preventing ghost connection rollbacks.
- **Configurable**: Configurable timeout duration, check intervals, custom warning/disconnect messages, and debug logging.
