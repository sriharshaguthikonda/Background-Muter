
# Background Muter
![image](https://img.shields.io/github/license/nefares/Background-Muter) ![image](https://img.shields.io/github/issues/nefares/Background-Muter) [![.NET](https://github.com/nefares/Background-Muter/actions/workflows/dotnet.yml/badge.svg)](https://github.com/nefares/Background-Muter/actions/workflows/dotnet.yml) ![GitHub all releases](https://img.shields.io/github/downloads/nefares/Background-Muter/total)

-------
### Now gathering your whitelists :cat: and blacklists :dog: here => (https://github.com/nefares/Background-Muter/discussions/28)
-------

This tool automatically mutes applications in the background, and unmutes them once they are switched to foreground.
You can add exceptions for which applications are never muted.

[comment]: ![image](https://user-images.githubusercontent.com/8545128/170842100-7c0d6dbd-acf8-4d28-b605-8a7abbbc106c.png)

![image](https://github.com/user-attachments/assets/eb353683-71df-4cc5-a833-60c150c3a528)

# Features
* Works out of the box with default settings
* Add exceptions for applications to never be muted
* Minimize to tray icon
* Dark Mode
* Refreshed desktop UI layout for improved spacing and readability
* Debounced foreground detection to reduce rapid mute/unmute flicker during fast app switching
* Foreground change processing coalesces to the latest window to avoid stale pauses/resumes
* **Per-tab media control for browsers via extension + native messaging (multi-profile aware)**
* Browser extension aggregates per-frame media state to avoid false "not playing"
* Extension settings messages are handled in a single listener to avoid duplicate replies
* GSMTC session control falls back when AUMID is missing to keep pause/resume working
* Auto-play respects active browser playback (e.g., Edge tabs) to avoid resuming while a browser is playing
* Auto-play monitor guards against overlapping ticks to prevent rapid play/pause bursts

# Requirements
* .NET 8.0 (https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* Windows 10/11
* Microsoft Edge (for the provided extension; Chromium-based Chrome works similarly)

# Architecture (per-tab / multi-profile)

```mermaid
flowchart LR
    subgraph Edge_Profile_A
        A1[Extension A\n(background.js + content.js)]
        NA[Native host A\n(WinBGMuter.exe -- native messaging)]
    end
    subgraph Edge_Profile_B
        B1[Extension B]
        NB[Native host B]
    end
    subgraph Main_App
        C1[WinBGMuter GUI]
        C2[BrowserCoordinator\n(named pipe server)]
    end

    A1 <--> NA
    B1 <--> NB
    NA <--> C2
    NB <--> C2
    C2 <--> C1
```

**What happens on focus change**
1) Edge window gains focus (any profile) → Extension sends `windowFocused` to its native host → main app → BrowserCoordinator  
2) Coordinator tells all other profiles to `pauseAll`  
3) Focused profile may `playFocused` its active tab if it was paused by the extension  
4) Main app never issues Win32 pauses to browsers (it is delegated to extensions)

# Setup – Main App
1. Extract or build `WinBGMuter.exe` (Release):
   ```powershell
   dotnet build "c:\Windows_software\Background-Muter\Background Muter.sln" -c:Release
   ```
2. Run the app:
   ```powershell
   "c:\Windows_software\Background-Muter\WinBGMute\bin\Release\net8.0-windows10.0.22621.0\WinBGMuter.exe"
   ```
3. Ensure **Pause on Unfocus** is enabled (if you use that feature). Browsers are already excluded from GSMTC control.

# Setup – Browser Extension (do this for **each Edge profile**)
1. Open `edge://extensions/`, enable **Developer mode**.
2. Click **Load unpacked** and select `c:\Windows_software\Background-Muter\BrowserExtension`.
3. Install the native host for that profile (replace `EXT_ID` with the loaded extension ID):
   ```powershell
   powershell -ExecutionPolicy Bypass -File "c:\Windows_software\Background-Muter\BrowserExtension\install-native-host.ps1" -ExtensionId EXT_ID
   ```
4. Repeat steps 1–3 for every Edge profile you want controlled.

# Extension Settings (Options page)
Open extension options (right-click icon → Options):
* **Pause on tab switch** – pause previous tab in the same window  
* **Pause on window switch** – pause previous window’s active tab only if the newly focused window has playing media  
* **Auto-play on window focus** – resume if the tab was paused by the extension

# Testing (cross-profile)
1. In Profile A, play a YouTube video (Window A).
2. In Profile B, focus its Edge window → Profile A should pause (`pauseAll` broadcast).
3. Focus back Profile A → Profile A may auto-play active tab (if enabled), Profile B stays paused.
4. Watch logs:
   * Service worker console in each profile (`edge://extensions/` → “service worker”).
   * App log: `WinBGMuter\Logs\` or the in-app console.

# Troubleshooting
* If it doesn’t pause when switching profiles: ensure native host is installed **in each profile** and BrowserCoordinator is running (main app open).
* If the main app is closed, the extension will retry the native host with backoff (up to about 5 minutes), so you may see occasional host spawns.
* If build fails due to locked exe: close running `WinBGMuter.exe` and rebuild.
* To wipe stale registry entries for native host, rerun the install script with correct ExtensionId.
* Developer note: native messaging uses length-prefixed stdin/stdout; avoid any other reads from stdin in the host process.

# Existing UI (classic)
* Run **WinBGMuter.exe**. The application will automatically start muting background processes with default settings.
* To add/remove exceptions: use the “Mute Exceptions” box or the “Mute Exception Changer” lists.
* Other toggles: Logger, Console, Dark Mode, Mute Condition (Background/Minimized), Minimize to Tray.

<img width="1027" height="418" alt="image" src="https://github.com/user-attachments/assets/9d9cceb9-3d16-400b-88e1-afbd90a5834d" />

# License

This program is licensed under GNU GPL v3 (https://choosealicense.com/licenses/gpl-3.0/)
