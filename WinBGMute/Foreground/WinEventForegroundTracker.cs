using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using WinBGMuter.Abstractions.Foreground;
using WinBGMuter.Logging;

namespace WinBGMuter.Foreground
{
    internal sealed class WinEventForegroundTracker : IForegroundTracker
    {
        private const uint EventSystemForeground = 0x0003;
        private const int ObjidWindow = 0x00000000;
        private const int ChildIdSelf = 0;
        private const uint WineventOutofcontext = 0x0000;
        private const uint WineventSkipownprocess = 0x0002;

        private readonly ILogger _logger;
        private readonly object _sync = new();
        private readonly int _debounceIntervalMs;

        private IntPtr _eventHook = IntPtr.Zero;
        private WinEventProc? _winEventProc;
        private Timer? _debounceTimer;
        private int _pendingProcessId = -1;
        private int _currentProcessId = -1;
        private bool _started;
        private bool _disposed;

        public WinEventForegroundTracker(int debounceIntervalMs = 200, ILogger? logger = null)
        {
            _debounceIntervalMs = debounceIntervalMs;
            _logger = logger ?? AppLogging.CreateLogger(LogCategories.Foreground);
        }

        public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

        public int CurrentProcessId
        {
            get
            {
                lock (_sync)
                {
                    return _currentProcessId;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (_started)
                {
                    return;
                }

                _winEventProc = OnWinEvent;
                _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

                _eventHook = SetWinEventHook(
                    EventSystemForeground,
                    EventSystemForeground,
                    IntPtr.Zero,
                    _winEventProc,
                    0,
                    0,
                    WineventOutofcontext | WineventSkipownprocess);

                if (_eventHook == IntPtr.Zero)
                {
                    _logger.LogError("Failed to register foreground window hook");
                }
                else
                {
                    _logger.LogInformation("Foreground hook registered with debounce {DebounceMs}ms", _debounceIntervalMs);
                }

                _currentProcessId = GetForegroundProcessId();
                _started = true;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                if (_eventHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_eventHook);
                    _eventHook = IntPtr.Zero;
                }

                _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _debounceTimer?.Dispose();
                _debounceTimer = null;

                _started = false;
                _pendingProcessId = -1;
                _logger.LogInformation("Foreground hook stopped");
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                Stop();
                _disposed = true;
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType != EventSystemForeground || idObject != ObjidWindow || idChild != ChildIdSelf)
            {
                return;
            }

            var processId = GetProcessIdFromWindow(hwnd);
            if (processId <= 0)
            {
                return;
            }

            lock (_sync)
            {
                if (!_started || _disposed)
                {
                    return;
                }

                _pendingProcessId = processId;
                _debounceTimer?.Change(_debounceIntervalMs, Timeout.Infinite);
            }
        }

        private void OnDebounceTimerElapsed(object? state)
        {
            int pending;
            int previous;

            lock (_sync)
            {
                if (!_started || _disposed)
                {
                    return;
                }

                pending = _pendingProcessId;
                previous = _currentProcessId;
                _pendingProcessId = -1;

                if (pending <= 0 || pending == previous)
                {
                    return;
                }

                _currentProcessId = pending;
            }

            var args = new ForegroundChangedEventArgs(previous, pending, GetForegroundWindow(), DateTime.UtcNow);
            _logger.LogInformation("Foreground changed from {PreviousPid} to {CurrentPid}", previous, pending);
            ForegroundChanged?.Invoke(this, args);
        }

        private static int GetProcessIdFromWindow(IntPtr hwnd)
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            return unchecked((int)pid);
        }

        private static int GetForegroundProcessId()
        {
            var hwnd = GetForegroundWindow();
            return hwnd == IntPtr.Zero ? -1 : GetProcessIdFromWindow(hwnd);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WinEventForegroundTracker));
            }
        }

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc? pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();
    }
}
