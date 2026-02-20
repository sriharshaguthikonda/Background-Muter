## Pause/Resume Roadmap

- Shift focus: implement pause/resume (not mute) for background sessions.

### Recently completed
- [x] Show window titles alongside process names in Running Apps / Never Mute / AutoPlay lists for clarity.
- [x] Pause previous window only when the newly focused window has playing media.
- [x] Fix native messaging host framing to avoid partial reads and stdin contention.
- [x] Backoff native host reconnects when the main app is not running.
- [x] Refresh main UI layout (spacing, sizing, readability).
- [x] Prevent cross-window tab switches from pausing when the target window is silent.

### 1) UX & Controls.
- [x] Tray icon state: show when pausing is active vs. snoozed/disabled.
- [x] Exceptions UI: allow adding/removing apps that should never be paused (e.g., music/voice apps).
- [ ] Onboarding note in-app: explain pause-on-unfocus behavior and how to whitelist apps.
- [x] save settings to file and load it when app loads.
- [x] settings option to close/minimize to tray.

### 2) Pause Logic
- [ ] Core: pause media sessions when window loses focus; resume on regain.
- [ ] Debounce: add grace period to avoid rapid pause/resume during quick Alt+Tab.
- [ ] Optional “reduce volume instead of pause” mode (only if harmless to sessions).
- [ ] Smart exclusions: never pause comms apps by default; user can override.
- [ ] Respect windowed fullscreen/games that misbehave—option to ignore specific titles.

### 3) Session Handling
- [ ] Track per-audio-session state; resume only sessions we paused.
- [ ] Re-attach if a session restarts (new session ID) while app is backgrounded.
- [ ] Handle multiple audio endpoints; remember per-endpoint rules.
- [ ] Detect silent/inactive sessions and auto-resume if they were paused but go idle to avoid stuck state.

### 4) Reliability & Performance
- [ ] Lightweight foreground detection + audio polling to minimize CPU.
- [ ] Add structured logging for pause/resume decisions; expose log location to users.
- [ ] Fallback: if pausing fails, surface a user-visible notification/toast.
- [ ] Optional “snooze pausing for N minutes” timer.

### 5) Config & Persistence
- [ ] Persist settings per user; include export/import of pause rules and exceptions.
- [ ] Hotkey customization for toggle and “pause/unpause current foreground app”.
- [ ] Configurable grace period and resume delay.

### 6) Documentation
- [ ] Add FAQ/troubleshooting section explaining pause vs. mute, common edge cases.
- [ ] Document privacy note if logs/telemetry are collected (telemetry off by default).
