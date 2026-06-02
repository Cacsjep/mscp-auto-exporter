using System;
using System.IO;
using AutoExporter.Contracts;
using VideoOS.Platform;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Publishes this machine's Agent node into Milestone configuration so it appears under
    /// Auto Exporter -> Agents in the Management Client, and keeps its status fresh. Uses the in-proc
    /// Configuration API (the agent is already logged into the SDK environment).
    ///
    /// The item's ObjectId is derived deterministically from the hostname, so the service always
    /// writes to the same node - no duplicates across restarts.
    /// </summary>
    internal sealed class AgentNode
    {
        private readonly MilestoneSession _session;
        private readonly AgentRegistration _reg;
        private readonly Guid _objectId;

        // Walking the whole export tree on every 30s heartbeat would be wasteful, so the used-size
        // scan is throttled. The cached value is republished on every heartbeat in between.
        private const int UsageScanIntervalMs = 120000;
        private int _lastUsageTick = unchecked(System.Environment.TickCount - UsageScanIntervalMs);

        public AgentNode(MilestoneSession session)
        {
            _session = session;
            _reg = new AgentRegistration
            {
                Hostname = System.Environment.MachineName,
                Version = Ids.IntegrationVersion,
                Status = "Online",
                LastSeenUtc = DateTime.UtcNow,
                ExportFolder = session.Config.ExportFolder,
                MaxGB = session.Config.MaxGB,
                RetentionDays = session.Config.RetentionDays,
                DisplayName = session.Config.DisplayName,
            };
            _objectId = AgentRegistration.ObjectIdFor(_reg.Hostname);
        }

        public void Register()
        {
            _reg.LastSeenUtc = DateTime.UtcNow;
            _reg.Status = "Online";
            RefreshUsage(force: true);
            Upsert("Online");
            Log.Info($"Registered agent node '{_reg.Hostname}' (ObjectId {_objectId}).");
        }

        public void Heartbeat()
        {
            _reg.LastSeenUtc = DateTime.UtcNow;
            _reg.Status = "Online";
            RefreshUsage(force: false);
            Upsert("Online");
        }

        // Recompute the export folder's total size, at most once per UsageScanIntervalMs. The
        // export folder is fixed for the life of the session (a config change restarts the service),
        // so the path never changes under us.
        private void RefreshUsage(bool force)
        {
            if (!force && unchecked(System.Environment.TickCount - _lastUsageTick) < UsageScanIntervalMs)
                return;
            _lastUsageTick = System.Environment.TickCount;
            _reg.UsedBytes = DirectorySize(_session.Config.ExportFolder);
        }

        private static long DirectorySize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public void MarkOffline()
        {
            _reg.LastSeenUtc = DateTime.UtcNow;
            _reg.Status = "Offline";
            Upsert("Offline");
            Log.Info("Agent node marked Offline.");
        }

        /// <summary>Create or update the agent configuration item with current registration state.</summary>
        private void Upsert(string status)
        {
            _reg.Status = status;

            // DisplayName is agent-owned: it comes from the local config (set in the tray), the same
            // way MaxGB / RetentionDays do. The admin cannot write the agent node, so the service is
            // the only writer and re-asserts the configured name on every heartbeat.
            _reg.DisplayName = _session.Config.DisplayName;

            var serverId = _session.ServerId;
            var fqid = new FQID(serverId, Guid.Empty, _objectId, FolderType.No, Ids.AgentKindId);

            var item = new Item(fqid, "Agent " + _reg.Hostname);
            foreach (var kv in _reg.ToProperties())
                item.Properties[kv.Key] = kv.Value;

            // SaveItemConfiguration upserts by ObjectId, so create + save is idempotent.
            Configuration.Instance.SaveItemConfiguration(Ids.PluginId, item);
        }
    }
}
