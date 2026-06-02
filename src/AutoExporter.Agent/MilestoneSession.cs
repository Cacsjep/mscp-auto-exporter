using System;
using System.Net;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using SdkEnvironment = VideoOS.Platform.SDK.Environment;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Owns the standalone Milestone SDK environment + login for the agent. Because this is a
    /// standalone process (not the Event Server "Service" environment), DBExporter/AVIExporter
    /// run here directly - no helper process needed.
    /// </summary>
    internal sealed class MilestoneSession : IDisposable
    {
        public MachineConfig Config { get; }
        public Uri ServerUri { get; private set; }
        public bool IsLoggedIn { get; private set; }

        private Action<TriggerRequest> _onRunJob;
        private bool _initialized;
        private ServerId _msgServerId;
        private object _runJobFilter;
        private object _queryFilter;
        private object _clearFilter;
        private object _pingFilter;

        // How many recent executions this agent returns to the admin Status view per query.
        private const int ReplyRecordCap = 200;

        public MilestoneSession(MachineConfig config) => Config = config;

        /// <summary>The management-server ServerId, available after <see cref="Login"/>.</summary>
        public ServerId ServerId => Configuration.Instance.ServerFQID.ServerId;

        public void Login()
        {
            ServerUri = NormalizeServerUri(Config.ServerUrl);
            // The OAuth token cache talks to the management server's IDP endpoint (server/idp).
            var idpUri = new Uri(ServerUri, "idp");
            // Only require HTTPS when the configured scheme is HTTPS (pre-2021R1 / plain HTTP -> false).
            bool secureOnly = string.Equals(ServerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

            // Log the exact endpoint so a "server not found" is debuggable: it shows the scheme and
            // port actually used (port defaults to 443 for https, 80 for http when none was given).
            Log.Info($"Connecting to management server {ServerUri.Scheme}://{ServerUri.Host}:{ServerUri.Port} " +
                     $"(from '{Config.ServerUrl}', secureOnly={secureOnly}).");

            // Init order mirrors the SDK samples: Environment -> UI -> Export, then add the OAuth
            // server + Login, then Media. Export must init before Login so the export pipeline has
            // the recorder hooks it needs.
            SdkEnvironment.Initialize();
            VideoOS.Platform.SDK.UI.Environment.Initialize();
            VideoOS.Platform.SDK.Export.Environment.Initialize();
            _initialized = true;

            // Modern (2021R1+) login: build an IDP token cache for the configured credentials and
            // add the server via AddServerOAuth. This is the path the official MIP SDK samples use.
            // The legacy AddServer(...) does not perform the IDP basic/windows token grant and the
            // token request fails with 401.
            var tokenCache = BuildTokenCache(idpUri, Config);
            SdkEnvironment.AddServerOAuth(secureOnly, ServerUri, tokenCache, false);

            // 6-arg login registers a real integration identity so Recording Servers trust the
            // session for media access.
            SdkEnvironment.Login(
                ServerUri,
                Ids.IntegrationId,
                Ids.IntegrationName,
                Ids.IntegrationVersion,
                Ids.IntegrationManufacturer,
                false);

            // Opens the connection to the Recording Servers the export pipeline pulls from.
            VideoOS.Platform.SDK.Media.Environment.Initialize();

            IsLoggedIn = true;
            Log.Info($"Logged into {ServerUri} as {Config.AuthMode}/{Config.Username} " +
                     $"(server '{Configuration.Instance.ServerName}').");
        }

        /// <summary>Build the IDP token cache for the configured auth mode (mirrors the SDK samples).</summary>
        private static VideoOS.Platform.OAuth.IMipTokenCache BuildTokenCache(Uri idpUri, MachineConfig c)
        {
            switch (c.AuthMode)
            {
                case AuthMode.Basic:
                    return new VideoOS.Platform.SDK.OAuth.MipTokenCache(
                        idpUri, new NetworkCredential(c.Username, c.Password), isBasicUser: true);

                case AuthMode.WindowsOtherUser:
                    SplitWindowsUser(c.Username, out var domain, out var user);
                    var nc = string.IsNullOrEmpty(domain)
                        ? new NetworkCredential(user, c.Password)
                        : new NetworkCredential(user, c.Password, domain);
                    return new VideoOS.Platform.SDK.OAuth.MipTokenCache(idpUri, nc, isBasicUser: false);

                default:
                    throw new InvalidOperationException("Unknown auth mode: " + c.AuthMode);
            }
        }

        // Accepts "DOMAIN\user" or "user@domain" or bare "user".
        private static void SplitWindowsUser(string raw, out string domain, out string user)
        {
            raw = (raw ?? "").Trim();
            int slash = raw.IndexOf('\\');
            if (slash > 0)
            {
                domain = raw.Substring(0, slash);
                user = raw.Substring(slash + 1);
                return;
            }
            int at = raw.IndexOf('@');
            if (at > 0)
            {
                user = raw.Substring(0, at);
                domain = raw.Substring(at + 1);
                return;
            }
            domain = "";
            user = raw;
        }

        private static Uri NormalizeServerUri(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) throw new InvalidOperationException("No server URL configured.");
            if (!raw.Contains("://")) raw = "https://" + raw;
            return new Uri(raw);
        }

        /// <summary>
        /// Register a handler for rule-triggered RunJob messages over MIP MessageCommunication.
        /// The Event Server bridge broadcasts on the management server's channel. We filter by
        /// message id here and by hostname in <see cref="OnRunJobMessage"/>.
        /// </summary>
        public void SubscribeRunJob(Action<TriggerRequest> handler)
        {
            _onRunJob = handler;
            _msgServerId = ServerId;

            MessageCommunicationManager.Start(_msgServerId);
            var mc = MessageCommunicationManager.Get(_msgServerId);
            _runJobFilter = mc.RegisterCommunicationFilter(
                OnRunJobMessage, new CommunicationIdFilter(Messages.RunJob));
            _queryFilter = mc.RegisterCommunicationFilter(
                OnQueryExecutions, new CommunicationIdFilter(Messages.QueryExecutions));
            _clearFilter = mc.RegisterCommunicationFilter(
                OnClearExecutions, new CommunicationIdFilter(Messages.ClearExecutions));
            _pingFilter = mc.RegisterCommunicationFilter(
                OnAgentPing, new CommunicationIdFilter(Messages.AgentPing));

            Log.Info("Subscribed to RunJob, QueryExecutions, ClearExecutions and AgentPing messages.");
        }

        // Supplies the current agent registration for the pong reply (set by AgentHost to the node's
        // live snapshot). The pong carries the full registration, not just the hostname, so the admin
        // Agents view reflects runtime changes (display name, max GB, used GB) without a config refresh.
        public Func<AgentRegistration> RegistrationProvider { get; set; }

        // The admin Agents view broadcasts AgentPing; every live agent answers with its current
        // registration so the view shows real-time status and fields (the cached config cannot be
        // trusted: its LastSeenUtc goes stale and field edits only appear after a manual refresh).
        private object OnAgentPing(Message message, FQID destination, FQID sender)
        {
            try
            {
                var reg = RegistrationProvider?.Invoke()
                          ?? new AgentRegistration { Hostname = System.Environment.MachineName, Status = "Online" };
                SendNotice(Messages.AgentPong, reg.Encode());
            }
            catch (Exception ex) { Log.Error("AgentPing handling failed: " + ex.Message); }
            return null;
        }

        // The admin Status view broadcasts QueryExecutions; every agent replies with its own
        // recent run history. The view aggregates the replies across all agents.
        private object OnQueryExecutions(Message message, FQID destination, FQID sender)
        {
            try
            {
                var recent = ExecutionStore.ReadRecent(ReplyRecordCap);
                if (recent.Count == 0) return null;
                SendNotice(Messages.ExecutionsReply, ExecutionCodec.EncodeList(recent));
            }
            catch (Exception ex)
            {
                Log.Error("QueryExecutions handling failed: " + ex.Message);
            }
            return null;
        }

        // The admin Executions view broadcasts ClearExecutions; every agent wipes its own history.
        private object OnClearExecutions(Message message, FQID destination, FQID sender)
        {
            try
            {
                ExecutionStore.Clear();
                Log.Info("Execution history cleared on admin request.");
            }
            catch (Exception ex)
            {
                Log.Error("ClearExecutions handling failed: " + ex.Message);
            }
            return null;
        }

        /// <summary>Transmit a string-payload message on the management server channel (broadcast).</summary>
        public void SendNotice(string messageId, string payload)
        {
            if (_msgServerId == null) return;
            try
            {
                var mc = MessageCommunicationManager.Get(_msgServerId);
                mc?.TransmitMessage(new Message(messageId, payload), null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error($"SendNotice '{messageId}' failed: {ex.Message}");
            }
        }

        private object OnRunJobMessage(Message message, FQID destination, FQID sender)
        {
            try
            {
                var req = TriggerRequest.Decode(message?.Data as string);
                if (req == null) return null;

                // Only act on triggers addressed to this machine.
                var me = System.Environment.MachineName;
                if (!string.Equals(req.AgentHostname, me, StringComparison.OrdinalIgnoreCase))
                    return null;

                _onRunJob?.Invoke(req);
            }
            catch (Exception ex)
            {
                Log.Error("RunJob message handling failed: " + ex.Message);
            }
            return null;
        }

        public void Dispose()
        {
            if (!_initialized) return;

            if (_msgServerId != null)
            {
                try
                {
                    var mc = MessageCommunicationManager.Get(_msgServerId);
                    if (_runJobFilter != null) mc?.UnRegisterCommunicationFilter(_runJobFilter);
                    if (_queryFilter != null) mc?.UnRegisterCommunicationFilter(_queryFilter);
                    if (_clearFilter != null) mc?.UnRegisterCommunicationFilter(_clearFilter);
                    if (_pingFilter != null) mc?.UnRegisterCommunicationFilter(_pingFilter);
                }
                catch { }
                try { MessageCommunicationManager.Stop(_msgServerId); } catch { }
            }

            try { SdkEnvironment.Logout(); } catch { }
            try { SdkEnvironment.RemoveAllServers(); } catch { }
            try { SdkEnvironment.UnInitialize(); } catch { }
            IsLoggedIn = false;
        }
    }
}
