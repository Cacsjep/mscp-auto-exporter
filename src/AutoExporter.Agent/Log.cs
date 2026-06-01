using System;
using System.IO;
using System.Text;
using AutoExporter.Contracts;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Minimal file logger to %ProgramData%\MSCPlugins\AutoExporter\agent.log. Never throws.
    /// Always logs everything (no level filter) so a log pasted into a GitHub issue has the full
    /// picture. The file is size-rotated (see <see cref="LogRotation"/>) so it cannot fill the disk.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "agent.log");

        public static void Debug(string msg) => Write("DBG ", msg);
        public static void Info(string msg)  => Write("INFO", msg);
        public static void Error(string msg) => Write("ERR ", msg);

        private static void Write(string level, string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    LogRotation.RollIfNeeded(Path);
                    File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* logging must never throw */ }
            Console.WriteLine(line);
        }
    }
}
