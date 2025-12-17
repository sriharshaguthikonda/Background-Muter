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
            
            LoggingEngine.LogLine($"[SessionResolver] Resolving for process '{processName}', found {sessions.Count} sessions",
                category: LoggingEngine.LogCategory.MediaControl);

            foreach (var s in sessions)
            {
                LoggingEngine.LogLine($"[SessionResolver] Session: {s.AppId} ({s.Key.Id}), State: {s.PlaybackState}",
                    category: LoggingEngine.LogCategory.MediaControl);
            }

            if (sessions.Count == 0)
            {
                return SessionResolution.NotFound;
            }

            // 1) Try to match by process name (case-insensitive partial match)
            var processMatch = sessions.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s.AppId) &&
                (s.AppId.Contains(processName, StringComparison.OrdinalIgnoreCase) ||
                 processName.Contains(s.AppId.Split('.').LastOrDefault() ?? "", StringComparison.OrdinalIgnoreCase)));

            if (processMatch != null)
            {
                LoggingEngine.LogLine($"[SessionResolver] Matched by process name: {processMatch.AppId}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return SessionResolution.Success(processMatch.Key);
            }

            // 2) exact map by known app identifiers / process name hint
            if (_processNameToSessionHint.TryGetValue(processName, out var hintedAppId))
            {
                var match = sessions.FirstOrDefault(s =>
                    string.Equals(s.AppId, hintedAppId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Key.Id, hintedAppId, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    LoggingEngine.LogLine($"[SessionResolver] Matched by hint: {match.AppId}",
                        category: LoggingEngine.LogCategory.MediaControl);
                    return SessionResolution.Success(match.Key);
                }
            }

            // 3) if only one playing session exists, choose it
            var playing = sessions.Where(s => s.PlaybackState == MediaPlaybackState.Playing).ToList();
            if (playing.Count == 1)
            {
                LoggingEngine.LogLine($"[SessionResolver] Single playing session: {playing[0].AppId}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return SessionResolution.Success(playing[0].Key);
            }

            // 4) if multiple playing, prefer most recently updated
            if (playing.Count > 1)
            {
                var pick = playing.OrderByDescending(s => s.LastUpdated).First();
                LoggingEngine.LogLine($"[SessionResolver] Multiple playing, picked: {pick.AppId}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return SessionResolution.Success(pick.Key);
            }

            // 5) If no playing sessions but there are paused ones, return the first one
            var paused = sessions.Where(s => s.PlaybackState == MediaPlaybackState.Paused).ToList();
            if (paused.Count > 0)
            {
                LoggingEngine.LogLine($"[SessionResolver] No playing, using paused: {paused[0].AppId}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return SessionResolution.Success(paused[0].Key);
            }

            // 6) If there's any session at all, use the first one
            if (sessions.Count > 0)
            {
                LoggingEngine.LogLine($"[SessionResolver] Fallback to first session: {sessions[0].AppId}",
                    category: LoggingEngine.LogCategory.MediaControl);
                return SessionResolution.Success(sessions[0].Key);
            }

            LoggingEngine.LogLine($"[SessionResolver] No match found for {processName}",
                category: LoggingEngine.LogCategory.MediaControl,
                loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
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
