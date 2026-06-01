using System;
using System.IO;
using System.Text;
using AutoExporter.Contracts;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Minimal file logger to %ProgramData%\MSCPlugins\AutoExporter\agent.log. Never throws.
    /// Verbosity is set once at startup via <see cref="Configure"/> from the machine config:
    /// Error (errors only), Info (default), or Debug (everything).
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        // 0 = Error, 1 = Info, 2 = Debug. Default Info.
        private static int _level = 1;

        public static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "agent.log");

        public static void Configure(string level)
        {
            switch ((level ?? "").Trim().ToLowerInvariant())
            {
                case "error": _level = 0; break;
                case "debug": _level = 2; break;
                default:      _level = 1; break;
            }
            Info("Log level set to " + Name(_level) + ".");
        }

        public static void Debug(string msg) { if (_level >= 2) Write("DBG ", msg); }
        public static void Info(string msg)  { if (_level >= 1) Write("INFO", msg); }
        public static void Error(string msg) => Write("ERR ", msg);

        private static string Name(int l) => l == 0 ? "Error" : l == 2 ? "Debug" : "Info";

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
