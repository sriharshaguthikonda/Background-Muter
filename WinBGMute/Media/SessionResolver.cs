using System;
using System.Collections.Generic;
using System.Linq;
using WinBGMuter.Abstractions;

namespace WinBGMuter.Media
{
    internal sealed class SessionResolver
    {
        private readonly IMediaController _mediaController;
        private readonly IReadOnlyDictionary<string, string> _processNameToSessionHint;

        public SessionResolver(IMediaController mediaController, IReadOnlyDictionary<string, string>? processNameToSessionHint = null)
        {
            _mediaController = mediaController;
            _processNameToSessionHint = processNameToSessionHint ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<SessionResolution> ResolveAsync(string processName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sessions = await _mediaController.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Count == 0)
            {
                return SessionResolution.NotFound;
            }

            // 1) exact map by known app identifiers / process name
            if (_processNameToSessionHint.TryGetValue(processName, out var hintedAppId))
            {
                var match = sessions.FirstOrDefault(s =>
                    string.Equals(s.AppId, hintedAppId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Key.Id, hintedAppId, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    return SessionResolution.Success(match.Key);
                }
            }

            // 2) if only one playing session exists, choose it
            var playing = sessions.Where(s => s.PlaybackState == MediaPlaybackState.Playing).ToList();
            if (playing.Count == 1)
            {
                return SessionResolution.Success(playing[0].Key);
            }

            // 3) if multiple, prefer most recently updated/playing
            if (playing.Count > 1)
            {
                var pick = playing.OrderByDescending(s => s.LastUpdated).First();
                return SessionResolution.Success(pick.Key);
            }

            return SessionResolution.Ambiguous;
        }
    }

    internal readonly struct SessionResolution
    {
        private SessionResolution(MediaSessionKey? session, SessionResolutionStatus status)
        {
            Session = session;
            Status = status;
        }

        public MediaSessionKey? Session { get; }
        public SessionResolutionStatus Status { get; }

        public static SessionResolution Success(MediaSessionKey session) => new(session, SessionResolutionStatus.Success);
        public static SessionResolution NotFound => new(null, SessionResolutionStatus.NotFound);
        public static SessionResolution Ambiguous => new(null, SessionResolutionStatus.Ambiguous);
    }

    internal enum SessionResolutionStatus
    {
        Success,
        NotFound,
        Ambiguous
    }
}
