# Changelog

All notable changes to **ClientGhostWatchdog** will be documented in this file.

## [1.0.0] - 2026-09-17

### Initial Release
- **Dedicated Watchdog Coroutine**: Background heartbeat monitoring independent of `Game.Update` to prevent frame hitching.
- **Singleplayer / Host Safety**: Automatically inactive on singleplayer sessions or when hosting local servers (`ZNet.instance.IsServer()`).
- **Connection Loss Detection**: Monitors `ZRpc` socket peer state and detects server disconnection, timeout silence, and socket closure.
- **Midpoint Warning Notice**: Displays a customizable on-screen warning alert (TopCenter hud notice) before forcing disconnection.
- **Clean Disconnect**: Triggers graceful return to main menu (`FejdStartup`) preserving player save integrity and preventing ghost connection rollbacks.
- **Configurable**: Configurable timeout duration, check intervals, custom warning/disconnect messages, and debug logging.
