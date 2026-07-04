using WinBGMute.Diagnostics;
using Xunit;

namespace WinBGMute.Tests.Diagnostics;

public class PerfProbeTests
{
    [Fact]
    public void AvgMs_IsZero_WhenNoSamples()
    {
        var p = new PerfProbe("x");
        Assert.Equal(0, p.Count);
        Assert.Equal(0d, p.AvgMs);
    }

    [Fact]
    public void AvgMs_AveragesSamples()
    {
        var p = new PerfProbe("tick");
        p.Add(10);
        p.Add(20);
        Assert.Equal(2, p.Count);
        Assert.Equal(15d, p.AvgMs);
        Assert.Contains("tick", p.Snapshot());
        Assert.Contains("ticks=2", p.Snapshot());
    }
}
