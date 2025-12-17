# Glossary

Definitions for terms used in the pause-on-unfocus work.

- **PID (Process ID):** Numeric identifier for a running process, used to correlate foreground windows and audio sessions.
- **Foreground window:** The window currently receiving user input; retrieved via `GetForegroundWindow()`.
- **WinEvent hook:** A callback registered with `SetWinEventHook` to receive events such as `EVENT_SYSTEM_FOREGROUND` when the foreground window changes.
- **Core Audio session:** An audio session returned by `IAudioSessionManager2` / `IAudioSessionEnumerator`; exposes process ID and activity state.
- **Audible process:** A process whose audio session is active and above a configured peak threshold.
- **GSMTC (Global System Media Transport Controls):** Windows API surface for media sessions that support play/pause/stop; accessed via `GlobalSystemMediaTransportControlsSessionManager`.
- **Media session resolver:** Logic that maps a PID to a specific GSMTC media session using heuristics and cached associations.
- **Policy mode:** Configuration describing what action to apply to background audio (`PauseOnly`, `PauseThenMuteFallback`, `MuteOnly`).
- **Mute fallback:** Secondary action to silence audio when pause is unsupported or ambiguous.
- **PausedByUs state:** Tracking record indicating we issued a pause for a given PID, enabling safe resumes only for those processes.
- **Plugin:** Optional per-application controller (e.g., VLC HTTP interface) that can pause/resume when GSMTC is unavailable.
