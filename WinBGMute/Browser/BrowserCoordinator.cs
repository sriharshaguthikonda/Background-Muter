using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Browser
{
    /// <summary>
    /// Coordinates browser media control across multiple extension instances (profiles).
    /// Runs as a localhost TCP server in the main app.
    /// </summary>
    internal sealed class BrowserCoordinator : IDisposable
    {
        private const int CoordinatorPort = 32145;
        private static readonly TimeSpan RecentPlaybackWindow = TimeSpan.FromSeconds(3);
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, ConnectedExtension> _extensions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _extensionTabStates = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, int>> _extensionTabWindows = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, DateTime>> _extensionWindowLastPlaying = new();
        private readonly ConcurrentDictionary<string, int> _extensionFocusedWindow = new();
        private Task? _serverTask;
        private TcpListener? _listener;
        private bool _disposed;

        public event EventHandler<BrowserFocusChangedEventArgs>? BrowserFocusChanged;
        public bool IsAnyTabPlaying => HasAnyPlayingTab();

        /// <summary>
        /// Start the localhost TCP server to accept extension connections.
        /// </summary>
        public void Start()
        {
            if (_serverTask != null) return;
            _serverTask = Task.Run(RunServerAsync);
            LoggingEngine.LogLine($"[BrowserCoordinator] Started TCP server on 127.0.0.1:{CoordinatorPort}",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener?.Stop();
            _serverTask?.Wait(2000);
        }

        private async Task RunServerAsync()
        {
            _listener = new TcpListener(IPAddress.Loopback, CoordinatorPort);
            _listener.Start();

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    
                    var extensionId = Guid.NewGuid().ToString();
                    var extension = new ConnectedExtension(extensionId, client, this);
                    _extensions.TryAdd(extensionId, extension);
                    _extensionTabStates.TryAdd(extensionId, new ConcurrentDictionary<int, bool>());
                    _extensionTabWindows.TryAdd(extensionId, new ConcurrentDictionary<int, int>());
                    _extensionWindowLastPlaying.TryAdd(extensionId, new ConcurrentDictionary<int, DateTime>());
                    
                    LoggingEngine.LogLine($"[BrowserCoordinator] Extension connected: {extensionId}",
                        category: LoggingEngine.LogCategory.MediaControl);
                    
                    _ = extension.StartAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LoggingEngine.LogLine($"[BrowserCoordinator] Server error: {ex.Message}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                    try
                    {
                        await Task.Delay(500, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Called when the main app detects a foreground window change to a browser.
        /// </summary>
        public void OnBrowserWindowFocused(string windowTitle, IntPtr windowHandle)
        {
            LoggingEngine.LogLine($"[BrowserCoordinator] Browser window focused: {windowTitle}",
                category: LoggingEngine.LogCategory.MediaControl);

            // Tell all extensions about the focus change
            // Each extension will determine if it owns this window
            var message = JsonSerializer.Serialize(new
            {
                action = "browserWindowFocused",
                windowTitle,
                windowHandle = windowHandle.ToInt64()
            });

            BroadcastToExtensions(message);
        }

        /// <summary>
        /// Pause all media across all connected extensions.
        /// </summary>
        public void PauseAllExtensions()
        {
            var message = JsonSerializer.Serialize(new { action = "pauseAll" });
            BroadcastToExtensions(message);
        }

        /// <summary>
        /// Tell extensions to pause all except the one that owns the given window.
        /// </summary>
        public void PauseAllExceptFocused(string focusedWindowTitle)
        {
            var message = JsonSerializer.Serialize(new
            {
                action = "pauseAllExceptFocused",
                focusedWindowTitle
            });
            BroadcastToExtensions(message);
        }

        private void BroadcastToExtensions(string message)
        {
            foreach (var ext in _extensions.Values)
            {
                try
                {
                    ext.SendMessage(message);
                }
                catch (Exception ex)
                {
                    LoggingEngine.LogLine($"[BrowserCoordinator] Failed to send to extension: {ex.Message}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                }
            }
        }

        internal void OnExtensionDisconnected(string extensionId)
        {
            _extensions.TryRemove(extensionId, out _);
            _extensionTabStates.TryRemove(extensionId, out _);
            _extensionTabWindows.TryRemove(extensionId, out _);
            _extensionWindowLastPlaying.TryRemove(extensionId, out _);
            _extensionFocusedWindow.TryRemove(extensionId, out _);
            LoggingEngine.LogLine($"[BrowserCoordinator] Extension disconnected: {extensionId}",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        internal void OnExtensionMessage(string extensionId, string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl))
                {
                    var type = typeEl.GetString();
                    
                    if (type == "browserLostFocus")
                    {
                        LoggingEngine.LogLine($"[BrowserCoordinator] Extension {extensionId} lost focus",
                            category: LoggingEngine.LogCategory.MediaControl);
                    }
                    else if (type == "mediaStateChanged")
                    {
                        if (TryGetInt(root, out var tabId, "tabId", "TabId") &&
                            TryGetBool(root, out var playing, "playing", "IsPlaying"))
                        {
                            UpdateExtensionTabState(extensionId, tabId, playing);

                            if (TryGetInt(root, out var windowId, "windowId", "WindowId"))
                            {
                                UpdateExtensionTabWindow(extensionId, tabId, windowId);
                                if (playing)
                                {
                                    MarkWindowPlaying(extensionId, windowId);
                                    if (IsExtensionFocusedWindow(extensionId, windowId))
                                    {
                                        PauseAllExceptExtension(extensionId);
                                    }
                                }
                            }
                        }
                    }
                    else if (type == "tabStates")
                    {
                        if (root.TryGetProperty("tabs", out var tabsElement) &&
                            tabsElement.ValueKind == JsonValueKind.Array)
                        {
                            var updated = new ConcurrentDictionary<int, bool>();
                            foreach (var tab in tabsElement.EnumerateArray())
                            {
                                if (TryGetInt(tab, out var tabId, "tabId", "TabId") &&
                                    TryGetBool(tab, out var playing, "playing", "IsPlaying"))
                                {
                                    updated[tabId] = playing;
                                    if (TryGetInt(tab, out var windowId, "windowId", "WindowId"))
                                    {
                                        UpdateExtensionTabWindow(extensionId, tabId, windowId);
                                        if (playing)
                                        {
                                            MarkWindowPlaying(extensionId, windowId);
                                        }
                                    }
                                }
                            }

                            _extensionTabStates.AddOrUpdate(extensionId, updated, (_, __) => updated);
                        }
                    }
                    else if (type == "tabActivated")
                    {
                        if (TryGetInt(root, out var tabId, "tabId", "TabId") &&
                            TryGetInt(root, out var windowId, "windowId", "WindowId"))
                        {
                            UpdateExtensionTabWindow(extensionId, tabId, windowId);
                        }
                    }
                    else if (type == "tabClosed")
                    {
                        if (TryGetInt(root, out var tabId, "tabId", "TabId"))
                        {
                            if (_extensionTabStates.TryGetValue(extensionId, out var tabs))
                            {
                                tabs.TryRemove(tabId, out _);
                            }
                            if (_extensionTabWindows.TryGetValue(extensionId, out var windows))
                            {
                                windows.TryRemove(tabId, out _);
                            }
                        }
                    }
                    else if (type == "windowFocused")
                    {
                        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : "";
                        LoggingEngine.LogLine($"[BrowserCoordinator] Extension {extensionId} focused: {title}",
                            category: LoggingEngine.LogCategory.MediaControl);

                        if (TryGetInt(root, out var focusedTabId, "tabId", "TabId") &&
                            TryGetInt(root, out var focusedWindowId, "windowId", "WindowId"))
                        {
                            UpdateExtensionTabWindow(extensionId, focusedTabId, focusedWindowId);
                            _extensionFocusedWindow[extensionId] = focusedWindowId;

                            if (!ShouldPauseOnWindowFocus(extensionId, focusedWindowId, out var usedGrace))
                            {
                                LoggingEngine.LogLine($"[BrowserCoordinator] Focused window {focusedWindowId} has no playing media; skipping pause",
                                    category: LoggingEngine.LogCategory.MediaControl);
                                return;
                            }

                            if (usedGrace)
                            {
                                LoggingEngine.LogLine($"[BrowserCoordinator] Focused window {focusedWindowId} was recently playing; pausing other profiles",
                                    category: LoggingEngine.LogCategory.MediaControl);
                            }
                        }

                        // This profile gained focus and is playing, pause all others.
                        PauseAllExceptExtension(extensionId);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[BrowserCoordinator] Error parsing message: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
        }

        private void PauseAllExceptExtension(string exceptExtensionId)
        {
            var message = JsonSerializer.Serialize(new { action = "pauseAll" });
            foreach (var kvp in _extensions)
            {
                if (kvp.Key != exceptExtensionId)
                {
                    try
                    {
                        kvp.Value.SendMessage(message);
                    }
                    catch { }
                }
            }
        }

        private void UpdateExtensionTabState(string extensionId, int tabId, bool isPlaying)
        {
            var tabs = _extensionTabStates.GetOrAdd(extensionId, _ => new ConcurrentDictionary<int, bool>());
            tabs[tabId] = isPlaying;
        }

        private void UpdateExtensionTabWindow(string extensionId, int tabId, int windowId)
        {
            var tabs = _extensionTabWindows.GetOrAdd(extensionId, _ => new ConcurrentDictionary<int, int>());
            tabs[tabId] = windowId;
        }

        private void MarkWindowPlaying(string extensionId, int windowId)
        {
            if (windowId <= 0)
            {
                return;
            }

            var windows = _extensionWindowLastPlaying.GetOrAdd(extensionId, _ => new ConcurrentDictionary<int, DateTime>());
            windows[windowId] = DateTime.UtcNow;
        }

        private bool ShouldPauseOnWindowFocus(string extensionId, int windowId, out bool usedGrace)
        {
            if (IsWindowPlaying(extensionId, windowId))
            {
                usedGrace = false;
                return true;
            }

            if (_extensionWindowLastPlaying.TryGetValue(extensionId, out var windows) &&
                windows.TryGetValue(windowId, out var lastPlayingUtc))
            {
                if (DateTime.UtcNow - lastPlayingUtc <= RecentPlaybackWindow)
                {
                    usedGrace = true;
                    return true;
                }
            }

            usedGrace = false;
            return false;
        }

        private bool IsExtensionFocusedWindow(string extensionId, int windowId)
        {
            return _extensionFocusedWindow.TryGetValue(extensionId, out var focusedWindowId) &&
                   focusedWindowId == windowId;
        }

        private bool IsWindowPlaying(string extensionId, int windowId)
        {
            if (!_extensionTabStates.TryGetValue(extensionId, out var tabStates) ||
                !_extensionTabWindows.TryGetValue(extensionId, out var tabWindows))
            {
                return false;
            }

            foreach (var kvp in tabStates)
            {
                if (kvp.Value && tabWindows.TryGetValue(kvp.Key, out var tabWindowId) && tabWindowId == windowId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyPlayingTab()
        {
            foreach (var extension in _extensionTabStates.Values)
            {
                foreach (var isPlaying in extension.Values)
                {
                    if (isPlaying)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetInt(JsonElement element, out int value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var el) && el.TryGetInt32(out value))
                {
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static bool TryGetBool(JsonElement element, out bool value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var el) &&
                    (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
                {
                    value = el.GetBoolean();
                    return true;
                }
            }

            value = false;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            
            foreach (var ext in _extensions.Values)
            {
                ext.Dispose();
            }
            _extensions.Clear();
        }

        private sealed class ConnectedExtension : IDisposable
        {
            private readonly string _id;
            private readonly TcpClient _client;
            private readonly BrowserCoordinator _coordinator;
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly SemaphoreSlim _writeLock = new(1, 1);

            public ConnectedExtension(string id, TcpClient client, BrowserCoordinator coordinator)
            {
                _id = id;
                _client = client;
                _coordinator = coordinator;
                var stream = client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            }

            public async Task StartAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested && _client.Connected)
                    {
                        var line = await _reader.ReadLineAsync();
                        if (line == null) break;
                        
                        _coordinator.OnExtensionMessage(_id, line);
                    }
                }
                catch (Exception ex)
                {
                    LoggingEngine.LogLine($"[BrowserCoordinator] Extension read error: {ex.Message}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                }
                finally
                {
                    _coordinator.OnExtensionDisconnected(_id);
                    Dispose();
                }
            }

            public void SendMessage(string message)
            {
                _writeLock.Wait();
                try
                {
                    if (_client.Connected)
                    {
                        _writer.WriteLine(message);
                    }
                }
                finally
                {
                    _writeLock.Release();
                }
            }

            public void Dispose()
            {
                _reader.Dispose();
                _writer.Dispose();
                _client.Close();
                _writeLock.Dispose();
            }
        }
    }

    internal sealed class BrowserFocusChangedEventArgs : EventArgs
    {
        public string ExtensionId { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        public bool HasFocus { get; init; }
    }
}
