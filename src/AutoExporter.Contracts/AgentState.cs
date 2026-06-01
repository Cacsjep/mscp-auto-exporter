using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Last known runtime state the agent service writes after each login attempt, so the tray
    /// can show whether the service itself actually connected to Milestone (separate from the
    /// tray's own REST check). Stored next to the machine config at
    /// <c>%ProgramData%\MSCPlugins\AutoExporter\agent.state</c>.
    /// </summary>
    public sealed class AgentState
    {
        public bool LoggedIn;
        public string Identity = "";    // the Windows account the service process runs as
        public string ServerUrl = "";
        public string AuthMode = "";    // Basic | WindowsOtherUser
        public string LoginUser = "";   // the Milestone user the service signed in as
        public string LastError = "";
        public DateTime UpdatedUtc;

        // Result of the recording-server reachability probe the service runs after login. Empty
        // when every recorder answered. Otherwise one human-readable line per unreachable server,
        // so the tray can warn the operator about DNS / hosts / firewall problems.
        public string RecorderWarnings = "";

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "agent.state");

        public void Save(string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.AppendLine("LoggedIn=" + (LoggedIn ? "true" : "false"));
                sb.AppendLine("Identity=" + Esc(Identity));
                sb.AppendLine("ServerUrl=" + Esc(ServerUrl));
                sb.AppendLine("AuthMode=" + Esc(AuthMode));
                sb.AppendLine("LoginUser=" + Esc(LoginUser));
                sb.AppendLine("LastError=" + Esc(LastError));
                sb.AppendLine("RecorderWarnings=" + Esc(RecorderWarnings));
                sb.AppendLine("UpdatedUtc=" + UpdatedUtc.ToString("o", CultureInfo.InvariantCulture));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { /* state writing must never throw */ }
        }

        /// <summary>Remove the state file so the tray shows no stale result until the service
        /// writes a fresh one (used right before a restart).</summary>
        public static void Clear(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        public static AgentState Load(string path = null)
        {
            path = path ?? DefaultPath;
            var s = new AgentState();
            if (!File.Exists(path)) return s;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq);
                    var val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "LoggedIn": s.LoggedIn = string.Equals(val.Trim(), "true", StringComparison.OrdinalIgnoreCase); break;
                        case "Identity": s.Identity = Unesc(val); break;
                        case "ServerUrl": s.ServerUrl = Unesc(val); break;
                        case "AuthMode": s.AuthMode = Unesc(val); break;
                        case "LoginUser": s.LoginUser = Unesc(val); break;
                        case "LastError": s.LastError = Unesc(val); break;
                        case "RecorderWarnings": s.RecorderWarnings = Unesc(val); break;
                        case "UpdatedUtc":
                            DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out s.UpdatedUtc);
                            break;
                    }
                }
            }
            catch { }
            return s;
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        static string Unesc(string s) => (s ?? "").Replace("\\n", "\n").Replace("\\\\", "\\");
    }
}
