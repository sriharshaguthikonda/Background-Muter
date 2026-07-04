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
using System.Linq;
using Microsoft.Win32;
using WinBGMuter.Config;
using WinBGMuter.Helpers;

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
        private readonly WinBGMute.Diagnostics.PerfProbe _muterProbe = new("MuterTimer");

        private sealed class ProcessDisplayItem
        {
            public ProcessDisplayItem(string processName, string? windowTitle, IntPtr windowHandle = default, int processId = 0)
            {
                ProcessName = processName;
                WindowTitle = windowTitle;
                WindowHandle = windowHandle;
                ProcessId = processId;
            }

            public string ProcessName { get; }
            public string? WindowTitle { get; }
            public IntPtr WindowHandle { get; }
            public int ProcessId { get; }

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(WindowTitle)
                    ? ProcessName
                    : $"{ProcessName} – {WindowTitle}";
            }
        }

        private static string ExtractProcessName(object? item)
        {
            return item switch
            {
                ProcessDisplayItem p => p.ProcessName,
                string s => s.Split('–')[0].Trim(),
                _ => item?.ToString() ?? string.Empty
            };
        }

        private static string? TryGetWindowTitle(Process proc)
        {
            try
            {
                var title = proc.MainWindowTitle;
                return string.IsNullOrWhiteSpace(title) ? null : title;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetWindowTitleByProcessName(string processName)
        {
            try
            {
                var proc = Process.GetProcessesByName(processName).FirstOrDefault();
                return proc != null ? TryGetWindowTitle(proc) : null;
            }
            catch
            {
                return null;
            }
        }

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

            // Enumerate all visible windows for audio-producing processes
            var allWindows = WindowEnumerator.GetWindowsForProcesses(audio_pids);
            var addedProcesses = new HashSet<int>();

            foreach (var window in allWindows)
            {
                try
                {
                    string pname = window.ProcessName;

                    // Exclusive: skip if in NeverMute or AutoPlay
                    if (NeverMuteListBox.Items.Cast<object?>().Any(i => string.Equals(ExtractProcessName(i), pname, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if (string.Equals(pname, autoPlayApp, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ProcessListListBox.Items.Add(new ProcessDisplayItem(pname, window.Title, window.Handle, window.ProcessId));
                    addedProcesses.Add(window.ProcessId);
                }
                catch (Exception ex)
                {
                    HandleError(ex, (object)window.ProcessId);
                }
            }

            // Add processes that have audio but no visible windows (background audio)
            foreach (var pid in audio_pids)
            {
                if (addedProcesses.Contains(pid))
                {
                    continue;
                }

                try
                {
                    Process proc = Process.GetProcessById(pid);
                    string pname = proc.ProcessName;

                    if (NeverMuteListBox.Items.Cast<object?>().Any(i => string.Equals(ExtractProcessName(i), pname, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if (string.Equals(pname, autoPlayApp, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ProcessListListBox.Items.Add(new ProcessDisplayItem(pname, "(background audio)", IntPtr.Zero, pid));
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
        // ── Modern Windows 11-style dark palette ──────────────────────
        private static readonly Color DarkBase     = Color.FromArgb(25, 25, 30);     // window bg
        private static readonly Color DarkCard     = Color.FromArgb(36, 36, 44);     // card/groupbox bg
        private static readonly Color DarkInput    = Color.FromArgb(45, 45, 56);     // inputs/lists
        private static readonly Color DarkBorder   = Color.FromArgb(60, 60, 75);     // subtle borders
        private static readonly Color DarkFg       = Color.FromArgb(230, 232, 240);  // primary text
        private static readonly Color DarkFgDim    = Color.FromArgb(150, 155, 170);  // secondary text
        private static readonly Color DarkAccent   = Color.FromArgb(96, 165, 250);   // blue accent
        private static readonly Color DarkAccentDim = Color.FromArgb(60, 110, 200);  // pressed accent
        private static readonly Color DarkHover    = Color.FromArgb(50, 52, 65);     // button hover
        private static readonly Color DarkLogBg    = Color.FromArgb(18, 18, 24);     // log/console bg
        private static readonly Color DarkStatusBg = Color.FromArgb(20, 20, 28);     // status bar

        private static readonly Font UIFont       = new Font("Segoe UI", 9F, FontStyle.Regular);
        private static readonly Font UIFontBold   = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        private static readonly Font UIFontSmall  = new Font("Segoe UI", 8F, FontStyle.Regular);

        private void SetDark(Control parent, bool dark)
        {
            int USE_DARK_MODE = dark ? 1 : 0;
            DwmSetWindowAttribute(parent.Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref USE_DARK_MODE, sizeof(int));

            // Request rounded corners on Windows 11
            int DWMWCP_ROUND = 2;
            DwmSetWindowAttribute(parent.Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));

            parent.SuspendLayout();
            ApplyThemeRecursive(parent, dark);
            parent.ResumeLayout(true);

            if (parent.Parent == null)
            {
                parent.Invalidate(true);
            }
        }

        private void ApplyThemeRecursive(Control parent, bool dark)
        {
            foreach (Control c in parent.Controls)
            {
                if (c.Tag == null)
                {
                    c.Tag = (c.BackColor, c.ForeColor, c.Font);
                }

                if (dark)
                {
                    ApplyDarkToControl(c);
                }
                else
                {
                    RestoreLightControl(c);
                }

                if (c.Controls.Count > 0)
                    ApplyThemeRecursive(c, dark);
            }
        }

        private void RestoreLightControl(Control c)
        {
            if (c.Tag is (Color bg, Color fg, Font f))
            {
                c.BackColor = bg;
                c.ForeColor = fg;
                c.Font = f;
            }

            if (c is Button btn)
            {
                btn.FlatStyle = FlatStyle.Standard;
            }
        }

        private void ApplyDarkToControl(Control c)
        {
            c.ForeColor = DarkFg;
            c.Font = UIFont;

            switch (c)
            {
                case Button btn:
                    btn.BackColor = DarkInput;
                    btn.ForeColor = DarkFg;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = DarkBorder;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.MouseOverBackColor = DarkHover;
                    btn.FlatAppearance.MouseDownBackColor = DarkAccentDim;
                    btn.Cursor = Cursors.Hand;
                    btn.Padding = new Padding(4, 2, 4, 2);
                    break;

                case ListBox lb:
                    lb.BackColor = DarkInput;
                    lb.ForeColor = DarkFg;
                    lb.BorderStyle = BorderStyle.FixedSingle;
                    lb.Font = UIFont;
                    lb.ItemHeight = 22;
                    break;

                case TextBox tb:
                    tb.BackColor = DarkInput;
                    tb.ForeColor = DarkFg;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case RichTextBox rtb:
                    rtb.BackColor = DarkLogBg;
                    rtb.ForeColor = DarkFgDim;
                    rtb.BorderStyle = BorderStyle.None;
                    rtb.Font = new Font("Cascadia Mono", 8.5F, FontStyle.Regular);
                    break;

                case NumericUpDown nud:
                    nud.BackColor = DarkInput;
                    nud.ForeColor = DarkFg;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    nud.Font = UIFont;
                    break;

                case CheckBox cb:
                    cb.BackColor = Color.Transparent;
                    cb.ForeColor = DarkFg;
                    cb.Font = UIFont;
                    break;

                case RadioButton rb:
                    rb.BackColor = Color.Transparent;
                    rb.ForeColor = DarkFg;
                    rb.Font = UIFont;
                    break;

                case GroupBox gb:
                    gb.BackColor = DarkCard;
                    gb.ForeColor = DarkAccent;
                    gb.Font = UIFontBold;
                    gb.Padding = new Padding(6, 8, 6, 6);
                    break;

                case Label lbl:
                    lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = DarkFgDim;
                    lbl.Font = UIFontSmall;
                    break;

                case StatusStrip ss:
                    ss.BackColor = DarkStatusBg;
                    ss.ForeColor = DarkFgDim;
                    ss.Font = UIFontSmall;
                    foreach (ToolStripItem item in ss.Items)
                    {
                        item.ForeColor = DarkFgDim;
                        item.Font = UIFontSmall;
                    }
                    break;

                case Panel pnl:
                    pnl.BackColor = DarkBase;
                    break;

                default:
                    c.BackColor = DarkBase;
                    break;
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
            LoggingEngine.InitializeFileLogging();
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            
            // Initialize logging engine with correct output target
            if (Properties.Settings.Default.EnableConsole)
            {
                LoggingEngine.RestoreDefault();
            }
            else
            {
                LoggingEngine.SetEngine(InternalLog, InternalLogLine);
            }
            
            // Enable logging if the setting is on
            LoggingEngine.Enabled = Properties.Settings.Default.EnableLogging;
            
            LoggingEngine.LogLine("Initializing...");

            if (m_enableDemo == true)
            {
                m_neverMuteList = Properties.Settings.Default.NeverMuteProcs;
                NeverMuteTextBox.Text = m_neverMuteList;
                LoggerCheckbox.Checked = Properties.Settings.Default.EnableLogging;
                LoggerCheckbox_CheckedChanged(sender, EventArgs.Empty);

                return;
            }

            m_volumeMixer = new VolumeMixer();

            ReloadSettings(sender, e);

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

            // Re-apply dark mode to dynamically created controls
            if (Properties.Settings.Default.EnableDarkMode)
            {
                SetDark(this, true);
            }

            MuterTimer.Start();
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
                var name = neverMuteEntry.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var title = TryGetWindowTitleByProcessName(name);
                NeverMuteListBox.Items.Add(new ProcessDisplayItem(name, title));
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

            MuterTimer.Stop();
            m_keepAliveTimer.Stop();
            m_keepAliveTimer.Dispose();
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
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
            _pauseSettings?.Save();
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
                TrayIcon.ShowBalloonTip(7000);
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            RefreshProcessListQuiet();
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
    if (!WinBGMute.UI.RefreshGate.ShouldRefresh(this.Visible, this.WindowState)) return;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    RefreshProcessList();
    sw.Stop();
    _muterProbe.Add(sw.ElapsedMilliseconds);
    if (_muterProbe.Count % 30 == 0)
        LoggingEngine.LogLine(_muterProbe.Snapshot(), loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG, category: LoggingEngine.LogCategory.AudioSessions);
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
                TrayIcon.ShowBalloonTip(7000);
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
                var selectedItem = ProcessListListBox.Items[ProcessListListBox.SelectedIndex];
                var selectedApp = ExtractProcessName(selectedItem);
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
                    var name = ExtractProcessName(item);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        newText += name + ",";
                    }
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

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            string message = e.Mode switch
            {
                PowerModes.Suspend => "[Power] System suspending (sleep/hibernate)",
                PowerModes.Resume => "[Power] System resumed",
                PowerModes.StatusChange => "[Power] Power status changed",
                _ => $"[Power] Power mode changed: {e.Mode}"
            };

            LoggingEngine.LogLine(message, category: LoggingEngine.LogCategory.General);
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
