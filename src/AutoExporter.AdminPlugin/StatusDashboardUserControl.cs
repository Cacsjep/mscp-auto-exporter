using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using MipMessage = VideoOS.Platform.Messaging.Message;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Read-only Status and Executions view. Broadcasts a QueryExecutions message over MIP
    /// MessageCommunication; every running agent replies with its recent run history, which we
    /// merge (deduped by run id, newest first) into the grid. Refresh re-queries.
    /// </summary>
    internal sealed class StatusDashboardUserControl : UserControl
    {
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HideSelection = false,
        };
        private readonly Button _clear = new Button { Text = "Clear history", Width = 90, Height = 23 };
        private readonly Button _stop = new Button { Text = "Stop run", Width = 80, Height = 23 };
        private readonly Label _status = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
        private readonly Timer _timer = new Timer { Interval = RefreshIntervalMs };

        // The view re-queries every few seconds so it stays live without a Refresh button.
        private const int RefreshIntervalMs = 5000;

        private readonly Dictionary<Guid, ExecutionRecord> _byRun = new Dictionary<Guid, ExecutionRecord>();
        private readonly object _gate = new object();
        private readonly HashSet<string> _agents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ServerId _serverId;
        private MessageCommunication _mc;
        private object _replyFilter;
        private bool _started;

        /// <summary>Raised on the UI thread after each refresh with the current run snapshot, so
        /// the Jobs section can show each job's last run and outcome.</summary>
        public event Action<List<ExecutionRecord>> ExecutionsUpdated;

        public StatusDashboardUserControl()
        {
            BuildLayout();
            _clear.Click += (_, __) => ClearHistory();
            _stop.Click += (_, __) => StopSelected();
            _timer.Tick += (_, __) => Query(reset: false);
            HandleCreated += (_, __) => { StartMessaging(); _timer.Start(); };
            HandleDestroyed += (_, __) => { _timer.Stop(); StopMessaging(); };
        }

        private void BuildLayout()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);

            _list.Columns.Add("Started", 140);
            _list.Columns.Add("Job", 150);
            _list.Columns.Add("Agent", 120);
            _list.Columns.Add("Outcome", 80);
            _list.Columns.Add("Progress", 110);
            _list.Columns.Add("Cameras", 70);
            _list.Columns.Add("Size", 90);
            _list.Columns.Add("Trigger", 70);
            _list.Columns.Add("Detail", 320);

            // Owner draw so the Progress column can show a real progress bar while a run is in flight.
            // The other cells fall back to a plain text draw, and we redraw the grid lines ourselves
            // because owner drawing turns the built-in ones off.
            _list.OwnerDraw = true;
            _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
            _list.DrawItem += (_, __) => { };   // Details view paints per subitem below
            _list.DrawSubItem += OnDrawSubItem;

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(6, 0, 6, 6),
                FlowDirection = FlowDirection.LeftToRight,
            };
            top.Controls.Add(_clear);
            top.Controls.Add(_stop);
            top.Controls.Add(_status);

            Controls.Add(_list);
            Controls.Add(top);
        }

        // ----- Messaging lifecycle -----

        private void StartMessaging()
        {
            if (_started) return;
            try
            {
                _serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(_serverId);
                _mc = MessageCommunicationManager.Get(_serverId);
                _replyFilter = _mc.RegisterCommunicationFilter(
                    OnExecutionsReply, new CommunicationIdFilter(Messages.ExecutionsReply));
                _started = true;
                Query(reset: true);
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("Status dashboard messaging start failed: " + ex.Message);
                SetStatus("Could not start messaging: " + ex.Message);
            }
        }

        private void StopMessaging()
        {
            try { if (_replyFilter != null) _mc?.UnRegisterCommunicationFilter(_replyFilter); } catch { }
            try { if (_serverId != null) MessageCommunicationManager.Stop(_serverId); } catch { }
            _replyFilter = null;
            _mc = null;
            _started = false;
        }

        // reset = true clears the grid first (manual / initial load). The periodic re-query passes
        // reset = false so the list stays stable and replies only update or add rows (no flicker).
        private void Query(bool reset)
        {
            if (!_started || _mc == null) { SetStatus("Messaging not ready."); return; }
            if (reset)
            {
                lock (_gate) { _byRun.Clear(); _agents.Clear(); }
                RenderList();
            }
            try
            {
                _mc.TransmitMessage(new MipMessage(Messages.QueryExecutions, ""), null, null, null);
                if (reset) SetStatus("Querying agents...");
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("Status dashboard query failed: " + ex.Message);
                SetStatus("Query failed: " + ex.Message);
            }
        }

        // Replies arrive on a MIP background thread. Merge, then marshal the redraw to the UI thread.
        private object OnExecutionsReply(MipMessage message, FQID destination, FQID sender)
        {
            try
            {
                var records = ExecutionCodec.DecodeList(message?.Data as string);
                if (records.Count > 0)
                {
                    lock (_gate)
                    {
                        foreach (var r in records)
                        {
                            if (r.RunId != Guid.Empty) _byRun[r.RunId] = r;
                            if (!string.IsNullOrEmpty(r.AgentHostname)) _agents.Add(r.AgentHostname);
                        }
                    }
                    PostRender();
                }
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("OnExecutionsReply failed: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Show a run immediately as Pending the moment the user clicks Run now, before the agent
        /// has even picked it up. Keyed by RunId, so the agent's later Running and finished records
        /// replace it in place.
        /// </summary>
        public void ShowPending(ExecutionRecord rec)
        {
            if (rec == null || rec.RunId == Guid.Empty) return;
            lock (_gate)
            {
                _byRun[rec.RunId] = rec;
                if (!string.IsNullOrEmpty(rec.AgentHostname)) _agents.Add(rec.AgentHostname);
            }
            PostRender();
        }

        // Clear the run history everywhere: tell every agent to wipe its store, and clear the view.
        // Because the periodic re-query only adds rows, a later empty reply will not bring them back.
        private void ClearHistory()
        {
            if (MessageBox.Show(this, "Clear the execution history on all agents? This cannot be undone.",
                    "Clear history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _mc?.TransmitMessage(new MipMessage(Messages.ClearExecutions, ""), null, null, null); }
            catch (Exception ex) { PluginFileLog.Error("ClearExecutions send failed: " + ex.Message); }
            lock (_gate) { _byRun.Clear(); _agents.Clear(); }
            RenderList();
        }

        // Ask the owning agent to stop the selected run. Only a queued (Pending) or in-progress
        // (Running) run can be stopped; the agent drops it from its queue or cancels the export.
        private void StopSelected()
        {
            if (_list.SelectedItems.Count == 0 || !(_list.SelectedItems[0].Tag is ExecutionRecord rec))
            {
                SetStatus("Select a running or pending run to stop.");
                return;
            }
            var outcome = (rec.Outcome ?? "").ToLowerInvariant();
            if (!outcome.Contains("pending") && !outcome.Contains("running"))
            {
                MessageBox.Show(this, "Only a running or pending run can be stopped.",
                    "Stop run", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var req = new TriggerRequest
                {
                    JobObjectId = rec.JobObjectId,
                    AgentHostname = rec.AgentHostname,
                    TriggerSource = "Stop",
                    RunId = rec.RunId,
                };
                _mc?.TransmitMessage(new MipMessage(Messages.StopJob, req.Encode()), null, null, null);
                SetStatus($"Stop requested for '{rec.JobName}'.");
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("StopJob send failed: " + ex.Message);
                MessageBox.Show(this, "Could not send the stop request: " + ex.Message,
                    "Stop run", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ----- Rendering -----

        private void PostRender()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)RenderList);
            }
            catch { }
        }

        private void RenderList()
        {
            List<ExecutionRecord> snapshot;
            int agentCount;
            lock (_gate)
            {
                snapshot = _byRun.Values.ToList();
                agentCount = _agents.Count;
            }
            snapshot.Sort((a, b) => b.StartedUtc.CompareTo(a.StartedUtc));

            var friendly = AgentsUserControl.FriendlyNames();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in snapshot)
            {
                var item = new ListViewItem(ToLocal(r.StartedUtc)) { Tag = r };
                item.SubItems.Add(r.JobName ?? "");
                item.SubItems.Add(AgentsUserControl.FriendlyName(r.AgentHostname, friendly));
                item.SubItems.Add(OutcomeText(r));
                item.SubItems.Add("");   // Progress, drawn as a bar in OnDrawSubItem
                item.SubItems.Add(r.CameraCount.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(HumanSize(r.BytesWritten));
                item.SubItems.Add(r.Trigger ?? "");
                item.SubItems.Add(DetailText(r));
                item.ForeColor = RowColor(r);
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            SetStatus($"{snapshot.Count} run(s) from {agentCount} agent(s). Last refreshed {NowLocal()}.");
            try { ExecutionsUpdated?.Invoke(snapshot); } catch { }
        }

        // Just the outcome word now ("Running", "Success", ...). The live percent is shown by the
        // progress bar in the Progress column instead of being appended here.
        internal static string OutcomeText(ExecutionRecord r)
            => r.Outcome ?? (r.Success ? "Success" : "Failed");

        private static bool IsRunning(ExecutionRecord r)
            => string.Equals(r.Outcome, "Running", StringComparison.OrdinalIgnoreCase);

        // Plain text draw for normal cells plus a drawn progress bar for the Progress column of a
        // running row, with grid lines redrawn to match the rest of the list.
        private static readonly Pen GridPen = new Pen(Color.FromArgb(0xDA, 0xDA, 0xDA));
        private const int ProgressCol = 4;

        private void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            // For column 0 the framework hands us the whole row bounds, so clip it to the column.
            var bounds = e.Bounds;
            if (e.ColumnIndex == 0 && _list.Columns.Count > 0)
                bounds = new Rectangle(e.Bounds.Left, e.Bounds.Top, _list.Columns[0].Width, e.Bounds.Height);

            bool selected = e.Item.Selected;
            using (var bg = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Window))
                e.Graphics.FillRectangle(bg, bounds);

            var rec = e.Item.Tag as ExecutionRecord;
            if (e.ColumnIndex == ProgressCol && rec != null && IsRunning(rec))
            {
                DrawProgressBar(e.Graphics, bounds, rec.Progress);
            }
            else
            {
                var fore = selected ? SystemColors.HighlightText : e.Item.ForeColor;
                var textRect = Rectangle.Inflate(bounds, -2, 0);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _list.Font, textRect, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            // Right and bottom grid lines, matching the built-in GridLines look.
            e.Graphics.DrawLine(GridPen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
            e.Graphics.DrawLine(GridPen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        }

        private static void DrawProgressBar(Graphics g, Rectangle cell, int progress)
        {
            int pct = progress < 0 ? 0 : (progress > 100 ? 100 : progress);
            var bar = Rectangle.Inflate(cell, -3, -4);
            if (bar.Width <= 2 || bar.Height <= 2) return;

            using (var track = new SolidBrush(Color.FromArgb(0xE6, 0xE6, 0xE6)))
                g.FillRectangle(track, bar);
            var fill = new Rectangle(bar.X, bar.Y, (int)(bar.Width * (pct / 100.0)), bar.Height);
            using (var fb = new SolidBrush(Color.RoyalBlue))
                g.FillRectangle(fb, fill);
            using (var border = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0)))
                g.DrawRectangle(border, bar);

            TextRenderer.DrawText(g, pct + "%", SystemFonts.DefaultFont, bar, Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static string DetailText(ExecutionRecord r)
        {
            if (!string.IsNullOrEmpty(r.Error)) return r.Error;
            if (r.SkippedCameras != null && r.SkippedCameras.Count > 0)
                return "Skipped: " + string.Join(", ", r.SkippedCameras);
            return r.OutputFolder ?? "";
        }

        private static Color RowColor(ExecutionRecord r)
        {
            var outcome = (r.Outcome ?? (r.Success ? "Success" : "Failed")).ToLowerInvariant();
            if (outcome.Contains("fail")) return Color.Firebrick;
            if (outcome.Contains("partial") || outcome.Contains("skip")) return Color.DarkGoldenrod;
            if (outcome.Contains("pending") || outcome.Contains("running")) return Color.RoyalBlue;
            return Color.ForestGreen;
        }

        private void SetStatus(string text)
        {
            if (_status.IsHandleCreated && _status.InvokeRequired)
            { try { _status.BeginInvoke((Action)(() => _status.Text = text)); } catch { } }
            else _status.Text = text;
        }

        private static string ToLocal(DateTime utc)
            => utc == DateTime.MinValue ? "" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        private static string NowLocal() => DateTime.Now.ToString("HH:mm:ss");

        private static string HumanSize(long bytes)
        {
            if (bytes <= 0) return "0";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Stop(); _timer.Dispose(); StopMessaging(); }
            base.Dispose(disposing);
        }
    }
}
