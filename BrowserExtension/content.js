// Background Muter - Tab Control Extension
// Content Script - Runs in each tab to detect and control media

(function() {
    'use strict';
    
    const DEBUG = true;
    function log(...args) {
        if (DEBUG) console.log("[BGMuter Content]", new Date().toLocaleTimeString(), ...args);
    }
    
    log("Content script loaded on:", window.location.href);
    
    // Track all media elements in this tab
    const mediaElements = new Set();
    let isPlaying = false;
    let lastNotifiedState = null;
    
    // Notify background script of media state change
    function notifyMediaState(playing) {
        // Always notify to ensure background has current state
        if (lastNotifiedState !== playing) {
            lastNotifiedState = playing;
            isPlaying = playing;
            log(">>> NOTIFYING background: Media state =", playing ? "PLAYING" : "PAUSED", "- Elements:", mediaElements.size);
            try {
                chrome.runtime.sendMessage({
                    type: "mediaStateChanged",
                    playing: playing
                }).catch(() => {});
            } catch (e) {
                log("Failed to notify:", e.message);
            }
        }
    }
    
    // Check if any media element is playing
    function checkPlayingState() {
        let anyPlaying = false;
        mediaElements.forEach(media => {
            if (!media.paused && !media.ended && media.readyState > 2) {
                anyPlaying = true;
            }
        });
        notifyMediaState(anyPlaying);
    }
    
    // Pause all media in this tab
    function pauseAllMedia() {
        let pausedCount = 0;
        mediaElements.forEach(media => {
            if (!media.paused) {
                media.pause();
                pausedCount++;
            }
        });
        log("Paused", pausedCount, "media elements");
    }
    
    // Play the most recent media element
    function playMedia() {
        // Find a paused media element and play it
        for (const media of mediaElements) {
            if (media.paused && media.readyState > 2) {
                media.play().catch(() => {});
                log("Resumed playback");
                break;
            }
        }
    }
    
    // Set up listeners for a media element
    function setupMediaElement(media) {
        if (mediaElements.has(media)) return;
        
        mediaElements.add(media);
        log("Media element detected:", media.nodeName, media.src || "(no src)");
        
        // Notify that media was detected
        try {
            chrome.runtime.sendMessage({ type: "mediaDetected" });
        } catch (e) {}
        
        media.addEventListener('play', () => checkPlayingState());
        media.addEventListener('pause', () => checkPlayingState());
        media.addEventListener('ended', () => checkPlayingState());
        media.addEventListener('emptied', () => {
            mediaElements.delete(media);
            checkPlayingState();
        });
        
        // Check initial state
        checkPlayingState();
    }
    
    // Find and track all existing media elements
    function findMediaElements() {
        document.querySelectorAll('video, audio').forEach(setupMediaElement);
    }
    
    // Watch for dynamically added media elements
    const observer = new MutationObserver((mutations) => {
        mutations.forEach(mutation => {
            mutation.addedNodes.forEach(node => {
                if (node.nodeName === 'VIDEO' || node.nodeName === 'AUDIO') {
                    setupMediaElement(node);
                }
                if (node.querySelectorAll) {
                    node.querySelectorAll('video, audio').forEach(setupMediaElement);
                }
            });
            
            mutation.removedNodes.forEach(node => {
                if (node.nodeName === 'VIDEO' || node.nodeName === 'AUDIO') {
                    mediaElements.delete(node);
                    checkPlayingState();
                }
            });
        });
    });
    
    // Listen for messages from background script
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        switch (message.action) {
            case "pause":
                pauseAllMedia();
                sendResponse({ success: true });
                break;
            case "play":
                playMedia();
                sendResponse({ success: true });
                break;
            case "getState":
                sendResponse({ playing: isPlaying, mediaCount: mediaElements.size });
                break;
            default:
                sendResponse({ success: false, error: "Unknown action" });
        }
        return true;
    });
    
    // Periodic check for media state (catches edge cases)
    setInterval(() => {
        findMediaElements();
        checkPlayingState();
    }, 1000);
    
    // Initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            log("DOM loaded, scanning for media...");
            findMediaElements();
            if (document.body) {
                observer.observe(document.body, { childList: true, subtree: true });
            }
        });
    } else {
        log("Document ready, scanning for media...");
        findMediaElements();
        if (document.body) {
            observer.observe(document.body, { childList: true, subtree: true });
        }
    }
    
    // Also scan after a delay (for dynamically loaded content like YouTube)
    setTimeout(() => {
        log("Delayed scan for media...");
        findMediaElements();
        checkPlayingState();
    }, 2000);
})();
