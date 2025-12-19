using System;

namespace WinBGMuter.Abstractions.Foreground
{
    public interface IForegroundTracker : IDisposable
    {
        event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

        int CurrentProcessId { get; }

        void Start();

        void Stop();
    }
}
