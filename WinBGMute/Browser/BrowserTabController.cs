using System;
using System.Collections.Generic;
using System.Linq;

namespace WinBGMuter.Browser
{
    /// <summary>
    /// High-level controller for managing browser tab media playback.
    /// Integrates with the native messaging host to send pause/play commands to specific tabs.
    /// </summary>
    internal sealed class BrowserTabController : IDisposable
    {
        private readonly NativeMessagingHost _nativeHost;
        private int _lastActiveTabId = -1;
        private int _lastActiveWindowId = -1;
        private bool _disposed;

        public BrowserTabController()
        {
            _nativeHost = new NativeMessagingHost();
            _nativeHost.TabActivated += OnTabActivated;
            _nativeHost.WindowFocused += OnWindowFocused;
            _nativeHost.TabStateChanged += OnTabStateChanged;
        }

        /// <summary>
        /// Start listening for browser extension messages.
        /// </summary>
        public void Start()
        {
            _nativeHost.Start();
        }

        /// <summary>
        /// Stop listening for browser extension messages.
        /// </summary>
        public void Stop()
        {
            _nativeHost.Stop();
        }

        private void OnTabActivated(object? sender, TabActivatedEventArgs e)
        {
            // When user switches to a different tab in the same window,
            // pause media in the previously active tab
            if (_lastActiveWindowId == e.WindowId && _lastActiveTabId != e.TabId && _lastActiveTabId != -1)
            {
                LoggingEngine.LogLine($"[BrowserTabController] Tab switch in window {e.WindowId}: {_lastActiveTabId} -> {e.TabId}",
                    category: LoggingEngine.LogCategory.MediaControl);

                // Check if the previous tab was playing
                var tabs = _nativeHost.GetTabs();
                if (tabs.TryGetValue(_lastActiveTabId, out var prevTab) && prevTab.IsPlaying)
                {
                    LoggingEngine.LogLine($"[BrowserTabController] Pausing previous tab: {_lastActiveTabId} ({prevTab.Title})",
                        category: LoggingEngine.LogCategory.MediaControl);
                    _nativeHost.SendPauseTab(_lastActiveTabId);
                }
            }

            _lastActiveTabId = e.TabId;
            _lastActiveWindowId = e.WindowId;
        }

        private void OnWindowFocused(object? sender, WindowFocusedEventArgs e)
        {
            // When user switches to a different browser window,
            // pause media in all tabs of the previous window (we track by tab ID)
            if (_lastActiveTabId != -1 && _lastActiveTabId != e.TabId)
            {
                var tabs = _nativeHost.GetTabs();
                if (tabs.TryGetValue(_lastActiveTabId, out var prevTab) && prevTab.IsPlaying)
                {
                    LoggingEngine.LogLine($"[BrowserTabController] Window switch: pausing tab {_lastActiveTabId} ({prevTab.Title})",
                        category: LoggingEngine.LogCategory.MediaControl);
                    _nativeHost.SendPauseTab(_lastActiveTabId);
                }
            }

            _lastActiveTabId = e.TabId;
            _lastActiveWindowId = e.WindowId;
        }

        private void OnTabStateChanged(object? sender, TabStateChangedEventArgs e)
        {
            LoggingEngine.LogLine($"[BrowserTabController] Tab {e.TabId} state changed: {(e.IsPlaying ? "playing" : "paused")} - {e.Title}",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        /// <summary>
        /// Pause all browser tabs that are currently playing media.
        /// </summary>
        public void PauseAllTabs()
        {
            foreach (var tab in _nativeHost.GetPlayingTabs())
            {
                _nativeHost.SendPauseTab(tab.TabId);
            }
        }

        /// <summary>
        /// Pause all browser tabs except the specified one.
        /// </summary>
        public void PauseAllTabsExcept(int exceptTabId)
        {
            _nativeHost.SendPauseAllExcept(exceptTabId);
        }

        /// <summary>
        /// Get all tabs that are currently playing media.
        /// </summary>
        public IEnumerable<TabInfo> GetPlayingTabs()
        {
            return _nativeHost.GetPlayingTabs();
        }

        /// <summary>
        /// Check if any browser tab is currently playing media.
        /// </summary>
        public bool IsAnyTabPlaying()
        {
            return _nativeHost.GetPlayingTabs().Any();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nativeHost.Dispose();
        }
    }
}
