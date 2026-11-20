using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using ClientGhostWatchdog.Configuration;
using ClientGhostWatchdog.Core.Watchdog;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

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
        internal static ManualLogSource Log { get; private set; } = null!;
        private Harmony? _harmony;
        private DebugHotkeyListener? _debugHotkeyListener;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Initialize(Config);

            // same check as jotunns GUIManager.IsHeadless(), jotunn isnt here at runtime
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }

            _debugHotkeyListener = gameObject.AddComponent<DebugHotkeyListener>();
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
            if (_debugHotkeyListener)
            {
                Destroy(_debugHotkeyListener);
                _debugHotkeyListener = null;
            }

            _harmony?.UnpatchSelf();
            Instance = null;
        }

        internal static void LogDebug(string message)
        {
            if (ModConfig.Instance.EnableDebugLogs.Value)
            {
                Log.LogInfo($"[DEBUG] {message}");
            }
        }
    }
}
