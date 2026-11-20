using HarmonyLib;
using ClientGhostWatchdog.Core.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    [HarmonyPatch(typeof(Game))]
    internal static class GamePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Game.Start))]
        private static void StartPostfix()
        {
            Plugin.LogDebug("Game.Start postfix triggered.");
            DisconnectReasonManager.Clear();
            WatchdogManager.Start("Game.Start");
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Game.OnDestroy))]
        private static void OnDestroyPostfix()
        {
            Plugin.LogDebug("Game.OnDestroy postfix triggered.");
            WatchdogManager.Stop("Game.OnDestroy");
        }
    }
}
