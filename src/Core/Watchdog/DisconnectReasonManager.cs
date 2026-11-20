using System.Collections;
using UnityEngine;

namespace ClientGhostWatchdog.Core.Watchdog
{
    internal static class DisconnectReasonManager
    {
        private static string? s_pendingReason;
        private static float s_reasonTimestamp = 0f;

        // 300s ttl covers slow saves and scene loads without showing stale dialogs later
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

        public static void SetReason(string reason)
        {
            s_pendingReason = reason;
            s_reasonTimestamp = Time.unscaledTime;
            Plugin.LogDebug($"DisconnectReasonManager: Registered pending reason (TTL={ReasonTtlSeconds}s): \"{reason}\"");
        }

        public static void Clear()
        {
            s_pendingReason = null;
            s_reasonTimestamp = 0f;
            Plugin.LogDebug("DisconnectReasonManager: Cleared pending reason.");
        }

        public static void HandleDialogAcknowledged(FejdStartup startup)
        {
            if (startup.m_connectionFailedError.TryGetComponent(out Localize localize))
            {
                localize.enabled = true;
            }

            Clear();
        }

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
            startup.m_connectionFailedPanel.SetActive(true);

            // turn off Localize so the base game doesnt overwrite the custom text
            if (startup.m_connectionFailedError.TryGetComponent(out Localize localize))
            {
                localize.enabled = false;
            }

            startup.m_connectionFailedError.text = reason;
            Plugin.LogDebug($"DisconnectReasonManager: Applied disconnect reason to FejdStartup: \"{reason}\"");
        }

        // reapply on the next frame in case ui animators or layout rebuilds reset the text
        private static IEnumerator ReinforceNoticeRoutine(FejdStartup startup, string reason)
        {
            yield return null;

            ApplyTextAndShowPanel(startup, reason);
            Plugin.LogDebug("DisconnectReasonManager: Reinforced disconnect reason on next frame.");
        }
    }
}
