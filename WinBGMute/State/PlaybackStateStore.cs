using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using WinBGMuter.Abstractions;

namespace WinBGMuter.State
{
    internal sealed class PlaybackStateStore : IStateStore
    {
        private readonly ConcurrentDictionary<int, PausedStateEntry> _pausedByUs = new();

        public void MarkPaused(int pid, string method, string sessionKey)
        {
            var entry = new PausedStateEntry(
                DateTimeOffset.UtcNow,
                method,
                sessionKey,
                GetProcessStartTime(pid));

            _pausedByUs[pid] = entry;
            LoggingEngine.LogLine($"[StateStore] Marked PID {pid} as paused by us (method={method})", category: LoggingEngine.LogCategory.State);
        }

        public void Clear(int pid)
        {
            if (_pausedByUs.TryRemove(pid, out _))
            {
                LoggingEngine.LogLine($"[StateStore] Cleared paused state for PID {pid}", category: LoggingEngine.LogCategory.State);
            }
        }

        public bool TryGetPaused(int pid, out PausedState state)
        {
            if (_pausedByUs.TryGetValue(pid, out var entry))
            {
                // Verify process is still the same instance (not PID reuse)
                var currentStartTime = GetProcessStartTime(pid);
                if (currentStartTime == null || entry.ProcessStartTime == null || currentStartTime == entry.ProcessStartTime)
                {
                    state = new PausedState(entry.PausedAtUtc, entry.Method, entry.SessionKey);
                    return true;
                }

                // PID was reused by a different process - clear stale state
                _pausedByUs.TryRemove(pid, out _);
                LoggingEngine.LogLine($"[StateStore] PID {pid} reused, clearing stale state", category: LoggingEngine.LogCategory.State);
            }

            state = null!;
            return false;
        }

        public IEnumerable<int> GetTrackedPids()
        {
            return _pausedByUs.Keys;
        }

        public void CleanupExitedProcesses()
        {
            foreach (var pid in _pausedByUs.Keys)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.HasExited)
                    {
                        _pausedByUs.TryRemove(pid, out _);
                    }
                }
                catch
                {
                    // Process doesn't exist anymore
                    _pausedByUs.TryRemove(pid, out _);
                }
            }
        }

        private static DateTime? GetProcessStartTime(int pid)
        {
            try
            {
                return Process.GetProcessById(pid).StartTime;
            }
            catch
            {
                return null;
            }
        }

        private sealed record PausedStateEntry(
            DateTimeOffset PausedAtUtc,
            string Method,
            string SessionKey,
            DateTime? ProcessStartTime);
    }
}
