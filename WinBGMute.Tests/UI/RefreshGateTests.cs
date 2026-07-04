using System.Windows.Forms;
using WinBGMute.UI;
using Xunit;

namespace WinBGMute.Tests.UI;

public class RefreshGateTests
{
    [Fact]
    public void Refresh_WhenVisibleAndNotMinimized()
        => Assert.True(RefreshGate.ShouldRefresh(true, FormWindowState.Normal));

    [Theory]
    [InlineData(false, FormWindowState.Normal)]     // hidden to tray
    [InlineData(true, FormWindowState.Minimized)]   // minimized
    [InlineData(false, FormWindowState.Minimized)]
    public void Skip_WhenHiddenOrMinimized(bool visible, FormWindowState state)
        => Assert.False(RefreshGate.ShouldRefresh(visible, state));
}
