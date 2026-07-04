using System.Windows.Forms;

namespace WinBGMute.UI;

internal static class RefreshGate
{
    /// <summary>UI list refresh is only useful when the window is actually on screen.</summary>
    public static bool ShouldRefresh(bool isVisible, FormWindowState state)
        => isVisible && state != FormWindowState.Minimized;
}
