using HarmonyLib;
using ClientGhostWatchdog.Watchdog;
using UnityEngine;

namespace ClientGhostWatchdog.Patches
{
    // Display disconnect reason when FejdStartup shows connection error dialog
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))]
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
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
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
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnConnectionFailedOk))]
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
