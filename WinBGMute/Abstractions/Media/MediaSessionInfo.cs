using System;

namespace WinBGMuter.Abstractions.Media
{
    public sealed class MediaSessionInfo
    {
        public MediaSessionInfo(string sessionId, string? appId, string? displayName, MediaPlaybackState playbackState, DateTime? lastUpdatedUtc)
        {
            SessionId = sessionId;
            AppId = appId;
            DisplayName = displayName;
            PlaybackState = playbackState;
            LastUpdatedUtc = lastUpdatedUtc;
        }

        public string SessionId { get; }

        public string? AppId { get; }

        public string? DisplayName { get; }

        public MediaPlaybackState PlaybackState { get; }

        public DateTime? LastUpdatedUtc { get; }
    }
}
