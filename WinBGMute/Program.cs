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
using WinBGMuter.Logging;

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

            AppLogging.SetMinimumLevelFromLegacy(LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG);

            var startupLogger = AppLogging.CreateLogger(LogCategories.State);
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "unknown";
            startupLogger.LogInformation("Starting Background Muter (assembly version: {AssemblyVersion})", version);

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args));
        }
    }
}