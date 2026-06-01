using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Local machine configuration the tray writes and the agent service reads:
    /// export folder, Milestone server + auth. Stored at
    /// <c>%ProgramData%\MSCPlugins\AutoExporter\agent.config</c> as a simple key=value file.
    /// The password is DPAPI-protected with machine scope so the (LocalSystem or configured)
    /// service account can decrypt it.
    /// </summary>
    public sealed class MachineConfig
    {
        public string ServerUrl = "";
        public AuthMode AuthMode = AuthMode.Basic;
        public string Username = "";
        public string Password = "";        // plaintext in-memory; encrypted at rest
        public string ExportFolder = "";

        // Agent log verbosity: Error | Info | Debug (default Info).
        public string LogLevel = "Info";

        // Agent-wide ring-storage limits for the export folder (0 = unlimited), same semantics
        // as the legacy export plugin's per-job MaxGB / MaxAgeDays retention.
        public int MaxGB;
        public int RetentionDays;

        // True once the tray has completed a successful login against ServerUrl. The tray uses
        // this to collapse the connection editor into a read-only summary on next open.
        public bool Registered;

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "agent.config");

        public void Save(string path = null)
        {
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine("ServerUrl=" + Escape(ServerUrl));
            sb.AppendLine("AuthMode=" + AuthMode);
            sb.AppendLine("Username=" + Escape(Username));
            sb.AppendLine("Password=" + Protect(Password));
            sb.AppendLine("ExportFolder=" + Escape(ExportFolder));
            sb.AppendLine("LogLevel=" + Escape(LogLevel));
            sb.AppendLine("MaxGB=" + MaxGB.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RetentionDays=" + RetentionDays.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Registered=" + (Registered ? "true" : "false"));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static MachineConfig Load(string path = null)
        {
            path = path ?? DefaultPath;
            var cfg = new MachineConfig();
            if (!File.Exists(path)) return cfg;

            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq);
                var val = line.Substring(eq + 1);
                switch (key)
                {
                    case "ServerUrl": cfg.ServerUrl = Unescape(val); break;
                    case "AuthMode":
                        cfg.AuthMode = Enum.TryParse<AuthMode>(val, out var m) ? m : AuthMode.Basic;
                        break;
                    case "Username": cfg.Username = Unescape(val); break;
                    case "Password": cfg.Password = Unprotect(val); break;
                    case "ExportFolder": cfg.ExportFolder = Unescape(val); break;
                    case "LogLevel": cfg.LogLevel = string.IsNullOrWhiteSpace(val) ? "Info" : Unescape(val); break;
                    case "MaxGB": cfg.MaxGB = ParseInt(val); break;
                    case "RetentionDays": cfg.RetentionDays = ParseInt(val); break;
                    case "Registered": cfg.Registered = string.Equals(val.Trim(), "true", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            return cfg;
        }

        // ── helpers ─────────────────────────────────────────────────────
        static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "");
        static string Unescape(string s) => (s ?? "").Replace("\\n", "\n").Replace("\\\\", "\\");

        static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(bytes);
        }

        static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}
