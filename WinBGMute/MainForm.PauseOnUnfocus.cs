using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WinBGMuter.Abstractions;
using WinBGMuter.Browser;
using WinBGMuter.Config;
using WinBGMuter.Controller;
using WinBGMuter.UI;

namespace WinBGMuter
{
    public partial class MainForm
    {
        private AppController? _appController;
        private PauseOnUnfocusSettings? _pauseSettings;
        private BrowserCoordinator? _browserCoordinator;

        private CheckBox? _pauseOnUnfocusCheckbox;
        private CheckBox? _autoPlaySpotifyCheckbox;
        private TextBox? _autoPlayAppTextBox;
        private NumericUpDown? _pauseCooldownNumeric;
        private GroupBox? _pauseSettingsGroupBox;
        private Icon? _trayBaseIcon;

        private void InitializePauseOnUnfocus()
        {
            _pauseSettings = new PauseOnUnfocusSettings();
            _pauseSettings.Load();

            // Start the browser coordinator for cross-profile extension communication
            _browserCoordinator = new BrowserCoordinator();
            _browserCoordinator.Start();
            LoggingEngine.LogLine("[PauseOnUnfocus] BrowserCoordinator started for extension communication",
                category: LoggingEngine.LogCategory.MediaControl);

            if (m_volumeMixer == null)
            {
                LoggingEngine.LogLine("[PauseOnUnfocus] VolumeMixer not initialized, skipping AppController init",
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return;
            }

            var autoPlaySpotify = Properties.Settings.Default.AutoPlaySpotify;
            var autoPlayAppName = Properties.Settings.Default.AutoPlayAppName;

            _appController = new AppController(
                m_volumeMixer,
                _pauseSettings.AudibilityThreshold,
                null,
                GetNeverPauseList,
                autoPlaySpotify,
                autoPlayAppName,
                _pauseSettings.PauseCooldownMs);

            _appController.Enabled = _pauseSettings.Enabled;

            // Ensure autoplay monitor starts even when Pause on Unfocus is disabled
            // so Spotify can resume on startup when idle.
            if (autoPlaySpotify)
            {
                _appController.AutoPlaySpotify = true;
            }

            if (_pauseSettings.Enabled)
            {
                _appController.Start();
            }

            _trayBaseIcon ??= TrayIcon?.Icon;
            SetupPauseOnUnfocusTrayMenu();
            SetupPauseOnUnfocusUIPanel();
            SetupAutoPlayListUI();
            UpdateTrayIconPauseState();

            LoggingEngine.LogLine($"[PauseOnUnfocus] Initialized (Enabled={_pauseSettings.Enabled}, Mode={_pauseSettings.Mode})",
                category: LoggingEngine.LogCategory.General);
        }

        private void SetupPauseOnUnfocusUIPanel()
        {
            if (_pauseSettings == null)
            {
                return;
            }

            // Create a compact panel for audio control options
            // Add to LogTextBox's parent area (below log) for better visibility
            _pauseSettingsGroupBox = new GroupBox
            {
                Text = "🔊 Audio Control",
                Dock = DockStyle.Bottom,
                Height = 140,
                Padding = new Padding(3)
            };

            var innerPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false
            };

            // Enable pause checkbox
            _pauseOnUnfocusCheckbox = new CheckBox
            {
                Text = "Enable Pause on Unfocus",
                Checked = _pauseSettings.Enabled,
                AutoSize = true,
                Margin = new Padding(3, 2, 3, 2)
            };
            _pauseOnUnfocusCheckbox.CheckedChanged += (s, e) =>
            {
                OnPauseOnUnfocusToggled(_pauseOnUnfocusCheckbox.Checked);
            };

            // Auto-play app checkbox
            _autoPlaySpotifyCheckbox = new CheckBox
            {
                Text = "Enable Auto-play when idle",
                Checked = Properties.Settings.Default.AutoPlaySpotify,
                AutoSize = true,
                Margin = new Padding(3, 2, 3, 2)
            };
            _autoPlaySpotifyCheckbox.CheckedChanged += (s, e) =>
            {
                OnAutoPlaySpotifyToggled(_autoPlaySpotifyCheckbox.Checked);
            };

            // Cooldown input
            _pauseCooldownNumeric = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 600000,
                Increment = 100,
                Value = Math.Max(_pauseSettings.PauseCooldownMs, 0),
                Width = 120,
                Margin = new Padding(3, 2, 3, 2)
            };
            _pauseCooldownNumeric.ValueChanged += (s, e) =>
            {
                OnPauseCooldownChanged((int)_pauseCooldownNumeric.Value);
            };

            var cooldownLabel = new Label
            {
                Text = "Cooldown (ms):",
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 2)
            };

            innerPanel.Controls.Add(_pauseOnUnfocusCheckbox);
            innerPanel.Controls.Add(_autoPlaySpotifyCheckbox);
            innerPanel.Controls.Add(cooldownLabel);
            innerPanel.Controls.Add(_pauseCooldownNumeric);

            _pauseSettingsGroupBox.Controls.Add(innerPanel);

            // Add to the main form's panel1 (bottom docked)
            if (panel1 != null)
            {
                panel1.Controls.Add(_pauseSettingsGroupBox);
                _pauseSettingsGroupBox.BringToFront();
            }
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

            // Insert before the separator
            var insertIndex = Math.Max(0, TrayContextMenu.Items.Count - 2);
            TrayContextMenu.Items.Insert(insertIndex, new ToolStripSeparator());
            TrayContextMenu.Items.Insert(insertIndex + 1, enableToggle);
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
            UpdateTrayIconPauseState();
        }

        private void OnAutoPlaySpotifyToggled(bool enabled)
        {
            Properties.Settings.Default.AutoPlaySpotify = enabled;

            if (_appController != null)
            {
                _appController.AutoPlaySpotify = enabled;
            }

            LoggingEngine.LogLine($"[AutoPlay] {(enabled ? "Enabled" : "Disabled")} for {Properties.Settings.Default.AutoPlayAppName}",
                category: LoggingEngine.LogCategory.General);
        }

        private void OnAutoPlayAppNameChanged(string appName)
        {
            Properties.Settings.Default.AutoPlayAppName = appName;

            if (_appController != null)
            {
                _appController.AutoPlayAppName = appName;
            }

            LoggingEngine.LogLine($"[AutoPlay] App changed to: {appName}",
                category: LoggingEngine.LogCategory.General);
        }

        private void OnPauseCooldownChanged(int cooldownMs)
        {
            if (_pauseSettings == null)
            {
                return;
            }

            _pauseSettings.PauseCooldownMs = cooldownMs;

            if (_appController != null)
            {
                _appController.PauseCooldownMs = cooldownMs;
            }

            _pauseSettings.Save();
        }

        private void CleanupPauseOnUnfocus()
        {
            _appController?.Stop();
            _appController?.Dispose();
            _appController = null;
        }

        private IEnumerable<string> GetNeverPauseList()
        {
            // Reuse the same canonical never-mute set for pause
            return GetNeverMuteSet();
        }

        private void UpdateTrayIconPauseState()
        {
            if (TrayIcon == null)
            {
                return;
            }

            var isEnabled = _pauseSettings?.Enabled ?? false;
            var stateLabel = isEnabled ? "Active" : "Disabled";
            TrayIcon.Text = $"BGMuter (Pause: {stateLabel})";

            if (_trayBaseIcon != null)
            {
                TrayIcon.Icon = isEnabled ? _trayBaseIcon : SystemIcons.Exclamation;
            }
        }
    }
}
