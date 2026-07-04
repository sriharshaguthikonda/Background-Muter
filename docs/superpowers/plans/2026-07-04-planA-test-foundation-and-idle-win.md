# Plan A — Test Foundation + Idle-CPU Quick Win Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.
> **Delegation:** Per user instruction, **Codex CLI generates the code for each task; Claude orchestrates, reviews, corrects, and commits.** Codex invocation (bypasses a broken Copilot-MCP that hangs startup):
> `codex exec --ignore-user-config -s workspace-write --skip-git-repo-check "<task prompt>"`
> If a write is blocked, escalate to `--dangerously-bypass-approvals-and-sandbox` (local git repo, recoverable).

**Goal:** Stand up a reusable xUnit test suite + CI and eliminate the always-on idle CPU burn (2s `RefreshProcessList` while hidden), with a measured baseline.

**Architecture:** Add a `WinBGMute.Tests` project (xUnit+Moq) referencing the WinExe via `InternalsVisibleTo`. Add a lightweight `PerfProbe` to measure the two hot timer ticks and capture a baseline. Extract the refresh-gate decision into a pure, unit-tested method and skip refresh while the window is hidden/minimized.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.22621.0`), WinForms, xUnit 2.9, Moq 4.20, GitHub Actions (existing `dotnet.yml`).

## Global Constraints

- Target framework (test project must match): `net8.0-windows10.0.22621.0`
- `Nullable` enable; `ImplicitUsings` enable; `UseWindowsForms` true.
- No behaviour change to muting/AutoPlay — same observable behaviour, less CPU.
- Private, offline. Conventional Commits. One logical change per commit.
- Build to designated folder, killing any running instance first (from `CLAUDE.md`):
  `taskkill.exe //IM WinBGMuter.exe //F 2>/dev/null; rm -rf C:/Windows_software/Background-Muter/build_output/* && dotnet build WinBGMute/ -c Debug --output C:/Windows_software/Background-Muter/build_output/`
- Run tests: `dotnet test "Background Muter.sln" --verbosity minimal`

---

### Task 1: Test project scaffold + smoke test

**Files:**
- Create: `WinBGMute.Tests/WinBGMute.Tests.csproj`
- Create: `WinBGMute.Tests/SmokeTest.cs`
- Modify: `WinBGMute/WinBGMuter.csproj` (add `InternalsVisibleTo`)
- Modify: `Background Muter.sln` (add project via `dotnet sln add`)

**Interfaces:**
- Produces: a runnable test assembly `WinBGMute.Tests` with `InternalsVisibleTo` access to `WinBGMuter` internals (needed for `ForegroundProcessManager`, `PerfProbe`, etc.).

- [ ] **Step 1: Create the test csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Moq" Version="4.20.72" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinBGMute\WinBGMuter.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Grant internals access** — add to `WinBGMute/WinBGMuter.csproj` inside a new `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="WinBGMute.Tests" />
</ItemGroup>
```

- [ ] **Step 3: Write the smoke test**

```csharp
using Xunit;

namespace WinBGMute.Tests;

public class SmokeTest
{
    [Fact]
    public void Harness_Runs()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Add project to solution**

Run: `dotnet sln "Background Muter.sln" add "WinBGMute.Tests/WinBGMute.Tests.csproj"`

- [ ] **Step 5: Run tests — expect green**

Run: `dotnet test "Background Muter.sln" --verbosity minimal`
Expected: build succeeds, `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 6: Commit**

```bash
git add "WinBGMute.Tests/WinBGMute.Tests.csproj" "WinBGMute.Tests/SmokeTest.cs" "WinBGMute/WinBGMuter.csproj" "Background Muter.sln"
git commit -m "test: scaffold WinBGMute.Tests (xUnit+Moq) with smoke test"
```

---

### Task 2: CI runs tests on this branch

**Files:**
- Modify: `.github/workflows/dotnet.yml`

**Context:** The workflow already does restore → build → `dotnet test`, but only triggers on `master`. Add the working branch so tests run on push/PR here. The new test project is picked up automatically by solution-level `dotnet test`.

- [ ] **Step 1: Extend triggers** — change the `on:` block to:

```yaml
on:
  push:
    branches: [ master, "feature/**" ]
  pull_request:
    branches: [ master ]
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/dotnet.yml
git commit -m "ci: run build+test on feature branches"
```

---

### Task 3: PerfProbe instrumentation + baseline capture

**Files:**
- Create: `WinBGMute/Diagnostics/PerfProbe.cs`
- Create: `WinBGMute.Tests/Diagnostics/PerfProbeTests.cs`
- Modify: `WinBGMute/MainForm.cs` (`MuterTimer_Tick`, ~758)
- Modify: `WinBGMute/Controller/AppController.cs` (`OnAutoPlayMonitorTickAsync`, ~150)
- Create: `docs/perf/baseline-2026-07-04.md`

**Interfaces:**
- Produces: `internal sealed class PerfProbe` with `void Add(long elapsedMs)`, `long Count`, `double AvgMs`, `string Snapshot()`.

- [ ] **Step 1: Write failing tests for PerfProbe**

```csharp
using WinBGMute.Diagnostics;
using Xunit;

namespace WinBGMute.Tests.Diagnostics;

public class PerfProbeTests
{
    [Fact]
    public void AvgMs_IsZero_WhenNoSamples()
    {
        var p = new PerfProbe("x");
        Assert.Equal(0, p.Count);
        Assert.Equal(0d, p.AvgMs);
    }

    [Fact]
    public void AvgMs_AveragesSamples()
    {
        var p = new PerfProbe("tick");
        p.Add(10);
        p.Add(20);
        Assert.Equal(2, p.Count);
        Assert.Equal(15d, p.AvgMs);
        Assert.Contains("tick", p.Snapshot());
        Assert.Contains("ticks=2", p.Snapshot());
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`PerfProbe` not defined)

Run: `dotnet test "Background Muter.sln" --verbosity minimal`
Expected: compile error / fail.

- [ ] **Step 3: Implement PerfProbe**

```csharp
namespace WinBGMute.Diagnostics;

/// <summary>Thread-safe running average of tick durations, for CPU profiling.</summary>
internal sealed class PerfProbe
{
    private readonly string _name;
    private long _count;
    private long _totalMs;
    private readonly object _lock = new();

    public PerfProbe(string name) => _name = name;

    public void Add(long elapsedMs)
    {
        lock (_lock)
        {
            _count++;
            _totalMs += elapsedMs;
        }
    }

    public long Count { get { lock (_lock) { return _count; } } }

    public double AvgMs { get { lock (_lock) { return _count == 0 ? 0d : (double)_totalMs / _count; } } }

    public string Snapshot()
    {
        lock (_lock)
        {
            double avg = _count == 0 ? 0d : (double)_totalMs / _count;
            return $"[perf] {_name}: ticks={_count} avg={avg:F2}ms total={_totalMs}ms";
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test "Background Muter.sln" --verbosity minimal`
Expected: PerfProbe tests green.

- [ ] **Step 5: Wire probe into the two hot ticks** (guarded, DEBUG-log every 30 ticks)

In `MainForm.cs` add a field `private readonly WinBGMute.Diagnostics.PerfProbe _muterProbe = new("MuterTimer");` and wrap the tick body:

```csharp
private void MuterTimer_Tick(object sender, EventArgs e)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    RefreshProcessList();
    sw.Stop();
    _muterProbe.Add(sw.ElapsedMilliseconds);
    if (_muterProbe.Count % 30 == 0)
        LoggingEngine.LogLine(_muterProbe.Snapshot(), loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG);
}
```

In `AppController.cs` add `private readonly WinBGMute.Diagnostics.PerfProbe _autoPlayProbe = new("AutoPlayTick");` and record around the body of `OnAutoPlayMonitorTickAsync` (start a `Stopwatch` at the top of the `try`, and in the `finally` do `_autoPlayProbe.Add(sw.ElapsedMilliseconds)` + the same `%30` DEBUG log). Keep the existing `Interlocked` guard.

- [ ] **Step 6: Build + run app, capture baseline**

Build (kill instance first) per Global Constraints, run `build_output/WinBGMuter.exe`, enable console/DEBUG logging, and observe: (a) idle in tray for ~2 min, (b) AutoPlay enabled for ~2 min. Record CPU% (Task Manager) + probe `avg`/`ticks` in `docs/perf/baseline-2026-07-04.md`:

```markdown
# CPU Baseline — 2026-07-04 (pre-optimization)
Commit: <hash>
| Scenario | CPU % (avg) | MuterTimer avg ms | AutoPlay avg ms | Notes |
|----------|-------------|-------------------|-----------------|-------|
| Idle in tray | <fill> | <fill> | n/a | window hidden |
| AutoPlay on  | <fill> | <fill> | <fill> | target app set |
```

- [ ] **Step 7: Commit**

```bash
git add WinBGMute/Diagnostics/PerfProbe.cs "WinBGMute.Tests/Diagnostics/PerfProbeTests.cs" WinBGMute/MainForm.cs WinBGMute/Controller/AppController.cs docs/perf/baseline-2026-07-04.md
git commit -m "perf: add PerfProbe tick instrumentation + capture CPU baseline"
```

---

### Task 4: Gate MuterTimer refresh on window visibility (idle win)

**Files:**
- Create: `WinBGMute/UI/RefreshGate.cs`
- Create: `WinBGMute.Tests/UI/RefreshGateTests.cs`
- Modify: `WinBGMute/MainForm.cs` (`MuterTimer_Tick` ~758; add restore-refresh)

**Interfaces:**
- Produces: `internal static class RefreshGate` with `static bool ShouldRefresh(bool isVisible, System.Windows.Forms.FormWindowState state)`.

**Rationale:** `RefreshProcessList` rebuilds a UI list nobody can see while the app is hidden/minimized to tray. Skipping it there removes the dominant idle CPU cost. Decision extracted to a pure method so it is unit-testable without a Form.

- [ ] **Step 1: Write failing tests**

```csharp
using System.Windows.Forms;
using WinBGMute.UI;
using Xunit;

namespace WinBGMute.Tests.UI;

public class RefreshGateTests
{
    [Fact]
    public void Refresh_WhenVisibleAndNotMinimized()
        => Assert.True(RefreshGate.ShouldRefresh(true, FormWindowState.Normal));

    [Theory]
    [InlineData(false, FormWindowState.Normal)]     // hidden to tray
    [InlineData(true, FormWindowState.Minimized)]   // minimized
    [InlineData(false, FormWindowState.Minimized)]
    public void Skip_WhenHiddenOrMinimized(bool visible, FormWindowState state)
        => Assert.False(RefreshGate.ShouldRefresh(visible, state));
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test "Background Muter.sln" --verbosity minimal`
Expected: `RefreshGate` not defined.

- [ ] **Step 3: Implement RefreshGate**

```csharp
using System.Windows.Forms;

namespace WinBGMute.UI;

internal static class RefreshGate
{
    /// <summary>UI list refresh is only useful when the window is actually on screen.</summary>
    public static bool ShouldRefresh(bool isVisible, FormWindowState state)
        => isVisible && state != FormWindowState.Minimized;
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test "Background Muter.sln" --verbosity minimal`
Expected: RefreshGate tests green.

- [ ] **Step 5: Apply the gate + refresh-on-restore**

In `MuterTimer_Tick` (after the probe Stopwatch start), short-circuit when hidden:

```csharp
private void MuterTimer_Tick(object sender, EventArgs e)
{
    if (!WinBGMute.UI.RefreshGate.ShouldRefresh(this.Visible, this.WindowState))
        return;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    RefreshProcessList();
    sw.Stop();
    _muterProbe.Add(sw.ElapsedMilliseconds);
    if (_muterProbe.Count % 30 == 0)
        LoggingEngine.LogLine(_muterProbe.Snapshot(), loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG);
}
```

In `TrayIcon_DoubleClick` (~726, the restore path that sets `WindowState = Normal`), refresh once on restore so the list is fresh when shown — append after the window is shown/normalized:

```csharp
RefreshProcessListQuiet();
```

- [ ] **Step 6: Build, run, verify behaviour + measure**

Build (kill instance first), run. Verify: while hidden in tray, MuterTimer no longer rebuilds the list (probe tick count stops climbing / CPU drops); on restore the list is correct and current. Record idle CPU delta vs Task 3 baseline in `docs/perf/baseline-2026-07-04.md` (new "post P3" row).

- [ ] **Step 7: Commit**

```bash
git add WinBGMute/UI/RefreshGate.cs "WinBGMute.Tests/UI/RefreshGateTests.cs" WinBGMute/MainForm.cs docs/perf/baseline-2026-07-04.md
git commit -m "perf: skip process-list refresh while window hidden/minimized"
```

---

## Self-Review

- **Spec coverage:** P0 (Tasks 1–2), P1 (Task 3), P3 (Task 4). P2 broad unit-net + P4–P8 event-driven/dedup → **Plan B** (written after Task 3, once COM internals are read + Codex analysis folded in). Reordering note: the full P2 net precedes the *risky* COM refactor in Plan B; the low-risk visibility gate (Task 4) carries its own focused test, so it ships in Plan A.
- **Placeholder scan:** none — the only `<fill>` items are the empirical baseline numbers, filled at runtime by design.
- **Type consistency:** `PerfProbe(name)` / `Add(long)` / `Count` / `AvgMs` / `Snapshot()` and `RefreshGate.ShouldRefresh(bool, FormWindowState)` are used identically in tests and wiring.

## Verification Gate (end of Plan A)

- `dotnet test "Background Muter.sln"` green (3 test classes).
- App builds to `build_output`, runs, muting + AutoPlay unchanged.
- `docs/perf/baseline-2026-07-04.md` shows idle CPU dropping after Task 4.
- 4 logical commits landed.
