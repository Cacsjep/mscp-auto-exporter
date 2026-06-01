using System;
using System.IO;
using System.Text;
using AutoExporter.Contracts;

namespace AutoExporter.Tray.Services
{
    /// <summary>Minimal tray logger to %ProgramData%\MSCPlugins\AutoExporter\tray.log. Never throws.</summary>
    public static class Log
    {
        private static readonly object Gate = new();

        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "tray.log");

        public static void Info(string msg) => Write("INFO", msg, null);
        public static void Warn(string msg) => Write("WARN", msg, null);
        public static void Error(string msg, Exception? ex = null) => Write("ERR ", msg, ex);

        private static void Write(string level, string msg, Exception? ex)
        {
            // Log the full exception (type + stack) for errors so a GitHub issue has what we need.
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}" + (ex != null ? Environment.NewLine + ex : "");
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    LogRotation.RollIfNeeded(Path);
                    File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
            Console.WriteLine(line);
        }
    }
}
