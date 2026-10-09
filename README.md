# ClientGhostWatchdog

[![GitHub Release](https://img.shields.io/github/v/release/dreamwraith/Valheim-ClientGhostWatchdog?logo=github&color=1081c2)](https://github.com/dreamwraith/Valheim-ClientGhostWatchdog/releases)
[![GitHub Downloads](https://img.shields.io/github/downloads/dreamwraith/Valheim-ClientGhostWatchdog/total?logo=github&color=1081c2)](https://github.com/dreamwraith/Valheim-ClientGhostWatchdog/releases)
[![Last Commit](https://img.shields.io/github/last-commit/dreamwraith/Valheim-ClientGhostWatchdog?logo=git)](https://github.com/dreamwraith/Valheim-ClientGhostWatchdog/commits/main)
[![Publish Status](https://img.shields.io/github/actions/workflow/status/dreamwraith/Valheim-ClientGhostWatchdog/publish.yml?label=Publish%20Portals&logo=githubactions)](https://github.com/dreamwraith/Valheim-ClientGhostWatchdog/actions)
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/DreamWraith/ClientGhostWatchdog?logo=thunderstore)](https://thunderstore.io/c/valheim/p/DreamWraith/ClientGhostWatchdog/)
[![Hexium](https://img.shields.io/badge/Hexium-ClientGhostWatchdog-6c5ce7)](https://valheim.hexium.gg/mods/DreamWraith/ClientGhostWatchdog)
[![Game: Valheim](https://img.shields.io/badge/Valheim-Deep_North_%2F_1.x-1b2838?logo=steam&logoColor=white)](https://store.steampowered.com/app/892970/Valheim/)
[![BepInEx Pack](https://img.shields.io/thunderstore/v/denikson/BepInExPack_Valheim?label=BepInEx&color=5B57E7)](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
[![Target: .NET 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet)](ClientGhostWatchdog.csproj)
[![Client Side Only](https://img.shields.io/badge/Type-100%25%20Client--Side-brightgreen)](README.md#the-solution)
[![License](https://img.shields.io/github/license/dreamwraith/Valheim-ClientGhostWatchdog?color=blue)](LICENSE.md)
[![AI Philosophy](https://img.shields.io/badge/AI%20Philosophy-Software%20Craft-2ea44f?logo=github)](https://gist.github.com/dreamwraith/77c91d656c842611bf8c40febf8056f2)

A standalone, 100% client-side watchdog mod for players connecting to remote/dedicated Valheim servers that prevents **"Zombie Client" / "Ghost Connection"** desync states.

> [!NOTE]
> **Client-Side Only:** This mod is installed locally on your PC. It does **not** need to be installed on the server and works when connecting to any dedicated or community server.

## The Problem

When playing on a dedicated or remote server, if the server drops your client's connection (for example, during daily scheduled server restarts, network glitches, or ISP timeouts with a `ClosedByPeer` status), vanilla Valheim has a critical client-side bug:
1. `ZNet.UpdatePeers()` ignores disconnection error codes reported by `ZRpc.Update()`.
2. `ZSteamSocket.IsConnected()` only checks pointer validity, remaining "connected" even after the socket closed.
3. The client's `ZNet.m_connectionStatus` remains `Connected`.
4. The client continues running in an empty, desynced world without realizing it was disconnected. Any structures built, items moved, or actions taken while desynced are lost or result in desync rollbacks.

## The Solution

**ClientGhostWatchdog** runs an independent background coroutine on your game client when connecting to a remote server:
- **No `Game.Update` Overhead**: Runs completely via a dedicated Unity coroutine with real-time sleep intervals, executing zero per-frame checks in `Game.Update`.
- **Disabled on Local Worlds**: Strictly inactive on singleplayer worlds or local server hosts (`ZNet.instance.IsServer()`). No coroutine is started and no checks run.
- **Two-Stage Detection**:
  - **Stage 1 (Halfway Warning)**: When server silence reaches halfway through the configured timeout period (e.g. 15s at the default 30s timeout), a prominent on-screen center notice warns the player: `"Server connection lost, attempting to reconnect... ({0}s)"`. The `{0}` placeholder dynamically updates with the countdown until disconnect.
  - **Auto-Recovery**: If server communication resumes during the warning stage, the warning is dismissed and an on-screen notice confirms: `"Server connection restored."`
  - **Stage 2 (Timeout & Clean Logout)**: If silence reaches the full timeout, the server socket closes, or the server peer drops, the mod displays `"Server connection lost. Disconnecting..."`, pauses for the configured notice delay (`DisconnectDelaySeconds`, default 5s) so the notice is readable, and triggers `Game.instance.Logout()`.
- **Severed Peer & badly behaved Mod Protection**: Catches scenarios where a third-party mod (such as `ServerCharacters` inventory sync timeout) or network failure drops the server peer without an engine logout, preventing players from being trapped in desync limbo.
- **Preserves Native Disconnect Reasons**: If an admin kicks or bans a player, or the server shuts down, the watchdog defers to native Valheim, allowing players to see the genuine error dialog instead of generic watchdog notices.
- **Portal & Teleport Hang Guard**: Resumes health checks after a configurable ceiling (`TeleportTimeoutSeconds`, default 30s) if a player gets stuck in an indefinite portal desync.
- **Preserves Player Progress**: Calling `Game.instance.Logout()` ensures the client cleanly saves their character profile (`SavePlayerProfile(setLogoutPoint: true)`) before returning to the main menu.
- **Informs the Player**: Displays an informative dialog on the main menu informing the player why they were returned to the menu and that their local save was preserved.
- **Defensive Lifecycle & State Safety**: Centralized state machine (`WatchdogManager`) with atomic, TTL-backed disconnect reasons (`DisconnectReasonManager`) to prevent stale error dialogs across scene transitions.

---

## Design Philosophy & Tradeoffs

This mod intentionally enforces a **fail-safe disconnect policy**:
- **Why fail-safe disconnect?**: In vanilla Valheim, hanging onto an unacknowledged dead connection risks losing valuable building, harvesting, or combat progress. By forcing a clean logout via `Game.instance.Logout()`, the mod saves the player's local inventory and state immediately before the desync worsens.
- **Handling unstable connections**: If you frequently play on high-latency satellite connections, fluctuating cellular networks, or Wi-Fi prone to multi-second packet drops:
  - Increase `TimeoutSeconds` (e.g. to `60` or `90` seconds). The halfway warning will scale accordingly (30s warning for a 60s timeout), giving your connection ample time to recover before any disconnect is triggered.
  - You can also set `TimeoutSeconds` to `0` to completely disable the watchdog without removing the mod.

---

## Configuration

The configuration file is automatically generated at `BepInEx/config/dreamwraith.ClientGhostWatchdog.cfg`:

| Section | Setting | Type | Default | Description |
|---|---|---|---|---|
| `1 - General` | `TimeoutSeconds` | `float` | `30` | Total seconds of server silence before disconnecting. Halfway through (e.g. 15s), a warning notice is displayed. Range: `0` to `300`. Set to `0` to disable the watchdog. |
| `1 - General` | `CheckIntervalSeconds` | `float` | `1.0` | Interval in seconds between watchdog checks in the background coroutine. Range: `1.0` to `5.0`. |
| `1 - General` | `TeleportTimeoutSeconds` | `float` | `30` | Maximum seconds a player can remain in a teleporting state before watchdog health checks resume. Prevents infinite limbo if a portal hangs. Range: `0` to `180`. Set to `0` to disable ceiling. |
| `2 - On-Screen Notices` | `WarningNoticeMessage` | `string` | `"Server connection lost, attempting to reconnect... ({0}s)"` | Large on-screen center notice displayed at the halfway mark. `{0}` is replaced with remaining seconds. |
| `2 - On-Screen Notices` | `RecoveredNoticeMessage` | `string` | `"Server connection restored."` | Large on-screen center notice displayed if connection recovers before timeout. |
| `2 - On-Screen Notices` | `DisconnectNoticeMessage` | `string` | `"Server connection lost. Disconnecting..."` | Large on-screen center notice displayed when timeout is reached. |
| `2 - On-Screen Notices` | `DisconnectDelaySeconds` | `float` | `5.0` | Seconds to wait after displaying the disconnect notice before executing logout, giving players time to read the notice. Range: `0` to `30`. Set to `0` for instantaneous logout. |
| `3 - Main Menu Dialog` | `ShowDisconnectReason` | `bool` | `true` | Whether to display an explanatory message on the main menu after being disconnected by this watchdog. |
| `3 - Main Menu Dialog` | `DisconnectMessage` | `string` | `"Server connection lost (Ghost connection prevented). Progress was saved locally."` | Custom message displayed on the main menu dialog when disconnected. |
| `4 - Debug` | `EnableDebugLogs` | `bool` | `false` | When enabled, prints detailed debug logs with every watchdog check tick, timer event, threshold evaluation, and connection lifecycle hook. |
| `4 - Debug` | `DebugSimulateSeverKey` | `KeyCode` | `None` | DEBUG ONLY: Keybind to simulate an abrupt peer disconnection (ServerCharacters behavior). Requires EnableDebugLogs to be true. Keep as None for normal play. |

---

## Installation

> [!TIP]
> **Client-Side Only**: Install this on your local PC via your mod manager (Gale / r2modman / Thunderstore) or manual drop. Nothing needs to be installed on the server.

1. Ensure **BepInExPack Valheim** is installed on your client.
2. Install via **Gale** / **r2modman**, or manually copy `ClientGhostWatchdog.dll` into your `Valheim/BepInEx/plugins/` folder.
3. Launch Valheim and connect to your server.

---

## Building from Source

The project uses a portable MSBuild configuration that auto-detects standard Steam paths.

```bash
dotnet build -c Release
```

The compiled assembly will be placed in `bin/Release/net48/ClientGhostWatchdog.dll`. Building in `Release` configuration also automatically packages the distribution ZIP to `bin/Publish/ClientGhostWatchdog-<Version>.zip`.

### Custom & CI Paths
For custom installations or CI/CD pipelines, you can specify paths using environment variables or a local `.user` property file:

- **Environment Variables**:
  - `VALHEIM_PATH`: Absolute path to your Valheim game root directory.
  - `BEPINEX_PATH`: Absolute path to your BepInEx `core` folder.
- **Local User Override**:
  Copy `ClientGhostWatchdog.csproj.user.example` to `ClientGhostWatchdog.csproj.user` in the project root (this file is git-ignored and automatically loaded by MSBuild):
  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <Project>
    <PropertyGroup>
      <GamePath>C:\Games\Valheim</GamePath>
      <BepInExCorePath>C:\Games\Valheim\BepInEx\core</BepInExCorePath>
    </PropertyGroup>
  </Project>
  ```

---

## Packaging, Publishing & Releases

All developer automation tools for release management, packaging, and publishing to **Thunderstore** and **Hexium** are organized in the [`.scripts/`](.scripts/) folder:

- **Release Management**: [`release.ps1`](.scripts/release.ps1) compiles in `Release`, creates mod & source archives, extracts changelog notes, and publishes GitHub Releases (Draft by default, or published with `-Publish`) using the `gh` CLI.
- **Packaging & Version Bumping**: [`package.ps1`](.scripts/package.ps1) increments SemVer in `ClientGhostWatchdog.csproj`, updates `manifest.json`, and bundles distribution archives.
- **Portal Publishing**: [`publish.ps1`](.scripts/publish.ps1) uploads directly to Thunderstore and Hexium APIs.
- **CI/CD Workflow**: [`.github/workflows/publish.yml`](.github/workflows/publish.yml) provides an automated GitHub Actions workflow to publish to Thunderstore and Hexium whenever a GitHub Release is published.

For detailed documentation on flags, workflows, and secret configuration, see [`.scripts/README.md`](.scripts/README.md).


## License, Author Notes
- This project is licensed under the GNU General Public License v3.0 - see the [LICENSE.md](LICENSE.md) file for details.
- [**A Small Note from Me about Valheim Modding Specifically**](https://gist.github.com/dreamwraith/98564f8441dc234bfadd7e2b605c694c) - Thoughts on open-source modding, community inclusivity, and anti-gatekeeping.
- [**A Note on AI, Software Craft, and Why This Code Exists**](https://gist.github.com/dreamwraith/77c91d656c842611bf8c40febf8056f2) - Personal essay on software craft, human agency, and engineering responsibility.