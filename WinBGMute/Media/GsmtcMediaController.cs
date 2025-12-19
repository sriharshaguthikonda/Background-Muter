using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;
using WinBGMuter.Abstractions.Media;
using WinBGMuter.Logging;

namespace WinBGMuter.Media
{
    internal sealed class GsmtcMediaController : IMediaController
    {
        private readonly ILogger _logger;
        private readonly object _sync = new();
        private Task<GlobalSystemMediaTransportControlsSessionManager?>? _managerTask;

        public GsmtcMediaController(ILogger? logger = null)
        {
            _logger = logger ?? AppLogging.CreateLogger(LogCategories.MediaControl);
        }

        public IReadOnlyCollection<MediaSessionInfo> GetSessions()
        {
            var manager = EnsureManager();
            if (manager is null)
            {
                _logger.LogWarning("GSMTC manager unavailable; returning no sessions");
                return Array.Empty<MediaSessionInfo>();
            }

            try
            {
                var sessions = manager.GetSessions();
                var results = sessions.Select(CreateSessionInfo).ToArray();

                _logger.LogDebug("Enumerated {Count} GSMTC sessions", results.Length);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate GSMTC sessions");
                return Array.Empty<MediaSessionInfo>();
            }
        }

        public MediaCommandResult TryPause(MediaSessionInfo session)
        {
            var target = FindSession(session.SessionId);
            if (target is null)
            {
                _logger.LogWarning("Pause failed: session {SessionId} not found", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.NotFound, session.SessionId);
            }

            var controls = target.GetPlaybackInfo()?.Controls;
            if (controls is null || !controls.IsPauseEnabled)
            {
                _logger.LogInformation("Pause not supported for session {SessionId}", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.NotSupported, session.SessionId);
            }

            try
            {
                var result = target.TryPauseAsync().AsTask().GetAwaiter().GetResult();
                return result
                    ? MediaCommandResult.Succeeded(session.SessionId)
                    : MediaCommandResult.Failed(MediaCommandFailureReason.UnknownError, session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pause failed for session {SessionId}", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.UnknownError, session.SessionId);
            }
        }

        public MediaCommandResult TryPlay(MediaSessionInfo session)
        {
            var target = FindSession(session.SessionId);
            if (target is null)
            {
                _logger.LogWarning("Play failed: session {SessionId} not found", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.NotFound, session.SessionId);
            }

            var controls = target.GetPlaybackInfo()?.Controls;
            if (controls is null || !controls.IsPlayEnabled)
            {
                _logger.LogInformation("Play not supported for session {SessionId}", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.NotSupported, session.SessionId);
            }

            try
            {
                var result = target.TryPlayAsync().AsTask().GetAwaiter().GetResult();
                return result
                    ? MediaCommandResult.Succeeded(session.SessionId)
                    : MediaCommandResult.Failed(MediaCommandFailureReason.UnknownError, session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Play failed for session {SessionId}", session.SessionId);
                return MediaCommandResult.Failed(MediaCommandFailureReason.UnknownError, session.SessionId);
            }
        }

        private GlobalSystemMediaTransportControlsSession? FindSession(string sessionId)
        {
            var manager = EnsureManager();
            if (manager is null)
            {
                return null;
            }

            try
            {
                return manager.GetSessions().FirstOrDefault(s => BuildSessionId(s) == sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to access GSMTC sessions while searching for {SessionId}", sessionId);
                return null;
            }
        }

        private GlobalSystemMediaTransportControlsSessionManager? EnsureManager()
        {
            Task<GlobalSystemMediaTransportControlsSessionManager?>? managerTask;
            lock (_sync)
            {
                _managerTask ??= InitializeManagerAsync();
                managerTask = _managerTask;
            }

            try
            {
                return managerTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acquire GSMTC session manager");
                return null;
            }
        }

        private Task<GlobalSystemMediaTransportControlsSessionManager?> InitializeManagerAsync()
        {
            return GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync()
                .AsTask()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Failed to request GSMTC session manager");
                        return null;
                    }

                    var manager = t.Result;
                    _logger.LogInformation("GSMTC session manager initialized");
                    return manager;
                });
        }

        private MediaSessionInfo CreateSessionInfo(GlobalSystemMediaTransportControlsSession session)
        {
            var playbackInfo = session.GetPlaybackInfo();
            var playbackState = MapPlaybackState(playbackInfo?.PlaybackStatus);
            DateTime? lastUpdatedUtc = null;

            var timeline = playbackInfo?.TimelineProperties;
            if (timeline is not null)
            {
                lastUpdatedUtc = timeline.LastUpdatedTime.UtcDateTime;
            }
            var displayName = TryGetDisplayName(session);

            return new MediaSessionInfo(
                BuildSessionId(session),
                session.SourceAppUserModelId,
                displayName,
                playbackState,
                lastUpdatedUtc);
        }

        private static MediaPlaybackState MapPlaybackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
        {
            return status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
                _ => MediaPlaybackState.Unknown,
            };
        }

        private string BuildSessionId(GlobalSystemMediaTransportControlsSession session)
        {
            if (!string.IsNullOrWhiteSpace(session.SourceAppUserModelId))
            {
                return session.SourceAppUserModelId;
            }

            return $"unknown-{session.GetHashCode().ToString(CultureInfo.InvariantCulture)}";
        }

        private string? TryGetDisplayName(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var properties = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
                if (properties is null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(properties.Title))
                {
                    return properties.Title;
                }

                if (!string.IsNullOrWhiteSpace(properties.Artist))
                {
                    return properties.Artist;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read media properties for {SessionId}", BuildSessionId(session));
            }

            return null;
        }
    }
}
