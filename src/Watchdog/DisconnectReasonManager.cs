using System.Collections;
using UnityEngine;

namespace ClientGhostWatchdog.Watchdog
{
    /// <summary>
    /// Preserves and displays custom disconnect explanations across scene transitions (world unload to main menu).
    /// </summary>
    internal static class DisconnectReasonManager
    {
        private static string? s_pendingReason;
        private static float s_reasonTimestamp = 0f;

        // 300s TTL accommodates long save times and scene load durations while preventing stale dialogs later
        private const float ReasonTtlSeconds = 300f;

        public static string? PendingReason
        {
            get
            {
                if (string.IsNullOrEmpty(s_pendingReason)) return null;
                float elapsed = Time.unscaledTime - s_reasonTimestamp;
                if (elapsed > ReasonTtlSeconds)
                {
                    Plugin.LogDebug($"DisconnectReasonManager: Pending reason expired (elapsed {elapsed:F1}s > TTL {ReasonTtlSeconds}s).");
                    s_pendingReason = null;
                    s_reasonTimestamp = 0f;
                    return null;
                }
                return s_pendingReason;
            }
        }

        // Stores custom disconnect reason and records timestamp
        public static void SetReason(string reason)
        {
            s_pendingReason = reason;
            s_reasonTimestamp = Time.unscaledTime;
            Plugin.LogDebug($"DisconnectReasonManager: Registered pending reason (TTL={ReasonTtlSeconds}s): \"{reason}\"");
        }

        // Clears stored reason
        public static void Clear()
        {
            s_pendingReason = null;
            s_reasonTimestamp = 0f;
            Plugin.LogDebug("DisconnectReasonManager: Cleared pending reason.");
        }

        // Applies pending disconnect reason to FejdStartup and reinforces on next frame
        public static void ApplyToMenu(FejdStartup startup)
        {
            string? reason = PendingReason;
            if (!string.IsNullOrEmpty(reason))
            {
                ApplyTextAndShowPanel(startup, reason!);
                startup.StartCoroutine(ReinforceNoticeRoutine(startup, reason!));
            }
        }

        private static void ApplyTextAndShowPanel(FejdStartup startup, string reason)
        {
            if (startup.m_connectionFailedPanel)
            {
                startup.m_connectionFailedPanel.SetActive(true);
            }

            if (startup.m_connectionFailedError)
            {
                // Disable Localize component to prevent native from overwriting custom text with generic "Disconnected"
                var localize = startup.m_connectionFailedError.GetComponent("Localize") as Behaviour;
                if (localize)
                {
                    localize.enabled = false;
                }

                startup.m_connectionFailedError.text = reason;
                Plugin.LogDebug($"DisconnectReasonManager: Applied disconnect reason to FejdStartup: \"{reason}\"");
            }
        }

        // Re-applies text on next frame in case UI animators or layout rebuilds reset text initially
        private static IEnumerator ReinforceNoticeRoutine(FejdStartup startup, string reason)
        {
            yield return null;

            if (startup != null && startup.m_connectionFailedError != null)
            {
                ApplyTextAndShowPanel(startup, reason);
                Plugin.LogDebug("DisconnectReasonManager: Reinforced disconnect reason on next frame.");
            }
        }
    }
}
