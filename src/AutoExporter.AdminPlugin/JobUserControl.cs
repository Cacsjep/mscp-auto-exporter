using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.UI;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Job editor (code built, no Designer file). Maps to and from a <see cref="JobConfig"/>.
    /// Hosted in a modal dialog by the Jobs tab. Storage folder, max GB and retention are agent
    /// wide settings now, so they are not here. Each job picks which agent runs it.
    /// </summary>
    internal sealed class JobUserControl : UserControl
    {
        private readonly TextBox _txtName = new TextBox();
        private readonly CheckBox _chkEnabled = new CheckBox { Text = "Enabled", Checked = true };
        private readonly ComboBox _cboAgent = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        private readonly RadioButton _radXProtect = new RadioButton { Text = "XProtect™ format", AutoSize = true };
        private readonly RadioButton _radAvi = new RadioButton { Text = "AVI", Checked = true, AutoSize = true };
        private readonly Label _lblFormatHint = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(360, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Text = "XProtect™ format opens only in a Smart Client ™ (the standalone player " +
                   "cannot be bundled here). Use AVI for a file that plays anywhere.",
        };
        private readonly CheckBox _chkEncrypt = new CheckBox { Text = "Encrypt", AutoSize = true };
        private readonly TextBox _txtPassword = new TextBox { UseSystemPasswordChar = true, Width = 180 };
        private readonly CheckBox _chkTimestamp = new CheckBox { Text = "Burn in timestamp", AutoSize = true };
        private readonly NumericUpDown _numRangeValue = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1, Width = 70 };
        private readonly ComboBox _cboRangeUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly ListBox _lstTargets = new ListBox { SelectionMode = SelectionMode.MultiExtended, Height = 120 };

        /// <summary>Current name in the editor (used as the tree node label when hosted as a node).</summary>
        public string JobName => _txtName.Text;

        /// <summary>
        /// Raised when the user changes any field. The item manager forwards this to the framework
        /// so the Management Client enables its Save / Cancel buttons. Programmatic loads
        /// (<see cref="FillContent"/>, <see cref="ClearContent"/>, <see cref="SetAgents"/>) are
        /// suppressed so opening a job does not look dirty.
        /// </summary>
        internal event EventHandler ContentChanged;

        // True while we are populating controls in code, so those changes are not reported as edits.
        private bool _suspendDirty;

        public JobUserControl()
        {
            _cboRangeUnit.Items.AddRange(new object[] { "Minutes", "Hours", "Days", "Months" });
            _cboRangeUnit.SelectedItem = "Days";
            BuildLayout();
            WireDirtyTracking();
        }

        // Report any user edit so the host can enable Save. UpdateFormatUi stays wired for the
        // format / encryption controls so the dependent enable-states keep working.
        private void WireDirtyTracking()
        {
            _radXProtect.CheckedChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };
            _radAvi.CheckedChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };
            _chkEncrypt.CheckedChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };

            _txtName.TextChanged += (_, __) => RaiseChanged();
            _chkEnabled.CheckedChanged += (_, __) => RaiseChanged();
            _cboAgent.SelectedIndexChanged += (_, __) => RaiseChanged();
            _txtPassword.TextChanged += (_, __) => RaiseChanged();
            _chkTimestamp.CheckedChanged += (_, __) => RaiseChanged();
            _numRangeValue.ValueChanged += (_, __) => RaiseChanged();
            _cboRangeUnit.SelectedIndexChanged += (_, __) => RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (!_suspendDirty) ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        // ----- Layout -----

        private void BuildLayout()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(12);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtName.Width = 320;

            AddRow(root, "Name", _txtName);
            AddRow(root, "", _chkEnabled);
            AddRow(root, "Agent", _cboAgent);
            AddRow(root, "Format", Flow(_radXProtect, _radAvi));
            AddRow(root, "", _lblFormatHint);
            AddRow(root, "Encryption", Flow(_chkEncrypt, _txtPassword));
            AddRow(root, "AVI options", Flow(_chkTimestamp));
            AddRow(root, "Time range", Flow(new Label { Text = "Last", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, _numRangeValue, _cboRangeUnit));

            var targetButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var btnAdd = new Button { Text = "Add cameras..." };
            var btnRemove = new Button { Text = "Remove" };
            btnAdd.Click += OnAddTargetsClick;
            btnRemove.Click += OnRemoveTargetClick;
            targetButtons.Controls.Add(btnAdd);
            targetButtons.Controls.Add(btnRemove);

            _lstTargets.Width = 320;
            var targetsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            targetsPanel.Controls.Add(_lstTargets);
            targetsPanel.Controls.Add(targetButtons);
            AddRow(root, "Cameras / groups", targetsPanel);

            Controls.Add(root);
        }

        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
            foreach (var c in controls) flow.Controls.Add(c);
            return flow;
        }

        private static void AddRow(TableLayoutPanel root, string label, Control control)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
            int row = root.RowCount;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(lbl, 0, row);
            root.Controls.Add(control, 1, row);
            root.RowCount = row + 1;
        }

        // ----- Agents -----

        /// <summary>
        /// Fill the agent dropdown with the known registered agents. The list shows each agent's
        /// friendly name (its display name when set, else the hostname) but the job still stores the
        /// hostname, which is the stable key used to route the job to its agent.
        /// </summary>
        public void SetAgents(IEnumerable<AgentRegistration> agents)
        {
            _suspendDirty = true;
            try
            {
                var current = (_cboAgent.SelectedItem as AgentChoice)?.Hostname;
                _cboAgent.Items.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in agents ?? Enumerable.Empty<AgentRegistration>())
                    if (a != null && !string.IsNullOrWhiteSpace(a.Hostname) && seen.Add(a.Hostname))
                        _cboAgent.Items.Add(new AgentChoice(a.Hostname, a.FriendlyName));
                SelectAgent(current);
            }
            finally { _suspendDirty = false; }
        }

        // ----- Load / save -----

        public void FillContent(JobConfig job)
        {
            if (job == null) { ClearContent(); return; }

            _suspendDirty = true;
            try
            {
                _txtName.Text = job.Name;
                _chkEnabled.Checked = job.Enabled;
                SelectAgent(job.AgentHostname);
                _radXProtect.Checked = job.Format != "AVI";
                _radAvi.Checked = job.Format == "AVI";
                _chkEncrypt.Checked = job.Encrypt;
                _txtPassword.Text = job.Password;
                _chkTimestamp.Checked = job.Timestamp;
                _numRangeValue.Value = Clamp(job.RangeValue, _numRangeValue.Minimum, _numRangeValue.Maximum);
                _cboRangeUnit.SelectedItem = _cboRangeUnit.Items.Contains(job.RangeUnit) ? job.RangeUnit : "Days";

                _lstTargets.Items.Clear();
                foreach (var t in job.Targets) _lstTargets.Items.Add(new TargetItem(t));

                UpdateFormatUi();
            }
            finally { _suspendDirty = false; }
        }

        private void SelectAgent(string hostname)
        {
            if (!string.IsNullOrEmpty(hostname))
            {
                foreach (var obj in _cboAgent.Items)
                    if (obj is AgentChoice c && string.Equals(c.Hostname, hostname, StringComparison.OrdinalIgnoreCase))
                    {
                        _cboAgent.SelectedItem = obj;
                        return;
                    }
                // The job points at an agent that is not currently registered (offline or removed).
                // Keep the assignment by adding a placeholder row so it is not silently reassigned.
                var placeholder = new AgentChoice(hostname, hostname);
                _cboAgent.Items.Add(placeholder);
                _cboAgent.SelectedItem = placeholder;
                return;
            }
            if (_cboAgent.Items.Count > 0) _cboAgent.SelectedIndex = 0;
        }

        public void ClearContent()
        {
            _suspendDirty = true;
            try
            {
                _txtName.Text = "";
                _chkEnabled.Checked = true;
                if (_cboAgent.Items.Count > 0) _cboAgent.SelectedIndex = 0;
                _radAvi.Checked = true;
                _chkEncrypt.Checked = false;
                _txtPassword.Text = "";
                _chkTimestamp.Checked = false;
                _numRangeValue.Value = 1;
                _cboRangeUnit.SelectedItem = "Days";
                _lstTargets.Items.Clear();
                UpdateFormatUi();
            }
            finally { _suspendDirty = false; }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Please enter a name for the job.";
            if (_cboAgent.SelectedItem == null)
                return "Please select the agent that runs this job. If the list is empty, start an agent first.";
            if (_lstTargets.Items.Count == 0)
                return "Please add at least one camera or camera group.";
            if (_chkEncrypt.Checked && string.IsNullOrEmpty(_txtPassword.Text))
                return "Encryption is enabled. Please enter a password.";
            if (_radAvi.Checked && _chkEncrypt.Checked)
                return "AVI format does not support encryption. Disable encryption or switch to XProtect.";
            return null;
        }

        public JobConfig ToConfig()
        {
            var job = new JobConfig
            {
                Name = _txtName.Text.Trim(),
                Enabled = _chkEnabled.Checked,
                AgentHostname = (_cboAgent.SelectedItem as AgentChoice)?.Hostname ?? "",
                Format = _radAvi.Checked ? "AVI" : "XProtect",
                Encrypt = _chkEncrypt.Checked,
                Password = _txtPassword.Text,
                IncludeAudio = true,   // audio is always exported (the UI option was removed)
                Timestamp = _chkTimestamp.Checked,
                RangeValue = (int)_numRangeValue.Value,
                RangeUnit = _cboRangeUnit.SelectedItem?.ToString() ?? "Days",
            };
            foreach (var obj in _lstTargets.Items)
                if (obj is TargetItem ti) job.Targets.Add(ti.Target);
            return job;
        }

        // ----- Handlers -----

        private void OnAddTargetsClick(object sender, EventArgs e)
        {
            try
            {
                var picker = new ItemPickerWpfWindow
                {
                    KindsFilter = new List<Guid> { Kind.Camera },
                    SelectionMode = SelectionModeOptions.MultiSelect,
                    Items = Configuration.Instance.GetItems(),
                    // Hide disabled cameras (export cannot use them). Folders/groups stay visible.
                    IsVisibleCallback = it =>
                        it?.FQID == null || it.FQID.FolderType != FolderType.No || it.FQID.Kind != Kind.Camera || it.Enabled,
                };
                if (picker.ShowDialog() != true) return;

                foreach (var item in picker.SelectedItems)
                {
                    bool isGroup = item.FQID != null && item.FQID.FolderType != FolderType.No;
                    var target = new JobTarget
                    {
                        Kind = isGroup ? "Group" : "Camera",
                        ObjectId = item.FQID.ObjectId,
                        Name = item.Name,
                    };
                    if (!_lstTargets.Items.Cast<object>().OfType<TargetItem>().Any(x => x.Target.ObjectId == target.ObjectId))
                        _lstTargets.Items.Add(new TargetItem(target));
                }
                RaiseChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open picker: " + ex.Message, "Picker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRemoveTargetClick(object sender, EventArgs e)
        {
            var selected = _lstTargets.SelectedItems.Cast<object>().ToList();
            foreach (var s in selected) _lstTargets.Items.Remove(s);
            if (selected.Count > 0) RaiseChanged();
        }

        private void UpdateFormatUi()
        {
            bool xp = _radXProtect.Checked;
            _chkEncrypt.Enabled = xp;
            _txtPassword.Enabled = xp && _chkEncrypt.Checked;
            if (!xp) _chkEncrypt.Checked = false;

            // Timestamp burn-in is an AVI-only feature.
            _chkTimestamp.Enabled = !xp;
            if (xp) _chkTimestamp.Checked = false;
        }

        private static decimal Clamp(int value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // An agent entry in the dropdown: shows the friendly name, carries the hostname (the value
        // stored on the job).
        private sealed class AgentChoice
        {
            public readonly string Hostname;
            private readonly string _display;
            public AgentChoice(string hostname, string display) { Hostname = hostname; _display = display; }
            public override string ToString() => string.IsNullOrWhiteSpace(_display) ? Hostname : _display;
        }

        // Wraps a JobTarget so the ListBox shows a friendly label.
        private sealed class TargetItem
        {
            public readonly JobTarget Target;
            public TargetItem(JobTarget t) => Target = t;
            public override string ToString()
            {
                var prefix = Target.Kind == "Group" ? "[Group] " : "[Camera] ";
                return prefix + (string.IsNullOrEmpty(Target.Name) ? Target.ObjectId.ToString() : Target.Name);
            }
        }
    }
}
