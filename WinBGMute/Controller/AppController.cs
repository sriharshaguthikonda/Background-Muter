using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinBGMuter.Abstractions;
using WinBGMuter.Actions;
using WinBGMuter.Audio;
using WinBGMuter.Foreground;
using WinBGMuter.Media;
using WinBGMuter.Policy;
using WinBGMuter.State;

namespace WinBGMuter.Controller
{
    internal sealed class AppController : IDisposable
    {
        private readonly WinEventForegroundTracker _foregroundTracker;
        private readonly VolumeMixer _volumeMixer;
        private readonly GsmtcMediaController _mediaController;
        private readonly SessionResolver _sessionResolver;
        private readonly PauseAction _pauseAction;
        private readonly ActionPolicyEngine _policyEngine;
        private readonly PlaybackStateStore _stateStore;
        private readonly Func<bool>? _isExternalMediaPlaying;

        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private bool _enabled = true;
        private bool _autoPlaySpotify = false;
        private string _autoPlayAppName = "Spotify";
        private System.Timers.Timer? _autoPlayMonitorTimer;
        private bool _autoPlayAppPausedByUs = false;
        private int _autoPlayTickInProgress = 0;

        private Func<IEnumerable<string>>? _getNeverPauseList;
        private int _pauseCooldownMs = 7000;
        private long _cooldownDeadlineMs = 0;

        // Per-window tracking for same-process window switching
        private IntPtr _previousWindowHandle = IntPtr.Zero;
        private int _previousWindowPid = -1;
        private string _previousWindowTitle = string.Empty;

        public AppController(
            VolumeMixer volumeMixer,
            float audibilityThreshold = 0.01f,
            IReadOnlyDictionary<string, string>? processNameToSessionHint = null,
            Func<IEnumerable<string>>? getNeverPauseList = null,
            bool autoPlaySpotify = false,
            string autoPlayAppName = "Spotify",
            int pauseCooldownMs = 7000,
            Func<bool>? isExternalMediaPlaying = null)
        {
            _volumeMixer = volumeMixer;
            _getNeverPauseList = getNeverPauseList;
            _autoPlaySpotify = autoPlaySpotify;
            _autoPlayAppName = autoPlayAppName;
            _pauseCooldownMs = pauseCooldownMs;
            _isExternalMediaPlaying = isExternalMediaPlaying;
            _foregroundTracker = new WinEventForegroundTracker();
            _mediaController = new GsmtcMediaController();
            _sessionResolver = new SessionResolver(_mediaController, processNameToSessionHint);
            _pauseAction = new PauseAction(_mediaController, _sessionResolver);
            _policyEngine = new ActionPolicyEngine();
            _stateStore = new PlaybackStateStore();

            _foregroundTracker.ForegroundChanged += OnForegroundChanged;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public bool AutoPlaySpotify
        {
            get => _autoPlaySpotify;
            set
            {
                _autoPlaySpotify = value;
                if (value)
                {
                    StartAutoPlayMonitor();
                }
                else
                {
                    StopAutoPlayMonitor();
                }
            }
        }

        public string AutoPlayAppName
        {
            get => _autoPlayAppName;
            set => _autoPlayAppName = value;
        }

        public int PauseCooldownMs
        {
            get => _pauseCooldownMs;
            set => _pauseCooldownMs = Math.Max(0, value);
        }

        public void Start()
        {
            _foregroundTracker.Start();
            if (_autoPlaySpotify)
            {
                StartAutoPlayMonitor();
            }
            LoggingEngine.LogLine("[AppController] Started", category: LoggingEngine.LogCategory.General);
        }

        public void Stop()
        {
            _foregroundTracker.Stop();
            StopAutoPlayMonitor();
            LoggingEngine.LogLine("[AppController] Stopped", category: LoggingEngine.LogCategory.General);
        }

        private void StartAutoPlayMonitor()
        {
            if (_autoPlayMonitorTimer != null)
            {
                return;
            }

            _autoPlayMonitorTimer = new System.Timers.Timer(1000); // Check every 1 second
            _autoPlayMonitorTimer.Elapsed += async (s, e) => await OnAutoPlayMonitorTickAsync();
            _autoPlayMonitorTimer.AutoReset = true;
            _autoPlayMonitorTimer.Start();
            LoggingEngine.LogLine($"[AutoPlay] Monitor started for {_autoPlayAppName}", category: LoggingEngine.LogCategory.MediaControl);
        }

        private void StopAutoPlayMonitor()
        {
            if (_autoPlayMonitorTimer == null)
            {
                return;
            }

            _autoPlayMonitorTimer.Stop();
            _autoPlayMonitorTimer.Dispose();
            _autoPlayMonitorTimer = null;
            _autoPlayAppPausedByUs = false;
            LoggingEngine.LogLine("[AutoPlay] Monitor stopped", category: LoggingEngine.LogCategory.MediaControl);
        }

        private async Task OnAutoPlayMonitorTickAsync()
        {
            if (!_autoPlaySpotify || string.IsNullOrWhiteSpace(_autoPlayAppName))
            {
                return;
            }

            if (Interlocked.Exchange(ref _autoPlayTickInProgress, 1) == 1)
            {
                return;
            }

            try
            {
                var sessions = await _mediaController.ListSessionsAsync().ConfigureAwait(false);

                var targetSession = sessions.FirstOrDefault(s => IsTargetAppSession(s));
                if (targetSession == null)
                {
                    return;
                }

                // Check if any other app is currently playing
                var otherPlaying = sessions.Any(s =>
                    s.PlaybackState == MediaPlaybackState.Playing &&
                    !IsTargetAppSession(s));
                var externalPlaying = _isExternalMediaPlaying?.Invoke() ?? false;
                if (externalPlaying)
                {
                    otherPlaying = true;
                }

                if (otherPlaying)
                {
                    ResetCooldownWindow();
                }

                if (otherPlaying)
                {
                    // Another app is playing - pause target app if it's playing
                    if (targetSession.PlaybackState == MediaPlaybackState.Playing)
                    {
                        var result = await _mediaController.TryPauseAsync(targetSession.Key).ConfigureAwait(false);
                        if (result == MediaControlResult.Success)
                        {
                            _autoPlayAppPausedByUs = true;
                            LoggingEngine.LogLine($"[AutoPlay] Paused {_autoPlayAppName} (other app started playing)",
                                category: LoggingEngine.LogCategory.MediaControl);
                        }
                    }
                }
                else
                {
                    // Wait until the cooldown window has been quiet
                    await WaitForCooldownAsync().ConfigureAwait(false);

                    // No other app is playing - resume target app if it's not playing
                    if (targetSession.PlaybackState != MediaPlaybackState.Playing)
                    {
                        var result = await _mediaController.TryPlayAsync(targetSession.Key).ConfigureAwait(false);
                        if (result == MediaControlResult.Success)
                        {
                            _autoPlayAppPausedByUs = false;
                            LoggingEngine.LogLine($"[AutoPlay] Resumed {_autoPlayAppName} (no other app playing)",
                                category: LoggingEngine.LogCategory.MediaControl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[AutoPlay] Monitor error: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
            }
            finally
            {
                Interlocked.Exchange(ref _autoPlayTickInProgress, 0);
            }
        }

        private async void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
        {
            if (!_enabled)
            {
                return;
            }

            // Avoid blocking the WinEvent callback thread
            _ = Task.Run(async () =>
            {
                if (!await _processingLock.WaitAsync(0))
                {
                    // Skip if already processing
                    return;
                }

                try
                {
                    await ProcessForegroundChangeAsync(e).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggingEngine.LogLine($"[AppController] Error processing foreground change: {ex.Message}",
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING,
                        category: LoggingEngine.LogCategory.General);
                }
                finally
                {
                    _processingLock.Release();
                }
            });
        }

        private async Task ProcessForegroundChangeAsync(ForegroundChangedEventArgs e)
        {
            ResetCooldownWindow();
            await WaitForCooldownAsync().ConfigureAwait(false);

            var foregroundPid = e.CurrentPid;
            var foregroundHwnd = e.Hwnd;
            var foregroundTitle = string.IsNullOrWhiteSpace(e.WindowTitle) ? "<no title>" : e.WindowTitle.Trim();
            string foregroundProcessName = "<unknown>";

            try
            {
                foregroundProcessName = Process.GetProcessById(foregroundPid).ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            LoggingEngine.LogLine($"[AppController] Foreground changed to {foregroundProcessName} (PID {foregroundPid}) Title=\"{foregroundTitle}\"",
                category: LoggingEngine.LogCategory.Foreground);

            // Per-window pause: If switching between different windows of the same process,
            // send pause to the previous window (but NOT for browsers - they use extension)
            if (_previousWindowHandle != IntPtr.Zero && 
                _previousWindowHandle != foregroundHwnd &&
                _previousWindowPid == foregroundPid &&
                !IsBrowserProcess(foregroundProcessName))
            {
                LoggingEngine.LogLine($"[AppController] Same-process window switch detected, pausing previous window: {_previousWindowTitle}",
                    category: LoggingEngine.LogCategory.MediaControl);
                
                Win32MediaCommandController.PostPause(_previousWindowHandle);
            }
            else if (IsBrowserProcess(foregroundProcessName) && _previousWindowHandle != foregroundHwnd)
            {
                LoggingEngine.LogLine($"[AppController] Browser window switch - skipping Win32 pause (extension handles this)",
                    category: LoggingEngine.LogCategory.MediaControl);
            }

            // Update previous window tracking
            _previousWindowHandle = foregroundHwnd;
            _previousWindowPid = foregroundPid;
            _previousWindowTitle = foregroundTitle;

            // 1) Get audio PIDs from VolumeMixer (already works for existing mute logic)
            int[] audioPids = _volumeMixer.GetPIDs();
            var audiblePids = new List<int>(audioPids);
            
            LoggingEngine.LogLine($"[AppController] Found {audiblePids.Count} audio PIDs: {string.Join(", ", audiblePids)}",
                category: LoggingEngine.LogCategory.Policy);

            // 2) Evaluate policy
            var decision = _policyEngine.Evaluate(foregroundPid, audiblePids, foregroundProcessName);
            
            LoggingEngine.LogLine($"[AppController] Policy decision: ToPause={decision.ToPause.Count}",
                category: LoggingEngine.LogCategory.Policy);

            // 3) Resume foreground if we paused it
            if (_stateStore.TryGetPaused(foregroundPid, out var pausedState))
            {
                LoggingEngine.LogLine($"[AppController] Resuming {foregroundProcessName} (was paused by us)",
                    category: LoggingEngine.LogCategory.Policy);

                var sessionKey = new MediaSessionKey(pausedState.SessionKey, null);
                var resumeResult = await _pauseAction.TryResumeAsync(sessionKey).ConfigureAwait(false);

                if (resumeResult == PauseResult.Success)
                {
                    _stateStore.Clear(foregroundPid);
                }
            }

            // 4) Pause/mute background apps
            var neverPauseList = _getNeverPauseList?.Invoke()?.ToHashSet(StringComparer.OrdinalIgnoreCase) 
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pid in decision.ToPause)
            {
                // Skip PID 0 (system idle process)
                if (pid == 0)
                {
                    continue;
                }

                string processName = GetProcessName(pid);

                // Skip if this is the same app as foreground (handles multi-process apps like browsers)
                if (string.Equals(processName, foregroundProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingEngine.LogLine($"[AppController] Skipping {processName} PID {pid} (same app as foreground)",
                        category: LoggingEngine.LogCategory.Policy);
                    continue;
                }

                // Skip browsers - they are controlled by the browser extension, not GSMTC
                if (IsBrowserProcess(processName))
                {
                    LoggingEngine.LogLine($"[AppController] Skipping {processName} PID {pid} (browser - controlled by extension)",
                        category: LoggingEngine.LogCategory.Policy);
                    continue;
                }

                // Skip if in never-pause list
                if (neverPauseList.Contains(processName))
                {
                    LoggingEngine.LogLine($"[AppController] Skipping {processName} (in never-pause list)",
                        category: LoggingEngine.LogCategory.Policy);
                    continue;
                }

                var pauseActionResult = await _pauseAction.TryPauseAsync(processName).ConfigureAwait(false);

                if (pauseActionResult.Result == PauseResult.Success && pauseActionResult.SessionKey != null)
                {
                    _stateStore.MarkPaused(pid, "pause", pauseActionResult.SessionKey.Id);
                    LoggingEngine.LogLine($"[AppController] Paused {processName} (session: {pauseActionResult.SessionKey.Id})",
                        category: LoggingEngine.LogCategory.Policy);
                }
                else
                {
                    LoggingEngine.LogLine($"[AppController] Pause not supported for {processName}",
                        category: LoggingEngine.LogCategory.Policy,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                }
            }

            // 5) Cleanup exited processes
            _stateStore.CleanupExitedProcesses();
        }

        private void ResetCooldownWindow()
        {
            var newDeadline = DateTimeOffset.UtcNow.AddMilliseconds(_pauseCooldownMs).ToUnixTimeMilliseconds();
            Interlocked.Exchange(ref _cooldownDeadlineMs, newDeadline);
        }

        private async Task WaitForCooldownAsync()
        {
            // Wait until deadline has passed with no newer activity.
            // If another activity arrives, its reset pushes the deadline out.
            while (true)
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var remaining = Interlocked.Read(ref _cooldownDeadlineMs) - nowMs;

                if (remaining <= 0)
                {
                    break;
                }

                // Sleep the smaller of remaining or the cooldown to stay responsive to resets.
                var delay = (int)Math.Min(remaining, _pauseCooldownMs);
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        private bool IsTargetAppSession(MediaSessionInfo session)
        {
            var appId = session.AppId?.ToLowerInvariant() ?? string.Empty;
            var displayName = session.DisplayName?.ToLowerInvariant() ?? string.Empty;
            var targetLower = _autoPlayAppName.ToLowerInvariant();
            return appId.Contains(targetLower) || displayName.Contains(targetLower);
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                return Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                return "<unknown>";
            }
        }

        // Browsers are controlled by the browser extension, not GSMTC
        private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "msedge",
            "chrome",
            "firefox",
            "brave",
            "opera",
            "vivaldi"
        };

        private static bool IsBrowserProcess(string processName)
        {
            return BrowserProcessNames.Contains(processName);
        }

        public void Dispose()
        {
            StopAutoPlayMonitor();
            _foregroundTracker.ForegroundChanged -= OnForegroundChanged;
            _foregroundTracker.Dispose();
            _processingLock.Dispose();
        }
    }
}
