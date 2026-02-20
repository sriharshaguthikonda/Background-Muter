using System.Runtime.InteropServices;
using WinBGMuter.Abstractions;

namespace WinBGMuter.Foreground
{
    /// <summary>
    /// WinEvent-based foreground tracker implementing the IForegroundTracker abstraction.
    /// </summary>
    internal sealed class WinEventForegroundTracker : IForegroundTracker
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const int OBJID_WINDOW = 0x00000000;
        private const int CHILDID_SELF = 0;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private delegate void WinEventProcDelegate(
            IntPtr hWinEventHook,
            uint @event,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventProcDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private readonly System.Timers.Timer _debounceTimer;
        private readonly object _sync = new();
        private int _lastPid = -1;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private int _pendingPid = -1;
        private IntPtr _pendingHwnd = IntPtr.Zero;
        private IntPtr _hook = IntPtr.Zero;
        private WinEventProcDelegate? _callback;

        public WinEventForegroundTracker(int debounceIntervalMs = 200)
        {
            _debounceTimer = new System.Timers.Timer(debounceIntervalMs)
            {
                AutoReset = false
            };
            _debounceTimer.Elapsed += DebounceElapsed;
        }

        public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

        public void Start()
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            _callback = WinEventProc;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
            _debounceTimer.Stop();
        }

        private void WinEventProc(
            IntPtr hWinEventHook,
            uint @event,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (@event != EVENT_SYSTEM_FOREGROUND || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            {
                return;
            }

            uint pid = 0;
            var threadId = GetWindowThreadProcessId(hwnd, out pid);
            if (threadId == 0 || pid == 0)
            {
                return;
            }

            lock (_sync)
            {
                _pendingPid = (int)pid;
                _pendingHwnd = hwnd;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void DebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            int pid;
            IntPtr hwnd;
            lock (_sync)
            {
                pid = _pendingPid;
                hwnd = _pendingHwnd;
                _pendingPid = -1;
                _pendingHwnd = IntPtr.Zero;
            }

            if (pid == -1)
            {
                return;
            }

            // Fire event if PID changed OR if window handle changed (same-process window switch)
            if (pid == _lastPid && hwnd == _lastHwnd)
            {
                return;
            }

            var previous = _lastPid;
            _lastPid = pid;
            _lastHwnd = hwnd;
            var title = GetWindowTitleSafe(hwnd);
            ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs(previous, pid, hwnd, DateTimeOffset.UtcNow, title));
        }

        public void Dispose()
        {
            Stop();
            _debounceTimer.Dispose();
        }

        private static string? GetWindowTitleSafe(IntPtr hwnd)
        {
            try
            {
                var length = GetWindowTextLength(hwnd);
                if (length <= 0)
                {
                    return null;
                }

                var sb = new System.Text.StringBuilder(length + 1);
                if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
                {
                    return sb.ToString();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
