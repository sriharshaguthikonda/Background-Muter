using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinBGMuter.Abstractions.Audio;
using WinBGMuter;

namespace WinBGMuter.Audio
{
    /// <summary>
    /// Provides read-only snapshots of the current Core Audio sessions.
    /// </summary>
    internal sealed class CoreAudioSessionScanner : IAudioSessionScanner, IDisposable
    {
        private readonly IMMDeviceEnumerator deviceEnumerator;
        private readonly IMMDevice speakers;
        private readonly IAudioSessionManager2 sessionManager;
        private readonly Guid sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
        private readonly ILogger? logger;

        public CoreAudioSessionScanner(ILogger? logger = null)
        {
            this.logger = logger;
            deviceEnumerator = new MMDeviceEnumerator();

            deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out speakers);
            speakers.Activate(ref sessionManagerGuid, 0, IntPtr.Zero, out var sessionManagerObject);
            sessionManager = (IAudioSessionManager2)sessionManagerObject;
        }

        public AudioSessionSnapshot CaptureSnapshot()
        {
            var timestampUtc = DateTime.UtcNow;
            var processes = new List<AudioProcessState>();

            sessionManager.GetSessionEnumerator(out var sessionEnumerator);
            if (sessionEnumerator == null)
            {
                return new AudioSessionSnapshot(timestampUtc, processes);
            }

            try
            {
                sessionEnumerator.GetCount(out var sessionCount);
                for (var index = 0; index < sessionCount; index++)
                {
                    IAudioSessionControl2? control = null;
                    try
                    {
                        sessionEnumerator.GetSession(index, out control);
                        if (control == null)
                        {
                            continue;
                        }

                        control.GetProcessId(out var processId);
                        var processName = ResolveProcessName(processId);

                        var peak = GetPeak(control);
                        var isActive = IsActive(control);

                        processes.Add(new AudioProcessState(processId, processName, isActive, peak, timestampUtc));
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to capture audio session at index {Index}", index);
                    }
                    finally
                    {
                        if (control != null)
                        {
                            Marshal.ReleaseComObject(control);
                        }
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessionEnumerator);
            }

            return new AudioSessionSnapshot(timestampUtc, processes);
        }

        private static string ResolveProcessName(int processId)
        {
            try
            {
                return Process.GetProcessById(processId).ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static float GetPeak(IAudioSessionControl2 control)
        {
            try
            {
                if (control is IAudioMeterInformation meter && meter.GetPeakValue(out var peakValue) == 0)
                {
                    return peakValue;
                }
            }
            catch
            {
                // ignored – metering is best-effort
            }

            return 0f;
        }

        private static bool IsActive(IAudioSessionControl2 control)
        {
            try
            {
                var result = control.GetState(out var state);
                if (result == 0)
                {
                    return state == AudioSessionState.AudioSessionStateActive;
                }
            }
            catch
            {
                // ignored – state is best-effort
            }

            return false;
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(sessionManager);
            Marshal.ReleaseComObject(speakers);
            Marshal.ReleaseComObject(deviceEnumerator);
        }
    }
}
