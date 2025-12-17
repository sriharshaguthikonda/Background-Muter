using System;
using System.Windows.Forms;
using WinBGMuter.Abstractions;

namespace WinBGMuter.UI
{
    internal static class TrayMenuExtensions
    {
        public static ToolStripMenuItem CreatePolicyModeMenu(PolicyMode currentMode, Action<PolicyMode> onModeChanged)
        {
            var modeMenu = new ToolStripMenuItem("Unfocus Mode");

            var pauseOnlyItem = new ToolStripMenuItem("Pause Only (GSMTC)")
            {
                Checked = currentMode == PolicyMode.PauseOnly,
                Tag = PolicyMode.PauseOnly
            };

            var pauseThenMuteItem = new ToolStripMenuItem("Pause + Mute Fallback")
            {
                Checked = currentMode == PolicyMode.PauseThenMuteFallback,
                Tag = PolicyMode.PauseThenMuteFallback
            };

            var muteOnlyItem = new ToolStripMenuItem("Mute Only (Legacy)")
            {
                Checked = currentMode == PolicyMode.MuteOnly,
                Tag = PolicyMode.MuteOnly
            };

            void OnItemClick(object? sender, EventArgs e)
            {
                if (sender is ToolStripMenuItem item && item.Tag is PolicyMode mode)
                {
                    pauseOnlyItem.Checked = mode == PolicyMode.PauseOnly;
                    pauseThenMuteItem.Checked = mode == PolicyMode.PauseThenMuteFallback;
                    muteOnlyItem.Checked = mode == PolicyMode.MuteOnly;
                    onModeChanged(mode);
                }
            }

            pauseOnlyItem.Click += OnItemClick;
            pauseThenMuteItem.Click += OnItemClick;
            muteOnlyItem.Click += OnItemClick;

            modeMenu.DropDownItems.Add(pauseOnlyItem);
            modeMenu.DropDownItems.Add(pauseThenMuteItem);
            modeMenu.DropDownItems.Add(muteOnlyItem);

            return modeMenu;
        }

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
