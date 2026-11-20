using HarmonyLib;
using ClientGhostWatchdog.Core.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    [HarmonyPatch(typeof(Terminal))]
    internal static class TerminalPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Terminal.InitTerminal))]
        private static void InitTerminalPostfix() => WatchdogManager.RegisterConsoleCommands();
    }
}
