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
                return MediaControlResult.NotSupported;
            }

            try
            {
                await s.TryPauseAsync();
                return MediaControlResult.Success;
            }
            catch
            {
                return MediaControlResult.Failed;
            }
        }

        public async Task<MediaControlResult> TryPlayAsync(MediaSessionKey session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var s = await GetSessionAsync(session, cancellationToken).ConfigureAwait(false);
            if (s == null)
            {
                return MediaControlResult.NotSupported;
            }

            try
            {
                await s.TryPlayAsync();
                return MediaControlResult.Success;
            }
            catch
            {
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
            var id = session.SourceAppUserModelId ?? Guid.NewGuid().ToString("N");
            return new MediaSessionKey(id, session.SourceAppUserModelId);
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
            return sessions.FirstOrDefault(s => (s.SourceAppUserModelId ?? string.Empty) == key.Id);
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
