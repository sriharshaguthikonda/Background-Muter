using System.Collections.Generic;

namespace WinBGMuter.Abstractions
{
    public interface IStateStore
    {
        void MarkPaused(int pid, string method, string sessionKey);
        void Clear(int pid);
        bool TryGetPaused(int pid, out PausedState state);
        IEnumerable<int> GetTrackedPids();
    }

    public sealed class PausedState
    {
        public PausedState(DateTimeOffset pausedAtUtc, string method, string sessionKey)
        {
            PausedAtUtc = pausedAtUtc;
            Method = method;
            SessionKey = sessionKey;
        }

        public DateTimeOffset PausedAtUtc { get; }
        public string Method { get; }
        public string SessionKey { get; }
    }
}
