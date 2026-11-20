using System;
using ClientGhostWatchdog.Configuration;
using UnityEngine;

namespace ClientGhostWatchdog.Core.Watchdog
{
    internal sealed class DebugHotkeyListener : MonoBehaviour
    {
        private void Awake()
        {
            ModConfig.Instance.EnableDebugLogs.SettingChanged += OnDebugSettingChanged;
            ModConfig.Instance.DebugSimulateSeverKey.SettingChanged += OnDebugSettingChanged;
            RefreshEnabledState();
        }

        private void OnDestroy()
        {
            ModConfig.Instance.EnableDebugLogs.SettingChanged -= OnDebugSettingChanged;
            ModConfig.Instance.DebugSimulateSeverKey.SettingChanged -= OnDebugSettingChanged;
        }

        private void Update()
        {
            if (Input.GetKeyDown(ModConfig.Instance.DebugSimulateSeverKey.Value))
            {
                WatchdogManager.SimulateSeveredPeer();
            }
        }

        private void OnDebugSettingChanged(object sender, EventArgs eventArgs) => RefreshEnabledState();

        private void RefreshEnabledState() =>
            enabled = ModConfig.Instance.EnableDebugLogs.Value &&
                      ModConfig.Instance.DebugSimulateSeverKey.Value != KeyCode.None;
    }
}
