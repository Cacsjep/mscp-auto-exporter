using System;
using System.Diagnostics;
using System.IO;

namespace AutoExporter.Tray.Services
{
    /// <summary>Small helpers for opening things in the Windows shell.</summary>
    public static class Shell
    {
        /// <summary>
        /// Open a folder in Explorer. Returns null on success, or a short reason the folder could
        /// not be opened (not configured, does not exist, or the shell call failed).
        /// </summary>
        public static string OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "No export folder is configured yet.";
            if (!Directory.Exists(path))
                return "The export folder does not exist yet: " + path;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Open export folder failed: " + path, ex);
                return "Could not open the folder: " + ex.Message;
            }
        }
    }
}
