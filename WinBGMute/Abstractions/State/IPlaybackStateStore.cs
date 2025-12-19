using System.Collections.Generic;

namespace WinBGMuter.Abstractions.State
{
    public interface IPlaybackStateStore
    {
        void MarkPaused(PausedSessionInfo sessionInfo);

        bool TryGetPaused(int processId, out PausedSessionInfo? sessionInfo);

        void Clear(int processId);

        void ClearMissingProcesses(IReadOnlyCollection<int> activeProcessIds);
    }
}
