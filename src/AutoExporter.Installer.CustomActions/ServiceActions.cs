using System;
using System.ServiceProcess;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace AutoExporter.Installer.CustomActions
{
    /// <summary>
    /// Deferred MSI custom action that drives a Windows service to a target state (Stopped or
    /// Started) within a timeout, returning failure if it does not get there. Used by the installer
    /// to cycle the Milestone Event Server around the plugin file copy. The CustomActionData is the
    /// SetProperty payload "ServiceName=..;DisplayName=..;Operation=Stop|Start;TimeoutSeconds=..".
    /// </summary>
    public static class ServiceActions
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        [CustomAction]
        public static ActionResult EnsureServiceState(Session session)
        {
            try
            {
                var serviceName = session.CustomActionData["ServiceName"];
                var displayName = session.CustomActionData["DisplayName"];
                var operation = session.CustomActionData["Operation"];
                var timeout = GetTimeout(session);

                using (var service = TryGetService(serviceName, displayName))
                {
                    if (service == null)
                    {
                        session.Log($"[AutoExporterCustomActions] Service '{displayName}' ({serviceName}) is not installed. Continuing.");
                        return ActionResult.Success;
                    }

                    session.Log($"[AutoExporterCustomActions] Ensuring service '{displayName}' ({serviceName}) reaches state '{operation}' within {timeout.TotalSeconds:0} seconds.");

                    switch (operation)
                    {
                        case "Stop":
                            return StopService(session, service, displayName, timeout);
                        case "Start":
                            return StartService(session, service, displayName, timeout);
                        default:
                            session.Log($"[AutoExporterCustomActions] Unknown service operation '{operation}'.");
                            return ActionResult.Failure;
                    }
                }
            }
            catch (Exception ex)
            {
                session.Log($"[AutoExporterCustomActions] Service action failed: {ex}");
                return ActionResult.Failure;
            }
        }

        private static ActionResult StopService(Session session, ServiceController service, string displayName, TimeSpan timeout)
        {
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                session.Log($"[AutoExporterCustomActions] Service '{displayName}' is already stopped.");
                return ActionResult.Success;
            }

            if (service.Status != ServiceControllerStatus.StopPending)
            {
                session.Log($"[AutoExporterCustomActions] Stopping service '{displayName}'.");
                service.Stop();
            }

            return WaitForStatus(session, service, displayName, ServiceControllerStatus.Stopped, timeout);
        }

        private static ActionResult StartService(Session session, ServiceController service, string displayName, TimeSpan timeout)
        {
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Running)
            {
                session.Log($"[AutoExporterCustomActions] Service '{displayName}' is already running.");
                return ActionResult.Success;
            }

            if (service.Status != ServiceControllerStatus.StartPending)
            {
                session.Log($"[AutoExporterCustomActions] Starting service '{displayName}'.");
                service.Start();
            }

            return WaitForStatus(session, service, displayName, ServiceControllerStatus.Running, timeout);
        }

        private static ActionResult WaitForStatus(Session session, ServiceController service, string displayName, ServiceControllerStatus targetStatus, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var nextStatusLog = DateTime.UtcNow;

            while (DateTime.UtcNow <= deadline)
            {
                service.Refresh();
                if (service.Status == targetStatus)
                {
                    session.Log($"[AutoExporterCustomActions] Service '{displayName}' reached state '{targetStatus}'.");
                    return ActionResult.Success;
                }

                if (DateTime.UtcNow >= nextStatusLog)
                {
                    session.Log($"[AutoExporterCustomActions] Service '{displayName}' current state: {service.Status}.");
                    nextStatusLog = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                }

                Thread.Sleep(PollInterval);
            }

            session.Log($"[AutoExporterCustomActions] Timed out waiting for '{displayName}' to reach state '{targetStatus}'.");
            return ActionResult.Failure;
        }

        private static ServiceController TryGetService(string serviceName, string displayName)
        {
            foreach (var service in ServiceController.GetServices())
            {
                if (!string.IsNullOrWhiteSpace(serviceName) &&
                    string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }

                if (!string.IsNullOrWhiteSpace(displayName) &&
                    string.Equals(service.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }

                service.Dispose();
            }

            return null;
        }

        private static TimeSpan GetTimeout(Session session)
        {
            if (int.TryParse(session.CustomActionData["TimeoutSeconds"], out var timeoutSeconds) && timeoutSeconds > 0)
            {
                return TimeSpan.FromSeconds(timeoutSeconds);
            }

            return DefaultTimeout;
        }
    }
}
