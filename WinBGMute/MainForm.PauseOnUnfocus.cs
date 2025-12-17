using System;
using WinBGMuter.Abstractions;
using WinBGMuter.Config;
using WinBGMuter.Controller;
using WinBGMuter.UI;

namespace WinBGMuter
{
    public partial class MainForm
    {
        private AppController? _appController;
        private PauseOnUnfocusSettings? _pauseSettings;

        private void InitializePauseOnUnfocus()
        {
            _pauseSettings = new PauseOnUnfocusSettings();
            _pauseSettings.Load();

            if (m_volumeMixer == null)
            {
                LoggingEngine.LogLine("[PauseOnUnfocus] VolumeMixer not initialized, skipping AppController init",
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return;
            }

            _appController = new AppController(
                m_volumeMixer,
                _pauseSettings.Mode,
                _pauseSettings.AudibilityThreshold);

            _appController.Enabled = _pauseSettings.Enabled;

            if (_pauseSettings.Enabled)
            {
                _appController.Start();
            }

            SetupPauseOnUnfocusTrayMenu();

            LoggingEngine.LogLine($"[PauseOnUnfocus] Initialized (Enabled={_pauseSettings.Enabled}, Mode={_pauseSettings.Mode})",
                category: LoggingEngine.LogCategory.General);
        }

        private void SetupPauseOnUnfocusTrayMenu()
        {
            if (_pauseSettings == null || TrayContextMenu == null)
            {
                return;
            }

            var enableToggle = TrayMenuExtensions.CreateEnableToggle(
                _pauseSettings.Enabled,
                OnPauseOnUnfocusToggled);

            var modeMenu = TrayMenuExtensions.CreatePolicyModeMenu(
                _pauseSettings.Mode,
                OnPolicyModeChanged);

            // Insert before the separator
            var insertIndex = Math.Max(0, TrayContextMenu.Items.Count - 2);
            TrayContextMenu.Items.Insert(insertIndex, new ToolStripSeparator());
            TrayContextMenu.Items.Insert(insertIndex + 1, enableToggle);
            TrayContextMenu.Items.Insert(insertIndex + 2, modeMenu);
        }

        private void OnPauseOnUnfocusToggled(bool enabled)
        {
            if (_pauseSettings == null || _appController == null)
            {
                return;
            }

            _pauseSettings.Enabled = enabled;
            _appController.Enabled = enabled;

            if (enabled)
            {
                _appController.Start();
                LoggingEngine.LogLine("[PauseOnUnfocus] Enabled", category: LoggingEngine.LogCategory.General);
            }
            else
            {
                _appController.Stop();
                LoggingEngine.LogLine("[PauseOnUnfocus] Disabled", category: LoggingEngine.LogCategory.General);
            }

            _pauseSettings.Save();
        }

        private void OnPolicyModeChanged(PolicyMode mode)
        {
            if (_pauseSettings == null)
            {
                return;
            }

            _pauseSettings.Mode = mode;
            _pauseSettings.Save();

            // Reinitialize controller with new mode
            if (_appController != null)
            {
                var wasEnabled = _appController.Enabled;
                _appController.Stop();
                _appController.Dispose();

                _appController = new AppController(
                    m_volumeMixer,
                    mode,
                    _pauseSettings.AudibilityThreshold);

                _appController.Enabled = wasEnabled;

                if (wasEnabled)
                {
                    _appController.Start();
                }
            }

            LoggingEngine.LogLine($"[PauseOnUnfocus] Mode changed to {mode}", category: LoggingEngine.LogCategory.General);
        }

        private void CleanupPauseOnUnfocus()
        {
            _appController?.Stop();
            _appController?.Dispose();
            _appController = null;
        }
    }
}
