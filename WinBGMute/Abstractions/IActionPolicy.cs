using System.Collections.Generic;

namespace WinBGMuter.Abstractions
{
    public interface IActionPolicy
    {
        PolicyDecision Evaluate(int foregroundPid, IReadOnlyList<int> audiblePids, string? foregroundProcessName = null);
    }

    public enum PolicyMode
    {
        PauseOnly,
        PauseThenMuteFallback,
        MuteOnly
    }

    public enum PolicyListMode
    {
        Blacklist,
        Whitelist
    }

    public sealed class PolicyDecision
    {
        public PolicyDecision(IReadOnlyList<int> toPause, IReadOnlyList<int> toMute, PolicyMode mode)
        {
            ToPause = toPause;
            ToMute = toMute;
            Mode = mode;
        }

        public IReadOnlyList<int> ToPause { get; }
        public IReadOnlyList<int> ToMute { get; }
        public PolicyMode Mode { get; }
    }
}
