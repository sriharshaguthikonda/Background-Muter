using WinBGMuter.Abstractions;

namespace WinBGMuter.Audio
{
    internal sealed class AudibilityDetector
    {
        private readonly float _peakThreshold;

        public AudibilityDetector(float peakThreshold)
        {
            _peakThreshold = peakThreshold;
        }

        /// <summary>
        /// Returns the set of PIDs considered audible from a snapshot.
        /// </summary>
        public IReadOnlyList<int> GetAudiblePids(AudioSessionSnapshot snapshot)
        {
            var audible = new List<int>();
            foreach (var p in snapshot.Processes)
            {
                if (IsAudible(p))
                {
                    audible.Add(p.Pid);
                }
            }

            return audible;
        }

        private bool IsAudible(AudioProcessState state)
        {
            // First cut: active & peak above threshold
            return state.IsActive && state.Peak >= _peakThreshold;
        }
    }
}
