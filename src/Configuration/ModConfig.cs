using BepInEx.Configuration;
using UnityEngine;

namespace ClientGhostWatchdog.Configuration
{
    internal sealed class ModConfig(ConfigFile config)
    {
        public static ModConfig Instance { get; private set; } = null!;

        public ConfigEntry<float> TimeoutSeconds { get; } = Bind(
            config,
            "1 - General",
            "TimeoutSeconds",
            30f,
            "Total seconds of server silence before disconnecting to prevent zombie/ghost client desyncs. Halfway through this time (e.g. 15s at default 30s), a warning is displayed. Set to 0 to disable.",
            new AcceptableValueRange<float>(0f, 300f));

        public ConfigEntry<float> CheckIntervalSeconds { get; } = Bind(
            config,
            "1 - General",
            "CheckIntervalSeconds",
            1.0f,
            "Interval in seconds between watchdog checks in the background coroutine timer.",
            new AcceptableValueRange<float>(1.0f, 5.0f));

        public ConfigEntry<float> TeleportTimeoutSeconds { get; } = Bind(
            config,
            "1 - General",
            "TeleportTimeoutSeconds",
            30f,
            "Maximum seconds a player can remain in a teleporting state before connection health checks resume. Prevents permanent stalls if a portal handshake hangs. Set to 0 to disable the limit.",
            new AcceptableValueRange<float>(0f, 180f));

        public ConfigEntry<string> WarningNoticeMessage { get; } = Bind(
            config,
            "2 - On-Screen Notices",
            "WarningNoticeMessage",
            "Server connection lost, attempting to reconnect... ({0}s)",
            "Large on-screen notice displayed halfway through TimeoutSeconds. {0} is replaced with remaining seconds until disconnect.");

        public ConfigEntry<string> RecoveredNoticeMessage { get; } = Bind(
            config,
            "2 - On-Screen Notices",
            "RecoveredNoticeMessage",
            "Server connection restored.",
            "Large on-screen notice displayed if server connection recovers before timing out.");

        public ConfigEntry<string> DisconnectNoticeMessage { get; } = Bind(
            config,
            "2 - On-Screen Notices",
            "DisconnectNoticeMessage",
            "Server connection lost. Disconnecting...",
            "Large on-screen notice displayed when the watchdog disconnects the player after a timeout, closed socket, or lost server peer.");

        public ConfigEntry<float> DisconnectDelaySeconds { get; } = Bind(
            config,
            "2 - On-Screen Notices",
            "DisconnectDelaySeconds",
            5.0f,
            "Seconds to wait after displaying the disconnect notice before logging out. Set to 0 for immediate logout.",
            new AcceptableValueRange<float>(0f, 30.0f));

        public ConfigEntry<bool> ShowDisconnectReason { get; } = Bind(
            config,
            "3 - Main Menu Dialog",
            "ShowDisconnectReason",
            true,
            "Whether to display an explanatory message on the main menu dialog after being disconnected by this watchdog.");

        public ConfigEntry<string> DisconnectMessage { get; } = Bind(
            config,
            "3 - Main Menu Dialog",
            "DisconnectMessage",
            "Server connection lost (Ghost connection prevented). Progress was saved.",
            "Custom message displayed on the main menu dialog when disconnected by this watchdog.");

        public ConfigEntry<bool> EnableDebugLogs { get; } = Bind(
            config,
            "4 - Debug",
            "EnableDebugLogs",
            false,
            "Whether to print detailed debug logs with every watchdog check, timer tick, and network/lifecycle event.");

        public ConfigEntry<KeyCode> DebugSimulateSeverKey { get; } = Bind(
            config,
            "4 - Debug",
            "DebugSimulateSeverKey",
            KeyCode.None,
            "Debug keybind that simulates an abrupt peer disconnect (ServerCharacters behavior). Requires EnableDebugLogs to be true. Leave as None for normal play.");

        public static void Initialize(ConfigFile configFile)
        {
            configFile.SaveOnConfigSet = false;
            try
            {
                Instance = new ModConfig(configFile);
                configFile.Save();
            }
            finally
            {
                configFile.SaveOnConfigSet = true;
            }
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile configFile,
            string section,
            string key,
            T defaultValue,
            string description,
            AcceptableValueBase? acceptableValues = null) =>
            configFile.Bind(section, key, defaultValue, new ConfigDescription(description, acceptableValues));
    }
}
