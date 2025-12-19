using System.Collections.Generic;

namespace WinBGMuter.Abstractions.Media
{
    public interface IMediaController
    {
        IReadOnlyCollection<MediaSessionInfo> GetSessions();

        MediaCommandResult TryPause(MediaSessionInfo session);

        MediaCommandResult TryPlay(MediaSessionInfo session);
    }
}
