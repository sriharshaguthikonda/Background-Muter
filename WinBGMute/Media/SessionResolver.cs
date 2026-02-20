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

            // 3) No match found - do NOT fallback to unrelated sessions!
            // Only pause apps that have their own media session
            LoggingEngine.LogLine($"[SessionResolver] No matching session for '{processName}' - skipping (not a media app)",
                category: LoggingEngine.LogCategory.MediaControl);
            return SessionResolution.NotFound;
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
