using WinBGMuter.Abstractions;

namespace WinBGMuter.Config
{
    internal sealed class PauseOnUnfocusSettings
    {
        private const string SettingKeyEnabled = "PauseOnUnfocusEnabled";
        private const string SettingKeyMode = "PauseOnUnfocusMode";
        private const string SettingKeyAudibilityThreshold = "AudibilityThreshold";

        public bool Enabled { get; set; } = false;
        public PolicyMode Mode { get; set; } = PolicyMode.PauseOnly;
        public float AudibilityThreshold { get; set; } = 0.01f;

        public void Load()
        {
            try
            {
                Enabled = Properties.Settings.Default[SettingKeyEnabled] as bool? ?? false;
            }
            catch
            {
                Enabled = false;
            }

            try
            {
                var modeValue = Properties.Settings.Default[SettingKeyMode] as int? ?? (int)PolicyMode.PauseOnly;
                Mode = (PolicyMode)modeValue;
            }
            catch
            {
                Mode = PolicyMode.PauseOnly;
            }

            try
            {
                AudibilityThreshold = Properties.Settings.Default[SettingKeyAudibilityThreshold] as float? ?? 0.01f;
            }
            catch
            {
                AudibilityThreshold = 0.01f;
            }
        }

        public void Save()
        {
            try
            {
                Properties.Settings.Default[SettingKeyEnabled] = Enabled;
                Properties.Settings.Default[SettingKeyMode] = (int)Mode;
                Properties.Settings.Default[SettingKeyAudibilityThreshold] = AudibilityThreshold;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Settings may not exist yet
            }
        }
    }
}
