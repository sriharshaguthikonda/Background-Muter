# Background Muter - Browser Extension

This extension enables **per-tab media control** for Edge/Chrome browsers, allowing Background Muter to pause specific browser tabs when you switch between them.

## Installation

### 1. Load the Extension

**Microsoft Edge:**
1. Open `edge://extensions/`
2. Enable "Developer mode" (toggle in bottom-left)
3. Click "Load unpacked"
4. Select this `BrowserExtension` folder
5. Note the **Extension ID** shown on the extension card

**Google Chrome:**
1. Open `chrome://extensions/`
2. Enable "Developer mode" (toggle in top-right)
3. Click "Load unpacked"
4. Select this `BrowserExtension` folder
5. Note the **Extension ID** shown on the extension card

### 2. Register Native Messaging Host

Run PowerShell as Administrator:

```powershell
cd "C:\Windows_software\Background-Muter\BrowserExtension"
.\install-native-host.ps1 -ExtensionId "YOUR_EXTENSION_ID_HERE"
```

Replace `YOUR_EXTENSION_ID_HERE` with the Extension ID from step 1.

### 3. Restart Browser

Close and reopen your browser for the native messaging host registration to take effect.

## How It Works

1. The content script (`content.js`) runs on every webpage and detects `<video>` and `<audio>` elements
2. When media plays or pauses, the content script notifies the background service worker
3. The background service worker communicates with the Background Muter app via native messaging
4. When you switch tabs, Background Muter sends a pause command to the previously active tab

## Troubleshooting

### Extension not connecting to Background Muter

1. Make sure Background Muter is running
2. Check that the native messaging host is registered correctly:
   - Open Registry Editor
   - Navigate to `HKEY_CURRENT_USER\Software\Microsoft\Edge\NativeMessagingHosts\com.backgroundmuter.tabcontrol`
   - Verify the path points to the correct manifest file

### Media not pausing

1. Some websites use custom video players that may not respond to pause commands
2. Check the browser console (F12 → Console) for any error messages from the extension

## Files

- `manifest.json` - Extension manifest (Manifest V3)
- `background.js` - Service worker for native messaging and tab management
- `content.js` - Content script that detects and controls media in tabs
- `native-messaging-host.json` - Native messaging host manifest
- `install-native-host.ps1` - Script to register the native messaging host
