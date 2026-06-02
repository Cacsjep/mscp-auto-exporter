using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.ServiceProcess;
using AutoExporter.Contracts;

namespace AutoExporter.Agent
{
    internal static class Program
    {
        public const string ServiceName = "AutoExporterAgent";
        public const string DisplayName = "Auto Exporter Agent";

        private static int Main(string[] args)
        {
            // Console / install entry points; with no args it runs as a Windows service.
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "--install":   return ScInstall();
                    case "--uninstall": return ScUninstall();
                    case "--console":   return RunConsole();
                    case "--configure": return WriteConfig(args);
                    default:
                        Console.Error.WriteLine($"Unknown arg '{args[0]}'. Use --install | --uninstall | --console | --configure.");
                        return 1;
                }
            }

            ServiceBase.Run(new AgentService());
            return 0;
        }

        /// <summary>
        /// Write the machine config from the values the installer collected, so the service is
        /// already configured on its first start. Invoked by the MSI as a deferred action running
        /// as LocalSystem. Arguments are --key=value pairs:
        ///   --server= --auth=(Basic|WindowsOtherUser) --user= --password=
        ///   --name= --folder= --maxgb= --retention= --loglevel=
        /// This reuses MachineConfig.Save so the format and DPAPI (LocalMachine) match exactly what
        /// the service reads. It never overwrites an existing config with a blank server.
        /// </summary>
        private static int WriteConfig(string[] args)
        {
            try
            {
                var kv = ParseKeyValues(args);
                string Get(string k) => kv.TryGetValue(k, out var v) ? v : "";
                int GetInt(string k) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;

                var server = Get("server").Trim();
                if (server.Length == 0)
                {
                    // Nothing to configure (user left the wizard blank). Leave any existing config
                    // untouched so an upgrade does not wipe a working configuration.
                    Console.WriteLine("No server provided, skipping config write.");
                    return 0;
                }

                var cfg = new MachineConfig
                {
                    ServerUrl = server,
                    AuthMode = string.Equals(Get("auth"), "WindowsOtherUser", StringComparison.OrdinalIgnoreCase)
                        ? AuthMode.WindowsOtherUser : AuthMode.Basic,
                    Username = Get("user"),
                    Password = Get("password"),
                    DisplayName = Get("name"),
                    ExportFolder = Get("folder"),
                    LogLevel = string.IsNullOrWhiteSpace(Get("loglevel")) ? "Info" : Get("loglevel"),
                    MaxGB = GetInt("maxgb"),
                    RetentionDays = GetInt("retention"),
                    Registered = false,
                };
                cfg.Save();
                Console.WriteLine("Configuration written for server " + server + ".");
                return 0;
            }
            catch (Exception ex)
            {
                // Non-fatal: the user can still configure via the tray. The CA ignores the result.
                Console.Error.WriteLine("Configure failed: " + ex.Message);
                return 0;
            }
        }

        // Parse --key=value tokens (value may be empty). Keys are lowercased.
        private static Dictionary<string, string> ParseKeyValues(string[] args)
        {
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("--")) continue;
                a = a.Substring(2);
                int eq = a.IndexOf('=');
                if (eq < 0) { kv[a] = ""; continue; }
                kv[a.Substring(0, eq)] = a.Substring(eq + 1);
            }
            return kv;
        }

        /// <summary>Run the host loop in the foreground for local debugging.</summary>
        private static int RunConsole()
        {
            Console.WriteLine($"{DisplayName} (console). Ctrl+C to stop.");
            var host = new AgentHost();
            host.Start();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; host.Stop(); };
            host.WaitForShutdown();
            return 0;
        }

        private static string ExePath => Assembly.GetEntryAssembly().Location;

        private static int ScInstall()
        {
            Sc($"create {ServiceName} binPath= \"{ExePath}\" start= auto DisplayName= \"{DisplayName}\"");
            Sc($"description {ServiceName} \"Performs Milestone XProtect™ export jobs on behalf of the Auto Exporter admin plugin.\"");
            Sc($"start {ServiceName}");
            return 0;
        }

        private static int ScUninstall()
        {
            Sc($"stop {ServiceName}");
            Sc($"delete {ServiceName}");
            return 0;
        }

        private static void Sc(string args)
        {
            var psi = new ProcessStartInfo("sc.exe", args) { UseShellExecute = false };
            using (var p = Process.Start(psi)) p.WaitForExit();
        }
    }
}
