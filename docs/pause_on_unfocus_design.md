# Pause-on-Unfocus design

This document captures the behaviour, constraints, and edge cases for adding **pause on unfocus** to Background-Muter. The goal is to pause audio apps when they lose focus and resume only when focus returns **if we initiated the pause**.

## Behaviour rules
- When the foreground window changes, pause any audible background app according to the current policy (pause-only, pause with mute fallback, or mute-only).
- Resume an app only when both are true:
  1. We previously paused it (`pausedByUs == true`).
  2. Its current playback state indicates it can resume (e.g., GSMTC reports paused, not stopped).
- Never issue a toggle-style command. Always request explicit pause or play operations.
- If mapping between process and media session is ambiguous or unsupported, prefer **no action** unless mute fallback is enabled.

## Constraints and realities
- True pause/resume is only supported for apps that expose **Global System Media Transport Controls (GSMTC)**.
- Apps without GSMTC support require a fallback: mute, or app-specific plugins (e.g., VLC HTTP interface).
- Foreground tracking must run on the UI user session; Windows services (SYSTEM) cannot control GSMTC reliably.
- WinEvent hooks must not block; expensive work is dispatched to a background queue.
- Avoid interrupting communication apps: they should be excluded by default or via user lists.

## Event flow
1. **Foreground change** detected via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`.
2. Determine the new foreground process ID and fetch the current audible process set from Core Audio session scans.
3. For each audible PID not matching the foreground:
   - Apply policy to select an action (pause, pause+mute fallback, or mute).
   - Attempt to pause via GSMTC using the session resolver; if unsupported and fallback is enabled, mute.
   - Record `pausedByUs` state only when an action succeeds.
4. When focus returns to a PID marked `pausedByUs`, attempt resume (play) if the media session state allows it; clear the flag afterward.
5. Clear state entries if a process exits or its session disappears.

## Edge cases
- **Rapid Alt+Tab:** debounce foreground changes to avoid oscillation.
- **Manual pause by user:** if the user pauses while focused, do not auto-resume on refocus.
- **Process restart / PID reuse:** stored state should be invalidated when the process start time changes.
- **Multiple active sessions:** if no confident mapping exists, do nothing (or fallback to mute if allowed).
- **Browser media:** expect coarse control (tab-specific pause is not achievable via GSMTC).

## Configuration surface
- Mode: `PauseOnly`, `PauseThenMuteFallback`, `MuteOnly`.
- Lists: included (whitelist) / excluded (blacklist) processes.
- Thresholds: audio peak threshold, poll interval, foreground debounce, resume grace.
- Plugin settings: per-app controls (e.g., VLC HTTP host/port/password).

## Logging expectations
- Startup version and hook registration results.
- Foreground changes with PIDs and window handles.
- Decisions per focus change: audible PIDs, chosen action, reason codes (unsupported, ambiguous, excluded, muted fallback).
- Actions taken: pause/resume/mute successes and failures.

## Acceptance checklist
- Focus changes emit logs without blocking the hook thread.
- Spotify/VLC sessions are visible via GSMTC enumeration.
- Audible PID detection ignores silent but open sessions.
- Apps we paused resume when focus returns; user-paused apps stay paused.
- Excluded processes are untouched.
