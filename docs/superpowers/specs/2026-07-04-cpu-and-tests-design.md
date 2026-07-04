# Design — CPU Reduction + Full Test Suite

**Date:** 2026-07-04
**Repo:** Background-Muter (C# WinForms, .NET 8, Windows tray audio-muter)
**Branch base:** `feature/tcp-coordinator`
**Author:** Claude (orchestrator) with Codex CLI (implementation + review passes)

## Goals

1. Reduce steady-state CPU usage, ending at a **fully event-driven** architecture (no fixed-interval polling for the hot paths), delivered **in phases**.
2. Stand up a **complete, reusable test suite** (xUnit + Moq) with CI, usable for all future work.
3. **Small, logical commits.** Every CPU phase is gated by tests and **measured against a baseline** (baseline + per-stage).

## Non-Goals

- No functional/behaviour change to muting or auto-play logic — same observable behaviour, less CPU.
- No UI redesign.
- No cross-platform work (Windows-only, Core Audio / GSMTC / WinEvent).

## Verified Hotspot Analysis

Confirmed by direct code trace (not estimate):

| # | Path | Fires | Per-tick work | Verdict |
|---|------|-------|---------------|---------|
| 1 | `MainForm.MuterTimer` (2s) → `RefreshProcessList()` — `MainForm.cs:758`, `RefreshProcessList` at `MainForm.cs:205`; started `MainForm.cs:624`, stopped only at `FormClosed` `MainForm.cs:682` | **Every 2s for the whole app lifetime, including minimized to tray** | `VolumeMixer.GetPIDs()` (Core Audio COM enum) + `WindowEnumerator.GetWindowsForProcesses` (EnumWindows sweep) + per-PID `Process.GetProcessById` + full ListBox rebuild | **Primary idle CPU burn.** UI work with no observer. |
| 2 | `AppController._autoPlayMonitorTimer` (1s) → `OnAutoPlayMonitorTickAsync()` — `AppController.cs:129`/`:150` | Every 1s **only when AutoPlay feature enabled** (`_autoPlaySpotify && _autoPlayAppName`); early-returns otherwise (`AppController.cs:152`) | `_mediaController.ListSessionsAsync()` (GSMTC) + LINQ + `IsNonTargetAudioActive()` (mixer scan) | Secondary, feature-gated. |
| 3 | Foreground detection — `ForegroundProcessManager` (`MainForm`) + `WinEventForegroundTracker` (`AppController`) | On foreground-change event | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + one-shot 200ms debounce | **Already event-driven.** Not a poll cost. But **two parallel engines** = tech-debt (dedup in P6). |
| 4 | `BrowserCoordinator` `Task.Delay(500)` loop — `BrowserCoordinator.cs:86` | While TCP server running | accept/poll loop (confirm in P7) | Minor. |
| 5 | `MainForm.m_keepAliveTimer` (600000ms / 10min) — `MainForm.cs:84` | Every 10min | trivial | Negligible; leave. |

**Conclusion:** Idle CPU is dominated by #1. #2 affects AutoPlay users. Target: convert both to event-driven; eliminate the always-on UI refresh first (biggest, cheapest idle win).

## Target Architecture (end state)

- **Session list** driven by Core Audio notifications: `IMMNotificationClient` (device add/remove/default change), `IAudioSessionNotification` (session created), `IAudioSessionEvents` (state/disconnect). UI list refreshes **only on real change**, debounced. No 2s poll.
- **AutoPlay** driven by GSMTC `MediaPlaybackState` change events + audio-session-state events. Pause/resume logic runs on event, debounced. No 1s poll.
- **Foreground** — single event-driven engine behind `IForegroundTracker` (retire the duplicate).
- **Visibility gate** — no UI-list work while the window is hidden/minimized regardless of source.

## Phased Plan

Each phase: small commits, tests green, and (for CPU phases) a measurement delta vs baseline recorded in `docs/perf/`.

### P0 — Test scaffold + CI *(2–3 commits)*
- New `WinBGMute.Tests` project (xUnit + Moq), added to `Background Muter.sln`.
- Reference `WinBGMuter` (may require making it testable — see Testability notes).
- One green smoke test proving the harness runs.
- CI workflow `.github/workflows/ci.yml`: `dotnet restore` + `build` + `test` on push/PR.
- **Accept:** `dotnet test` green locally + in CI.

### P1 — Instrumentation + baseline *(1 commit)*
- Add opt-in perf counters: per-timer tick count + `Stopwatch` cumulative/avg duration for `MuterTimer` tick and AutoPlay tick, logged via `LoggingEngine` behind a debug flag.
- Capture baselines: (a) idle in tray, (b) AutoPlay enabled. Record in `docs/perf/baseline-2026-07-04.md` (CPU% + avg tick ms + ticks/min).
- **Accept:** baseline doc committed with real numbers.

### P2 — Unit tests, safety net *(2–4 commits)*
- Cover pure logic before refactor: `Policy/ActionPolicyEngine`, `State/PlaybackStateStore`, `Media/SessionResolver`, `Config/PauseOnUnfocusSettings`+`SettingsFileStore`, `Audio/AudibilityDetector`.
- Use existing `Abstractions/` interfaces; mock COM/OS boundaries with Moq.
- **Accept:** meaningful assertions (not smoke), all green; these tests must stay green through P3–P7.

### P3 — Idle win: visibility-gate MuterTimer *(1 commit)*
- Skip `RefreshProcessList()` when the window is not visible (hidden/minimized to tray); refresh once on re-show/restore. Optionally stop `MuterTimer` while hidden and restart on show.
- **Accept:** idle-in-tray CPU drops toward ~0 vs P1 baseline; list still correct when window shown. Delta recorded.

### P4 — Event-driven session list *(2–3 commits)*
- Implement Core Audio notification clients (`IMMNotificationClient`, `IAudioSessionNotification`, `IAudioSessionEvents`) in/around `VolumeMixer`/`CoreAudioSessionScanner`; raise a debounced "sessions changed" event; `MainForm` refreshes on that event.
- Retire the `MuterTimer` 2s poll (or reduce to a slow safety net only).
- Marshal to UI thread; keep enumerator/registration alive; dispose on shutdown.
- **Codex** does the COM implementation pass; Claude verifies + measures.
- **Accept:** list updates on real session changes; no periodic wake; delta recorded; P2 tests green.

### P5 — Event-driven AutoPlay *(2–3 commits)*
- Subscribe to GSMTC playback-state changes + audio-session-state events; run existing pause/resume decision (`OnAutoPlayMonitorTickAsync` body) on event, debounced.
- Retire `_autoPlayMonitorTimer` 1s poll.
- **Accept:** AutoPlay behaviour unchanged in manual test; no 1s wake; delta recorded; tests green.

### P6 — Unify foreground engines *(2–3 commits)* *(user-requested)*
- Route `MainForm` through the `IForegroundTracker` / `WinEventForegroundTracker` used by `AppController`; retire `ForegroundProcessManager`.
- Add tracker unit tests where feasible.
- **Accept:** single foreground engine; foreground-based muting behaviour unchanged; tests green.

### P7 — BrowserCoordinator loop *(1 commit)*
- Confirm the `Task.Delay(500)` loop (`BrowserCoordinator.cs:86`) and convert any polling to await/event-driven accept.
- **Accept:** no fixed-interval wake in the coordinator; browser integration still works; delta recorded.

### P8 — Wrap *(1 commit)*
- Final `docs/perf/summary.md` (before/after table across phases). Update `CLAUDE.md`/`README` test + perf notes.

## Testing Strategy

- **Framework:** xUnit + Moq (standard .NET, low friction).
- **Scope now:** unit tests over pure logic through `Abstractions/` seams; COM/OS mocked.
- **Future-ready:** project structured so integration/COM smoke tests can be added later without restructuring.
- **Testability notes:** `WinBGMuter` is `WinExe`; the test project references it directly (`InternalsVisibleTo` for `internal` types like `ForegroundProcessManager`). Extract logic behind interfaces only where a seam is missing and cheap.
- **Gate:** P2 tests must remain green through every later phase — they are the refactor safety net.

## Measurement Methodology

- Metric: process CPU% (Task Manager / `Get-Counter`) at idle-in-tray and AutoPlay-on, plus in-app avg tick ms + ticks/min from P1 instrumentation.
- Record baseline (P1) and a delta after each CPU phase (P3–P7) in `docs/perf/`.
- A phase is "done" only with a recorded measurement showing no regression and (for CPU phases) improvement or justified neutrality. *(Evidence before done.)*

## Risks & Mitigations

- **COM threading** (P4/P5): notifications arrive off the UI thread → marshal via `Control.BeginInvoke`; ensure STA; keep registrations rooted; dispose on exit. Mitigate with debounce + P2 net + manual smoke.
- **Event storms**: debounce (reuse the 200ms pattern) so bursts don't thrash the UI.
- **Behaviour drift**: P2 unit tests + manual UAT of muting/AutoPlay after P4–P6.
- **WinExe testability**: `InternalsVisibleTo`; extract seams minimally.

## Engine Split (per standing user preference)

- **Codex CLI** — heavy lifting: COM notification implementation (P4/P5), plus a review pass per phase.
- **Claude** — orchestration, verification against baseline, test/CI wiring, small logical commits, correction of Codex output.

## Commit Discipline

Conventional Commits, one logical change each, e.g.:
`test: scaffold WinBGMute.Tests (xUnit+Moq) + CI`, `perf: gate MuterTimer refresh on window visibility`, `perf: event-driven audio session list; retire 2s poll`.
