using BepInEx.Configuration;
using UnityEngine;

namespace ClientGhostWatchdog.Configuration
{
    internal static class ModConfig
    {
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

        public static void Initialize(ConfigFile config)
        {
            // 1 - General Watchdog Settings
            TimeoutSeconds = config.Bind(
                "1 - General",
                "TimeoutSeconds",
                30f,
                new ConfigDescription(
                    "Total seconds of server silence before disconnecting to prevent zombie/ghost client desyncs. Halfway through this time (e.g. 15s at default 30s), a warning is displayed. Set to 0 to disable.",
                    new AcceptableValueRange<float>(0f, 300f))
            );

            CheckIntervalSeconds = config.Bind(
                "1 - General",
                "CheckIntervalSeconds",
                1.0f,
                new ConfigDescription(
                    "Interval in seconds between watchdog checks in the background coroutine timer.",
                    new AcceptableValueRange<float>(1.0f, 5.0f))
            );

            TeleportTimeoutSeconds = config.Bind(
                "1 - General",
                "TeleportTimeoutSeconds",
                30f,
                new ConfigDescription(
                    "Maximum seconds a player can remain in a teleporting/portal state before connection health checks resume. Prevents infinite limbo if a portal handshake hangs. Set to 0 to disable ceiling.",
                    new AcceptableValueRange<float>(0f, 180f))
            );

            // 2 - On-Screen Notices
            WarningNoticeMessage = config.Bind(
                "2 - On-Screen Notices",
                "WarningNoticeMessage",
                "Server connection lost, attempting to reconnect... ({0}s)",
                "Large on-screen notice displayed halfway through TimeoutSeconds. {0} is replaced with remaining seconds until disconnect."
            );

            RecoveredNoticeMessage = config.Bind(
                "2 - On-Screen Notices",
                "RecoveredNoticeMessage",
                "Server connection restored.",
                "Large on-screen notice displayed if server connection recovers before timing out."
            );

            DisconnectNoticeMessage = config.Bind(
                "2 - On-Screen Notices",
                "DisconnectNoticeMessage",
                "Server connection lost. Disconnecting...",
                "Large on-screen notice displayed when the timeout is reached and the player is being disconnected."
            );

            DisconnectDelaySeconds = config.Bind(
                "2 - On-Screen Notices",
                "DisconnectDelaySeconds",
                5.0f,
                new ConfigDescription(
                    "Seconds to wait after displaying the on-screen disconnect notice before executing logout, giving players time to read the notice. Set to 0 for instantaneous logout.",
                    new AcceptableValueRange<float>(0f, 30.0f))
            );

            // 3 - Main Menu Dialog
            ShowDisconnectReason = config.Bind(
                "3 - Main Menu Dialog",
                "ShowDisconnectReason",
                true,
                "Whether to display an explanatory message on the main menu dialog after being disconnected by this watchdog."
            );

            DisconnectMessage = config.Bind(
                "3 - Main Menu Dialog",
                "DisconnectMessage",
                "Server connection lost (Ghost connection prevented). Progress was saved.",
                "Custom message displayed on the main menu dialog when disconnected by this watchdog."
            );

            // 4 - Debug Logging
            EnableDebugLogs = config.Bind(
                "4 - Debug",
                "EnableDebugLogs",
                false,
                "Whether to print detailed debug logs with every watchdog check, timer tick, and network/lifecycle event."
            );

            DebugSimulateSeverKey = config.Bind(
                "4 - Debug",
                "DebugSimulateSeverKey",
                KeyCode.None,
                "DEBUG ONLY: Keybind to simulate an abrupt external peer disconnection (ServerCharacters behavior). Requires EnableDebugLogs to be true. Keep as None for normal play."
            );
        }
    }
}
