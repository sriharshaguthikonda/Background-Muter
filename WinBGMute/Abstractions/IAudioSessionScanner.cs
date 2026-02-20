using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Abstractions
{
    /// <summary>
    /// Provides snapshots of current audio sessions/process activity.
    /// </summary>
    public interface IAudioSessionScanner
    {
        Task<AudioSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    }

    public sealed class AudioSessionSnapshot
    {
        public AudioSessionSnapshot(DateTimeOffset timestamp, IReadOnlyList<AudioProcessState> processes)
        {
            Timestamp = timestamp;
            Processes = processes;
        }

        public DateTimeOffset Timestamp { get; }
        public IReadOnlyList<AudioProcessState> Processes { get; }
    }

    public sealed class AudioProcessState
    {
        public AudioProcessState(int pid, string processName, bool isActive, float peak, DateTimeOffset lastSeen)
        {
            Pid = pid;
            ProcessName = processName;
            IsActive = isActive;
            Peak = peak;
            LastSeen = lastSeen;
        }

        public int Pid { get; }
        public string ProcessName { get; }
        public bool IsActive { get; }
        public float Peak { get; }
        public DateTimeOffset LastSeen { get; }
    }
}
