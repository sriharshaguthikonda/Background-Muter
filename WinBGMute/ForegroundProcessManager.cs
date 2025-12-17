/*
 *  Background Muter - Automatically mute background applications
 *  Copyright (C) 2022  Nefares (nefares@protonmail.com) github.com/nefares
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/



using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;

using Timer = System.Timers.Timer;




namespace WinBGMuter
{
    internal sealed class ForegroundProcessManager : IDisposable
    {
        //WinAPI to translate PID from hWND
        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        //WinAPI to set a hook for when a window size changes
        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetWinEventHook(UInt32 eventMin,
            UInt32 eventMax,
            IntPtr hmodWinEventProc,
            WinEventProcDelegate pfnWinEventProc,
            UInt32 idProcess,
            UInt32 idThread,
            UInt32 dwFlags);

        //WinAPI to unhook win event
        [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate void WinEventProcDelegate(
          IntPtr hWinEventHook,
          UInt32 ev,
          IntPtr hwnd,
          Int32 idObject,
          Int32 idChild,
          UInt32 dwEventThread,
          UInt32 dwmsEventTime
          );

        private const UInt32 EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const Int32 OBJID_WINDOW = 0x00000000;
        private const Int32 CHILDID_SELF = 0;

        private IntPtr m_hWinEventHook = IntPtr.Zero;
        private WinEventProcDelegate m_winEventProc = null!;
        private static readonly ConcurrentStack<int> m_JobStack = new ConcurrentStack<int>();
        private readonly Timer m_debounceTimer;
        private readonly object m_debounceLock = new object();

        private int m_lastForegroundId = -1;
        private int m_pendingForegroundId = -1;

        public ForegroundProcessManager(int debounceIntervalMs = 200)
        {
            m_debounceTimer = new Timer(debounceIntervalMs)
            {
                AutoReset = false
            };

            m_debounceTimer.Elapsed += DebounceTimerElapsed;
        }

        public (bool, int) GetJobThreadSafe()
        {
            if (m_JobStack.TryPop(out var pid))
            {
                if (!m_JobStack.IsEmpty)
                {
                    LoggingEngine.LogLine("[*] discarding previous foreground processes ");
                }

                m_JobStack.Clear();

                return (true, pid);
            }

            return (false, -1);
        }

        public void Init()
        {
            m_winEventProc = new WinEventProcDelegate(WinEventProc);

            m_hWinEventHook = SetWinEventHook(
                0x0003, 0x0003,
                IntPtr.Zero, m_winEventProc, 0, 0,
                0x0000);
        }

        public void CleanUp()
        {
            m_debounceTimer.Elapsed -= DebounceTimerElapsed;
            m_debounceTimer.Stop();
            m_debounceTimer.Dispose();
            UnhookWinEvent(m_hWinEventHook);

        }

        public void Dispose()
        {
            CleanUp();
        }

        private void WinEventProc(
          IntPtr hWinEventHook,
          UInt32 ev,
          IntPtr hwnd,
          Int32 idObject,
          Int32 idChild,
          UInt32 dwEventThread,
          UInt32 dwmsEventTime

          )
        {
            uint testpid = 0;
            var threadId = GetWindowThreadProcessId(hwnd, out testpid);
            if (threadId == 0)
            {
                return;
            }

            if (ev == EVENT_SYSTEM_FOREGROUND &&
                idObject == OBJID_WINDOW &&
                idChild == CHILDID_SELF)
            {
                uint pid = 0;
                GetWindowThreadProcessId(hwnd, out pid);

                int fpid = (int)pid;
                Process? foreground = null;
                try
                {
                    foreground = Process.GetProcessById(fpid);


                    string pname = String.Empty;

                    if (foreground != null)
                    {
                        pname = foreground.ProcessName;
                    }

                    if ((fpid != 0) && (pname != String.Empty))
                    {
                        DebounceForegroundChange(fpid);
                    }

                }
                catch(Exception e)
                {
                    LoggingEngine.LogLine($"[-] Foreground Window {hwnd} and/or process {fpid} do not exist => {e.Message}", Color.Red);
                }
                finally
                {

                }


            }
        }

        private void DebounceForegroundChange(int pid)
        {
            lock (m_debounceLock)
            {
                m_pendingForegroundId = pid;
                m_debounceTimer.Stop();
                m_debounceTimer.Start();
            }
        }

        private void DebounceTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            int pid;

            lock (m_debounceLock)
            {
                pid = m_pendingForegroundId;
                m_pendingForegroundId = -1;
            }

            if (pid == -1 || pid == m_lastForegroundId)
            {
                return;
            }

            string pname = String.Empty;

            try
            {
                var proc = Process.GetProcessById(pid);
                pname = proc.ProcessName;
            }
            catch (Exception)
            {
                pname = "<unknown>";
            }

            m_lastForegroundId = pid;

            LoggingEngine.LogLine($"[+] Foreground process changed to {pname} - {pid}", Color.Cyan);
            m_JobStack.Push(pid);
        }
    }
}
