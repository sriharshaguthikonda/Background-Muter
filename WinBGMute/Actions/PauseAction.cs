using System;
using System.Threading;
using System.Threading.Tasks;
using WinBGMuter.Abstractions;
using WinBGMuter.Media;

namespace WinBGMuter.Actions
{
    internal sealed class PauseAction
    {
        private readonly IMediaController _mediaController;
        private readonly SessionResolver _resolver;

        public PauseAction(IMediaController mediaController, SessionResolver resolver)
        {
            _mediaController = mediaController;
            _resolver = resolver;
        }

        public async Task<PauseResult> TryPauseAsync(string processName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = await _resolver.ResolveAsync(processName, cancellationToken).ConfigureAwait(false);

            switch (resolution.Status)
            {
                case SessionResolutionStatus.NotFound:
                    LoggingEngine.LogLine($"[PauseAction] No session found for {processName}", category: LoggingEngine.LogCategory.MediaControl);
                    return PauseResult.NotSupported;

                case SessionResolutionStatus.Ambiguous:
                    LoggingEngine.LogLine($"[PauseAction] Ambiguous session for {processName}", category: LoggingEngine.LogCategory.MediaControl);
                    return PauseResult.Ambiguous;

                case SessionResolutionStatus.Success:
                    break;

                default:
                    return PauseResult.Failed;
            }

            var session = resolution.Session!;
            var result = await _mediaController.TryPauseAsync(session, cancellationToken).ConfigureAwait(false);

            return result switch
            {
                MediaControlResult.Success => PauseResult.Success,
                MediaControlResult.NotSupported => PauseResult.NotSupported,
                _ => PauseResult.Failed
            };
        }

        public async Task<PauseResult> TryResumeAsync(MediaSessionKey sessionKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _mediaController.TryPlayAsync(sessionKey, cancellationToken).ConfigureAwait(false);

            return result switch
            {
                MediaControlResult.Success => PauseResult.Success,
                MediaControlResult.NotSupported => PauseResult.NotSupported,
                _ => PauseResult.Failed
            };
        }
    }

    internal enum PauseResult
    {
        Success,
        NotSupported,
        Ambiguous,
        Failed
    }
}
