// Background Muter - Tab Control Extension
// Background Service Worker

const NATIVE_HOST_NAME = "com.backgroundmuter.tabcontrol";
const DEBUG = true;
const RECONNECT_BASE_MS = 2 * 60 * 1000; // 2 min
const RECONNECT_MAX_MS = 5 * 60 * 1000; // 5 min
const RECONNECT_JITTER_MS = 15000; // 0-15s

function log(...args) {
    if (DEBUG) console.log("[BGMuter]", new Date().toLocaleTimeString(), ...args);
}

function logState() {
    log("=== STATE ===");
    log("  lastActiveTabId:", lastActiveTabId);
    log("  lastActiveWindowId:", lastActiveWindowId);
    log("  tabMediaState size:", tabMediaState.size);
    tabMediaState.forEach((state, tabId) => {
        const frames = state.frameStates ? state.frameStates.size : 0;
        log(`    Tab ${tabId}: window=${state.windowId}, playing=${state.playing}, frames=${frames}, title="${state.title}"`);
    });
    log("=============");
}

// Track tabs with active media
// tabId -> { playing: bool, title: string, url: string, windowId: number, pausedByExtension: bool, frameStates: Map<frameId, bool> }
const tabMediaState = new Map();

// Track the last active tab that was playing
let lastPlayingTabId = null;
let lastActiveTabId = null;
let lastActiveWindowId = null;
let pendingFocusLossTimer = null;

const FOCUS_LOSS_DEBOUNCE_MS = 800;

function isWindowPlaying(windowId) {
    for (const state of tabMediaState.values()) {
        if (state.windowId === windowId && state.playing) {
            return true;
        }
    }
    return false;
}

function scheduleFocusLossPause() {
    if (!settings.pauseOnTabSwitch) {
        log("BROWSER LOST FOCUS ignored (pauseOnTabSwitch disabled)");
        return;
    }

    if (pendingFocusLossTimer) {
        clearTimeout(pendingFocusLossTimer);
        pendingFocusLossTimer = null;
    }

    pendingFocusLossTimer = setTimeout(async () => {
        pendingFocusLossTimer = null;

        try {
            const windows = await chrome.windows.getAll({ populate: false });
            const hasFocused = windows.some(w => w.focused);
            if (hasFocused) {
                log("BROWSER LOST FOCUS canceled (another Edge window is focused)");
                return;
            }
        } catch (e) {
            log("BROWSER LOST FOCUS check failed:", e.message);
        }

        log("BROWSER LOST FOCUS (confirmed)");
        if (settings.pauseOnWindowSwitch && settings.pauseOnTabSwitch) {
            pauseAllTabs().catch(() => {});
        }
        if (nativePort) {
            nativePort.postMessage({ type: "browserLostFocus" });
        }
    }, FOCUS_LOSS_DEBOUNCE_MS);
}

function clearFocusLossPause() {
    if (!pendingFocusLossTimer) return;
    clearTimeout(pendingFocusLossTimer);
    pendingFocusLossTimer = null;
}

async function tryGetTabState(tabId) {
    try {
        const existing = tabMediaState.get(tabId);
        if (existing && typeof existing.playing === "boolean") {
            return { playing: existing.playing, mediaCount: existing.frameStates?.size ?? 0 };
        }
        return await chrome.tabs.sendMessage(tabId, { action: "getState" });
    } catch (e) {
        return null;
    }
}

// Settings (loaded from storage)
let settings = {
    pauseOnTabSwitch: true,
    pauseOnWindowSwitch: true,
    autoPlayOnWindowFocus: true
};

// Load settings from storage
async function loadSettings() {
    try {
        const [syncResult, localResult] = await Promise.all([
            chrome.storage.sync.get(null),
            chrome.storage.local.get(null)
        ]);
        settings = { ...settings, ...localResult, ...syncResult };
        // Extension is only for pausing behavior; enforce the core switches ON.
        if (!settings.pauseOnWindowSwitch || !settings.pauseOnTabSwitch) {
            settings.pauseOnWindowSwitch = true;
            settings.pauseOnTabSwitch = true;
            await Promise.all([
                chrome.storage.sync.set({
                    pauseOnTabSwitch: true,
                    pauseOnWindowSwitch: true
                }),
                chrome.storage.local.set({
                    pauseOnTabSwitch: true,
                    pauseOnWindowSwitch: true
                })
            ]);
            log("Settings forced ON for pauseOnWindowSwitch and pauseOnTabSwitch");
        }
        log("Settings loaded:", settings);
    } catch (e) {
        log("!!! Error loading settings:", e.message);
    }
}

// Native messaging port
let nativePort = null;
let nativeHostEnabled = true; // Set to false to run standalone without native app
let reconnectAttempt = 0;
let reconnectTimer = null;

function resetReconnectBackoff() {
    reconnectAttempt = 0;
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
    }
}

function scheduleReconnect(reason) {
    if (!nativeHostEnabled) return;
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
    }

    const baseDelay = RECONNECT_BASE_MS * Math.pow(2, reconnectAttempt);
    const cappedDelay = Math.min(RECONNECT_MAX_MS, baseDelay);
    const jitter = Math.floor(Math.random() * RECONNECT_JITTER_MS);
    const delay = Math.min(RECONNECT_MAX_MS, cappedDelay + jitter);

    reconnectAttempt = Math.min(reconnectAttempt + 1, 10);
    log("Native host disconnected" + (reason ? ` (${reason})` : "") + `. Reconnecting in ${Math.round(delay / 1000)}s`);

    reconnectTimer = setTimeout(() => {
        connectNativeHost();
    }, delay);
}

// Connect to native messaging host
function connectNativeHost() {
    if (!nativeHostEnabled) {
        console.log("[BGMuter] Native messaging disabled, running standalone");
        return;
    }

    if (nativePort) {
        return;
    }
    
    try {
        nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
        resetReconnectBackoff();
        
        nativePort.onMessage.addListener((message) => {
            console.log("[BGMuter] Received from native:", message);
            handleNativeMessage(message);
        });
        
        nativePort.onDisconnect.addListener(() => {
            const error = chrome.runtime.lastError?.message || "Unknown error";
            console.log("[BGMuter] Native host disconnected:", error);
            nativePort = null;
            
            // Don't retry if host not found - run standalone instead
            if (error.includes("not found") || error.includes("Specified native messaging host not found")) {
                console.log("[BGMuter] Native host not installed, running in standalone mode");
                nativeHostEnabled = false;
                resetReconnectBackoff();
            } else {
                // Try to reconnect with backoff for other errors
                scheduleReconnect(error);
            }
        });
        
        console.log("[BGMuter] Connected to native host");
        
        // Send initial state
        sendTabStates();
    } catch (e) {
        console.error("[BGMuter] Failed to connect to native host:", e);
        nativeHostEnabled = false;
    }
}

// Handle messages from native app (coordinator)
function handleNativeMessage(message) {
    log("<<< Received from native app:", message);
    
    switch (message.action) {
        case "pauseTab":
            pauseTab(message.tabId);
            break;
        case "playTab":
            playTab(message.tabId);
            break;
        case "pauseAllExcept":
            pauseAllTabsExcept(message.tabId);
            break;
        case "pauseAll":
            // Coordinator telling us to pause all media in this profile
            log(">>> COORDINATOR: Pause all tabs in this profile");
            pauseAllTabs();
            break;
        case "playFocused":
            // Coordinator telling us to play the focused tab
            log(">>> COORDINATOR: Play focused tab");
            if (lastActiveTabId) {
                const state = tabMediaState.get(lastActiveTabId);
                if (state && state.pausedByExtension) {
                    playTab(lastActiveTabId);
                }
            }
            break;
        case "getTabStates":
            sendTabStates();
            break;
        default:
            log("Unknown action from native:", message.action);
    }
}

// Pause all tabs in this profile
async function pauseAllTabs() {
    for (const [tabId, state] of tabMediaState) {
        if (state.playing) {
            log(">>> Pausing tab:", tabId, state.title);
            await pauseTab(tabId);
        }
    }
}

// Send current tab states to native app
function sendTabStates() {
    if (!nativePort) return;
    
    const states = [];
    tabMediaState.forEach((state, tabId) => {
        states.push({
            tabId: tabId,
            playing: state.playing,
            title: state.title,
            url: state.url
        });
    });
    
    nativePort.postMessage({
        type: "tabStates",
        tabs: states
    });
}

// Pause media in a specific tab (and mark as paused by extension)
async function pauseTab(tabId, markAsPausedByExtension = true) {
    try {
        log(">>> SENDING PAUSE to tab", tabId);
        const response = await chrome.tabs.sendMessage(tabId, { action: "pause" });
        log("<<< PAUSE response from tab", tabId, ":", response);
        
        // Mark this tab as paused by extension so we can resume it later
        if (markAsPausedByExtension) {
            const state = tabMediaState.get(tabId);
            if (state) {
                state.pausedByExtension = true;
                tabMediaState.set(tabId, state);
                log("  Marked tab", tabId, "as pausedByExtension=true");
            }
        }
    } catch (e) {
        log("!!! PAUSE FAILED for tab", tabId, "- Error:", e.message);
    }
}

// Play media in a specific tab
async function playTab(tabId) {
    try {
        log(">>> SENDING PLAY to tab", tabId);
        const response = await chrome.tabs.sendMessage(tabId, { action: "play" });
        log("<<< PLAY response from tab", tabId, ":", response);
        
        // Clear the pausedByExtension flag
        const state = tabMediaState.get(tabId);
        if (state) {
            state.pausedByExtension = false;
            tabMediaState.set(tabId, state);
            log("  Cleared pausedByExtension flag for tab", tabId);
        }
    } catch (e) {
        log("!!! PLAY FAILED for tab", tabId, "- Error:", e.message);
    }
}

// Pause all tabs except the specified one
async function pauseAllTabsExcept(exceptTabId) {
    for (const [tabId, state] of tabMediaState) {
        if (tabId !== exceptTabId && state.playing) {
            await pauseTab(tabId);
        }
    }
}

// Listen for messages from content scripts
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'settingsChanged') {
        settings = { ...settings, ...message.settings };
        log("Settings updated from options page:", settings);
        sendResponse({ success: true });
        return true;
    }

    if (!sender.tab) {
        log("Message from non-tab context:", message);
        return;
    }
    
    const tabId = sender.tab.id;
    
    switch (message.type) {
        case "mediaStateChanged":
            const oldState = tabMediaState.get(tabId);
            const frameId = sender.frameId ?? 0;
            const frameStates = oldState?.frameStates ?? new Map();
            frameStates.set(frameId, message.playing);
            const anyPlaying = Array.from(frameStates.values()).some(Boolean);
            tabMediaState.set(tabId, {
                playing: anyPlaying,
                title: sender.tab.title || "",
                url: sender.tab.url || "",
                windowId: sender.tab.windowId,
                pausedByExtension: oldState?.pausedByExtension || false,
                frameStates: frameStates
            });
            
            log("*** MEDIA STATE CHANGED ***");
            log("  Tab:", tabId);
            log("  Title:", sender.tab.title);
            log("  Was:", oldState?.playing ? "PLAYING" : "PAUSED/NONE");
            log("  Now:", anyPlaying ? "PLAYING" : "PAUSED");
            logState();
            
            // Notify native app
            if (nativePort) {
                nativePort.postMessage({
                    type: "mediaStateChanged",
                    tabId: tabId,
                    playing: anyPlaying,
                    title: sender.tab.title,
                    url: sender.tab.url
                });
            }
            break;
            
        case "mediaDetected":
            log("Media element detected in tab", tabId, "-", sender.tab.title);
            if (!tabMediaState.has(tabId)) {
                const frameId = sender.frameId ?? 0;
                const frameStates = new Map();
                frameStates.set(frameId, false);
                tabMediaState.set(tabId, {
                    playing: false,
                    title: sender.tab.title || "",
                    url: sender.tab.url || "",
                    windowId: sender.tab.windowId,
                    pausedByExtension: false,
                    frameStates: frameStates
                });
            }
            break;
    }
    
    sendResponse({ success: true });
    return true;
});

// Clean up when tab is closed
chrome.tabs.onRemoved.addListener((tabId) => {
    tabMediaState.delete(tabId);
    
    if (nativePort) {
        nativePort.postMessage({
            type: "tabClosed",
            tabId: tabId
        });
    }
});

// Track active tab changes - THIS IS THE CORE LOGIC FOR PER-TAB PAUSE
chrome.tabs.onActivated.addListener(async (activeInfo) => {
    try {
        const tab = await chrome.tabs.get(activeInfo.tabId);
        
        log("========================================");
        log("TAB ACTIVATED EVENT");
        log("  New tab:", activeInfo.tabId, "in window", activeInfo.windowId);
        log("  Title:", tab.title);
        log("  Previous tab:", lastActiveTabId, "in window", lastActiveWindowId);
        log("  Settings: pauseOnTabSwitch =", settings.pauseOnTabSwitch);
        
        const isSameWindow = lastActiveWindowId === activeInfo.windowId;

        // Only pause if setting is enabled and this is a same-window tab switch
        if (settings.pauseOnTabSwitch && isSameWindow && lastActiveTabId !== null && lastActiveTabId !== activeInfo.tabId) {
            const prevState = tabMediaState.get(lastActiveTabId);
            log("  Previous tab state:", prevState ? (prevState.playing ? "PLAYING" : "PAUSED") : "NOT TRACKED");
            
            if (prevState && prevState.playing) {
                log("  >>> WILL PAUSE previous tab:", lastActiveTabId, "-", prevState.title);
                await pauseTab(lastActiveTabId);
            } else {
                log("  >>> NOT pausing previous tab (not playing or not tracked)");
            }
        } else if (!settings.pauseOnTabSwitch) {
            log("  >>> Tab pause DISABLED in settings, skipping");
        } else if (!isSameWindow) {
            log("  >>> Tab switch across windows, skipping pause logic");
        } else {
            log("  >>> Same tab or no previous tab, skipping pause logic");
        }

        // Ensure the tab's window association stays current
        const currentState = tabMediaState.get(activeInfo.tabId);
        if (currentState) {
            currentState.windowId = activeInfo.windowId;
            currentState.title = tab.title || currentState.title;
            currentState.url = tab.url || currentState.url;
            tabMediaState.set(activeInfo.tabId, currentState);
        }
        
        // Update tracking
        lastActiveTabId = activeInfo.tabId;
        lastActiveWindowId = activeInfo.windowId;
        log("  Updated lastActiveTabId to:", lastActiveTabId);
        log("========================================");
        
        // Notify native app if connected
        if (nativePort) {
            nativePort.postMessage({
                type: "tabActivated",
                tabId: activeInfo.tabId,
                windowId: activeInfo.windowId,
                title: tab.title,
                url: tab.url
            });
        }
    } catch (e) {
        log("!!! ERROR in tab activation handler:", e.message);
    }
});

// Track window focus changes
chrome.windows.onFocusChanged.addListener(async (windowId) => {
    if (windowId === chrome.windows.WINDOW_ID_NONE) {
        // Browser may have lost focus (or switching between windows).
        log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
        log("BROWSER LOST FOCUS (pending confirmation)");
        log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
        scheduleFocusLossPause();
        return;
    }

    clearFocusLossPause();
    
    try {
        const tabs = await chrome.tabs.query({ active: true, windowId: windowId });
        if (tabs.length > 0) {
            const tab = tabs[0];
            
            log("########################################");
            log("WINDOW/BROWSER GAINED FOCUS");
            log("  Window:", windowId, "active tab:", tab.id);
            log("  Title:", tab.title);
            log("  Previous tab:", lastActiveTabId, "in window", lastActiveWindowId);
            log("  Settings: pauseOnWindowSwitch =", settings.pauseOnWindowSwitch);
            log("  Settings: autoPlayOnWindowFocus =", settings.autoPlayOnWindowFocus);
            
            const isWindowSwitch = lastActiveWindowId !== windowId;
            const isTabSwitch = lastActiveTabId !== tab.id;
            log("  isWindowSwitch:", isWindowSwitch, "isTabSwitch:", isTabSwitch);

            const activeState = await tryGetTabState(tab.id);
            if (activeState && typeof activeState.playing === "boolean") {
                const existing = tabMediaState.get(tab.id);
                const frameStates = existing?.frameStates ?? new Map();
                if (!existing?.frameStates) {
                    frameStates.set(0, activeState.playing);
                }
                const aggregatedPlaying = frameStates.size > 0
                    ? Array.from(frameStates.values()).some(Boolean)
                    : activeState.playing;
                tabMediaState.set(tab.id, {
                    playing: aggregatedPlaying,
                    title: tab.title || existing?.title || "",
                    url: tab.url || existing?.url || "",
                    windowId: windowId,
                    pausedByExtension: existing?.pausedByExtension || false,
                    frameStates: frameStates
                });
            }

            const targetWindowHasPlaying = isWindowPlaying(windowId);
            log("  Target window has playing media:", targetWindowHasPlaying);
            
            // PAUSE previous window's tab if setting enabled and we switched windows
            if (settings.pauseOnWindowSwitch && isWindowSwitch && lastActiveTabId !== null && lastActiveTabId !== tab.id) {
                const prevState = tabMediaState.get(lastActiveTabId);
                log("  Previous tab state:", prevState ? JSON.stringify(prevState) : "NOT TRACKED");

                if (!targetWindowHasPlaying) {
                    log("  >>> Target window has no playing media, keeping previous window playing");
                } else if (prevState && prevState.playing) {
                    log("  >>> WILL PAUSE previous window's tab:", lastActiveTabId, "-", prevState.title);
                    await pauseTab(lastActiveTabId);
                } else if (!prevState) {
                    // Tab not tracked - try to pause anyway (best effort)
                    log("  >>> Previous tab NOT TRACKED, trying to pause anyway:", lastActiveTabId);
                    await pauseTab(lastActiveTabId, false); // Don't mark as pausedByExtension since we're not sure
                } else {
                    log("  >>> NOT pausing previous tab (already paused)");
                }
            } else if (!settings.pauseOnWindowSwitch) {
                log("  >>> Window pause DISABLED in settings, skipping");
            } else if (!isWindowSwitch) {
                log("  >>> Same window, skipping pause logic");
            } else {
                log("  >>> No previous tab or same tab, skipping pause logic");
            }
            
            // AUTO-PLAY current window's tab if setting enabled and it was paused by extension
            // This triggers when browser regains focus (after BROWSER_LOST_FOCUS)
            if (settings.autoPlayOnWindowFocus) {
                const currentState = tabMediaState.get(tab.id);
                log("  Current tab state:", currentState ? JSON.stringify(currentState) : "NOT TRACKED");
                
                if (currentState && currentState.pausedByExtension) {
                    log("  >>> WILL AUTO-PLAY current tab:", tab.id, "-", currentState.title);
                    await playTab(tab.id);
                } else {
                    log("  >>> NOT auto-playing (not paused by extension or not tracked)");
                }
            } else {
                log("  >>> Auto-play DISABLED in settings, skipping");
            }
            
            // Update tracking
            lastActiveTabId = tab.id;
            lastActiveWindowId = windowId;
            log("  Updated tracking to tab:", lastActiveTabId, "window:", lastActiveWindowId);
            log("########################################");
            
            if (nativePort) {
                nativePort.postMessage({
                    type: "windowFocused",
                    windowId: windowId,
                    tabId: tab.id,
                    title: tab.title,
                    url: tab.url
                });
            }
        }
    } catch (e) {
        log("!!! ERROR in window focus handler:", e.message);
    }
});

// Initialize the last active tab on startup
async function initializeActiveTab() {
    try {
        const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
        if (tabs.length > 0) {
            lastActiveTabId = tabs[0].id;
            lastActiveWindowId = tabs[0].windowId;
            log("Initialized with active tab:", lastActiveTabId, "window:", lastActiveWindowId);
            log("Tab title:", tabs[0].title);
        } else {
            log("No active tab found during initialization");
        }
    } catch (e) {
        log("!!! Error initializing active tab:", e.message);
    }
}

// Listen for storage changes (in case settings changed from another context)
chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName === 'sync' || areaName === 'local') {
        for (const [key, { newValue }] of Object.entries(changes)) {
            if (key in settings) {
                settings[key] = newValue;
                log("Setting changed:", key, "=", newValue);
            }
        }
    }
});

// Initialize
log("==============================================");
log("BACKGROUND MUTER EXTENSION STARTING");
log("==============================================");

// Load settings first, then initialize
(async () => {
    await loadSettings();
    await initializeActiveTab();
    connectNativeHost();
    log("Extension initialized - ready to track tabs");
    logState();
})();
