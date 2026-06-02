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
    /// Jobs tab: list, add, edit and delete export jobs. Jobs are persisted as MIP configuration
    /// items (kind <see cref="Ids.JobKindId"/>) through the Configuration API, scoped to an agent
    /// by the job's AgentHostname property (not by tree parent).
    /// </summary>
    internal sealed class JobsUserControl : UserControl
    {
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
        private readonly Timer _timer = new Timer { Interval = RefreshIntervalMs };

        public JobsUserControl()
        {
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Window;

            _list.Columns.Add("Name", 150);
            _list.Columns.Add("Enabled", 55);
            _list.Columns.Add("Agent", 100);
            _list.Columns.Add("Format", 60);
            _list.Columns.Add("Range", 95);
            _list.Columns.Add("Cameras", 60);
            _list.Columns.Add("Last run", 130);
            _list.Columns.Add("Last status", 90);
            _list.DoubleClick += (_, __) => EditSelected();

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6) };
            var add = new Button { Text = "Add job...", Width = 90 };
            var edit = new Button { Text = "Edit", Width = 70 };
            var del = new Button { Text = "Delete", Width = 70 };
            var run = new Button { Text = "Run now", Width = 80 };
            add.Click += (_, __) => AddJob();
            edit.Click += (_, __) => EditSelected();
            del.Click += (_, __) => DeleteSelected();
            run.Click += (_, __) => RunSelected();
            top.Controls.Add(add);
            top.Controls.Add(edit);
            top.Controls.Add(del);
            top.Controls.Add(run);

            Controls.Add(_list);
            Controls.Add(top);

            _timer.Tick += (_, __) => Reload();
            HandleCreated += (_, __) => { Reload(); _timer.Start(); };
            HandleDestroyed += (_, __) => _timer.Stop();
        }

        /// <summary>Raised when the user clicks Run now, with a Pending record so the Executions
        /// view can show the run immediately (before the agent has picked it up).</summary>
        public event Action<ExecutionRecord> RunRequested;

        // Latest execution per job (JobObjectId -> record), fed from the executions view.
        private readonly Dictionary<Guid, ExecutionRecord> _latestByJob = new Dictionary<Guid, ExecutionRecord>();

        /// <summary>Update the Last run / Last status columns from the current run history.</summary>
        public void SetExecutions(List<ExecutionRecord> records)
        {
            _latestByJob.Clear();
            if (records != null)
                foreach (var r in records)
                {
                    if (r.JobObjectId == Guid.Empty) continue;
                    if (!_latestByJob.TryGetValue(r.JobObjectId, out var cur) || r.StartedUtc > cur.StartedUtc)
                        _latestByJob[r.JobObjectId] = r;
                }

            foreach (ListViewItem row in _list.Items)
            {
                if (!(row.Tag is Item it)) continue;
                if (_latestByJob.TryGetValue(it.FQID.ObjectId, out var rec))
                {
                    row.SubItems[6].Text = rec.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    row.SubItems[7].Text = rec.Outcome ?? (rec.Success ? "Success" : "Failed");
                }
            }
        }

        public void Reload()
        {
            // Preserve the current selection across the periodic refresh.
            var selectedId = (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is Item sel)
                ? sel.FQID.ObjectId : Guid.Empty;

            _list.BeginUpdate();
            _list.Items.Clear();
            try
            {
                var friendly = AgentsUserControl.FriendlyNames();
                var items = Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, Ids.JobKindId) ?? new List<Item>();
                foreach (var it in items)
                {
                    if (it?.Properties == null) continue;
                    var job = JobConfig.FromProperties(it.Properties);
                    var row = new ListViewItem(string.IsNullOrEmpty(it.Name) ? job.Name : it.Name) { Tag = it };
                    row.SubItems.Add(job.Enabled ? "Yes" : "No");
                    row.SubItems.Add(string.IsNullOrEmpty(job.AgentHostname)
                        ? "(none)" : AgentsUserControl.FriendlyName(job.AgentHostname, friendly));
                    row.SubItems.Add(job.Format);
                    row.SubItems.Add($"Last {job.RangeValue} {job.RangeUnit}");
                    row.SubItems.Add(job.Targets.Count.ToString());
                    string lastRun = "-", lastStatus = "-";
                    if (_latestByJob.TryGetValue(it.FQID.ObjectId, out var rec))
                    {
                        lastRun = rec.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                        lastStatus = rec.Outcome ?? (rec.Success ? "Success" : "Failed");
                    }
                    row.SubItems.Add(lastRun);
                    row.SubItems.Add(lastStatus);
                    if (it.FQID.ObjectId == selectedId) row.Selected = true;
                    _list.Items.Add(row);
                }
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("Jobs reload failed: " + ex.Message);
            }
            _list.EndUpdate();
        }

        private static List<AgentRegistration> Agents()
            => AgentsUserControl.ReadAgents().Where(a => !string.IsNullOrWhiteSpace(a.Hostname)).ToList();

        private void AddJob()
        {
            using (var dlg = new JobEditorForm("Add job", new JobConfig(), Agents()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var serverId = Configuration.Instance.ServerFQID.ServerId;
                    var fqid = new FQID(serverId, Guid.Empty, Guid.NewGuid(), FolderType.No, Ids.JobKindId);
                    var item = new Item(fqid, dlg.Result.Name);
                    foreach (var kv in dlg.Result.ToProperties()) item.Properties[kv.Key] = kv.Value;
                    Configuration.Instance.SaveItemConfiguration(Ids.PluginId, item);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the job: " + ex.Message, "Add job", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Reload();
            }
        }

        private void EditSelected()
        {
            var item = SelectedItem();
            if (item == null) return;
            var job = JobConfig.FromProperties(item.Properties);
            using (var dlg = new JobEditorForm("Edit job", job, Agents()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    item.Name = dlg.Result.Name;
                    item.Properties.Clear();
                    foreach (var kv in dlg.Result.ToProperties()) item.Properties[kv.Key] = kv.Value;
                    Configuration.Instance.SaveItemConfiguration(Ids.PluginId, item);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the job: " + ex.Message, "Edit job", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Reload();
            }
        }

        private void DeleteSelected()
        {
            var item = SelectedItem();
            if (item == null) return;
            if (MessageBox.Show(this, $"Delete job '{item.Name}'?", "Delete job",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, item); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not delete the job: " + ex.Message, "Delete job", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Reload();
        }

        private void RunSelected()
        {
            var item = SelectedItem();
            if (item == null) return;
            var job = JobConfig.FromProperties(item.Properties);
            if (string.IsNullOrWhiteSpace(job.AgentHostname))
            {
                MessageBox.Show(this, "This job has no agent assigned. Edit it and pick an agent first.",
                    "Run now", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(serverId);
                var mc = MessageCommunicationManager.Get(serverId);
                var req = new TriggerRequest
                {
                    JobObjectId = item.FQID.ObjectId,
                    AgentHostname = job.AgentHostname,
                    TriggerSource = "Manual",
                    RunId = Guid.NewGuid(),
                };
                mc.TransmitMessage(new MipMessage(Messages.RunJob, req.Encode()), null, null, null);

                // Show it as Pending right away. The agent's Running and finished records (same
                // RunId) replace this row as they arrive.
                RunRequested?.Invoke(new ExecutionRecord
                {
                    RunId = req.RunId,
                    JobObjectId = item.FQID.ObjectId,
                    JobName = job.Name,
                    AgentHostname = job.AgentHostname,
                    StartedUtc = DateTime.UtcNow,
                    Trigger = "Manual",
                    Outcome = "Pending",
                    // Show the job's configured camera/group count instead of 0 while pending.
                    CameraCount = job.Targets.Count,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not send the job: " + ex.Message, "Run now", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Item SelectedItem()
            => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Item : null;

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
