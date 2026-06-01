using System;
using AutoExporter.Contracts;
using VideoOS.Platform;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Publishes this machine's Agent node into Milestone configuration so it appears under
    /// Exporter -> Agents in the Management Client, and keeps its status fresh. Uses the in-proc
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

        public AgentNode(MilestoneSession session)
        {
            _session = session;
            _reg = new AgentRegistration
            {
                Hostname = System.Environment.MachineName,
                Version = "1.0",
                Status = "Online",
                LastSeenUtc = DateTime.UtcNow,
                ExportFolder = session.Config.ExportFolder,
                MaxGB = session.Config.MaxGB,
                RetentionDays = session.Config.RetentionDays,
            };
            _objectId = AgentRegistration.ObjectIdFor(_reg.Hostname);
        }

        public void Register()
        {
            _reg.LastSeenUtc = DateTime.UtcNow;
            _reg.Status = "Online";
            Upsert("Online");
            Log.Info($"Registered agent node '{_reg.Hostname}' (ObjectId {_objectId}).");
        }

        public void Heartbeat()
        {
            _reg.LastSeenUtc = DateTime.UtcNow;
            _reg.Status = "Online";
            Upsert("Online");
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

            // DisplayName is admin-owned (set in the Management Client). Preserve whatever is
            // already stored so our heartbeat does not clobber a friendly name.
            try
            {
                var existing = Configuration.Instance.GetItemConfiguration(Ids.PluginId, Ids.AgentKindId, _objectId);
                if (existing?.Properties != null)
                    _reg.DisplayName = AgentRegistration.FromProperties(existing.Properties).DisplayName;
            }
            catch { }

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
