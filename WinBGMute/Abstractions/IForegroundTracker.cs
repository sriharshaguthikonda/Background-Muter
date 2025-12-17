namespace WinBGMuter.Abstractions
{
    /// <summary>
    /// Tracks foreground changes and exposes subscription for PID changes.
    /// </summary>
    public interface IForegroundTracker : IDisposable
    {
        event EventHandler<ForegroundChangedEventArgs> ForegroundChanged;

        void Start();
        void Stop();
    }

    public sealed class ForegroundChangedEventArgs : EventArgs
    {
        public ForegroundChangedEventArgs(int previousPid, int currentPid, IntPtr hwnd, DateTimeOffset timestamp)
        {
            PreviousPid = previousPid;
            CurrentPid = currentPid;
            Hwnd = hwnd;
            Timestamp = timestamp;
        }

        public int PreviousPid { get; }
        public int CurrentPid { get; }
        public IntPtr Hwnd { get; }
        public DateTimeOffset Timestamp { get; }
    }
}
