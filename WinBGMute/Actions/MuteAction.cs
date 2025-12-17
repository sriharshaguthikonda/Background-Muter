namespace WinBGMuter.Actions
{
    internal sealed class MuteAction
    {
        private readonly VolumeMixer _volumeMixer;

        public MuteAction(VolumeMixer volumeMixer)
        {
            _volumeMixer = volumeMixer;
        }

        public MuteResult TryMute(int pid)
        {
            try
            {
                _volumeMixer.SetApplicationMute(pid, true);
                LoggingEngine.LogLine($"[MuteAction] Muted PID {pid}", category: LoggingEngine.LogCategory.MediaControl);
                return MuteResult.Success;
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[MuteAction] Failed to mute PID {pid}: {ex.Message}", category: LoggingEngine.LogCategory.MediaControl, loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return MuteResult.Failed;
            }
        }

        public MuteResult TryUnmute(int pid)
        {
            try
            {
                _volumeMixer.SetApplicationMute(pid, false);
                LoggingEngine.LogLine($"[MuteAction] Unmuted PID {pid}", category: LoggingEngine.LogCategory.MediaControl);
                return MuteResult.Success;
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[MuteAction] Failed to unmute PID {pid}: {ex.Message}", category: LoggingEngine.LogCategory.MediaControl, loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return MuteResult.Failed;
            }
        }
    }

    internal enum MuteResult
    {
        Success,
        Failed
    }
}
