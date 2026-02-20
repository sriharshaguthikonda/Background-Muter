using System;
using System.Windows.Forms;

namespace WinBGMuter.UI
{
    internal static class TrayMenuExtensions
    {
        public static ToolStripMenuItem CreateEnableToggle(bool enabled, Action<bool> onToggle)
        {
            var toggleItem = new ToolStripMenuItem("Pause on Unfocus")
            {
                Checked = enabled,
                CheckOnClick = true
            };

            toggleItem.CheckedChanged += (s, e) =>
            {
                onToggle(toggleItem.Checked);
            };

            return toggleItem;
        }
    }
}
