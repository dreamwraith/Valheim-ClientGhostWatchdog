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
        public static ConfigEntry<string> WarningNoticeMessage = null!;
        public static ConfigEntry<string> RecoveredNoticeMessage = null!;
        public static ConfigEntry<string> DisconnectNoticeMessage = null!;
        public static ConfigEntry<bool> ShowDisconnectReason = null!;
        public static ConfigEntry<string> DisconnectMessage = null!;
        public static ConfigEntry<bool> EnableDebugLogs = null!;

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
                "Server connection lost (Ghost connection prevented). Progress was saved locally.",
                "Custom message displayed on the main menu dialog when disconnected by this watchdog."
            );

            // 4 - Debug Logging
            EnableDebugLogs = Config.Bind(
                "4 - Debug",
                "EnableDebugLogs",
                false,
                "Whether to print detailed debug logs with every watchdog check, timer tick, and network/lifecycle event."
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

                // Stop immediately if state transitions to local host or disconnected
                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    Plugin.LogDebug($"Watchdog tick: Terminating. Local world or server host detected (instance null: {ZNet.instance == null}, IsServer: {ZNet.instance?.IsServer()}).");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                {
                    Plugin.LogDebug($"Watchdog tick: Terminating. Connection status is {ZNet.GetConnectionStatus()} (not Connected).");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                if (Game.instance == null || Game.instance.IsShuttingDown())
                {
                    Plugin.LogDebug("Watchdog tick: Terminating. Game instance is null or shutting down.");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                // Suppress checks during player teleportation or initial loading
                if (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting())
                {
                    Plugin.LogDebug($"Watchdog tick: Skipping check during transient player state (localPlayer null: {Player.m_localPlayer == null}, isTeleporting: {Player.m_localPlayer?.IsTeleporting()}).");
                    continue;
                }

                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null || serverPeer.m_rpc == null)
                {
                    Plugin.LogDebug("Watchdog tick: Server peer or rpc is null, skipping check.");
                    continue;
                }

                bool socketClosed = !serverPeer.m_rpc.IsConnected();
                float timeSincePing = serverPeer.m_rpc.GetTimeSinceLastPing();
                float timeout = Plugin.TimeoutSeconds.Value;
                float warningThreshold = timeout / 2f;

                Plugin.LogDebug($"Watchdog check: pingSilence={timeSincePing:F2}s, socketClosed={socketClosed}, warningThreshold={warningThreshold:F1}s, timeout={timeout:F1}s, warningActive={s_isWarningActive}");

                // Stage 2: Server socket closed or ping silence exceeded timeout threshold -> clean disconnect
                if (socketClosed || timeSincePing >= timeout)
                {
                    string reasonDetail = socketClosed
                        ? "Server socket connection closed"
                        : $"Server stopped responding to pings for {timeSincePing:F1}s (threshold: {timeout:F1}s)";

                    Plugin.Log.LogWarning($"[ClientGhostWatchdog] {reasonDetail}. Forcing clean logout to preserve player progress.");
                    Plugin.LogDebug($"Watchdog trigger: Stage 2 disconnect initiated. Reason: {reasonDetail}");

                    if (MessageHud.instance != null && !string.IsNullOrEmpty(Plugin.DisconnectNoticeMessage.Value))
                    {
                        MessageHud.instance.ShowMessage(
                            MessageHud.MessageType.Center,
                            Plugin.DisconnectNoticeMessage.Value
                        );
                    }

                    ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorDisconnected);

                    if (Plugin.ShowDisconnectReason.Value)
                    {
                        DisconnectReasonManager.SetReason(Plugin.DisconnectMessage.Value);
                    }

                    s_isWarningActive = false;
                    s_watchdogCoroutine = null;

                    // Game.instance.Logout() triggers SavePlayerProfile(setLogoutPoint: true) before exiting world
                    Game.instance.Logout();
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
                // Disable Localize component to prevent vanilla from overwriting custom text with generic "Disconnected"
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
}
