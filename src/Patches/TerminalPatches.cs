using HarmonyLib;
using ClientGhostWatchdog.Watchdog;

namespace ClientGhostWatchdog.Patches
{
    // Register debug console command for simulating badly behaved peer severance
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class Terminal_InitTerminal_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Terminal.commands.ContainsKey("cgw_simulatesever"))
            {
                return;
            }

            new Terminal.ConsoleCommand(
                "cgw_simulatesever",
                "Simulates an abrupt external peer severance (ServerCharacters behavior) for watchdog testing.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    WatchdogManager.SimulateSeveredPeer();
                }
            );
        }
    }
}
