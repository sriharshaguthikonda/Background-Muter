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

using System.Diagnostics;
using WinBGMuter.Browser;
using WinBGMuter.Config;

namespace WinBGMuter
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Process myproc = Process.GetCurrentProcess();
            myproc.PriorityClass = ProcessPriorityClass.BelowNormal;

            // Check if launched by browser extension for native messaging
            if (IsNativeMessagingLaunch(args))
            {
                RunNativeMessagingHost();
                return;
            }

            SettingsFileStore.Load();
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args));
        }

        /// <summary>
        /// Check if the app was launched by browser for native messaging.
        /// </summary>
        private static bool IsNativeMessagingLaunch(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            foreach (var arg in args)
            {
                // Chrome/Edge extension origin
                if (arg.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // Native messaging parent window argument
                if (arg.StartsWith("--parent-window=", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Run as a native messaging host for browser extension communication.
        /// Connects to the main app's BrowserCoordinator to forward messages.
        /// </summary>
        private static void RunNativeMessagingHost()
        {
            LoggingEngine.LogLevel = LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG;
            LoggingEngine.HasDateTime = true;
            // Native messaging uses stdout for protocol; disable console logging here.
            LoggingEngine.Enabled = false;
            LoggingEngine.InitializeFileLogging();
            LoggingEngine.LogLine("[NativeMessaging] Host starting", category: LoggingEngine.LogCategory.MediaControl);

            using var host = new NativeMessagingHost();
            using var coordinatorClient = new BrowserCoordinatorClient();
            
            // Try to connect to the main app's coordinator
            bool connectedToCoordinator = coordinatorClient.TryConnect(2000);
            
            if (!connectedToCoordinator)
            {
                LoggingEngine.LogLine("[CoordinatorClient] Connection timed out - main app not running?",
                    category: LoggingEngine.LogCategory.MediaControl,
                    loglevel: LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING);
                return;
            }

            // Forward messages from coordinator to extension (via stdout)
            coordinatorClient.MessageReceived += (s, json) =>
            {
                host.SendRawMessage(json);
            };
            
            // Forward extension events to coordinator
            host.WindowFocused += (s, e) =>
            {
                coordinatorClient.SendMessage(new { type = "windowFocused", e.TabId, e.WindowId, e.Title });
            };
            host.BrowserLostFocus += (s, e) =>
            {
                coordinatorClient.SendMessage(new { type = "browserLostFocus" });
            };
            host.TabStateChanged += (s, e) =>
            {
                coordinatorClient.SendMessage(new { type = "mediaStateChanged", e.TabId, e.IsPlaying, e.Title });
            };
            
            host.Start();

            // Keep running until stdin is closed (browser disconnects)
            host.WaitForExit();
        }
    }
}
