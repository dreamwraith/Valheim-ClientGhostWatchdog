using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using ClientGhostWatchdog.Configuration;
using ClientGhostWatchdog.Watchdog;
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
        public const string NetworkPerformanceSystemGUID = "MidnightsFX.NetworkPerformanceSystem";

        internal static Plugin? Instance { get; private set; }
        internal static ManualLogSource Log = null!;
        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Initialize(Config);

            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully.");
        }

        private void Start()
        {
            if (Chainloader.PluginInfos.ContainsKey(NetworkPerformanceSystemGUID))
            {
                Log.LogInfo($"Detected {NetworkPerformanceSystemGUID}; NPS client watchdog stood down in favor of {ModName}.");
            }
        }

        private void OnDestroy()
        {
            WatchdogManager.Stop("Plugin.OnDestroy");
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            if (ModConfig.EnableDebugLogs.Value && ModConfig.DebugSimulateSeverKey.Value != KeyCode.None)
            {
                if (Input.GetKeyDown(ModConfig.DebugSimulateSeverKey.Value))
                {
                    WatchdogManager.SimulateSeveredPeer();
                }
            }
        }

        // Conditionally emits detailed debug messages when EnableDebugLogs is true
        internal static void LogDebug(string message)
        {
            if (ModConfig.EnableDebugLogs.Value)
            {
                Log.LogInfo($"[DEBUG] {message}");
            }
        }
    }
}
