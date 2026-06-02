using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using VideoOS.Platform;
using VideoOS.Platform.ConfigurationItems;

namespace AutoExporter.Agent
{
    /// <summary>
    /// After login the service checks that every recording server is actually reachable from this
    /// machine. A common field problem is a recorder that the management server knows about but
    /// that this host cannot resolve or reach (bad DNS, missing hosts entry, firewall). The export
    /// would then fail for that recorder's cameras with a confusing error, so we surface it up front.
    ///
    /// The probe is a plain TCP connect to each recorder's web service host and port. It does not
    /// log in or read media, so it is cheap and safe to run on every successful login.
    /// </summary>
    internal static class RecorderProbe
    {
        // Default Recording Server web service port, used when the recorder URI has none.
        private const int DefaultRecorderPort = 7563;
        private const int ConnectTimeoutMs = 3000;

        /// <summary>
        /// Returns a human-readable warning (one unreachable recorder per line) or an empty string
        /// when every enabled recorder answered. Never throws.
        /// </summary>
        public static string Check()
        {
            // First gather the targets (this reads recorder config, which can be a few network
            // round-trips), keeping any that cannot even be described as an immediate warning.
            var targets = new List<Target>();
            var prebaked = new List<string>();
            try
            {
                var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);
                foreach (var rs in management.RecordingServerFolder.RecordingServers)
                {
                    string name = "(recording server)";
                    try
                    {
                        name = string.IsNullOrEmpty(rs.Name) ? name : rs.Name;
                        if (!IsEnabled(rs)) continue;

                        ResolveHostPort(rs, out var host, out var port);
                        if (string.IsNullOrEmpty(host))
                            prebaked.Add($"{name}: no address configured.");
                        else
                            targets.Add(new Target { Name = name, Host = host, Port = port });
                    }
                    catch (Exception ex)
                    {
                        prebaked.Add($"{name}: could not be checked ({ex.Message}).");
                    }
                }
            }
            catch (Exception ex)
            {
                // Enumeration itself failed (rare). Report it rather than silently passing.
                Log.Error("Recorder probe could not list recording servers: " + ex.Message);
                return "";
            }

            // Probe the reachable-looking targets concurrently so the total time is bounded by the
            // connect timeout, not the number of recorders (a system can have many). Each task
            // returns its own warning (or null), so there is no shared state to race on. The target
            // is kept beside its task so a faulted or timed-out probe can still be named.
            var probes = targets.Select(t => new
            {
                Target = t,
                Task = Task.Run(() => CanConnect(t.Host, t.Port, out var reason)
                    ? null
                    : $"{t.Name} ({t.Host}:{t.Port}) is not reachable: {reason}")
            }).ToList();
            try
            {
                // Generous overall cap so a stuck DNS resolver cannot hang the service forever.
                Task.WaitAll(probes.Select(p => p.Task).ToArray(), ConnectTimeoutMs + 5000);
            }
            catch (Exception ex)
            {
                Log.Error("Recorder probe connect phase failed: " + ex.Message);
            }

            var unreachable = new List<string>(prebaked);
            foreach (var p in probes)
            {
                if (p.Task.Status == TaskStatus.RanToCompletion)
                {
                    if (p.Task.Result != null) unreachable.Add(p.Task.Result);
                }
                else
                {
                    // Faulted or still running past the cap: we could not confirm reachability, so
                    // warn rather than silently treating the recorder as reachable.
                    unreachable.Add($"{p.Target.Name} ({p.Target.Host}:{p.Target.Port}) could not be verified (the reachability check did not complete).");
                }
            }

            if (unreachable.Count == 0)
            {
                Log.Info("Recorder probe: all recording servers reachable.");
                return "";
            }

            var sb = new StringBuilder();
            foreach (var line in unreachable) sb.AppendLine(line);
            var text = sb.ToString().TrimEnd();
            Log.Error("Recorder probe found unreachable recording servers:" + System.Environment.NewLine + text);
            return text;
        }

        private struct Target
        {
            public string Name;
            public string Host;
            public int Port;
        }

        private static bool IsEnabled(RecordingServer rs)
        {
            try { return rs.Enabled; } catch { return true; }
        }

        // Prefer the recorder's web service URI; fall back to its host name with the default port.
        private static void ResolveHostPort(RecordingServer rs, out string host, out int port)
        {
            host = "";
            port = DefaultRecorderPort;

            string uri = null;
            try { uri = rs.WebServerUri; } catch { }
            if (!string.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var u))
            {
                host = u.Host;
                if (u.Port > 0) port = u.Port;
                return;
            }

            // No usable URI: fall back to the configured host name and recorder port.
            try { host = rs.HostName; } catch { }
            try { if (rs.PortNumber > 0) port = rs.PortNumber; } catch { }
        }

        private static bool CanConnect(string host, int port, out string reason)
        {
            reason = "";
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.BeginConnect(host, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                    {
                        reason = "connection timed out";
                        return false;
                    }
                    client.EndConnect(connect);
                    return true;
                }
            }
            catch (SocketException ex)
            {
                reason = ex.SocketErrorCode == SocketError.HostNotFound
                    ? "host name could not be resolved (check DNS or the hosts file)"
                    : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
