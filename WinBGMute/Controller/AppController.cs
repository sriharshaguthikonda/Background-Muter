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
        private readonly CoreAudioSessionScanner _audioScanner;
        private readonly AudibilityDetector _audibilityDetector;
        private readonly GsmtcMediaController _mediaController;
        private readonly SessionResolver _sessionResolver;
        private readonly PauseAction _pauseAction;
        private readonly MuteAction _muteAction;
        private readonly ActionPolicyEngine _policyEngine;
        private readonly PlaybackStateStore _stateStore;

        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private bool _enabled = true;

        public AppController(
            VolumeMixer volumeMixer,
            PolicyMode policyMode = PolicyMode.PauseThenMuteFallback,
            float audibilityThreshold = 0.01f,
            IReadOnlyDictionary<string, string>? processNameToSessionHint = null)
        {
            _foregroundTracker = new WinEventForegroundTracker();
            _audioScanner = new CoreAudioSessionScanner();
            _audibilityDetector = new AudibilityDetector(audibilityThreshold);
            _mediaController = new GsmtcMediaController();
            _sessionResolver = new SessionResolver(_mediaController, processNameToSessionHint);
            _pauseAction = new PauseAction(_mediaController, _sessionResolver);
            _muteAction = new MuteAction(volumeMixer);
            _policyEngine = new ActionPolicyEngine(policyMode);
            _stateStore = new PlaybackStateStore();

            _foregroundTracker.ForegroundChanged += OnForegroundChanged;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public void Start()
        {
            _foregroundTracker.Start();
            LoggingEngine.LogLine("[AppController] Started", category: LoggingEngine.LogCategory.General);
        }

        public void Stop()
        {
            _foregroundTracker.Stop();
            LoggingEngine.LogLine("[AppController] Stopped", category: LoggingEngine.LogCategory.General);
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
            var foregroundPid = e.CurrentPid;
            string foregroundProcessName = "<unknown>";

            try
            {
                foregroundProcessName = Process.GetProcessById(foregroundPid).ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            LoggingEngine.LogLine($"[AppController] Foreground changed to {foregroundProcessName} (PID {foregroundPid})",
                category: LoggingEngine.LogCategory.Foreground);

            // 1) Get audio snapshot
            var snapshot = await _audioScanner.GetSnapshotAsync().ConfigureAwait(false);
            var audiblePids = _audibilityDetector.GetAudiblePids(snapshot);

            // 2) Evaluate policy
            var decision = _policyEngine.Evaluate(foregroundPid, audiblePids, foregroundProcessName);

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
                else if (pausedState.Method == "mute")
                {
                    _muteAction.TryUnmute(foregroundPid);
                    _stateStore.Clear(foregroundPid);
                }
            }

            // 4) Pause/mute background apps
            foreach (var pid in decision.ToPause)
            {
                string processName = GetProcessName(pid);

                var pauseResult = await _pauseAction.TryPauseAsync(processName).ConfigureAwait(false);

                if (pauseResult == PauseResult.Success)
                {
                    var sessions = await _mediaController.ListSessionsAsync().ConfigureAwait(false);
                    var sessionKey = sessions.FirstOrDefault(s =>
                        string.Equals(s.AppId, processName, StringComparison.OrdinalIgnoreCase))?.Key.Id ?? processName;
                    _stateStore.MarkPaused(pid, "pause", sessionKey);
                }
                else if (_policyEngine.Mode == PolicyMode.PauseThenMuteFallback)
                {
                    // Fallback to mute
                    if (_muteAction.TryMute(pid) == MuteResult.Success)
                    {
                        _stateStore.MarkPaused(pid, "mute", string.Empty);
                    }
                }
            }

            foreach (var pid in decision.ToMute)
            {
                if (_muteAction.TryMute(pid) == MuteResult.Success)
                {
                    _stateStore.MarkPaused(pid, "mute", string.Empty);
                }
            }

            // 5) Cleanup exited processes
            _stateStore.CleanupExitedProcesses();
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

        public void Dispose()
        {
            _foregroundTracker.ForegroundChanged -= OnForegroundChanged;
            _foregroundTracker.Dispose();
            _processingLock.Dispose();
        }
    }
}
