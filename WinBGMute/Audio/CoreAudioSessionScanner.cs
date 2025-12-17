using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinBGMuter.Abstractions;

namespace WinBGMuter.Audio
{
    internal sealed class CoreAudioSessionScanner : IAudioSessionScanner
    {
        public Task<AudioSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processes = new List<AudioProcessState>();
            var timestamp = DateTimeOffset.UtcNow;

            IMMDeviceEnumerator deviceEnumerator = null!;
            IMMDevice speakers = null!;
            IAudioSessionManager2 mgr = null!;
            IAudioSessionEnumerator? sessionEnum = null;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out speakers);

                var iid = typeof(IAudioSessionManager2).GUID;
                speakers.Activate(ref iid, 0, IntPtr.Zero, out var o);
                mgr = (IAudioSessionManager2)o;

                mgr.GetSessionEnumerator(out sessionEnum);
                if (sessionEnum == null)
                {
                    return Task.FromResult(new AudioSessionSnapshot(timestamp, processes));
                }

                sessionEnum.GetCount(out var count);
                for (int i = 0; i < count; i++)
                {
                    sessionEnum.GetSession(i, out IAudioSessionControl2 ctl);
                    ctl.GetProcessId(out var pid);

                    // pid can be 0 for system sounds; skip
                    if (pid == 0)
                    {
                        Marshal.ReleaseComObject(ctl);
                        continue;
                    }

                    string processName = "<unknown>";
                    try
                    {
                        processName = Process.GetProcessById(pid).ProcessName;
                    }
                    catch
                    {
                        // best-effort; keep unknown
                    }

                    bool isActive = false;
                    float peak = 0;

                    // ISimpleAudioVolume also implements IAudioMeterInformation in many cases; we only have volume here,
                    // so we approximate using GetMute/volume state. Keep fields minimal until audibility heuristic (Commit 07).
                    if (ctl is ISimpleAudioVolume vol)
                    {
                        vol.GetMute(out var muted);
                        isActive = !muted;
                    }

                    processes.Add(new AudioProcessState(pid, processName, isActive, peak, timestamp));
                    Marshal.ReleaseComObject(ctl);
                }
            }
            catch (Exception ex)
            {
                LoggingEngine.LogLine($"[AudioSessionScanner] enumeration failed: {ex.Message}", loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING, category: LoggingEngine.LogCategory.AudioSessions);
            }
            finally
            {
                if (sessionEnum != null) Marshal.ReleaseComObject(sessionEnum);
                if (mgr != null) Marshal.ReleaseComObject(mgr);
                if (speakers != null) Marshal.ReleaseComObject(speakers);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }

            return Task.FromResult(new AudioSessionSnapshot(timestamp, processes));
        }
    }
}
