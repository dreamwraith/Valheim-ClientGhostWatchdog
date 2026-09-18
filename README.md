# ClientGhostWatchdog

A standalone, 100% client-side watchdog mod for players connecting to remote/dedicated Valheim servers that prevents **"Zombie Client" / "Ghost Connection"** desync states.

> [!NOTE]
> **Client-Side Only:** This mod is installed locally on your PC. It does **not** need to be installed on the server and works when connecting to any dedicated or community server.

---

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

The compiled assembly will be placed in `bin/Release/net48/ClientGhostWatchdog.dll`.

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

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE.md](LICENSE.md) file for details.

---

## A Small Note from Me about Valheim Modding Specifically

I created this mod to solve a very real problem my friends who live in remote or internet challenged circumstances continue to encounter. I am admittedly naive and green in the world of Valheim/Unity modding, but I am far from new to software development or working with complex systems.

The current state of the Valheim modding community, not unlike all modding communities, is replete with factions, infighting, disagreements, and unsavory folk as much as it is full of amazing people who just want to share their joy with others. I firmly believe in a space such as modding, all things should be open in nature, not gatekept. As such, this will be licensed under GPL 3.0. To any of you modders out there in the community who feels similarly, I'm open to learning more and helping grow the open source modding scene as much as I can.

That openness extends to the human just as much as the code. That is to say, while I hold no ill will towards any particular individuals, I will not hold back in calling out bigotry, in particular identity or sexually based hate speech or discrimination, or gatekeeping within the community for any reason. If you are that kind of person, or that kind of modder, don't expect any slack from me. I will work with others to replace your work with similar. Call it knock-off work, if you must, but I will sleep soundly knowing that I am doing it to spite your hateful ass. If you are against those hateful people, and things they say and do, then I hope you will consider this an olive branch extended to you, as well as an apology on behalf of those too stupid or too self-loathing to accept the error of their ways. I do realize that many modders have, and continue to put in countless hours into this game, and have put in even more hours into open-sourcing many of their mods, and I am endlessly appreciative of that, as it has resulted in much joy in my life. I only hope to be able to contribute to that end of things, in an accepting and open environment.

Peace, Love, and Joy in All things - unless you're a bigoted, transphobic, gatekeeping, jerk - In which case you can kindly fuck off.

---

> [!CAUTION]
> ### Excerpts From: "A Note on AI, Software Craft, and Why This Code Exists" by dreamwraith, via ed-galaxy-sync
>
> I want to be upfront and blunt about how AI was used on this project, where I draw a hard line in the sand, and more importantly, how I feel about it on a more broad level.
>
> Look... I have massive, fundamental gripes with artificial intelligence. The environmental toll of running massive data center furnaces is pretty disgusting, and the blatant theft of scraping people's work without permission is impossible to defend. A couple of years ago, corporate executives used the hype around AI as a convenient excuse to lay me off. Since then, while I've watched former coworkers get dragged through the mud of mindless "you MUST use AI for everything" corporate mandates, I've managed to stay employed the old-fashioned way, by actually knowing how to solve hard problems, understand systems, and write real code.
>
> Watching people posture as "artists" or "creators" while generating synthetic images, fake voice acting, or slop stories and lore makes my blood curdle. It's not creativity, it's literal theft, laundering the actual talent, sweat, and soul of real human beings into digitized mush.
>
> But programming has always lived in a messy, chaotic in between or trench or some shit. I have never considered raw lines of syntax as some high art form. The real craft is the ideas, the problems, and how you solve them, the architecture that is designed, with a wholistic view. It's figuring out where data lives, how state transitions, how things crash when everything hits the fan, and how to model messy real-world garbage. In that context, using an LLM to bounce ideas off of, write boring documentation, check syntax, or scaffold boilerplate isn't much different than copy-pasting from Stack Overflow or leaning on IntelliSense and autocomplete, tools that Visual Studio and other IDEs have given us for decades. It speeds up the typing, but it doesn't do the godddamned thinking!
>
>...
>
> Those are just the high (low?) lights. An AI cannot design that journey. An AI can read code, but it doesn't have the lived experience of being a software developer or senior data engineer for decades. It doesn't know the sheer misery of a database lock timeout at 2 in the morning. It doesn't know when a design has hit a brick wall, and it sure as hell won't decide to tear down working code and throw away days of effort just to build something better. Hell, it will more often do everything it can to preserve completely dead code paths as "fallback" logic rather than nuke everything and start over. Those choices only come from stubborn human curiosity, frustration, and bruised egos from riding the high of feeling like it works, only to realize your design sucks and you have to redo it all over again.
>
> We're also living in a bubble right now, not dissimilar, but in my opinion, far worse than the dot com bubble of my youth. Venture capital is burning billions (trillions?) to subsidize cheap API tokens, making LLMs look practically free. That party *IS* going to end. When the real bills come due, prices will spike, the subsidies will evaporate, and anyone who relied on AI to build crap they don't actually understand is going to be dead in the water. People, normal people, will lose everything because of the greed and insanity of people who are wealthy, or who think claude already thinks and feels. The only real safety net in software, nay, in life even, is knowing how to build, read, create, modify, throw away, and goddamnit just DO things yourself.
>
> I used AI here as a rubber duck, a technical documentation assistant, and an accelerator for repetitive grunt work. But **I take 100% personal responsibility for every single line of code in this repository.** Every schema, spatial query, stored procedure, migration script, and edge-case handler was manually inspected, reasoned through, rewritten, and held to my own standards*.
>
> <small>*\*(It should be noted that I HATE writing unit tests and documentation, so those standards might be a bit lower... :D)*</small>
> 
> **If something breaks in this toolset, that's on me, not the machine.**
>
> ---
>
> **A Final Thought on the Bigger Picture:**
> Beyond the code, lets all be clear-eyed about what's happening. The wealthy elite and tech oligarchs aren't pushing AI to make our lives better, they're using it to cut payroll, consolidate power, further enrich themeselves, and keep the working class under their thumb. They will continue to tighten the screws on our necks at every opportunity. At the same time, it risks becoming a sort of digital, mental methadone, numbing our curiosity, dulling our critical thinking, and training both programmers and non-programmers alike to passively accept whatever synthetic mush a machine feeds them. Use it as a tool, but keep it firmly in that category. The circular saw doesn't build the house any more than the hammer. Don't let it rot your brain. For fucks sake! Don't let AI or LLM's replace your brain. Keep building real things with your own hands, feet, or whatever appendages you may have available to you (shout out to my differently abled homies).