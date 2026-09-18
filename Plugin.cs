using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ClientGhostWatchdog
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "dreamwraith.ClientGhostWatchdog";
        public const string ModName = "ClientGhostWatchdog";
        public const string ModVersion = VersionInfo.Version;

        internal static Plugin? Instance { get; private set; }
        internal static ManualLogSource Log = null!;
        private Harmony? _harmony;

        // Configuration entries
        public static ConfigEntry<float> TimeoutSeconds = null!;
        public static ConfigEntry<float> CheckIntervalSeconds = null!;
        public static ConfigEntry<float> TeleportTimeoutSeconds = null!;
        public static ConfigEntry<string> WarningNoticeMessage = null!;
        public static ConfigEntry<string> RecoveredNoticeMessage = null!;
        public static ConfigEntry<string> DisconnectNoticeMessage = null!;
        public static ConfigEntry<float> DisconnectDelaySeconds = null!;
        public static ConfigEntry<bool> ShowDisconnectReason = null!;
        public static ConfigEntry<string> DisconnectMessage = null!;
        public static ConfigEntry<bool> EnableDebugLogs = null!;
        public static ConfigEntry<KeyCode> DebugSimulateSeverKey = null!;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // 1 - General Watchdog Settings
            TimeoutSeconds = Config.Bind(
                "1 - General",
                "TimeoutSeconds",
                30f,
                new ConfigDescription(
                    "Total seconds of server silence before disconnecting to prevent zombie/ghost client desyncs. Halfway through this time (e.g. 15s at default 30s), a warning is displayed. Set to 0 to disable.",
                    new AcceptableValueRange<float>(0f, 300f))
            );

            CheckIntervalSeconds = Config.Bind(
                "1 - General",
                "CheckIntervalSeconds",
                1.0f,
                new ConfigDescription(
                    "Interval in seconds between watchdog checks in the background coroutine timer.",
                    new AcceptableValueRange<float>(1.0f, 5.0f))
            );

            TeleportTimeoutSeconds = Config.Bind(
                "1 - General",
                "TeleportTimeoutSeconds",
                30f,
                new ConfigDescription(
                    "Maximum seconds a player can remain in a teleporting/portal state before connection health checks resume. Prevents infinite limbo if a portal handshake hangs. Set to 0 to disable ceiling.",
                    new AcceptableValueRange<float>(0f, 180f))
            );

            // 2 - On-Screen Notices
            WarningNoticeMessage = Config.Bind(
                "2 - On-Screen Notices",
                "WarningNoticeMessage",
                "Server connection lost, attempting to reconnect... ({0}s)",
                "Large on-screen notice displayed halfway through the check period. {0} is replaced with remaining seconds until disconnect."
            );

            RecoveredNoticeMessage = Config.Bind(
                "2 - On-Screen Notices",
                "RecoveredNoticeMessage",
                "Server connection restored.",
                "Large on-screen notice displayed if server connection recovers before timing out."
            );

            DisconnectNoticeMessage = Config.Bind(
                "2 - On-Screen Notices",
                "DisconnectNoticeMessage",
                "Server connection lost. Disconnecting...",
                "Large on-screen notice displayed when the timeout is reached and the player is being disconnected."
            );

            DisconnectDelaySeconds = Config.Bind(
                "2 - On-Screen Notices",
                "DisconnectDelaySeconds",
                5.0f,
                new ConfigDescription(
                    "Seconds to wait after displaying the on-screen disconnect notice before executing logout, giving players time to read the notice. Set to 0 for instantaneous logout.",
                    new AcceptableValueRange<float>(0f, 30.0f))
            );

            // 3 - Main Menu Dialog
            ShowDisconnectReason = Config.Bind(
                "3 - Main Menu Dialog",
                "ShowDisconnectReason",
                true,
                "Whether to display an explanatory message on the main menu dialog after being disconnected by this watchdog."
            );

            DisconnectMessage = Config.Bind(
                "3 - Main Menu Dialog",
                "DisconnectMessage",
                "Server connection lost (Ghost connection prevented). Progress was saved.",
                "Custom message displayed on the main menu dialog when disconnected by this watchdog."
            );

            // 4 - Debug Logging
            EnableDebugLogs = Config.Bind(
                "4 - Debug",
                "EnableDebugLogs",
                false,
                "Whether to print detailed debug logs with every watchdog check, timer tick, and network/lifecycle event."
            );

            DebugSimulateSeverKey = Config.Bind(
                "4 - Debug",
                "DebugSimulateSeverKey",
                KeyCode.None,
                "DEBUG ONLY: Keybind to simulate an abrupt external peer disconnection (ServerCharacters behavior). Requires EnableDebugLogs to be true. Keep as None for normal play."
            );

            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully.");
        }

        private void OnDestroy()
        {
            WatchdogManager.Stop("Plugin.OnDestroy");
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            if (EnableDebugLogs != null && EnableDebugLogs.Value && DebugSimulateSeverKey != null && DebugSimulateSeverKey.Value != KeyCode.None)
            {
                if (Input.GetKeyDown(DebugSimulateSeverKey.Value))
                {
                    SimulateSeveredPeer();
                }
            }
        }

        // Simulates badly behaved third-party mod behavior (e.g. ServerCharacters abruptly calling ZNet.Disconnect on the server peer)
        internal static void SimulateSeveredPeer()
        {
            if (EnableDebugLogs == null || !EnableDebugLogs.Value)
            {
                Log.LogWarning("[DEBUG TEST] Cannot simulate peer disconnect: EnableDebugLogs must be true in config.");
                return;
            }

            if (ZNet.instance == null || ZNet.instance.IsServer() || Player.m_localPlayer == null)
            {
                Log.LogWarning("[DEBUG TEST] Cannot simulate peer disconnect: Must be in an active multiplayer session as a client.");
                return;
            }

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null)
            {
                Log.LogWarning("[DEBUG TEST] Server peer is already null.");
                return;
            }

            Log.LogWarning("[DEBUG TEST] Intentionally calling ZNet.instance.Disconnect(serverPeer) to simulate badly behaved third-party mod behavior...");
            ZNet.instance.Disconnect(serverPeer);
        }

        // Conditionally emits detailed debug messages when EnableDebugLogs is true
        internal static void LogDebug(string message)
        {
            if (EnableDebugLogs != null && EnableDebugLogs.Value)
            {
                Log.LogInfo($"[DEBUG] {message}");
            }
        }
    }

    /// <summary>
    /// Manages the background coroutine watchdog that monitors remote dedicated server responsiveness.
    /// </summary>
    public static class WatchdogManager
    {
        private static Coroutine? s_watchdogCoroutine;
        private static bool s_isWarningActive = false;
        private static int s_lastLoggedRemainingSec = -1;
        private static float s_lastNoticeDisplayTime = 0f;
        private static float s_teleportStartTime = -1f;

        public static bool IsRunning => s_watchdogCoroutine != null;
        public static bool IsWarningActive => s_isWarningActive;

        // Starts the watchdog coroutine if connecting as a client to a remote server
        public static void Start(string caller)
        {
            Plugin.LogDebug($"WatchdogManager.Start invoked by '{caller}'. Checking eligibility...");

            // Bypassed on local singleplayer worlds and local server hosts
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                Plugin.LogDebug($"WatchdogManager.Start: Bypassed. Local world or server host detected (instance null: {ZNet.instance == null}, IsServer: {ZNet.instance?.IsServer()}).");
                return;
            }

            if (Plugin.TimeoutSeconds.Value <= 0f)
            {
                Plugin.LogDebug("WatchdogManager.Start: Bypassed. Watchdog disabled (TimeoutSeconds <= 0).");
                return;
            }

            Stop($"restarting from {caller}");

            if (Plugin.Instance != null)
            {
                s_watchdogCoroutine = Plugin.Instance.StartCoroutine(WatchdogRoutine());
                Plugin.Log.LogInfo("[ClientGhostWatchdog] Watchdog coroutine started for server connection.");
                Plugin.LogDebug($"WatchdogManager.Start: Coroutine initiated successfully. Interval={Plugin.CheckIntervalSeconds.Value}s, Timeout={Plugin.TimeoutSeconds.Value}s.");
            }
        }

        // Halts active watchdog coroutine and clears tracking flags
        public static void Stop(string reason)
        {
            if (s_watchdogCoroutine != null)
            {
                Plugin.LogDebug($"WatchdogManager.Stop: Halting active coroutine. Reason: {reason}.");
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.StopCoroutine(s_watchdogCoroutine);
                }
                s_watchdogCoroutine = null;
            }
            else
            {
                Plugin.LogDebug($"WatchdogManager.Stop: No active coroutine to stop. Reason: {reason}.");
            }

            s_isWarningActive = false;
            s_lastLoggedRemainingSec = -1;
            s_lastNoticeDisplayTime = 0f;
            s_teleportStartTime = -1f;
        }

        // Safely displays the disconnect notice, waits for the configured delay, and logs out
        private static IEnumerator DisconnectRoutine(string reasonDetail)
        {
            Plugin.Log.LogWarning($"[ClientGhostWatchdog] {reasonDetail}. Forcing clean logout to preserve player progress.");
            s_isWarningActive = false;

            // Only display watchdog banner and custom menu dialog if connection was still Connected.
            // If native already registered a specific error (e.g. ErrorKicked, ErrorBanned, ErrorFull),
            // preserve it so the player is not misled by a watchdog banner and sees the true native disconnect reason.
            if (ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
            {
                if (MessageHud.instance != null && !string.IsNullOrEmpty(Plugin.DisconnectNoticeMessage.Value))
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Plugin.DisconnectNoticeMessage.Value);
                }

                if (Plugin.ShowDisconnectReason.Value)
                {
                    DisconnectReasonManager.SetReason(Plugin.DisconnectMessage.Value);
                }

                float delay = Mathf.Clamp(Plugin.DisconnectDelaySeconds?.Value ?? 5f, 0f, 30f);
                if (delay > 0f)
                {
                    yield return new WaitForSecondsRealtime(delay);
                }

                ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorDisconnected);
            }
            else
            {
                Plugin.LogDebug($"DisconnectRoutine: Preserving existing native connection status ({ZNet.GetConnectionStatus()}).");
            }

            s_watchdogCoroutine = null;

            if (Game.instance != null && !Game.instance.IsShuttingDown())
            {
                Game.instance.Logout();
            }
        }

        // Main watchdog loop checking server responsiveness at configured intervals
        private static IEnumerator WatchdogRoutine()
        {
            Plugin.LogDebug("WatchdogManager: Coroutine execution loop entered.");

            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(1.0f, Plugin.CheckIntervalSeconds.Value));

                if (Plugin.TimeoutSeconds.Value <= 0f)
                {
                    Plugin.LogDebug("Watchdog tick: TimeoutSeconds <= 0 (watchdog disabled via config).");
                    s_isWarningActive = false;
                    continue;
                }

                // 1. Guard against local solo singleplayer and local server hosts
                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    Plugin.LogDebug($"Watchdog tick: Terminating. Local world or server host detected (instance null: {ZNet.instance == null}, IsServer: {ZNet.instance?.IsServer()}).");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                // 2. Terminate if game is shutting down
                if (Game.instance == null || Game.instance.IsShuttingDown())
                {
                    Plugin.LogDebug("Watchdog tick: Terminating. Game instance is null or shutting down.");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                // 3. Consolidated connection status check (handles both in-game and pre-spawn disconnects)
                if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                {
                    if (Player.m_localPlayer != null)
                    {
                        // In-game: save profile and show disconnect notice
                        yield return DisconnectRoutine($"Connection status lost ({ZNet.GetConnectionStatus()})");
                    }
                    else
                    {
                        // Pre-spawn loading: connection aborted before entering world, stop silently
                        Plugin.LogDebug($"Watchdog tick: Initial connection status is {ZNet.GetConnectionStatus()} before player spawn. Terminating.");
                        s_watchdogCoroutine = null;
                    }
                    yield break;
                }

                // 4. Pre-spawn wait: Connected, but character has not spawned into the world yet
                if (Player.m_localPlayer == null)
                {
                    Plugin.LogDebug("Watchdog tick: Waiting for local player to spawn into world...");
                    continue;
                }

                // 5. Player is actively in the world. Handle teleportation transient state with a timeout guard
                if (Player.m_localPlayer.IsTeleporting())
                {
                    float maxTeleport = Plugin.TeleportTimeoutSeconds.Value;

                    // If configured to 0 or negative, suppress checks indefinitely while teleporting
                    if (maxTeleport <= 0f)
                    {
                        Plugin.LogDebug("Watchdog tick: Skipping check during player teleportation (ceiling disabled via config).");
                        continue;
                    }

                    if (s_teleportStartTime < 0f)
                    {
                        s_teleportStartTime = Time.unscaledTime;
                    }

                    float teleportElapsed = Time.unscaledTime - s_teleportStartTime;
                    if (teleportElapsed < maxTeleport)
                    {
                        Plugin.LogDebug($"Watchdog tick: Skipping check during player teleportation (elapsed: {teleportElapsed:F1}s / max: {maxTeleport:F1}s).");
                        continue;
                    }

                    Plugin.Log.LogWarning($"[ClientGhostWatchdog] Player has been in teleporting state for {teleportElapsed:F1}s (ceiling: {maxTeleport:F1}s). Resuming connection health checks.");
                }
                else
                {
                    s_teleportStartTime = -1f;
                }

                // 6. Check server peer existence and socket health
                // In Valheim, if another mod (such as ServerCharacters) or the network layer calls ZNet.instance.Disconnect(peer),
                // the server peer is removed and disposed. In multiplayer, a null server peer while in-world means connection is severed!
                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null || serverPeer.m_rpc == null)
                {
                    yield return DisconnectRoutine("Server peer connection lost (disposed by network layer)");
                    yield break;
                }

                bool socketClosed = !serverPeer.m_rpc.IsConnected();
                if (socketClosed)
                {
                    yield return DisconnectRoutine("Server socket connection closed");
                    yield break;
                }

                float timeSincePing = serverPeer.m_rpc.GetTimeSinceLastPing();
                float timeout = Plugin.TimeoutSeconds.Value;
                float warningThreshold = timeout / 2f;

                Plugin.LogDebug($"Watchdog check: pingSilence={timeSincePing:F2}s, socketClosed={socketClosed}, warningThreshold={warningThreshold:F1}s, timeout={timeout:F1}s, warningActive={s_isWarningActive}");

                // Stage 2: Ping silence exceeded timeout threshold -> clean disconnect
                if (timeSincePing >= timeout)
                {
                    yield return DisconnectRoutine($"Server stopped responding to pings for {timeSincePing:F1}s (threshold: {timeout:F1}s)");
                    yield break;
                }

                // Stage 1: Halfway warning notice with dynamic countdown
                if (timeSincePing >= warningThreshold)
                {
                    int remainingSec = Mathf.Max(1, Mathf.CeilToInt(timeout - timeSincePing));

                    if (!s_isWarningActive)
                    {
                        s_isWarningActive = true;
                        Plugin.Log.LogWarning($"[ClientGhostWatchdog] Server silence detected ({timeSincePing:F1}s >= halfway threshold {warningThreshold:F1}s). Displaying reconnect warning.");
                        Plugin.LogDebug($"Watchdog trigger: Stage 1 warning activated. remainingSec={remainingSec}s.");
                    }

                    float now = Time.unscaledTime;
                    if (remainingSec != s_lastLoggedRemainingSec || now - s_lastNoticeDisplayTime >= 1.5f)
                    {
                        s_lastLoggedRemainingSec = remainingSec;
                        s_lastNoticeDisplayTime = now;

                        if (MessageHud.instance != null && !string.IsNullOrEmpty(Plugin.WarningNoticeMessage.Value))
                        {
                            string formatted = FormatNotice(Plugin.WarningNoticeMessage.Value, remainingSec);
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, formatted);
                            Plugin.LogDebug($"Watchdog notice refreshed: \"{formatted}\"");
                        }
                    }
                }
                // Auto-Recovery: Server ping responded before reaching timeout
                else if (s_isWarningActive)
                {
                    s_isWarningActive = false;
                    s_lastLoggedRemainingSec = -1;
                    Plugin.Log.LogInfo($"[ClientGhostWatchdog] Server connection recovered after {timeSincePing:F1}s silence.");
                    Plugin.LogDebug($"Watchdog trigger: Recovery detected (silence dropped to {timeSincePing:F2}s). Warning cleared.");

                    if (MessageHud.instance != null && !string.IsNullOrEmpty(Plugin.RecoveredNoticeMessage.Value))
                    {
                        MessageHud.instance.ShowMessage(
                            MessageHud.MessageType.Center,
                            Plugin.RecoveredNoticeMessage.Value
                        );
                    }
                }
            }
        }

        // Formats the warning template with the countdown value if {0} placeholder exists
        private static string FormatNotice(string template, int remainingSec)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            try
            {
                return template.Contains("{0}") ? string.Format(template, remainingSec) : template;
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }

    /// <summary>
    /// Preserves and displays custom disconnect explanations across scene transitions (world unload to main menu).
    /// </summary>
    internal static class DisconnectReasonManager
    {
        private static string? s_pendingReason;
        private static float s_reasonTimestamp = 0f;

        // 300s TTL accommodates long save times and scene load durations while preventing stale dialogs later
        private const float ReasonTtlSeconds = 300f;

        public static string? PendingReason
        {
            get
            {
                if (string.IsNullOrEmpty(s_pendingReason)) return null;
                float elapsed = Time.unscaledTime - s_reasonTimestamp;
                if (elapsed > ReasonTtlSeconds)
                {
                    Plugin.LogDebug($"DisconnectReasonManager: Pending reason expired (elapsed {elapsed:F1}s > TTL {ReasonTtlSeconds}s).");
                    s_pendingReason = null;
                    s_reasonTimestamp = 0f;
                    return null;
                }
                return s_pendingReason;
            }
        }

        // Stores custom disconnect reason and records timestamp
        public static void SetReason(string reason)
        {
            s_pendingReason = reason;
            s_reasonTimestamp = Time.unscaledTime;
            Plugin.LogDebug($"DisconnectReasonManager: Registered pending reason (TTL={ReasonTtlSeconds}s): \"{reason}\"");
        }

        // Clears stored reason
        public static void Clear()
        {
            s_pendingReason = null;
            s_reasonTimestamp = 0f;
            Plugin.LogDebug("DisconnectReasonManager: Cleared pending reason.");
        }

        // Applies pending disconnect reason to FejdStartup and reinforces on next frame
        public static void ApplyToMenu(FejdStartup startup)
        {
            string? reason = PendingReason;
            if (!string.IsNullOrEmpty(reason))
            {
                ApplyTextAndShowPanel(startup, reason!);
                startup.StartCoroutine(ReinforceNoticeRoutine(startup, reason!));
            }
        }

        private static void ApplyTextAndShowPanel(FejdStartup startup, string reason)
        {
            if (startup.m_connectionFailedPanel != null)
            {
                startup.m_connectionFailedPanel.SetActive(true);
            }

            if (startup.m_connectionFailedError != null)
            {
                // Disable Localize component to prevent native from overwriting custom text with generic "Disconnected"
                var localize = startup.m_connectionFailedError.GetComponent("Localize") as Behaviour;
                if (localize != null)
                {
                    localize.enabled = false;
                }

                startup.m_connectionFailedError.text = reason;
                Plugin.LogDebug($"DisconnectReasonManager: Applied disconnect reason to FejdStartup: \"{reason}\"");
            }
        }

        // Re-applies text on next frame in case UI animators or layout rebuilds reset text initially
        private static IEnumerator ReinforceNoticeRoutine(FejdStartup startup, string reason)
        {
            yield return null;

            if (startup != null && startup.m_connectionFailedError != null)
            {
                ApplyTextAndShowPanel(startup, reason);
                Plugin.LogDebug("DisconnectReasonManager: Reinforced disconnect reason on next frame.");
            }
        }
    }

    // Start watchdog once the client successfully completes peer handshake with dedicated server
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    public static class ZNet_RPC_PeerInfo_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            Plugin.LogDebug("ZNet.RPC_PeerInfo postfix triggered.");

            if (__instance.IsServer())
            {
                Plugin.LogDebug("ZNet.RPC_PeerInfo: Host is server; ignoring watchdog start.");
                return;
            }

            WatchdogManager.Start("ZNet.RPC_PeerInfo");
        }
    }

    // Ensure watchdog runs when entering a game session connected to a dedicated server
    [HarmonyPatch(typeof(Game), "Start")]
    public static class Game_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.LogDebug("Game.Start postfix triggered.");
            DisconnectReasonManager.Clear();

            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                Plugin.LogDebug("Game.Start: Local world or server host detected; ignoring watchdog start.");
                return;
            }

            WatchdogManager.Start("Game.Start");
        }
    }

    // Stop watchdog upon leaving the game world
    [HarmonyPatch(typeof(Game), "OnDestroy")]
    public static class Game_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.LogDebug("Game.OnDestroy postfix triggered.");
            WatchdogManager.Stop("Game.OnDestroy");
        }
    }


    // Stop watchdog on network shutdown
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    public static class ZNet_Shutdown_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.LogDebug("ZNet.Shutdown postfix triggered.");
            WatchdogManager.Stop("ZNet.Shutdown");
        }
    }

    // Display disconnect reason when FejdStartup shows connection error dialog
    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    public static class FejdStartup_ShowConnectError_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(FejdStartup __instance)
        {
            Plugin.LogDebug("FejdStartup.ShowConnectError postfix triggered.");
            WatchdogManager.Stop("FejdStartup.ShowConnectError");
            DisconnectReasonManager.ApplyToMenu(__instance);
        }
    }

    // Check for pending disconnect reason when main menu initializes
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    public static class FejdStartup_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(FejdStartup __instance)
        {
            Plugin.LogDebug("FejdStartup.Start postfix triggered.");
            WatchdogManager.Stop("FejdStartup.Start");
            DisconnectReasonManager.ApplyToMenu(__instance);
        }
    }

    // Re-enable Localize component and clear disconnect reason once player acknowledges popup
    [HarmonyPatch(typeof(FejdStartup), "OnConnectionFailedOk")]
    public static class FejdStartup_OnConnectionFailedOk_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(FejdStartup __instance)
        {
            Plugin.LogDebug("FejdStartup.OnConnectionFailedOk postfix triggered.");

            if (__instance.m_connectionFailedError != null)
            {
                var localize = __instance.m_connectionFailedError.GetComponent("Localize") as Behaviour;
                if (localize != null)
                {
                    localize.enabled = true;
                }
            }

            DisconnectReasonManager.Clear();
        }
    }

    // Register debug console command for simulating badly behaved peer severance
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Terminal_InitTerminal_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Terminal.commands.ContainsKey("cgw_simulatesever"))
            {
                return;
            }

            new Terminal.ConsoleCommand(
                "cgw_simulatesever",
                "Simulates an abrupt external peer severance (ServerCharacters behavior) for watchdog testing.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    Plugin.SimulateSeveredPeer();
                }
            );
        }
    }
}
