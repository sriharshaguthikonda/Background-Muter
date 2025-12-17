using System.Collections.Generic;

namespace WinBGMuter.Abstractions
{
    public interface IActionPolicy
    {
        PolicyDecision Evaluate(int foregroundPid, IReadOnlyList<int> audiblePids, string? foregroundProcessName = null);
    }

    public enum PolicyMode
    {
        PauseOnly
    }

    public enum PolicyListMode
    {
        Blacklist,
        Whitelist
    }

    public sealed class PolicyDecision
    {
        public PolicyDecision(IReadOnlyList<int> toPause)
        {
            ToPause = toPause;
        }

        public IReadOnlyList<int> ToPause { get; }
    }
}
