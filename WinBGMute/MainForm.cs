/*
 *  Background Muter - Automatically mute background applications
 *  Copyright (C) 2022  Nefares (nefares@protonmail.com) github.com/nefares
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/


using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;
using WinBGMuter.Config;

namespace WinBGMuter
{
    public partial class MainForm : Form
    {
        /// <summary>
        /// used for dark mode
        /// </summary>
        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_NCRENDERING_ENABLED,
            DWMWA_NCRENDERING_POLICY,
            DWMWA_TRANSITIONS_FORCEDISABLED,
            DWMWA_ALLOW_NCPAINT,
            DWMWA_CAPTION_BUTTON_BOUNDS,
            DWMWA_NONCLIENT_RTL_LAYOUT,
            DWMWA_FORCE_ICONIC_REPRESENTATION,
            DWMWA_FLIP3D_POLICY,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            DWMWA_HAS_ICONIC_BITMAP,
            DWMWA_DISALLOW_PEEK,
            DWMWA_EXCLUDED_FROM_PEEK,
            DWMWA_CLOAK,
            DWMWA_CLOAKED,
            DWMWA_FREEZE_REPRESENTATION,
            DWMWA_PASSIVE_UPDATE_MODE,
            DWMWA_USE_HOSTBACKDROPBRUSH,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR,
            DWMWA_CAPTION_COLOR,
            DWMWA_TEXT_COLOR,
            DWMWA_VISIBLE_FRAME_BORDER_THICKNESS,
            DWMWA_SYSTEMBACKDROP_TYPE,
            DWMWA_LAST
        };

        // used for dark mode
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

        private VolumeMixer m_volumeMixer;
        private ForegroundProcessManager m_processManager;

        private string m_neverMuteList;
        private bool m_settingsChanged = false;
        private bool m_enableMiniStart = false;
        private bool m_enableDemo = false;
        private int m_errorCount = 0;
        private bool m_isMuteConditionBackground = true;

        // @todo untested whether this works
        private static string m_previous_fname = "wininit";
        private static int m_previous_fpid = -1;

        // keep alive timer @todo replace the Forms timer with the System.Timer
        private static System.Timers.Timer m_keepAliveTimer = new System.Timers.Timer(600000);

        private void InternalLog(object olog, object? ocolor = null, object? ofont = null)
        {
            string log = olog == null ? string.Empty : (string)(olog);
            Color? color = ocolor == null ? null : (Color)ocolor;
            Font? font = ofont == null ? null : (Font)ofont;

            if (this == null)
            {
                return;
            }

            try
            {
                //Invoke method to allow access outside of creator thread
                this.Invoke(() =>
                {
                    this.LogTextBox.SelectionStart = this.LogTextBox.TextLength;
                    this.LogTextBox.SelectionLength = 0;
                    this.LogTextBox.SelectionColor = color == null ? this.LogTextBox.SelectionColor : (Color)color;
                    this.LogTextBox.SelectionFont = font == null ? this.LogTextBox.SelectionFont : (Font)font;
                    this.LogTextBox.AppendText(log);
                    this.LogTextBox.SelectionColor = this.LogTextBox.ForeColor;
                    this.LogTextBox.ScrollToCaret();

                    this.StatusBox.Text = log;

                });
            }
            catch (Exception ex)
            {

            }
        }

        private void InternalLogLine(object olog, object? ocolor = null, object? ofont = null)
        {
            string log = olog == null ? string.Empty : (string)(olog);
            Color? color = ocolor == null ? null : (Color)ocolor;
            Font? font = ofont == null ? null : (Font)ofont;
            InternalLog(log + Environment.NewLine, color, font);
        }

        private void HandleError(Exception ex, object? data = null)
        {
            m_errorCount += 1;

            if (m_errorCount >= 30)
            {
                return;
            }

            int pid = (data is int) ? (int)data : -1;
            LoggingEngine.LogLine("[-] Process access failed for PID " + pid.ToString() + " @" + ex.Source, Color.Red);
            m_volumeMixer.ReloadAudio(true);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsIconic(IntPtr hWnd);

        private void RefreshProcessList()
        {
            // clear process list
            ProcessListListBox.Items.Clear();

            if (m_volumeMixer == null)
            {
                return;
            }
            // get a process PID list of processes with an audio channel
            int[] audio_pids = m_volumeMixer.GetPIDs();

            var autoPlayApp = Properties.Settings.Default.AutoPlayAppName?.Trim() ?? string.Empty;

            foreach (var pid in audio_pids)
            {
                try
                {
                    Process proc = Process.GetProcessById(pid);
                    string pname = proc.ProcessName;

                    // Exclusive: skip if in NeverMute or AutoPlay
                    if (NeverMuteListBox.Items.Contains(pname))
                    {
                        continue;
                    }
                    if (string.Equals(pname, autoPlayApp, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ProcessListListBox.Items.Add(pname);
                }
                catch (Exception ex)
                {
                    HandleError(ex, (object)pid);
                }
            }
        }

        private void RefreshProcessListQuiet()
        {
            LoggingEngine.Log("[R]", Color.Aqua, null, LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG);
            LoggingEngine.LOG_LEVEL_TYPE currentLogLevel = LoggingEngine.LogLevel;
            LoggingEngine.LogLevel = LoggingEngine.LOG_LEVEL_TYPE.LOG_NONE;
            RefreshProcessList();
            LoggingEngine.LogLevel = currentLogLevel;
        }

        private void EnableAutoStart(bool isEnabled)
        {
            string autostartPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string linkDir = autostartPath;
            string linkName = "Background Muter.lnk";
            string fullPath = Path.Combine(linkDir, linkName);
            string programPath = Application.ExecutablePath;
            string programArgs = "--startMinimized";

            if (File.Exists(fullPath))
            {
                FileInfo fileInfo = new FileInfo(fullPath);
                fileInfo.Delete();
            }

            if (isEnabled)
            {
                ShortcutManager.CreateShortcut(this.Text, programPath, linkName, linkDir, programArgs);
                LoggingEngine.LogLine($"Setting autostart @{linkDir} -> {linkName}");
            }
        }

        /// <summary>
        /// Recursively set dark mode for all underlaying controls by storing the original colors in the control's tags
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="dark"></param>
        private void SetDark(Control parent, bool dark)
        {
            // only works on main window
            int USE_DARK_MODE = dark ? 1 : 0;
            DwmSetWindowAttribute(parent.Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref USE_DARK_MODE, sizeof(int));

            Color bgcolor;
            Color fgcolor;

            foreach (Control c in parent.Controls)
            {
                bgcolor = c.BackColor;
                fgcolor = c.ForeColor;

                (Color, Color) tag = (bgcolor, fgcolor);

                if ((c.Tag != null) && (!dark))
                {
                    tag = ((Color, Color))c.Tag;
                    bgcolor = tag.Item1;
                    fgcolor = tag.Item2;
                }

                if (c.Tag == null)
                {
                    c.Tag = (c.BackColor, c.ForeColor);
                }
                else
                {

                }

                if (dark)
                {
                    bgcolor = Color.FromArgb(25, 25, 25);
                    fgcolor = Color.White;
                }
                else if (c.Tag != null)
                {
                    fgcolor = tag.Item1;
                    fgcolor = tag.Item2;

                }

                c.BackColor = bgcolor;
                c.ForeColor = fgcolor;

                if (c.Controls.Count > 0)
                    SetDark(c, dark);

                parent.Refresh();

                // if main window, force redraw as refresh does not work
                if (parent.Parent == null)
                {
                    parent.Hide();
                    parent.Show();
                }
            }
        }

        public MainForm(string[] args)
        {
            if (args.Length != 0)
            {
                foreach (string arg in args)
                {
                    switch (arg)
                    {
                        case "--startMinimized":
                            m_enableMiniStart = true;
                            break;
                        case "--demo":
                            m_enableDemo = true;
                            break;
                        default:
                            MessageBox.Show($"Unknown argument {arg}");
                            break;
                    }
                }
            }
            InitializeComponent();
        }

        ~MainForm()
        {

        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            try
            {
                m_processManager.CleanUp();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cleanup failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReloadSettings(object sender, EventArgs e)
        {
            m_neverMuteList = Properties.Settings.Default.NeverMuteProcs;
            NeverMuteTextBox.Text = m_neverMuteList;

            LoggerCheckbox.Checked = Properties.Settings.Default.EnableLogging;
            ConsoleLogging.Checked = Properties.Settings.Default.EnableConsole;
            DarkModeCheckbox.Checked = Properties.Settings.Default.EnableDarkMode;
            AutostartCheckbox.Checked = Properties.Settings.Default.EnableAutostart;
            MinimizeToTrayCheckbox.Checked = Properties.Settings.Default.MinimizeToTray;
            CloseToTrayCheckbox.Checked = Properties.Settings.Default.CloseToTray;

            if (Properties.Settings.Default.IsMuteConditionBackground == true)
            {
                BackGroundRadioButton.Checked = true;
                m_isMuteConditionBackground = true;
            }

            else
            {
                MinimizedRadioButton.Checked = true;
                m_isMuteConditionBackground = false;

            }

            LoggerCheckbox_CheckedChanged(sender, EventArgs.Empty);
            ConsoleLogging_CheckedChanged(sender, EventArgs.Empty);
            DarkModeCheckbox_CheckedChanged(sender, EventArgs.Empty);
            AutostartCheckbox_CheckedChanged(sender, EventArgs.Empty);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoggingEngine.LogLevel = LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG;
            LoggingEngine.HasDateTime = true;
            LoggingEngine.LogLine("Initializing...");

            if (m_enableDemo == true)
            {
                m_neverMuteList = Properties.Settings.Default.NeverMuteProcs;
                NeverMuteTextBox.Text = m_neverMuteList;
                LoggerCheckbox.Checked = Properties.Settings.Default.EnableLogging;
                LoggerCheckbox_CheckedChanged(sender, EventArgs.Empty);

                m_processManager = new ForegroundProcessManager();
                m_processManager.Init();

                return;
            }

            m_volumeMixer = new VolumeMixer();
            m_processManager = new ForegroundProcessManager();

            ReloadSettings(sender, e);

            m_processManager.Init();

            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

            Properties.Settings.Default.PropertyChanged += Default_PropertyChanged;

            SaveChangesButton.Enabled = false;

            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            if (assembly.Location.Length == 0)
            {
                MessageBox.Show("Assembly Location not detected. This may be due to a non-standard build process. Beware that this may break some features.");
            }

            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);

            this.Text += " - v" + fvi.ProductVersion;

            m_keepAliveTimer.Elapsed += KeepAliveTimer_Tick;
            m_keepAliveTimer.AutoReset = true;
            m_keepAliveTimer.Enabled = true;

            InitializePauseOnUnfocus();
        }

        private void Default_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            m_settingsChanged = true;
            this.SaveChangesButton.Enabled = true;
        }

        private void PopulateNeverMuteListBox()
        {
            string[] neverMuteList = m_neverMuteList.Split(',', StringSplitOptions.RemoveEmptyEntries);

            NeverMuteListBox.Items.Clear();

            foreach (string neverMuteEntry in neverMuteList)
            {
                NeverMuteListBox.Items.Add(neverMuteEntry.Trim());
            }
        }

        /// <summary>
        /// Returns a case-insensitive HashSet of process names from the never-mute list.
        /// This is the canonical way to check if a process should be skipped.
        /// </summary>
        private HashSet<string> GetNeverMuteSet()
        {
            if (string.IsNullOrEmpty(m_neverMuteList))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return m_neverMuteList
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a process name is in the never-mute list (case-insensitive exact match).
        /// </summary>
        private bool IsInNeverMuteList(string processName)
        {
            return GetNeverMuteSet().Contains(processName);
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            TrayIcon.Visible = false;

            CleanupPauseOnUnfocus();

            if (m_settingsChanged)
            {
                var res = MessageBox.Show("Settings changed. Would you like to save?", "Saving...", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    SaveChangesButton_Click(sender, e);
                }
            }
        }

        private void NeverMuteTextBox_TextChanged(object sender, EventArgs e)
        {
            PopulateNeverMuteListBox();
            m_neverMuteList = NeverMuteTextBox.Text;
            Properties.Settings.Default.NeverMuteProcs = m_neverMuteList;
            RefreshProcessListQuiet();
        }

        private void SaveChangesButton_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Save();
            SettingsFileStore.Save();
            m_settingsChanged = false;
            this.SaveChangesButton.Enabled = false;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized && Properties.Settings.Default.MinimizeToTray)
            {
                this.WindowState = FormWindowState.Minimized;
                Hide();
                TrayIcon.Visible = true;
                TrayIcon.ShowBalloonTip(2000);
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void LoggerCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (LoggerCheckbox.Checked)
            {
                Properties.Settings.Default.EnableLogging = true;
                LogTextBox.Enabled = true;
                LogTextBox.Visible = true;
                LoggingEngine.Enabled = true;

                //this.Size = new Size(this.Size.Width, this.Size.Height + 100);

            }
            else
            {
                Properties.Settings.Default.EnableLogging = false;
                LogTextBox.Enabled = false;
                LogTextBox.Visible = false;
                LoggingEngine.Enabled = false;



        //this.Size = new Size(this.Size.Width, this.Size.Height - 100);

    }
}

private void MuterTimer_Tick(object sender, EventArgs e)
{
    RefreshProcessList();
}

private void CloseMenuTray_Click(object sender, EventArgs e)
{
    Application.Exit();
}

private void OpenMenuTray_Click(object sender, EventArgs e)
{
    TrayIcon_DoubleClick(sender, e);
}

private void ConsoleLogging_CheckedChanged(object sender, EventArgs e)
{
    Properties.Settings.Default.EnableConsole = ConsoleLogging.Checked;
            if (ConsoleLogging.Checked)
            {
                LoggingEngine.RestoreDefault();
            }
            else
            {
                LoggingEngine.SetEngine(InternalLog, InternalLogLine);
            }
        }

        private void DarkModeCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.EnableDarkMode = DarkModeCheckbox.Checked;

            if (DarkModeCheckbox.Checked)
                SetDark(this, true);
            else
                SetDark(this, false);
        }

        private void AutostartCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.EnableAutostart = AutostartCheckbox.Checked;

            if (AutostartCheckbox.Checked)
            {
                EnableAutoStart(true);
            }
            else
            {
                EnableAutoStart(false);
            }
        }

        private void MinimizeToTrayCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.MinimizeToTray = MinimizeToTrayCheckbox.Checked;
        }

        private void CloseToTrayCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.CloseToTray = CloseToTrayCheckbox.Checked;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Properties.Settings.Default.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                Hide();
                TrayIcon.Visible = true;
                TrayIcon.ShowBalloonTip(2000);
            }
        }

        private void ReloadAudioButton_Click(object sender, EventArgs e)
        {
            m_volumeMixer.UnloadAudio(true);
            m_volumeMixer.ReloadAudio(true);
            RefreshProcessListQuiet();
        }

        private void ProcessToMuteButton_Click(object sender, EventArgs e)
        {
            try
            {
                var selectedApp = ProcessListListBox.Items[ProcessListListBox.SelectedIndex]?.ToString();
                if (string.IsNullOrEmpty(selectedApp))
                {
                    return;
                }

                // Remove from AutoPlay if present (exclusive)
                RemoveFromAutoPlayList(selectedApp);

                NeverMuteTextBox.AppendText("," + selectedApp);
                NeverMuteTextBox_TextChanged(sender, EventArgs.Empty);

                if (ProcessListListBox.SelectedIndex != -1)
                    ProcessListListBox.Items.RemoveAt(ProcessListListBox.SelectedIndex);
            }
            catch (Exception ex)
            {

            }
        }

        private void RemoveFromAutoPlayList(string appName)
        {
            var currentAutoPlay = Properties.Settings.Default.AutoPlayAppName?.Trim() ?? string.Empty;
            if (string.Equals(currentAutoPlay, appName, StringComparison.OrdinalIgnoreCase))
            {
                Properties.Settings.Default.AutoPlayAppName = string.Empty;
                if (_appController != null)
                {
                    _appController.AutoPlayAppName = string.Empty;
                }
                // Refresh the AutoPlay list UI if it exists
                _autoPlayListBox?.Items.Clear();
                SettingsFileStore.Save();
            }
        }

        private void MuteToProcessButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (NeverMuteListBox.SelectedIndex != -1)
                    NeverMuteListBox.Items.RemoveAt(NeverMuteListBox.SelectedIndex);

                var newText = String.Empty;
                foreach (var item in NeverMuteListBox.Items)
                {
                    newText += item.ToString() + ",";
                }

                NeverMuteTextBox.Text = newText;

                NeverMuteTextBox_TextChanged(sender, EventArgs.Empty);
            }
            catch (Exception ex)
            {
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Reset();
            ReloadSettings(sender, e);
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"                                                                      
Background Muter - Automatically mute background applications                  
Copyright(C) 2022  Nefares(nefares@protonmail.com) github.com / nefares       
                                                                              
This program is free software: you can redistribute it and / or modify        
it under the terms of the GNU General Public License as published by          
the Free Software Foundation, either version 3 of the License, or             
(at your option) any later version.                                           
                                                                              
                                                                              
This program is distributed in the hope that it will be useful,               
but WITHOUT ANY WARRANTY; without even the implied warranty of                
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the                 
GNU General Public License for more details.                                                                                                    
                                                                              
You should have received a copy of the GNU General Public License             
along with this program.If not, see < https://www.gnu.org/licenses/>          
", "About", MessageBoxButtons.OK);
        }

        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e)
        {

        }

        private void BackGroundRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.IsMuteConditionBackground = true;
            m_isMuteConditionBackground = true;
        }

        private void MinimizedRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.IsMuteConditionBackground = false;
            m_isMuteConditionBackground = false;
        }

        private void AdvancedButton_MouseClick(object sender, MouseEventArgs e)
        {
            AdvancedMenuStrip.Show(AdvancedButton, new Point(e.X, e.Y));
        }

        private void KeepAliveTimer_Tick(object sender, EventArgs e)
        {
            LoggingEngine.Log("<Keep Alive>");
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (m_enableMiniStart)
            {
                this.WindowState = FormWindowState.Minimized;
                this.MainForm_Resize(sender, e);
                //if (!this.IsHandleCreated) CreateHandle();
            }
        }

        private void tableLayoutPanel6_Paint(object sender, PaintEventArgs e)
        {

        }

        private void EnableConsole_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.EnableConsole = ConsoleLogging.Checked;

            if (ConsoleLogging.Checked)
            {
                LoggingEngine.RestoreDefault();
            }
            else
            {
                LoggingEngine.SetEngine(InternalLog, InternalLogLine);
            }
        }
    }
}