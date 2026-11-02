using System;
using System.Collections;
using ClientGhostWatchdog.Configuration;
using UnityEngine;

namespace ClientGhostWatchdog.Watchdog
{
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

            if (ModConfig.TimeoutSeconds.Value <= 0f)
            {
                Plugin.LogDebug("WatchdogManager.Start: Bypassed. Watchdog disabled (TimeoutSeconds <= 0).");
                return;
            }

            Stop($"restarting from {caller}");

            if (Plugin.Instance)
            {
                s_watchdogCoroutine = Plugin.Instance.StartCoroutine(WatchdogRoutine());
                Plugin.Log.LogInfo("[ClientGhostWatchdog] Watchdog coroutine started for server connection.");
                Plugin.LogDebug($"WatchdogManager.Start: Coroutine initiated successfully. Interval={ModConfig.CheckIntervalSeconds.Value}s, Timeout={ModConfig.TimeoutSeconds.Value}s.");
            }
        }

        // Halts active watchdog coroutine and clears tracking flags
        public static void Stop(string reason)
        {
            if (s_watchdogCoroutine != null)
            {
                Plugin.LogDebug($"WatchdogManager.Stop: Halting active coroutine. Reason: {reason}.");
                if (Plugin.Instance)
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
                if (MessageHud.instance && !string.IsNullOrEmpty(ModConfig.DisconnectNoticeMessage.Value))
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, ModConfig.DisconnectNoticeMessage.Value);
                }

                if (ModConfig.ShowDisconnectReason.Value)
                {
                    DisconnectReasonManager.SetReason(ModConfig.DisconnectMessage.Value);
                }

                float delay = Mathf.Clamp(ModConfig.DisconnectDelaySeconds?.Value ?? 5f, 0f, 30f);
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

            if (Game.instance && !Game.instance.IsShuttingDown())
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
                yield return new WaitForSecondsRealtime(Mathf.Max(1.0f, ModConfig.CheckIntervalSeconds.Value));

                if (ModConfig.TimeoutSeconds.Value <= 0f)
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
                    float maxTeleport = ModConfig.TeleportTimeoutSeconds.Value;

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
                float timeout = ModConfig.TimeoutSeconds.Value;
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

                        if (MessageHud.instance != null && !string.IsNullOrEmpty(ModConfig.WarningNoticeMessage.Value))
                        {
                            string formatted = FormatNotice(ModConfig.WarningNoticeMessage.Value, remainingSec);
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

                    if (MessageHud.instance != null && !string.IsNullOrEmpty(ModConfig.RecoveredNoticeMessage.Value))
                    {
                        MessageHud.instance.ShowMessage(
                            MessageHud.MessageType.Center,
                            ModConfig.RecoveredNoticeMessage.Value
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

        // Simulates badly behaved third-party mod behavior (e.g. ServerCharacters abruptly calling ZNet.Disconnect on the server peer)
        internal static void SimulateSeveredPeer()
        {
            if (!ModConfig.EnableDebugLogs.Value)
            {
                Plugin.Log.LogWarning("[DEBUG TEST] Cannot simulate peer disconnect: EnableDebugLogs must be true in config.");
                return;
            }

            if (!ZNet.instance || ZNet.instance.IsServer() || !Player.m_localPlayer)
            {
                Plugin.Log.LogWarning("[DEBUG TEST] Cannot simulate peer disconnect: Must be in an active multiplayer session as a client.");
                return;
            }

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null)
            {
                Plugin.Log.LogWarning("[DEBUG TEST] Server peer is already null.");
                return;
            }

            Plugin.Log.LogWarning("[DEBUG TEST] Intentionally calling ZNet.instance.Disconnect(serverPeer) to simulate badly behaved third-party mod behavior...");
            ZNet.instance.Disconnect(serverPeer);
        }
    }
}
