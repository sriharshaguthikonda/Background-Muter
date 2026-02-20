using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinBGMuter.Abstractions;
using Windows.Media.Control;

namespace WinBGMuter.Media
{
    internal sealed class GsmtcMediaController : IMediaController
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;

        public async Task<IReadOnlyList<MediaSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureManagerAsync().ConfigureAwait(false);

            if (_manager == null)
            {
                return Array.Empty<MediaSessionInfo>();
            }

            var sessions = _manager.GetSessions();
            var result = new List<MediaSessionInfo>(sessions.Count);

            foreach (var s in sessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = BuildKey(s);
                var info = s.GetPlaybackInfo();
                var state = MapState(info?.PlaybackStatus);
                result.Add(new MediaSessionInfo(
                    key,
                    displayName: s.SourceAppUserModelId,
                    appId: s.SourceAppUserModelId,
                    playbackState: state,
                    lastUpdated: info?.Controls?.IsPauseEnabled == true || info?.Controls?.IsPlayEnabled == true
                        ? DateTimeOffset.UtcNow
                        : DateTimeOffset.MinValue));
            }

            return result;
        }

        public async Task<MediaControlResult> TryPauseAsync(MediaSessionKey session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var s = await GetSessionAsync(session, cancellationToken).ConfigureAwait(false);
            if (s == null)
            {
                LoggingEngine.LogLine($"[GSMTC] TryPauseAsync: Session not found for {session.Id}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return MediaControlResult.NotSupported;
            }

            try
            {
                var playbackInfo = s.GetPlaybackInfo();
                if (playbackInfo?.Controls?.IsPauseEnabled != true)
                {
                    LoggingEngine.LogLine($"[GSMTC] TryPauseAsync: Pause not supported for {session.Id}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                    return MediaControlResult.NotSupported;
                }

                bool success = await s.TryPauseAsync();
                LoggingEngine.LogLine($"[GSMTC] TryPauseAsync: {session.Id} -> {(success ? "SUCCESS" : "FAILED")}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return success ? MediaControlResult.Success : MediaControlResult.Failed;
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[GSMTC] TryPauseAsync exception: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_ERROR);
                return MediaControlResult.Failed;
            }
        }

        public async Task<MediaControlResult> TryPlayAsync(MediaSessionKey session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var s = await GetSessionAsync(session, cancellationToken).ConfigureAwait(false);
            if (s == null)
            {
                LoggingEngine.LogLine($"[GSMTC] TryPlayAsync: Session not found for {session.Id}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return MediaControlResult.NotSupported;
            }

            try
            {
                var playbackInfo = s.GetPlaybackInfo();
                if (playbackInfo?.Controls?.IsPlayEnabled != true)
                {
                    LoggingEngine.LogLine($"[GSMTC] TryPlayAsync: Play not supported for {session.Id}",
                        category: LoggingEngine.LogCategory.MediaControl,
                        loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                    return MediaControlResult.NotSupported;
                }

                bool success = await s.TryPlayAsync();
                LoggingEngine.LogLine($"[GSMTC] TryPlayAsync: {session.Id} -> {(success ? "SUCCESS" : "FAILED")}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return success ? MediaControlResult.Success : MediaControlResult.Failed;
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[GSMTC] TryPlayAsync exception: {ex.Message}",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_ERROR);
                return MediaControlResult.Failed;
            }
        }

        private async Task EnsureManagerAsync()
        {
            if (_manager != null)
            {
                return;
            }

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }

        private static MediaSessionKey BuildKey(GlobalSystemMediaTransportControlsSession session)
        {
            var appId = session.SourceAppUserModelId;
            if (!string.IsNullOrWhiteSpace(appId))
            {
                return new MediaSessionKey(appId, appId);
            }

            // Fallback for sessions without AUMID: use hash to enable lookup within this process.
            var fallbackId = session.GetHashCode().ToString("X");
            return new MediaSessionKey(fallbackId, null);
        }

        private async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync(MediaSessionKey key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureManagerAsync().ConfigureAwait(false);

            if (_manager == null)
            {
                return null;
            }

            var sessions = _manager.GetSessions();
            if (!string.IsNullOrWhiteSpace(key.SourceAppUserModelId))
            {
                return sessions.FirstOrDefault(s => (s.SourceAppUserModelId ?? string.Empty) == key.SourceAppUserModelId);
            }

            var keyId = key.Id ?? string.Empty;
            var aumidMatch = sessions.FirstOrDefault(s => (s.SourceAppUserModelId ?? string.Empty) == keyId);
            if (aumidMatch != null)
            {
                return aumidMatch;
            }

            return sessions.FirstOrDefault(s =>
                string.IsNullOrWhiteSpace(s.SourceAppUserModelId) &&
                s.GetHashCode().ToString("X") == keyId);
        }

        private static MediaPlaybackState MapState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
        {
            return status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
                _ => MediaPlaybackState.Unknown
            };
        }
    }
}
