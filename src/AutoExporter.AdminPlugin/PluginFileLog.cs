using System;
using System.IO;
using System.Text;
using AutoExporter.Contracts;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Minimal file logger for the Event Server side of the plugin, to
    /// %ProgramData%\MSCPlugins\AutoExporter\plugin.log. Never throws.
    /// </summary>
    internal static class PluginFileLog
    {
        private static readonly object Gate = new object();

        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "plugin.log");

        public static void Info(string msg)  => Write("INFO", msg);
        public static void Error(string msg) => Write("ERR ", msg);

        // Log the full exception (type + stack) so a GitHub issue has what we need.
        public static void Error(string msg, Exception ex) => Write("ERR ", msg + Environment.NewLine + ex);

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    LogRotation.RollIfNeeded(Path);
                    File.AppendAllText(Path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch { /* logging must never throw */ }
        }
    }
}
