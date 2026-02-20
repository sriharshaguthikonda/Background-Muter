using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinBGMuter.Helpers
{
    /// <summary>
    /// Represents a window with its handle, process, and title.
    /// </summary>
    internal sealed class WindowInfo
    {
        public IntPtr Handle { get; }
        public int ProcessId { get; }
        public string ProcessName { get; }
        public string Title { get; }

        public WindowInfo(IntPtr handle, int processId, string processName, string title)
        {
            Handle = handle;
            ProcessId = processId;
            ProcessName = processName;
            Title = title;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Title)
                ? ProcessName
                : $"{ProcessName} – {Title}";
        }
    }

    /// <summary>
    /// Enumerates all visible windows belonging to specified processes.
    /// </summary>
    internal static class WindowEnumerator
    {
        private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        /// <summary>
        /// Gets all visible top-level windows for the given process IDs.
        /// </summary>
        public static List<WindowInfo> GetWindowsForProcesses(IEnumerable<int> processIds)
        {
            var pidSet = new HashSet<int>(processIds);
            var processNames = new Dictionary<int, string>();

            foreach (var pid in pidSet)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    processNames[pid] = proc.ProcessName;
                }
                catch
                {
                    processNames[pid] = "<unknown>";
                }
            }

            var windows = new List<WindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                // Skip windows that are owned (child dialogs, tooltips, etc.)
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out int pid);

                if (!pidSet.Contains(pid))
                {
                    return true;
                }

                var title = GetWindowTitle(hWnd);

                // Skip windows with no title (background windows, utility windows)
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                if (processNames.TryGetValue(pid, out var processName))
                {
                    windows.Add(new WindowInfo(hWnd, pid, processName, title));
                }

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        /// Gets all visible top-level windows for a specific process ID.
        /// </summary>
        public static List<WindowInfo> GetWindowsForProcess(int processId)
        {
            return GetWindowsForProcesses(new[] { processId });
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
