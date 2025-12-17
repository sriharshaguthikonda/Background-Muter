using System;
using System.Collections.Generic;

namespace WinBGMuter.Abstractions.Audio
{
    public sealed class AudioSessionSnapshot
    {
        public AudioSessionSnapshot(DateTime timestampUtc, IReadOnlyCollection<AudioProcessState> processes)
        {
            TimestampUtc = timestampUtc;
            Processes = processes;
        }

        public DateTime TimestampUtc { get; }

        public IReadOnlyCollection<AudioProcessState> Processes { get; }
    }
}
