using System;

namespace WinBGMuter.Abstractions.Audio
{
    public sealed class AudioProcessState
    {
        public AudioProcessState(int processId, string processName, bool isActive, float peak, DateTime lastSeenUtc)
        {
            ProcessId = processId;
            ProcessName = processName;
            IsActive = isActive;
            Peak = peak;
            LastSeenUtc = lastSeenUtc;
        }

        public int ProcessId { get; }

        public string ProcessName { get; }

        public bool IsActive { get; }

        public float Peak { get; }

        public DateTime LastSeenUtc { get; }
    }
}
