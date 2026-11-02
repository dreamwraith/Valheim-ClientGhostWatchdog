using HarmonyLib;
using ClientGhostWatchdog.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    // Ensure watchdog runs when entering a game session connected to a dedicated server
    [HarmonyPatch(typeof(Game), nameof(Game.Start))]
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
    [HarmonyPatch(typeof(Game), nameof(Game.OnDestroy))]
    public static class Game_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.LogDebug("Game.OnDestroy postfix triggered.");
            WatchdogManager.Stop("Game.OnDestroy");
        }
    }
}
