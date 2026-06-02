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
        private const int RetryDelayMs = 20000;
        // Consecutive failed heartbeats (each is a real round-trip to the server) before we treat
        // the connection as lost and reconnect. 3 x 30s tolerates a brief hiccup but recovers from a
        // management server restart or the server going offline.
        private const int HeartbeatFailuresBeforeReconnect = 3;

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
            Log.Info(Diagnostics.Banner("Auto Exporter Agent", typeof(Program).Assembly));
            Log.Info("Service identity: " + identity);

            // Lifecycle loop: connect (with retry), run until the connection drops or we are told to
            // stop, then tear down and reconnect. This survives a Milestone restart, the management
            // server going offline, and a slow boot where Milestone takes minutes to come up.
            while (!_shutdown.IsSet)
            {
                if (!ConnectWithRetry(identity)) break;   // returns false only on shutdown
                if (_shutdown.IsSet) break;

                Log.Info("Agent connected.");
                bool connectionLost = RunLoop();
                TeardownSession(tryMarkOffline: !connectionLost);   // offline write would just block on a dead server

                if (!connectionLost) break;   // clean shutdown
                Log.Info("Connection to Milestone lost. Reconnecting.");
                WriteState(false, identity, MachineConfig.Load(), "Connection to Milestone lost, reconnecting...");
            }

            Log.Info("MIP thread stopped.");
        }

        // Connect with retry: a transient failure (server still starting, brief network hiccup,
        // "did not respond") must not park the service forever. We reload the config each attempt so
        // a credential change picked up by the tray takes effect, and write the current result to
        // agent.state so the tray footer always reflects reality. Returns false only on shutdown.
        private bool ConnectWithRetry(string identity)
        {
            while (!_shutdown.IsSet)
            {
                var cfg = MachineConfig.Load();

                if (string.IsNullOrWhiteSpace(cfg.ServerUrl))
                {
                    WriteState(false, identity, cfg, "No Milestone server configured yet. Open the tray app and connect.");
                    if (_shutdown.Wait(RetryDelayMs)) return false;
                    continue;
                }

                try
                {
                    _session = new MilestoneSession(cfg);
                    _session.Login();

                    _node = new AgentNode(_session);
                    _node.Register();

                    // The pong reply carries the node's live registration so the admin Agents view
                    // shows fresh fields (name, max GB, used GB) without a config cache refresh.
                    _session.RegistrationProvider = () => _node?.Snapshot();
                    _session.SubscribeRunJob(req => _jobs.Enqueue(req));

                    // Now that we are logged in, check that every recording server is reachable
                    // from this host so the operator learns about DNS / hosts / firewall problems
                    // up front rather than as a confusing export failure later.
                    var recorderWarnings = RecorderProbe.Check();
                    WriteState(true, identity, cfg, "", recorderWarnings);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Agent login failed (will retry): " + ex);
                    var reason = Humanize(ex, cfg, identity);

                    // The SDK often reports a TLS trust problem as a generic ServerNotFound. If the
                    // server is https, classify the certificate so the operator gets the real reason
                    // (untrusted root CA, or a name mismatch from connecting by IP) instead.
                    try
                    {
                        var tls = TlsProbe.Classify(_session?.ServerUri);
                        if (!string.IsNullOrEmpty(tls))
                        {
                            Log.Info("TLS probe: " + tls);
                            if (!tls.StartsWith("TLS OK")) reason = tls;
                        }
                    }
                    catch (Exception probeEx) { Log.Error("TLS probe failed: " + probeEx.Message); }

                    WriteState(false, identity, cfg, reason);
                    TeardownSession(tryMarkOffline: false);
                    if (_shutdown.Wait(RetryDelayMs)) return false;   // wait, then retry
                }
            }
            return false;
        }

        // Main work loop. Returns true if it exited because the connection was lost (so the caller
        // reconnects), false if shutdown was requested.
        private bool RunLoop()
        {
            int lastBeat = Environment.TickCount;
            int beatFailures = 0;

            while (!_shutdown.IsSet)
            {
                while (_jobs.TryDequeue(out var req))
                    RunJobSafe(req);

                if (unchecked(Environment.TickCount - lastBeat) >= HeartbeatMs)
                {
                    lastBeat = Environment.TickCount;
                    if (SafeHeartbeat())
                    {
                        beatFailures = 0;
                    }
                    else if (++beatFailures >= HeartbeatFailuresBeforeReconnect)
                    {
                        Log.Error($"Heartbeat failed {beatFailures} times in a row, treating the connection as lost.");
                        return true;   // connection lost -> reconnect
                    }
                }

                Pump(250);
            }
            return false;   // shutdown
        }

        private void TeardownSession(bool tryMarkOffline)
        {
            if (tryMarkOffline) { try { _node?.MarkOffline(); } catch { } }
            try { _session?.Dispose(); } catch { }
            _node = null;
            _session = null;
        }

        private void RunJobSafe(TriggerRequest req)
        {
            try { new JobRunner(_session, () => _shutdown.IsSet).Run(req); }
            catch (Exception ex) { Log.Error("RunJob failed: " + ex); }
        }

        // Returns true if the heartbeat round-trip to the server succeeded.
        private bool SafeHeartbeat()
        {
            try { _node?.Heartbeat(); return true; }
            catch (Exception ex) { Log.Error("Heartbeat failed: " + ex.Message); return false; }
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
                return $"Server not found at '{cfg?.ServerUrl}'. Check the address and port, that this machine " +
                       "can reach the management server, and the scheme (enter http:// if the server is not on HTTPS).";
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
