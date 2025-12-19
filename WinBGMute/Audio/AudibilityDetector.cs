using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using WinBGMuter.Abstractions.Audio;
using WinBGMuter.Logging;

namespace WinBGMuter.Audio
{
    /// <summary>
    /// Determines which processes are currently audible based on Core Audio session snapshots.
    /// </summary>
    internal sealed class AudibilityDetector
    {
        private readonly float _peakThreshold;
        private readonly ILogger _logger;

        public AudibilityDetector(float peakThreshold = 0.01f, ILogger? logger = null)
        {
            if (peakThreshold < 0 || peakThreshold > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(peakThreshold), "Peak threshold must be between 0 and 1.");
            }

            _peakThreshold = peakThreshold;
            _logger = logger ?? AppLogging.CreateLogger(LogCategories.AudioSessions);
        }

        /// <summary>
        /// Returns the set of processes that are considered audible for the given snapshot.
        /// Multiple sessions from the same process are deduplicated, keeping the loudest peak.
        /// </summary>
        public IReadOnlyCollection<AudioProcessState> GetAudibleProcesses(AudioSessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var audible = new Dictionary<int, AudioProcessState>();

            foreach (var process in snapshot.Processes)
            {
                if (!process.IsActive)
                {
                    continue;
                }

                if (process.Peak < _peakThreshold)
                {
                    continue;
                }

                if (audible.TryGetValue(process.ProcessId, out var existing))
                {
                    if (process.Peak > existing.Peak)
                    {
                        audible[process.ProcessId] = process;
                    }
                }
                else
                {
                    audible[process.ProcessId] = process;
                }
            }

            _logger.LogDebug(
                "Audible at {TimestampUtc}: {Processes}",
                snapshot.TimestampUtc,
                FormatProcessList(audible.Values));

            return audible.Values.ToList();
        }

        private static string FormatProcessList(IEnumerable<AudioProcessState> processes)
        {
            return string.Join(", ", processes.Select(p => $"{p.ProcessName}({p.ProcessId}) peak={p.Peak:0.000}"));
        }
    }
}
