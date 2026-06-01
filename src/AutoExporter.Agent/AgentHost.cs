using System;
using System.Collections.Concurrent;
using System.Security.Principal;
using System.Threading;
using AutoExporter.Contracts;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Owns the agent lifecycle on a single dedicated STA thread that pumps the Win32 message
    /// loop. All SDK work (login, self-registration, heartbeat, exports) runs on that thread,
    /// because the export pipeline and recorder-status callbacks require a pumped message loop.
    /// Rule triggers arrive on an SDK callback thread and are queued for the MIP thread to drain.
    /// </summary>
    internal sealed class AgentHost
    {
        private readonly ManualResetEventSlim _shutdown = new ManualResetEventSlim(false);
        private readonly ConcurrentQueue<TriggerRequest> _jobs = new ConcurrentQueue<TriggerRequest>();
        private Thread _mip;
        private MilestoneSession _session;
        private AgentNode _node;

        private const int HeartbeatMs = 30000;

        public void Start()
        {
            _mip = new Thread(MipMain) { IsBackground = true, Name = "AutoExporter.Mip" };
            _mip.SetApartmentState(ApartmentState.STA);
            _mip.Start();
        }

        public void Stop()
        {
            Log.Info("AgentHost stopping.");
            _shutdown.Set();
            try { _mip?.Join(TimeSpan.FromSeconds(15)); } catch { }
        }

        public void WaitForShutdown()
        {
            try { _mip?.Join(); } catch { }
        }

        private void MipMain()
        {
            Log.Info("MIP thread starting.");
            RuntimeArming.Arm();

            var identity = CurrentIdentity();
            Log.Configure(MachineConfig.Load().LogLevel);
            Log.Info("Service identity: " + identity);

            // Connect with retry: a transient failure (server still starting, brief network
            // hiccup, "did not respond") must not park the service forever. We reload the config
            // each attempt so a credential change picked up by the tray takes effect, and write the
            // current result to agent.state so the tray footer always reflects reality.
            const int RetryDelayMs = 20000;
            while (!_shutdown.IsSet)
            {
                var cfg = MachineConfig.Load();
                Log.Configure(cfg.LogLevel);

                if (string.IsNullOrWhiteSpace(cfg.ServerUrl))
                {
                    WriteState(false, identity, cfg, "No Milestone server configured yet. Open the tray app and connect.");
                    if (_shutdown.Wait(RetryDelayMs)) return;
                    continue;
                }

                try
                {
                    _session = new MilestoneSession(cfg);
                    _session.Login();

                    _node = new AgentNode(_session);
                    _node.Register();

                    _session.SubscribeRunJob(req => _jobs.Enqueue(req));

                    // Now that we are logged in, check that every recording server is reachable
                    // from this host so the operator learns about DNS / hosts / firewall problems
                    // up front rather than as a confusing export failure later.
                    var recorderWarnings = RecorderProbe.Check();
                    WriteState(true, identity, cfg, "", recorderWarnings);
                    break;  // connected
                }
                catch (Exception ex)
                {
                    Log.Error("Agent login failed (will retry): " + ex);
                    WriteState(false, identity, cfg, Humanize(ex, cfg, identity));
                    try { _session?.Dispose(); } catch { }
                    _session = null;
                    if (_shutdown.Wait(RetryDelayMs)) return;   // wait, then retry
                }
            }
            if (_shutdown.IsSet) return;

            Log.Info("Agent started.");
            int lastBeat = Environment.TickCount;

            try
            {
                while (!_shutdown.IsSet)
                {
                    while (_jobs.TryDequeue(out var req))
                        RunJobSafe(req);

                    if (unchecked(Environment.TickCount - lastBeat) >= HeartbeatMs)
                    {
                        SafeHeartbeat();
                        lastBeat = Environment.TickCount;
                    }

                    Pump(250);
                }
            }
            finally
            {
                try { _node?.MarkOffline(); } catch { }
                try { _session?.Dispose(); } catch { }
                Log.Info("MIP thread stopped.");
            }
        }

        private void RunJobSafe(TriggerRequest req)
        {
            try { new JobRunner(_session).Run(req); }
            catch (Exception ex) { Log.Error("RunJob failed: " + ex); }
        }

        private void SafeHeartbeat()
        {
            try { _node?.Heartbeat(); }
            catch (Exception ex) { Log.Error("Heartbeat failed: " + ex.Message); }
        }

        private static string CurrentIdentity()
        {
            try { return WindowsIdentity.GetCurrent()?.Name ?? "(unknown)"; }
            catch { return "(unknown)"; }
        }

        private static void WriteState(bool loggedIn, string identity, MachineConfig cfg, string error, string recorderWarnings = "")
        {
            new AgentState
            {
                LoggedIn = loggedIn,
                Identity = identity,
                ServerUrl = cfg?.ServerUrl ?? "",
                AuthMode = cfg?.AuthMode.ToString() ?? "",
                LoginUser = cfg?.Username ?? "",
                LastError = error ?? "",
                RecorderWarnings = recorderWarnings ?? "",
                UpdatedUtc = DateTime.UtcNow,
            }.Save();
        }

        // Turn an SDK login exception into a one-line reason the tray can show the operator.
        private static string Humanize(Exception ex, MachineConfig cfg, string identity)
        {
            var msg = ex?.ToString() ?? "";
            bool Has(string n) => msg.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Has("VMO61008") || Has("NotAuthorized") || Has("sufficient permissions"))
                return "Login refused: the configured account has no permissions on this server. " +
                       "Add the account to a Role in Management Client.";
            if (Has("ServerNotFound") || Has("No such host") || Has("could not be resolved"))
                return "Server not found. Check the server address and that it is reachable.";
            if (Has("invalid_grant") || Has("InvalidCredentials") || Has("IDP 401") || Has("401"))
                return "Username or password is incorrect.";
            if (Has("Timeout") || Has("timed out"))
                return "The management server did not respond. Check the URL and that it is running.";

            var line = (ex?.Message ?? "").Split('\n')[0].Trim();
            return line.Length == 0 ? "Login failed (see agent.log)." : line;
        }

        // Pump the Win32 message queue so SDK callbacks keep flowing while idle between jobs.
        private static void Pump(int ms)
        {
            int start = Environment.TickCount;
            do
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(25);
            }
            while (unchecked(Environment.TickCount - start) < ms);
        }
    }
}
