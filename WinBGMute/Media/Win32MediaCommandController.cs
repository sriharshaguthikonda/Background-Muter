using System;
using System.Runtime.InteropServices;

namespace WinBGMuter.Media
{
    /// <summary>
    /// Sends per-window media commands via WM_APPCOMMAND to allow window-level control (e.g., Edge).
    /// </summary>
    internal static class Win32MediaCommandController
    {
        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_PLAY = 46;
        private const int APPCOMMAND_MEDIA_PAUSE = 47;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public static bool TryPause(int pid, out string handleKey)
        {
            var hwnd = FindWindowForPid(pid);
            if (hwnd == IntPtr.Zero)
            {
                handleKey = string.Empty;
                return false;
            }

            SendAppCommand(hwnd, APPCOMMAND_MEDIA_PAUSE);
            handleKey = BuildHandleKey(hwnd);
            return true;
        }

        public static bool TryResume(string handleKey)
        {
            if (!TryParseHandleKey(handleKey, out var hwnd) || hwnd == IntPtr.Zero)
            {
                return false;
            }

            SendAppCommand(hwnd, APPCOMMAND_MEDIA_PLAY);
            return true;
        }

        private static void SendAppCommand(IntPtr hwnd, int command)
        {
            // Return value is command routing info; no reliable success indicator, so we just send.
            SendMessage(hwnd, WM_APPCOMMAND, hwnd, new IntPtr(command << 16));
        }

        private static bool TryParseHandleKey(string handleKey, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            if (string.IsNullOrWhiteSpace(handleKey))
            {
                return false;
            }

            const string prefix = "HWND:";
            if (!handleKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var hex = handleKey.Substring(prefix.Length);
            if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                hwnd = new IntPtr(value);
                return true;
            }

            return false;
        }

        private static string BuildHandleKey(IntPtr hwnd) => $"HWND:{hwnd.ToInt64():X}";

        private static IntPtr FindWindowForPid(int pid)
        {
            IntPtr result = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowThreadProcessId(hWnd, out var windowPid) != 0 && windowPid == (uint)pid)
                {
                    result = hWnd;
                    return false; // stop enumeration
                }

                return true; // continue enumeration
            }, IntPtr.Zero);

            return result;
        }
    }
}
