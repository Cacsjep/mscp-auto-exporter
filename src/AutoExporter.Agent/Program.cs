using System;
using System.Diagnostics;
using System.Reflection;
using System.ServiceProcess;

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
                    default:
                        Console.Error.WriteLine($"Unknown arg '{args[0]}'. Use --install | --uninstall | --console.");
                        return 1;
                }
            }

            ServiceBase.Run(new AgentService());
            return 0;
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
            Sc($"description {ServiceName} \"Performs Milestone XProtect export jobs on behalf of the Auto Exporter admin plugin.\"");
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
