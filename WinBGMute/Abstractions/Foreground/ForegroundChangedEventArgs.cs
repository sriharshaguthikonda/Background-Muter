using System;

namespace WinBGMuter.Abstractions.Foreground
{
    public sealed class ForegroundChangedEventArgs : EventArgs
    {
        public ForegroundChangedEventArgs(int previousProcessId, int currentProcessId, IntPtr windowHandle, DateTime timestampUtc)
        {
            PreviousProcessId = previousProcessId;
            CurrentProcessId = currentProcessId;
            WindowHandle = windowHandle;
            TimestampUtc = timestampUtc;
        }

        public int PreviousProcessId { get; }

        public int CurrentProcessId { get; }

        public IntPtr WindowHandle { get; }

        public DateTime TimestampUtc { get; }
    }
}
