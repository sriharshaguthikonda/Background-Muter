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

        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private bool _enabled = true;

        private Func<IEnumerable<string>>? _getNeverPauseList;

        public AppController(
            VolumeMixer volumeMixer,
            float audibilityThreshold = 0.01f,
            IReadOnlyDictionary<string, string>? processNameToSessionHint = null,
            Func<IEnumerable<string>>? getNeverPauseList = null)
        {
            _volumeMixer = volumeMixer;
            _getNeverPauseList = getNeverPauseList;
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
