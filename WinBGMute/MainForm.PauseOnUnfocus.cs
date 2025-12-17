using System;
using System.Linq;
using System.Windows.Forms;
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

        private CheckBox? _pauseOnUnfocusCheckbox;
        private CheckBox? _enableMutingCheckbox;
        private ComboBox? _pauseModeComboBox;
        private GroupBox? _pauseSettingsGroupBox;

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
                _pauseSettings.AudibilityThreshold,
                null,
                GetNeverPauseList);

            _appController.Enabled = _pauseSettings.Enabled;

            if (_pauseSettings.Enabled)
            {
                _appController.Start();
            }

            SetupPauseOnUnfocusTrayMenu();
            SetupPauseOnUnfocusUIPanel();

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
                Height = 110,
                Padding = new Padding(3)
            };

            var innerPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false
            };

            // Enable muting checkbox
            _enableMutingCheckbox = new CheckBox
            {
                Text = "Enable Muting",
                Checked = m_enableMuting,
                AutoSize = true,
                Margin = new Padding(3, 2, 3, 2)
            };
            _enableMutingCheckbox.CheckedChanged += (s, e) =>
            {
                m_enableMuting = _enableMutingCheckbox.Checked;
                Properties.Settings.Default.EnableMuting = m_enableMuting;

                // When muting is disabled, unmute all apps
                if (!m_enableMuting && m_volumeMixer != null)
                {
                    foreach (var pid in m_volumeMixer.GetPIDs())
                    {
                        m_volumeMixer.SetApplicationMute(pid, false);
                    }
                    LoggingEngine.LogLine("[Muting] Disabled - unmuted all apps",
                        category: LoggingEngine.LogCategory.General);
                }
                else
                {
                    LoggingEngine.LogLine("[Muting] Enabled",
                        category: LoggingEngine.LogCategory.General);
                }
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
                UpdateModeComboState();
            };

            // Mode combo box (compact)
            _pauseModeComboBox = new ComboBox
            {
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = _pauseSettings.Enabled,
                Margin = new Padding(3, 2, 3, 2)
            };
            _pauseModeComboBox.Items.Add("Pause Only");
            _pauseModeComboBox.Items.Add("Pause + Mute Fallback");
            _pauseModeComboBox.Items.Add("Mute Only");
            _pauseModeComboBox.SelectedIndex = (int)_pauseSettings.Mode;
            _pauseModeComboBox.SelectedIndexChanged += (s, e) =>
            {
                if (_pauseModeComboBox.SelectedIndex >= 0)
                {
                    OnPolicyModeChanged((PolicyMode)_pauseModeComboBox.SelectedIndex);
                }
            };

            innerPanel.Controls.Add(_enableMutingCheckbox);
            innerPanel.Controls.Add(_pauseOnUnfocusCheckbox);
            innerPanel.Controls.Add(_pauseModeComboBox);

            _pauseSettingsGroupBox.Controls.Add(innerPanel);

            // Add to the main form's panel1 (bottom docked)
            if (panel1 != null)
            {
                panel1.Controls.Add(_pauseSettingsGroupBox);
                _pauseSettingsGroupBox.BringToFront();
            }
        }

        private void UpdateModeComboState()
        {
            if (_pauseModeComboBox != null && _pauseOnUnfocusCheckbox != null)
            {
                _pauseModeComboBox.Enabled = _pauseOnUnfocusCheckbox.Checked;
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
                    _pauseSettings.AudibilityThreshold,
                    null,
                    GetNeverPauseList);

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

        private IEnumerable<string> GetNeverPauseList()
        {
            // Reuse the same canonical never-mute set for pause
            return GetNeverMuteSet();
        }
    }
}
