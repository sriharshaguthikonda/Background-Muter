using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using WinBGMuter.Config;

namespace WinBGMuter
{
    public partial class MainForm
    {
        private ListBox? _autoPlayListBox;
        private Button? _neverMuteToAutoPlayButton;
        private Button? _autoPlayToNeverMuteButton;

        private void SetupAutoPlayListUI()
        {
            // Add AutoPlay columns to the existing tableLayoutPanel4 (Mute Exception Changer)
            // Current layout: [Running Apps] [Arrows] [NeverMute] (3 columns)
            // New layout: [Running Apps] [Arrows] [NeverMute] [Arrows] [AutoPlay] (5 columns)

            if (tableLayoutPanel4 == null)
            {
                return;
            }

            // Expand to 5 columns
            tableLayoutPanel4.ColumnCount = 5;
            tableLayoutPanel4.ColumnStyles.Clear();
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));  // Running Apps
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));   // Arrows (Run -> NeverMute)
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));  // NeverMute
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));   // Arrows (NeverMute -> AutoPlay)
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));  // AutoPlay

            // Create arrow buttons panel for NeverMute <-> AutoPlay
            var arrowPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Name = "AutoPlayArrowPanel"
            };
            arrowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
            arrowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 15F));
            arrowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 14F));
            arrowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 15F));
            arrowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));

            _neverMuteToAutoPlayButton = new Button
            {
                Text = ">",
                Dock = DockStyle.Fill,
                Name = "NeverMuteToAutoPlayButton"
            };
            _neverMuteToAutoPlayButton.Click += NeverMuteToAutoPlayButton_Click;

            _autoPlayToNeverMuteButton = new Button
            {
                Text = "<",
                Dock = DockStyle.Fill,
                Name = "AutoPlayToNeverMuteButton"
            };
            _autoPlayToNeverMuteButton.Click += AutoPlayToNeverMuteButton_Click;

            arrowPanel.Controls.Add(_neverMuteToAutoPlayButton, 0, 1);
            arrowPanel.Controls.Add(_autoPlayToNeverMuteButton, 0, 3);

            // Create AutoPlay list panel
            var autoPlayPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Name = "AutoPlayPanel"
            };
            autoPlayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            autoPlayPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var autoPlayLabel = new Label
            {
                Text = "🎵 AutoPlay",
                Dock = DockStyle.Fill,
                AutoSize = false
            };

            _autoPlayListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Name = "AutoPlayListBox"
            };

            autoPlayPanel.Controls.Add(autoPlayLabel, 0, 0);
            autoPlayPanel.Controls.Add(_autoPlayListBox, 0, 1);

            // Add to tableLayoutPanel4
            tableLayoutPanel4.Controls.Add(arrowPanel, 3, 0);
            tableLayoutPanel4.Controls.Add(autoPlayPanel, 4, 0);

            // Load current AutoPlay app if set
            LoadAutoPlayList();
        }

        private void LoadAutoPlayList()
        {
            if (_autoPlayListBox == null)
            {
                return;
            }

            _autoPlayListBox.Items.Clear();
            var autoPlayAppName = Properties.Settings.Default.AutoPlayAppName;
            if (!string.IsNullOrWhiteSpace(autoPlayAppName))
            {
                var trimmed = autoPlayAppName.Trim();
                var title = TryGetWindowTitleByProcessName(trimmed);
                _autoPlayListBox.Items.Add(new ProcessDisplayItem(trimmed, title));
            }
        }

        private void SaveAutoPlayList()
        {
            if (_autoPlayListBox == null)
            {
                return;
            }

            var appName = _autoPlayListBox.Items.Count > 0
                ? ExtractProcessName(_autoPlayListBox.Items[0])
                : string.Empty;

            Properties.Settings.Default.AutoPlayAppName = appName;

            if (_appController != null)
            {
                _appController.AutoPlayAppName = appName;
            }

            SettingsFileStore.Save();
        }

        private void NeverMuteToAutoPlayButton_Click(object? sender, EventArgs e)
        {
            if (_autoPlayListBox == null || NeverMuteListBox.SelectedIndex < 0)
            {
                return;
            }

            var selectedItem = NeverMuteListBox.SelectedItem;
            var selectedApp = ExtractProcessName(selectedItem);
            if (string.IsNullOrEmpty(selectedApp))
            {
                return;
            }

            // Only one AutoPlay app allowed; replace existing
            _autoPlayListBox.Items.Clear();
            _autoPlayListBox.Items.Add(new ProcessDisplayItem(selectedApp, TryGetWindowTitleByProcessName(selectedApp)));

            // Remove from NeverMute list
            RemoveFromNeverMuteList(selectedApp);

            SaveAutoPlayList();
            RefreshProcessListQuiet();
        }

        private void AutoPlayToNeverMuteButton_Click(object? sender, EventArgs e)
        {
            if (_autoPlayListBox == null || _autoPlayListBox.SelectedIndex < 0)
            {
                return;
            }

            var selectedItem = _autoPlayListBox.SelectedItem;
            var selectedApp = ExtractProcessName(selectedItem);
            if (string.IsNullOrEmpty(selectedApp))
            {
                return;
            }

            // Move to NeverMute list
            NeverMuteTextBox.AppendText("," + selectedApp);
            NeverMuteTextBox_TextChanged(this, EventArgs.Empty);

            // Remove from AutoPlay list
            _autoPlayListBox.Items.Clear();
            SaveAutoPlayList();
        }

        private void RemoveFromNeverMuteList(string appName)
        {
            var currentList = Properties.Settings.Default.NeverMuteProcs ?? string.Empty;
            var items = currentList
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.Equals(s, appName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var newList = string.Join(",", items);
            Properties.Settings.Default.NeverMuteProcs = newList;
            m_neverMuteList = newList;
            NeverMuteTextBox.Text = newList;

            PopulateNeverMuteListBox();
        }

    }
}
