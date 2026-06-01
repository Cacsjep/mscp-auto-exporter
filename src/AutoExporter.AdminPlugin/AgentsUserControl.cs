using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using MipMessage = VideoOS.Platform.Messaging.Message;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Agents section: a read-only list of the agents that running services have self-registered
    /// (kind <see cref="Ids.AgentKindId"/>), read from the Configuration API. The list refreshes
    /// itself on a timer (no Refresh button). An agent that has gone offline (its service was
    /// stopped or uninstalled) can be removed here, which also deletes the jobs assigned to it.
    /// </summary>
    internal sealed class AgentsUserControl : UserControl
    {
        // An agent is considered offline once it has missed several heartbeats (the agent beats
        // every 30s) or has explicitly marked itself Offline on shutdown.
        private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(90);
        private const int RefreshIntervalMs = 3000;

        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false,
        };
        private readonly Button _remove = new Button { Text = "Remove agent", Width = 110, Enabled = false };
        private readonly Timer _timer = new Timer { Interval = RefreshIntervalMs };

        public AgentsUserControl()
        {
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Window;

            _list.Columns.Add("Name", 140);
            _list.Columns.Add("Hostname", 120);
            _list.Columns.Add("Status", 70);
            _list.Columns.Add("Version", 60);
            _list.Columns.Add("Max GB", 60);
            _list.Columns.Add("Max days", 65);
            _list.Columns.Add("Last seen", 140);
            _list.Columns.Add("Export folder", 240);
            _list.SelectedIndexChanged += (_, __) => UpdateButtons();

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
            _remove.Click += (_, __) => RemoveSelected();
            top.Controls.Add(_remove);

            Controls.Add(_list);
            Controls.Add(top);

            _timer.Tick += (_, __) => Reload();
            HandleCreated += (_, __) => { Reload(); _timer.Start(); };
            HandleDestroyed += (_, __) => _timer.Stop();
        }

        public void Reload()
        {
            // Keep the current selection across the periodic refresh so the operator does not lose
            // the row they were about to act on.
            var selectedHost = (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is AgentRegistration sel)
                ? sel.Hostname : null;

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var reg in ReadAgents())
            {
                bool offline = IsOffline(reg);
                var item = new ListViewItem(reg.FriendlyName) { Tag = reg };
                item.SubItems.Add(reg.Hostname);
                item.SubItems.Add(offline ? "Offline" : "Online");
                item.SubItems.Add(string.IsNullOrEmpty(reg.Version) ? "-" : reg.Version);
                item.SubItems.Add(reg.MaxGB > 0 ? reg.MaxGB.ToString() : "Unlimited");
                item.SubItems.Add(reg.RetentionDays > 0 ? reg.RetentionDays.ToString() : "Unlimited");
                item.SubItems.Add(reg.LastSeenUtc == DateTime.MinValue ? "(never)" : reg.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(reg.ExportFolder ?? "");
                item.ForeColor = offline ? Color.Firebrick : Color.ForestGreen;
                if (reg.Hostname == selectedHost) item.Selected = true;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            UpdateButtons();
        }

        // Offline when the agent said so, or when no heartbeat has arrived for a while.
        private static bool IsOffline(AgentRegistration reg)
        {
            if (string.Equals(reg.Status, "Offline", StringComparison.OrdinalIgnoreCase)) return true;
            if (reg.LastSeenUtc == DateTime.MinValue) return true;
            return DateTime.UtcNow - reg.LastSeenUtc > OfflineAfter;
        }

        // Removal is only offered for an offline agent. A live agent would just re-register itself.
        private void UpdateButtons()
        {
            _remove.Enabled = _list.SelectedItems.Count > 0
                              && _list.SelectedItems[0].Tag is AgentRegistration reg
                              && IsOffline(reg);
        }

        private void RemoveSelected()
        {
            if (_list.SelectedItems.Count == 0 || !(_list.SelectedItems[0].Tag is AgentRegistration reg)) return;
            if (!IsOffline(reg)) return;

            var jobs = JobsForAgent(reg.Hostname);
            var prompt = jobs.Count == 0
                ? $"Remove offline agent '{reg.FriendlyName}'?"
                : $"Remove offline agent '{reg.FriendlyName}' and the {jobs.Count} job(s) assigned to it?\r\n\r\n" +
                  string.Join("\r\n", jobs.Select(j => "  - " + (string.IsNullOrEmpty(j.Name) ? "(unnamed)" : j.Name)));

            if (MessageBox.Show(this, prompt, "Remove agent",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string directError = null;
            try
            {
                // Delete the assigned jobs first so we do not leave jobs pointing at a missing agent.
                // Jobs are a registered tree node kind, so the admin client can delete them directly.
                foreach (var job in jobs)
                    Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, job);

                // The agent item is not a registered tree node, so the management server may reject
                // an admin-side delete (the same Bad Request that blocks renaming). Try it anyway.
                var objectId = AgentRegistration.ObjectIdFor(reg.Hostname);
                var agentItem = Configuration.Instance.GetItemConfiguration(Ids.PluginId, Ids.AgentKindId, objectId);
                if (agentItem != null)
                    Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, agentItem);
            }
            catch (Exception ex)
            {
                directError = ex.Message;
                PluginFileLog.Error("Direct agent removal failed, falling back to the Event Server bridge: " + ex.Message);
            }

            // If the direct delete was rejected, ask the Event Server bridge to remove it server-side
            // (where the write is accepted). The list updates on its own once the bridge is done.
            if (directError != null)
            {
                if (TryRequestServerSideRemoval(reg.Hostname))
                    MessageBox.Show(this,
                        "The agent could not be removed directly, so the Event Server was asked to remove it. The list will update shortly.",
                        "Remove agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, "Could not remove the agent: " + directError,
                        "Remove agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Reload();
        }

        // Broadcast a RemoveAgent request to the Event Server bridge.
        private static bool TryRequestServerSideRemoval(string hostname)
        {
            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(serverId);
                var mc = MessageCommunicationManager.Get(serverId);
                mc.TransmitMessage(new MipMessage(Messages.RemoveAgent, hostname), null, null, null);
                return true;
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("RemoveAgent message send failed: " + ex.Message);
                return false;
            }
        }

        // The job items (kind JobKindId) whose AgentHostname matches this agent.
        private static List<Item> JobsForAgent(string hostname)
        {
            var result = new List<Item>();
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, Ids.JobKindId);
                if (items != null)
                    foreach (var it in items)
                    {
                        if (it?.Properties == null) continue;
                        var job = JobConfig.FromProperties(it.Properties);
                        if (string.Equals(job.AgentHostname, hostname, StringComparison.OrdinalIgnoreCase))
                            result.Add(it);
                    }
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("JobsForAgent failed: " + ex.Message);
            }
            return result;
        }

        internal static List<AgentRegistration> ReadAgents()
        {
            var result = new List<AgentRegistration>();
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, Ids.AgentKindId);
                if (items != null)
                    foreach (var it in items)
                        if (it?.Properties != null) result.Add(AgentRegistration.FromProperties(it.Properties));
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("ReadAgents failed: " + ex.Message);
            }
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
