using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Browser
{
    /// <summary>
    /// Coordinates browser media control across multiple extension instances (profiles).
    /// Runs as a named pipe server in the main app.
    /// </summary>
    internal sealed class BrowserCoordinator : IDisposable
    {
        private const string PipeName = "BackgroundMuter_BrowserCoordinator";
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, ConnectedExtension> _extensions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _extensionTabStates = new();
        private Task? _serverTask;
        private bool _disposed;

        public event EventHandler<BrowserFocusChangedEventArgs>? BrowserFocusChanged;
        public bool IsAnyTabPlaying => HasAnyPlayingTab();

        /// <summary>
        /// Start the named pipe server to accept extension connections.
        /// </summary>
        public void Start()
        {
            if (_serverTask != null) return;
            _serverTask = Task.Run(RunServerAsync);
            LoggingEngine.LogLine("[BrowserCoordinator] Started pipe server",
                category: LoggingEngine.LogCategory.MediaControl);
        }

        public void Stop()
        {
            _cts.Cancel();
            _serverTask?.Wait(2000);
        }

        private async Task RunServerAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    pipe.SetAccessControl(CreatePipeSecurity());

                    await pipe.WaitForConnectionAsync(_cts.Token);
                    
                    var extensionId = Guid.NewGuid().ToString();
                    var extension = new ConnectedExtension(extensionId, pipe, this);
                    _extensions.TryAdd(extensionId, extension);
                    _extensionTabStates.TryAdd(extensionId, new ConcurrentDictionary<int, bool>());
                    
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
                }
            }
        }

        private static PipeSecurity CreatePipeSecurity()
        {
            var pipeSecurity = new PipeSecurity();
            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var currentUser = WindowsIdentity.GetCurrent().User;
            var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(authenticatedUsers, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            if (currentUser != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
            }
            pipeSecurity.AddAccessRule(new PipeAccessRule(localSystem, PipeAccessRights.FullControl, AccessControlType.Allow));

            return pipeSecurity;
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
                                }
                            }

                            _extensionTabStates.AddOrUpdate(extensionId, updated, (_, __) => updated);
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
                        }
                    }
                    else if (type == "windowFocused")
                    {
                        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : "";
                        LoggingEngine.LogLine($"[BrowserCoordinator] Extension {extensionId} focused: {title}",
                            category: LoggingEngine.LogCategory.MediaControl);
                        
                        // This profile gained focus, pause all others
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
            private readonly NamedPipeServerStream _pipe;
            private readonly BrowserCoordinator _coordinator;
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly SemaphoreSlim _writeLock = new(1, 1);

            public ConnectedExtension(string id, NamedPipeServerStream pipe, BrowserCoordinator coordinator)
            {
                _id = id;
                _pipe = pipe;
                _coordinator = coordinator;
                _reader = new StreamReader(pipe, Encoding.UTF8);
                _writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
            }

            public async Task StartAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested && _pipe.IsConnected)
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
                    if (_pipe.IsConnected)
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
                _pipe.Dispose();
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
