using System;
using System.Diagnostics;
using System.IO;

namespace AutoExporter.Tray.Services
{
    /// <summary>Locations of the tray and service log files, and a helper to open one in the
    /// system default text viewer.</summary>
    public static class LogFiles
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter");

        public static string TrayLog => Path.Combine(Dir, "tray.log");
        public static string ServiceLog => Path.Combine(Dir, "agent.log");

        /// <summary>Open the log in the default viewer, creating an empty file first if needed.</summary>
        public static void Open(string path)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(path)) File.AppendAllText(path, "");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Open log failed: " + path, ex);
            }
        }
    }
}
