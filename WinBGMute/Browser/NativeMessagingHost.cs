using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Browser
{
    /// <summary>
    /// Handles native messaging communication with browser extensions.
    /// Uses stdin/stdout for message passing as per Chrome Native Messaging protocol.
    /// </summary>
    internal sealed class NativeMessagingHost : IDisposable
    {
        private const int MaxMessageSize = 1024 * 1024; // 1MB max
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, TabInfo> _tabs = new();
        private Task? _readTask;
        private bool _disposed;

        public event EventHandler<TabStateChangedEventArgs>? TabStateChanged;
        public event EventHandler<TabActivatedEventArgs>? TabActivated;
        public event EventHandler<WindowFocusedEventArgs>? WindowFocused;
        public event EventHandler? BrowserLostFocus;

        public void Start()
        {
            if (_readTask != null) return;

            _readTask = Task.Run(ReadMessagesAsync, _cts.Token);
            LoggingEngine.LogLine("[NativeMessaging] Host started, listening for browser messages",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        public void Stop()
        {
            _cts.Cancel();
            _readTask?.Wait(1000);
        }

        public void WaitForExit()
        {
            _readTask?.Wait();
        }

        private async Task ReadMessagesAsync()
        {
            using var stdin = Console.OpenStandardInput();
            var buffer = new byte[4];

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Read message length (4 bytes, little-endian)
                    var bytesRead = await ReadExactlyAsync(stdin, buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break; // EOF
                    }
                    if (bytesRead < buffer.Length)
                    {
                        LoggingEngine.LogLine("[NativeMessaging] Incomplete message length received, closing host",
                            category: LoggingEngine.LogCategory.MediaControl,
                            loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                        break;
                    }

                    int messageLength = BitConverter.ToInt32(buffer, 0);
                    if (messageLength <= 0 || messageLength > MaxMessageSize)
                    {
                        LoggingEngine.LogLine($"[NativeMessaging] Invalid message length {messageLength}, closing host",
                            category: LoggingEngine.LogCategory.MediaControl,
                            loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                        break;
                    }

                    // Read message content
                    var messageBuffer = new byte[messageLength];
                    bytesRead = await ReadExactlyAsync(stdin, messageBuffer, 0, messageLength, _cts.Token).ConfigureAwait(false);
                    if (bytesRead < messageLength)
                    {
                        LoggingEngine.LogLine("[NativeMessaging] Incomplete message payload received, closing host",
                            category: LoggingEngine.LogCategory.MediaControl,
                            loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                        break;
                    }

                    var json = Encoding.UTF8.GetString(messageBuffer);
                    ProcessMessage(json);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LoggingEngine.LogLine($"[NativeMessaging] Error reading message: {ex.Message}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                }
            }
        }

        private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return totalRead;
                }
                totalRead += read;
            }

            return totalRead;
        }

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                {
                    return;
                }

                var type = typeElement.GetString();

                switch (type)
                {
                    case "mediaStateChanged":
                        HandleMediaStateChanged(root);
                        break;
                    case "tabActivated":
                        HandleTabActivated(root);
                        break;
                    case "windowFocused":
                        HandleWindowFocused(root);
                        break;
                    case "browserLostFocus":
                        BrowserLostFocus?.Invoke(this, EventArgs.Empty);
                        break;
                    case "tabClosed":
                        HandleTabClosed(root);
                        break;
                    case "tabStates":
                        HandleTabStates(root);
                        break;
                }
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[NativeMessaging] Error processing message: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
        }

        private void HandleMediaStateChanged(JsonElement root)
        {
            var tabId = root.GetProperty("tabId").GetInt32();
            var playing = root.GetProperty("playing").GetBoolean();
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";

            _tabs[tabId] = new TabInfo(tabId, playing, title, url);

            LoggingEngine.LogLine($"[NativeMessaging] Tab {tabId} media state: {(playing ? "playing" : "paused")} - {title}",
                category: LoggingEngine.LogCategory.MediaControl);

            TabStateChanged?.Invoke(this, new TabStateChangedEventArgs(tabId, playing, title, url));
        }

        private void HandleTabActivated(JsonElement root)
        {
            var tabId = root.GetProperty("tabId").GetInt32();
            var windowId = root.GetProperty("windowId").GetInt32();
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

            LoggingEngine.LogLine($"[NativeMessaging] Tab activated: {tabId} in window {windowId} - {title}",
                category: LoggingEngine.LogCategory.MediaControl);

            TabActivated?.Invoke(this, new TabActivatedEventArgs(tabId, windowId, title));
        }

        private void HandleWindowFocused(JsonElement root)
        {
            var windowId = root.GetProperty("windowId").GetInt32();
            var tabId = root.GetProperty("tabId").GetInt32();
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

            LoggingEngine.LogLine($"[NativeMessaging] Window focused: {windowId}, active tab: {tabId} - {title}",
                category: LoggingEngine.LogCategory.MediaControl);

            WindowFocused?.Invoke(this, new WindowFocusedEventArgs(windowId, tabId, title));
        }

        private void HandleTabClosed(JsonElement root)
        {
            var tabId = root.GetProperty("tabId").GetInt32();
            _tabs.TryRemove(tabId, out var removed);

            LoggingEngine.LogLine($"[NativeMessaging] Tab closed: {tabId}",
                category: LoggingEngine.LogCategory.MediaControl);

            var title = removed?.Title ?? string.Empty;
            var url = removed?.Url ?? string.Empty;
            TabStateChanged?.Invoke(this, new TabStateChangedEventArgs(tabId, false, title, url));
        }

        private void HandleTabStates(JsonElement root)
        {
            if (!root.TryGetProperty("tabs", out var tabsArray))
            {
                return;
            }

            _tabs.Clear();
            foreach (var tab in tabsArray.EnumerateArray())
            {
                var tabId = tab.GetProperty("tabId").GetInt32();
                var playing = tab.GetProperty("playing").GetBoolean();
                var title = tab.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url = tab.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";

                _tabs[tabId] = new TabInfo(tabId, playing, title, url);
            }

            LoggingEngine.LogLine($"[NativeMessaging] Received {_tabs.Count} tab states",
                category: LoggingEngine.LogCategory.MediaControl);

            foreach (var tab in _tabs.Values)
            {
                TabStateChanged?.Invoke(this, new TabStateChangedEventArgs(tab.TabId, tab.IsPlaying, tab.Title, tab.Url));
            }
        }

        /// <summary>
        /// Send a command to pause a specific tab.
        /// </summary>
        public void SendPauseTab(int tabId)
        {
            SendMessage(new { action = "pauseTab", tabId = tabId });
        }

        /// <summary>
        /// Send a command to play a specific tab.
        /// </summary>
        public void SendPlayTab(int tabId)
        {
            SendMessage(new { action = "playTab", tabId = tabId });
        }

        /// <summary>
        /// Send a command to pause all tabs except the specified one.
        /// </summary>
        public void SendPauseAllExcept(int exceptTabId)
        {
            SendMessage(new { action = "pauseAllExcept", tabId = exceptTabId });
        }

        /// <summary>
        /// Request current tab states from the extension.
        /// </summary>
        public void RequestTabStates()
        {
            SendMessage(new { action = "getTabStates" });
        }

        private void SendMessage(object message)
        {
            var json = JsonSerializer.Serialize(message);
            SendRawMessage(json);
        }

        /// <summary>
        /// Send a raw JSON message to the extension via stdout.
        /// </summary>
        public void SendRawMessage(string json)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var lengthBytes = BitConverter.GetBytes(bytes.Length);

                using var stdout = Console.OpenStandardOutput();
                stdout.Write(lengthBytes, 0, 4);
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();

                LoggingEngine.LogLine($"[NativeMessaging] Sent: {json}",
                    category: LoggingEngine.LogCategory.MediaControl);
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[NativeMessaging] Error sending message: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
        }

        /// <summary>
        /// Get all known tabs with their current states.
        /// </summary>
        public IReadOnlyDictionary<int, TabInfo> GetTabs() => _tabs;

        /// <summary>
        /// Get tabs that are currently playing media.
        /// </summary>
        public IEnumerable<TabInfo> GetPlayingTabs()
        {
            foreach (var tab in _tabs.Values)
            {
                if (tab.IsPlaying)
                {
                    yield return tab;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts.Dispose();
        }
    }

    internal sealed record TabInfo(int TabId, bool IsPlaying, string Title, string Url);

    internal sealed class TabStateChangedEventArgs : EventArgs
    {
        public int TabId { get; }
        public bool IsPlaying { get; }
        public string Title { get; }
        public string Url { get; }

        public TabStateChangedEventArgs(int tabId, bool isPlaying, string title, string url)
        {
            TabId = tabId;
            IsPlaying = isPlaying;
            Title = title;
            Url = url;
        }
    }

    internal sealed class TabActivatedEventArgs : EventArgs
    {
        public int TabId { get; }
        public int WindowId { get; }
        public string Title { get; }

        public TabActivatedEventArgs(int tabId, int windowId, string title)
        {
            TabId = tabId;
            WindowId = windowId;
            Title = title;
        }
    }

    internal sealed class WindowFocusedEventArgs : EventArgs
    {
        public int WindowId { get; }
        public int TabId { get; }
        public string Title { get; }

        public WindowFocusedEventArgs(int windowId, int tabId, string title)
        {
            WindowId = windowId;
            TabId = tabId;
            Title = title;
        }
    }
}
