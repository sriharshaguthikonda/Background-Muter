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

            // Create the group box for pause settings
            _pauseSettingsGroupBox = new GroupBox
            {
                Text = "⏸️ Pause on Unfocus",
                Dock = DockStyle.Top,
                Height = 100,
                Padding = new Padding(5)
            };

            var innerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true
            };
            innerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            innerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            innerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            // Enable checkbox
            _pauseOnUnfocusCheckbox = new CheckBox
            {
                Text = "Enable Pause on Unfocus",
                Checked = _pauseSettings.Enabled,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            _pauseOnUnfocusCheckbox.CheckedChanged += (s, e) =>
            {
                OnPauseOnUnfocusToggled(_pauseOnUnfocusCheckbox.Checked);
                UpdateModeComboState();
            };

            // Mode label
            var modeLabel = new Label
            {
                Text = "Mode:",
                Dock = DockStyle.Fill,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Mode combo box
            _pauseModeComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = _pauseSettings.Enabled
            };
            _pauseModeComboBox.Items.Add("Pause Only (GSMTC)");
            _pauseModeComboBox.Items.Add("Pause + Mute Fallback");
            _pauseModeComboBox.Items.Add("Mute Only (Legacy)");
            _pauseModeComboBox.SelectedIndex = (int)_pauseSettings.Mode;
            _pauseModeComboBox.SelectedIndexChanged += (s, e) =>
            {
                if (_pauseModeComboBox.SelectedIndex >= 0)
                {
                    OnPolicyModeChanged((PolicyMode)_pauseModeComboBox.SelectedIndex);
                }
            };

            // Mode selection panel (label + combo)
            var modePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true
            };
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modePanel.Controls.Add(modeLabel, 0, 0);
            modePanel.Controls.Add(_pauseModeComboBox, 1, 0);

            innerPanel.Controls.Add(_pauseOnUnfocusCheckbox, 0, 0);
            innerPanel.Controls.Add(modePanel, 0, 1);

            _pauseSettingsGroupBox.Controls.Add(innerPanel);

            // Add to the settings panel (groupBox3 -> tableLayoutPanel5)
            if (tableLayoutPanel5 != null)
            {
                tableLayoutPanel5.RowCount += 1;
                tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
                tableLayoutPanel5.Controls.Add(_pauseSettingsGroupBox, 0, tableLayoutPanel5.RowCount - 1);
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
