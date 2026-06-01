using System;
using System.ServiceProcess;

namespace AutoExporter.Tray.Services
{
    /// <summary>Start / stop / restart + status for the Auto Exporter Agent Windows service.</summary>
    public static class ServiceControl
    {
        public const string ServiceName = "AutoExporterAgent";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static string Status()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                return sc.Status.ToString();
            }
            catch { return "NotInstalled"; }
        }

        public static void Start()
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, Timeout);
            }
        }

        public static void Stop()
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
            }
        }

        public static void Restart()
        {
            Stop();
            Start();
        }
    }
}
