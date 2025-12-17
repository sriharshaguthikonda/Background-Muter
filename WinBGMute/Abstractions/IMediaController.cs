using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WinBGMuter.Abstractions
{
    public interface IMediaController
    {
        Task<IReadOnlyList<MediaSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default);

        Task<MediaControlResult> TryPauseAsync(MediaSessionKey session, CancellationToken cancellationToken = default);

        Task<MediaControlResult> TryPlayAsync(MediaSessionKey session, CancellationToken cancellationToken = default);
    }

    public sealed record MediaSessionKey(string Id, string? SourceAppUserModelId);

    public sealed class MediaSessionInfo
    {
        public MediaSessionInfo(MediaSessionKey key, string? displayName, string? appId, MediaPlaybackState playbackState, DateTimeOffset lastUpdated)
        {
            Key = key;
            DisplayName = displayName;
            AppId = appId;
            PlaybackState = playbackState;
            LastUpdated = lastUpdated;
        }

        public MediaSessionKey Key { get; }
        public string? DisplayName { get; }
        public string? AppId { get; }
        public MediaPlaybackState PlaybackState { get; }
        public DateTimeOffset LastUpdated { get; }
    }

    public enum MediaPlaybackState
    {
        Unknown = 0,
        Playing,
        Paused,
        Stopped
    }

    public enum MediaControlResult
    {
        Success,
        NotSupported,
        Failed
    }
}
