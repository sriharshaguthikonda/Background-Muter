namespace WinBGMuter.Abstractions.Media
{
    public sealed class MediaCommandResult
    {
        public MediaCommandResult(bool success, MediaCommandFailureReason failureReason, string? sessionId)
        {
            Success = success;
            FailureReason = failureReason;
            SessionId = sessionId;
        }

        public bool Success { get; }

        public MediaCommandFailureReason FailureReason { get; }

        public string? SessionId { get; }

        public static MediaCommandResult Succeeded(string? sessionId)
        {
            return new MediaCommandResult(true, MediaCommandFailureReason.None, sessionId);
        }

        public static MediaCommandResult Failed(MediaCommandFailureReason reason, string? sessionId)
        {
            return new MediaCommandResult(false, reason, sessionId);
        }
    }
}
