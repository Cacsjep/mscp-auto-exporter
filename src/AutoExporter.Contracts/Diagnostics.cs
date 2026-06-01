using System;
using System.Reflection;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Builds a one-line environment banner each component logs at startup, so a log pasted into a
    /// GitHub issue already carries the version, OS, machine and process bitness without us asking.
    /// </summary>
    public static class Diagnostics
    {
        public static string Banner(string component, Assembly asm)
        {
            string version = "?";
            try { version = asm?.GetName()?.Version?.ToString() ?? "?"; } catch { }
            string os = "?";
            try { os = Environment.OSVersion.ToString(); } catch { }
            string clr = "?";
            try { clr = Environment.Version.ToString(); } catch { }
            string machine = "?";
            try { machine = Environment.MachineName; } catch { }

            return $"{component} starting. version={version} machine={machine} os={os} " +
                   $"clr={clr} 64bit={Environment.Is64BitProcess}";
        }
    }
}
