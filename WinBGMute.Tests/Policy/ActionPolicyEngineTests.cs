using System.Diagnostics;
using WinBGMuter.Abstractions;
using WinBGMuter.Policy;
using Xunit;

namespace WinBGMute.Tests.Policy;

public sealed class ActionPolicyEngineTests
{
    [Fact]
    public void Evaluate_SkipsForegroundProcess()
    {
        var policy = new ActionPolicyEngine();
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(child.Id, new[] { child.Id });

            Assert.Empty(decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    [Fact]
    public void Evaluate_SkipsCurrentProcess()
    {
        var policy = new ActionPolicyEngine();
        var currentPid = Environment.ProcessId;

        var decision = policy.Evaluate(-1, new[] { currentPid });

        Assert.Empty(decision.ToPause);
    }

    [Fact]
    public void Evaluate_BlacklistMode_PausesAudibleProcessWhenNotExcluded()
    {
        var policy = new ActionPolicyEngine(
            PolicyListMode.Blacklist,
            excludedProcesses: ["definitely-not-cmd"]);
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(-1, new[] { child.Id });

            Assert.Equal(new[] { child.Id }, decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    [Fact]
    public void Evaluate_BlacklistMode_DoesNotPauseExcludedProcess()
    {
        var policy = new ActionPolicyEngine(
            PolicyListMode.Blacklist,
            excludedProcesses: ["cmd"]);
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(-1, new[] { child.Id });

            Assert.Empty(decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    [Fact]
    public void Evaluate_WhitelistMode_PausesIncludedProcess()
    {
        var policy = new ActionPolicyEngine(
            PolicyListMode.Whitelist,
            includedProcesses: ["CMD"]);
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(-1, new[] { child.Id });

            Assert.Equal(new[] { child.Id }, decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    [Fact]
    public void Evaluate_WhitelistMode_DoesNotPauseProcessOutsideIncludeList()
    {
        var policy = new ActionPolicyEngine(
            PolicyListMode.Whitelist,
            includedProcesses: ["definitely-not-cmd"]);
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(-1, new[] { child.Id });

            Assert.Empty(decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    [Fact]
    public void Evaluate_IgnoresPidWhenProcessLookupFails()
    {
        var policy = new ActionPolicyEngine();

        var decision = policy.Evaluate(-1, new[] { -12345 });

        Assert.Empty(decision.ToPause);
    }

    [Fact]
    public void Evaluate_PreservesDuplicateAudiblePidsThatPassPolicy()
    {
        var policy = new ActionPolicyEngine();
        var child = StartLongRunningCmd();

        try
        {
            var decision = policy.Evaluate(-1, new[] { child.Id, child.Id });

            Assert.Equal(new[] { child.Id, child.Id }, decision.ToPause);
        }
        finally
        {
            Stop(child);
        }
    }

    private static Process StartLongRunningCmd()
    {
        var comSpec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = comSpec,
            Arguments = "/c ping -n 30 127.0.0.1 > NUL",
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        Assert.NotNull(process);
        Assert.Equal("cmd", process.ProcessName, StringComparer.OrdinalIgnoreCase);
        return process;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for an already-exited short-lived test process.
        }
        finally
        {
            process.Dispose();
        }
    }
}
