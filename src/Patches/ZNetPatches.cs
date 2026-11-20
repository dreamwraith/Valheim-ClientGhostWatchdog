using HarmonyLib;
using ClientGhostWatchdog.Core.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    [HarmonyPatch(typeof(ZNet))]
    internal static class ZNetPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ZNet.RPC_PeerInfo))]
        private static void RpcPeerInfoPostfix()
        {
            Plugin.LogDebug("ZNet.RPC_PeerInfo postfix triggered.");
            WatchdogManager.Start("ZNet.RPC_PeerInfo");
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ZNet.Shutdown))]
        private static void ShutdownPostfix()
        {
            Plugin.LogDebug("ZNet.Shutdown postfix triggered.");
            WatchdogManager.Stop("ZNet.Shutdown");
        }
    }
}
