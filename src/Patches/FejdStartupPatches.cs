using HarmonyLib;
using ClientGhostWatchdog.Core.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    [HarmonyPatch(typeof(FejdStartup))]
    internal static class FejdStartupPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(FejdStartup.ShowConnectError))]
        private static void ShowConnectErrorPostfix(FejdStartup __instance)
        {
            Plugin.LogDebug("FejdStartup.ShowConnectError postfix triggered.");
            WatchdogManager.Stop("FejdStartup.ShowConnectError");
            DisconnectReasonManager.ApplyToMenu(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(FejdStartup.OnConnectionFailedOk))]
        private static void OnConnectionFailedOkPostfix(FejdStartup __instance)
        {
            Plugin.LogDebug("FejdStartup.OnConnectionFailedOk postfix triggered.");
            DisconnectReasonManager.HandleDialogAcknowledged(__instance);
        }
    }
}
