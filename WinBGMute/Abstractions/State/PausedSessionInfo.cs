using System;

namespace WinBGMuter.Abstractions.State
{
    public sealed class PausedSessionInfo
    {
        public PausedSessionInfo(int processId, string method, string? sessionKey, DateTime pausedAtUtc)
        {
            ProcessId = processId;
            Method = method;
            SessionKey = sessionKey;
            PausedAtUtc = pausedAtUtc;
        }

        public int ProcessId { get; }

        public string Method { get; }

        public string? SessionKey { get; }

        public DateTime PausedAtUtc { get; }
    }
}
