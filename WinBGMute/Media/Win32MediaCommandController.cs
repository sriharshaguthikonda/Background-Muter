using System;
using System.Runtime.InteropServices;

namespace WinBGMuter.Media
{
    /// <summary>
    /// Sends media commands (play, pause, stop) to specific windows using WM_APPCOMMAND.
    /// This enables per-window media control, unlike GSMTC which is app-level.
    /// </summary>
    internal static class Win32MediaCommandController
    {
        private const int WM_APPCOMMAND = 0x0319;

        // Media command codes (shifted left by 16 bits for lParam)
        private const int APPCOMMAND_MEDIA_PLAY = 46;
        private const int APPCOMMAND_MEDIA_PAUSE = 47;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

        // For simulating key presses
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        private const uint GA_ROOT = 2;

        /// <summary>
        /// Sends a pause command to the specified window.
        /// </summary>
        public static bool SendPause(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            var lParam = MakeCommandLParam(APPCOMMAND_MEDIA_PAUSE);
            var result = SendMessage(hWnd, WM_APPCOMMAND, hWnd, lParam);
            
            LoggingEngine.LogLine($"[Win32Media] SendPause to {hWnd} -> result={result}",
                category: LoggingEngine.LogCategory.MediaControl);
            
            return true;
        }

        /// <summary>
        /// Sends a play command to the specified window.
        /// </summary>
        public static bool SendPlay(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            var lParam = MakeCommandLParam(APPCOMMAND_MEDIA_PLAY);
            var result = SendMessage(hWnd, WM_APPCOMMAND, hWnd, lParam);
            
            LoggingEngine.LogLine($"[Win32Media] SendPlay to {hWnd} -> result={result}",
                category: LoggingEngine.LogCategory.MediaControl);
            
            return true;
        }

        /// <summary>
        /// Sends a play/pause toggle command to the specified window.
        /// </summary>
        public static bool SendPlayPauseToggle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            var lParam = MakeCommandLParam(APPCOMMAND_MEDIA_PLAY_PAUSE);
            var result = SendMessage(hWnd, WM_APPCOMMAND, hWnd, lParam);
            
            LoggingEngine.LogLine($"[Win32Media] SendPlayPauseToggle to {hWnd} -> result={result}",
                category: LoggingEngine.LogCategory.MediaControl);
            
            return true;
        }

        /// <summary>
        /// Sends a stop command to the specified window.
        /// </summary>
        public static bool SendStop(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            var lParam = MakeCommandLParam(APPCOMMAND_MEDIA_STOP);
            var result = SendMessage(hWnd, WM_APPCOMMAND, hWnd, lParam);
            
            LoggingEngine.LogLine($"[Win32Media] SendStop to {hWnd} -> result={result}",
                category: LoggingEngine.LogCategory.MediaControl);
            
            return true;
        }

        /// <summary>
        /// Posts a pause command asynchronously to the specified window.
        /// Tries multiple approaches: direct window, root ancestor, and keyboard simulation.
        /// </summary>
        public static bool PostPause(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            // Try 1: Send to the root ancestor window (works better for browsers)
            var rootHwnd = GetAncestor(hWnd, GA_ROOT);
            if (rootHwnd != IntPtr.Zero && rootHwnd != hWnd)
            {
                var lParam = MakeCommandLParam(APPCOMMAND_MEDIA_PAUSE);
                SendMessage(rootHwnd, WM_APPCOMMAND, rootHwnd, lParam);
                LoggingEngine.LogLine($"[Win32Media] SendPause to root {rootHwnd} (from {hWnd})",
                    category: LoggingEngine.LogCategory.MediaControl);
            }

            // Try 2: Post to the window itself
            var lParam2 = MakeCommandLParam(APPCOMMAND_MEDIA_PAUSE);
            var result = PostMessage(hWnd, WM_APPCOMMAND, hWnd, lParam2);
            
            LoggingEngine.LogLine($"[Win32Media] PostPause to {hWnd} -> success={result}",
                category: LoggingEngine.LogCategory.MediaControl);
            
            return result;
        }

        /// <summary>
        /// Sends a global media play/pause key press using keyboard simulation.
        /// This pauses whatever media is currently active system-wide.
        /// </summary>
        public static void SendGlobalMediaPlayPause()
        {
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, 0);
            keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
            
            LoggingEngine.LogLine($"[Win32Media] Sent global Media Play/Pause key",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        private static IntPtr MakeCommandLParam(int command)
        {
            // lParam format: command << 16
            return new IntPtr(command << 16);
        }
    }
}
