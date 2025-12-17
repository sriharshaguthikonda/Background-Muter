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

        private readonly System.Timers.Timer _debounceTimer;
        private readonly object _sync = new();
        private int _lastPid = -1;
        private int _pendingPid = -1;
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
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void DebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            int pid;
            lock (_sync)
            {
                pid = _pendingPid;
                _pendingPid = -1;
            }

            if (pid == -1 || pid == _lastPid)
            {
                return;
            }

            var previous = _lastPid;
            _lastPid = pid;
            ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs(previous, pid, IntPtr.Zero, DateTimeOffset.UtcNow));
        }

        public void Dispose()
        {
            Stop();
            _debounceTimer.Dispose();
        }
    }
}
