namespace WinBGMute.Diagnostics;

/// <summary>Thread-safe running average of tick durations, for CPU profiling.</summary>
internal sealed class PerfProbe
{
    private readonly string _name;
    private long _count;
    private long _totalMs;
    private readonly object _lock = new();

    public PerfProbe(string name) => _name = name;

    public void Add(long elapsedMs)
    {
        lock (_lock)
        {
            _count++;
            _totalMs += elapsedMs;
        }
    }

    public long Count { get { lock (_lock) { return _count; } } }

    public double AvgMs { get { lock (_lock) { return _count == 0 ? 0d : (double)_totalMs / _count; } } }

    public string Snapshot()
    {
        lock (_lock)
        {
            double avg = _count == 0 ? 0d : (double)_totalMs / _count;
            return $"[perf] {_name}: ticks={_count} avg={avg:F2}ms total={_totalMs}ms";
        }
    }
}
