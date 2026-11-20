using System;
using System.Collections;
using ClientGhostWatchdog.Configuration;
using UnityEngine;

namespace ClientGhostWatchdog.Core.Watchdog
{
    internal static class WatchdogManager
    {
        private static readonly WaitForSecondsRealtime s_checkIntervalWait = new WaitForSecondsRealtime(1.0f);
        private static readonly WaitForSecondsRealtime s_disconnectDelayWait = new WaitForSecondsRealtime(5.0f);

        private static Coroutine? s_watchdogCoroutine;
        private static bool s_isWarningActive = false;
        private static int s_lastLoggedRemainingSeconds = -1;
        private static float s_lastNoticeDisplayTime = 0f;
        private static float s_teleportStartTime = -1f;

        public static void Start(string caller)
        {
            Plugin.LogDebug($"WatchdogManager.Start invoked by '{caller}'. Checking eligibility...");

            if (!ZNet.instance || ZNet.instance.IsServer())
            {
                Plugin.LogDebug($"WatchdogManager.Start: Bypassed. Local world or server host detected (instance null: {!ZNet.instance}, IsServer: {ZNet.instance && ZNet.instance.IsServer()}).");
                return;
            }

            if (ModConfig.Instance.TimeoutSeconds.Value <= 0f)
            {
                Plugin.LogDebug("WatchdogManager.Start: Bypassed. Watchdog disabled (TimeoutSeconds <= 0).");
                return;
            }

            Stop($"restarting from {caller}");

            if (Plugin.Instance)
            {
                s_watchdogCoroutine = Plugin.Instance.StartCoroutine(WatchdogRoutine());
                Plugin.Log.LogInfo("Watchdog coroutine started for server connection.");
                Plugin.LogDebug($"WatchdogManager.Start: Coroutine initiated successfully. Interval={ModConfig.Instance.CheckIntervalSeconds.Value}s, Timeout={ModConfig.Instance.TimeoutSeconds.Value}s.");
            }
        }

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
            s_lastLoggedRemainingSeconds = -1;
            s_lastNoticeDisplayTime = 0f;
            s_teleportStartTime = -1f;
        }

        internal static void RegisterConsoleCommands()
        {
            if (Terminal.commands.ContainsKey("cgw_simulatesever"))
            {
                return;
            }

            new Terminal.ConsoleCommand(
                "cgw_simulatesever",
                "Simulates an abrupt external peer severance (ServerCharacters behavior) for watchdog testing.",
                (Terminal.ConsoleEventArgs _) => SimulateSeveredPeer()
            );
        }

        private static IEnumerator DisconnectRoutine(string reasonDetail)
        {
            Plugin.Log.LogWarning($"{reasonDetail}. Forcing clean logout to preserve player progress.");
            s_isWarningActive = false;

            // only show the watchdog banner and dialog if the game still thinks the client is connected, so real errors like ErrorKicked or ErrorBanned stay intact
            if (ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
            {
                string disconnectNotice = ModConfig.Instance.DisconnectNoticeMessage.Value;
                if (MessageHud.instance && !string.IsNullOrEmpty(disconnectNotice))
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, disconnectNotice);
                }

                if (ModConfig.Instance.ShowDisconnectReason.Value)
                {
                    DisconnectReasonManager.SetReason(ModConfig.Instance.DisconnectMessage.Value);
                }

                float delay = ModConfig.Instance.DisconnectDelaySeconds.Value;
                if (delay > 0f)
                {
                    s_disconnectDelayWait.Reset();
                    s_disconnectDelayWait.waitTime = delay;
                    yield return s_disconnectDelayWait;
                }

                ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorDisconnected);
            }
            else
            {
                Plugin.LogDebug($"DisconnectRoutine: Preserving existing connection status ({ZNet.GetConnectionStatus()}).");
            }

            s_watchdogCoroutine = null;

            if (Game.instance && !Game.instance.IsShuttingDown())
            {
                Game.instance.Logout();
            }
        }

        private static IEnumerator WatchdogRoutine()
        {
            Plugin.LogDebug("WatchdogManager: Coroutine execution loop entered.");
            s_checkIntervalWait.Reset();

            while (true)
            {
                s_checkIntervalWait.waitTime = ModConfig.Instance.CheckIntervalSeconds.Value;
                yield return s_checkIntervalWait;

                if (ModConfig.Instance.TimeoutSeconds.Value <= 0f)
                {
                    Plugin.LogDebug("Watchdog tick: TimeoutSeconds <= 0 (watchdog disabled via config).");
                    s_isWarningActive = false;
                    continue;
                }

                if (!ZNet.instance || ZNet.instance.IsServer())
                {
                    Plugin.LogDebug($"Watchdog tick: Terminating. Local world or server host detected (instance null: {!ZNet.instance}, IsServer: {ZNet.instance && ZNet.instance.IsServer()}).");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                if (!Game.instance || Game.instance.IsShuttingDown())
                {
                    Plugin.LogDebug("Watchdog tick: Terminating. Game instance is null or shutting down.");
                    s_watchdogCoroutine = null;
                    yield break;
                }

                if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                {
                    if (Player.m_localPlayer)
                    {
                        yield return DisconnectRoutine($"Connection status lost ({ZNet.GetConnectionStatus()})");
                    }
                    else
                    {
                        Plugin.LogDebug($"Watchdog tick: Initial connection status is {ZNet.GetConnectionStatus()} before player spawn. Terminating.");
                        s_watchdogCoroutine = null;
                    }
                    yield break;
                }

                if (!Player.m_localPlayer)
                {
                    Plugin.LogDebug("Watchdog tick: Waiting for local player to spawn into world...");
                    continue;
                }

                if (Player.m_localPlayer.IsTeleporting())
                {
                    float maxTeleport = ModConfig.Instance.TeleportTimeoutSeconds.Value;

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

                    Plugin.Log.LogWarning($"Player has been in teleporting state for {teleportElapsed:F1}s (ceiling: {maxTeleport:F1}s). Resuming connection health checks.");
                }
                else
                {
                    s_teleportStartTime = -1f;
                }

                // if another mod like ServerCharacters calls ZNet.Disconnect on the server peer, GetServerPeer goes null without logging the player out
                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null)
                {
                    yield return DisconnectRoutine("Server peer connection lost (disposed by network layer)");
                    yield break;
                }

                if (!serverPeer.m_rpc.IsConnected())
                {
                    yield return DisconnectRoutine("Server socket connection closed");
                    yield break;
                }

                float timeSincePing = serverPeer.m_rpc.GetTimeSinceLastPing();
                float timeout = ModConfig.Instance.TimeoutSeconds.Value;
                float warningThreshold = timeout / 2f;

                Plugin.LogDebug($"Watchdog check: pingSilence={timeSincePing:F2}s, warningThreshold={warningThreshold:F1}s, timeout={timeout:F1}s, warningActive={s_isWarningActive}");

                if (timeSincePing >= timeout)
                {
                    yield return DisconnectRoutine($"Server stopped responding to pings for {timeSincePing:F1}s (threshold: {timeout:F1}s)");
                    yield break;
                }

                if (timeSincePing >= warningThreshold)
                {
                    int remainingSeconds = Mathf.Max(1, Mathf.CeilToInt(timeout - timeSincePing));

                    if (!s_isWarningActive)
                    {
                        s_isWarningActive = true;
                        Plugin.Log.LogWarning($"Server silence detected ({timeSincePing:F1}s >= halfway threshold {warningThreshold:F1}s). Displaying reconnect warning.");
                        Plugin.LogDebug($"Watchdog trigger: Stage 1 warning activated. remainingSeconds={remainingSeconds}s.");
                    }

                    float now = Time.unscaledTime;
                    if (remainingSeconds != s_lastLoggedRemainingSeconds || now - s_lastNoticeDisplayTime >= 1.5f)
                    {
                        s_lastLoggedRemainingSeconds = remainingSeconds;
                        s_lastNoticeDisplayTime = now;

                        string warningTemplate = ModConfig.Instance.WarningNoticeMessage.Value;
                        if (MessageHud.instance && !string.IsNullOrEmpty(warningTemplate))
                        {
                            string formatted = FormatNotice(warningTemplate, remainingSeconds);
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, formatted);
                            Plugin.LogDebug($"Watchdog notice refreshed: \"{formatted}\"");
                        }
                    }
                }
                else if (s_isWarningActive)
                {
                    s_isWarningActive = false;
                    s_lastLoggedRemainingSeconds = -1;
                    Plugin.Log.LogInfo($"Server connection recovered after {timeSincePing:F1}s silence.");
                    Plugin.LogDebug($"Watchdog trigger: Recovery detected (silence dropped to {timeSincePing:F2}s). Warning cleared.");

                    string recoveredNotice = ModConfig.Instance.RecoveredNoticeMessage.Value;
                    if (MessageHud.instance && !string.IsNullOrEmpty(recoveredNotice))
                    {
                        MessageHud.instance.ShowMessage(
                            MessageHud.MessageType.Center,
                            recoveredNotice
                        );
                    }
                }
            }
        }

        private static string FormatNotice(string template, int remainingSeconds)
        {
            try
            {
                return template.IndexOf("{0}", StringComparison.Ordinal) >= 0
                    ? string.Format(template, remainingSeconds)
                    : template;
            }
            catch (FormatException)
            {
                return template;
            }
        }

        // mimics a mod like ServerCharacters calling ZNet.Disconnect on the server peer without triggering logout
        internal static void SimulateSeveredPeer()
        {
            if (!ModConfig.Instance.EnableDebugLogs.Value)
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
