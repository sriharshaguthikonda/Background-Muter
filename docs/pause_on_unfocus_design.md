# Pause-on-Unfocus: Behaviour & Constraints

## Goal
When the foreground app changes, pause audio-producing apps that lose focus. Resume them only if we paused them. Use GSMTC when possible; otherwise optionally fallback to mute.

## Behaviour Rules
1) On foreground change, determine the audible processes (from Core Audio sessions + audibility heuristic).  
2) For each audible PID not equal to the foreground PID, evaluate policy:  
   - `PauseOnly`: try pause via resolver → GSMTC; if unsupported/ambiguous, do nothing.  
   - `PauseThenMuteFallback`: try pause; if unsupported/ambiguous, mute.  
   - `MuteOnly`: mute (legacy behaviour).  
3) For the new foreground PID, resume only if `pausedByUs[pid]` exists (never toggle).  
4) If mapping is ambiguous, do not pause (unless mute fallback enabled).  
5) Keep hook callbacks fast; queue work off-thread.

## Constraints & Scope
- True pause/resume requires GSMTC; not all apps expose it.  
- PID↔media-session mapping is heuristic; prefer safety over incorrect control.  
- Exclusions: self-process, system processes, user-configured whitelist/blacklist.  
- State must survive rapid Alt+Tab and handle session churn; clear state when process exits or PID is reused.

## Components (planned)
- **Foreground tracker**: WinEvent hook (`EVENT_SYSTEM_FOREGROUND`) with debounce.  
- **Audio session scanner**: Core Audio snapshot → `AudioProcessState { Pid, IsActive, Peak }`.  
- **Audibility detector**: Peak-based threshold to classify audible PIDs.  
- **Media controller (GSMTC)**: enumerate sessions, query playback state, pause/play.  
- **Session resolver**: map PID → media session (name map, single playing session, recency/stickiness).  
- **Actions**: `PauseAction`, `ResumeAction`, `MuteFallbackAction`.  
- **Policy engine**: mode + include/exclude lists.  
- **State store**: `pausedByUs[pid] = { pausedAtUtc, method, sessionKey }`.

## Failure Handling
- If pause unsupported: log `NotSupported`, optionally mute if policy says so.  
- If ambiguous mapping: log `Ambiguous`, skip (or mute fallback).  
- If resume requested but state missing: skip.  
- Always log decisions: foreground pid, audible set, chosen session, action result.

## Non-Goals
- “Pause every audio app perfectly” without per-app integration.  
- Toggling play/pause.  
- Resuming something the user paused manually.

## Acceptance (Commit 01)
- Docs clearly describe behaviours above, especially “resume only if pausedByUs == true”.
