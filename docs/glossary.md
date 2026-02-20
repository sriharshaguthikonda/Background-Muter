# Glossary

- **Foreground window**: The window currently receiving user input (from `GetForegroundWindow` / `EVENT_SYSTEM_FOREGROUND`).  
- **PID (Process ID)**: Identifier for a running process.  
- **Audio session**: Core Audio session associated with a process. Multiple sessions can map to one PID.  
- **GSMTC**: Global/System Media Transport Controls (Windows media sessions API). Required for real pause/resume.  
- **Media session**: A GSMTC media session (may not expose PID directly).  
- **Session resolver**: Logic that maps a PID to the best matching media session.  
- **Audible process**: Process with an active audio session whose peak exceeds the audibility threshold.  
- **Policy mode**: Determines what to do with non-foreground audible PIDs (`PauseOnly`, `PauseThenMuteFallback`, `MuteOnly`).  
- **PausedByUs state**: Record that we paused a PID (with method + session key) so we only resume if we issued the pause.  
- **Mute fallback**: Legacy behaviour using Core Audio mute when pause is unsupported or disabled.  
- **Plugin**: App-specific pause/resume handler (e.g., VLC HTTP).  
- **Debounce**: Delay to coalesce rapid foreground change events before acting.  
- **Ambiguous mapping**: No confident PID→session resolution; we avoid pausing in this case unless policy allows mute fallback.  
- **Excluded/Included list**: User-configured blacklist/whitelist controlling which processes we act on.
