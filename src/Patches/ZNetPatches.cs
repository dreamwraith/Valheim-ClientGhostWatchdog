using HarmonyLib;
using ClientGhostWatchdog.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    // Start watchdog once the client successfully completes peer handshake with dedicated server
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
    public static class ZNet_RPC_PeerInfo_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            Plugin.LogDebug("ZNet.RPC_PeerInfo postfix triggered.");

            if (__instance.IsServer())
            {
                Plugin.LogDebug("ZNet.RPC_PeerInfo: Host is server; ignoring watchdog start.");
                return;
            }

            WatchdogManager.Start("ZNet.RPC_PeerInfo");
        }
    }

    // Stop watchdog on network shutdown
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    public static class ZNet_Shutdown_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.LogDebug("ZNet.Shutdown postfix triggered.");
            WatchdogManager.Stop("ZNet.Shutdown");
        }
    }
}
