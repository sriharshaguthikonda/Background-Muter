using System;
using System.Collections.Generic;
using System.Linq;
using WinBGMuter.Abstractions;

namespace WinBGMuter.Policy
{
    internal sealed class ActionPolicyEngine : IActionPolicy
    {
        private readonly PolicyMode _mode;
        private readonly PolicyListMode _listMode;
        private readonly HashSet<string> _includedProcesses;
        private readonly HashSet<string> _excludedProcesses;
        private readonly int _selfPid;

        public ActionPolicyEngine(
            PolicyMode mode = PolicyMode.PauseThenMuteFallback,
            PolicyListMode listMode = PolicyListMode.Blacklist,
            IEnumerable<string>? includedProcesses = null,
            IEnumerable<string>? excludedProcesses = null)
        {
            _mode = mode;
            _listMode = listMode;
            _includedProcesses = new HashSet<string>(includedProcesses ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _excludedProcesses = new HashSet<string>(excludedProcesses ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _selfPid = Environment.ProcessId;
        }

        public PolicyMode Mode => _mode;

        public PolicyDecision Evaluate(int foregroundPid, IReadOnlyList<int> audiblePids, string? foregroundProcessName = null)
        {
            var toPause = new List<int>();
            var toMute = new List<int>();

            foreach (var pid in audiblePids)
            {
                if (pid == foregroundPid || pid == _selfPid)
                {
                    continue;
                }

                if (!ShouldActOn(pid, foregroundProcessName))
                {
                    continue;
                }

                switch (_mode)
                {
                    case PolicyMode.PauseOnly:
                        toPause.Add(pid);
                        break;

                    case PolicyMode.PauseThenMuteFallback:
                        toPause.Add(pid);
                        break;

                    case PolicyMode.MuteOnly:
                        toMute.Add(pid);
                        break;
                }
            }

            return new PolicyDecision(toPause, toMute, _mode);
        }

        private bool ShouldActOn(int pid, string? processName)
        {
            if (processName == null)
            {
                try
                {
                    processName = System.Diagnostics.Process.GetProcessById(pid).ProcessName;
                }
                catch
                {
                    return false;
                }
            }

            return _listMode switch
            {
                PolicyListMode.Whitelist => _includedProcesses.Contains(processName),
                PolicyListMode.Blacklist => !_excludedProcesses.Contains(processName),
                _ => true
            };
        }
    }
}
