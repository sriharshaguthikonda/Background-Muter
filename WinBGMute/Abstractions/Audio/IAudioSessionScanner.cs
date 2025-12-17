namespace WinBGMuter.Abstractions.Audio
{
    public interface IAudioSessionScanner
    {
        AudioSessionSnapshot CaptureSnapshot();
    }
}
