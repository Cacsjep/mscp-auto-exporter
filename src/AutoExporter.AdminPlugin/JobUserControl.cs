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
    ///
    /// Only the options that apply to the chosen format are shown: encryption for XProtect, the
    /// timestamp burn-in for AVI, and the full timelapse panel for Timelapse. Switching format hides
    /// the others rather than just disabling them.
    /// </summary>
    internal sealed class JobUserControl : UserControl
    {
        private readonly TextBox _txtName = new TextBox();
        private readonly CheckBox _chkEnabled = new CheckBox { Text = "", AutoSize = true, Checked = true };
        private readonly ComboBox _cboAgent = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        // Format pick, value order matches the SelectedIndex switch in SelectedFormat / SelectFormat.
        private readonly ComboBox _cboFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

        // XProtect
        private readonly CheckBox _chkSign = new CheckBox { Text = "", AutoSize = true };
        private readonly CheckBox _chkEncrypt = new CheckBox { Text = "", AutoSize = true };
        private readonly TextBox _txtPassword = new TextBox { UseSystemPasswordChar = true, Width = 180 };

        // AVI
        private readonly CheckBox _chkAviTimestamp = new CheckBox { Text = "", AutoSize = true };

        // Timelapse
        private readonly ComboBox _cboTlMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly NumericUpDown _numTlInterval = new NumericUpDown { Minimum = 1, Maximum = 86400, Value = 60, Width = 70 };
        private readonly NumericUpDown _numEvtInterval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 10, Width = 60 };
        private readonly NumericUpDown _numEvtMin = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 55 };
        private readonly NumericUpDown _numEvtMax = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 10, Width = 55 };
        private readonly NumericUpDown _numEvtMergeGap = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 2, Width = 55 };
        private readonly ComboBox _cboTlFps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        private readonly CheckBox _chkTlTimestamp = new CheckBox { Text = "", AutoSize = true };
        private readonly CheckBox _chkTlDaily = new CheckBox { Text = "", AutoSize = true };
        private readonly TextBox _txtTlDailyStart = new TextBox { Width = 60, Text = "08:00" };
        private readonly TextBox _txtTlDailyEnd = new TextBox { Width = 60, Text = "17:00" };
        // Plain language help for the event based fields, shown only in that mode. A RichTextBox so the
        // field names can be bold. It spans both columns and auto-sizes its height to the wrapped text.
        private readonly RichTextBox _rtbTlEventHelp = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = System.Drawing.SystemColors.Control,
            ForeColor = System.Drawing.SystemColors.GrayText,
            ScrollBars = RichTextBoxScrollBars.None,
            TabStop = false,
            Dock = DockStyle.Top,
            Height = 60,
            Margin = new Padding(0, 7, 0, 0),   // sit a little below the daily window row
        };

        private readonly NumericUpDown _numRangeValue = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1, Width = 70 };
        private readonly ComboBox _cboRangeUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly ListBox _lstTargets = new ListBox { SelectionMode = SelectionMode.MultiExtended, Height = 120 };

        // Format-specific rows we show or hide as a whole (label plus control).
        private Row _rowSign;
        private Row _rowEncrypt;
        private Row _rowAviTimestamp;
        // Timelapse rows: the ones common to both modes, plus the mode-specific ones.
        private Row _rowTlMode;
        private Row _rowTlContinuous;
        private Row _rowTlEvtInterval;
        private Row _rowTlEvtFrames;
        private Row _rowTlEvtMerge;
        private Row _rowTlFps;
        private Row _rowTlTimestamp;
        private Row _rowTlDaily;
        private Row _rowTlEvtHelp;

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
            _cboFormat.Items.AddRange(new object[] { "XProtect", "AVI", "Timelapse (MP4)" });
            _cboFormat.SelectedIndex = 1;
            _cboTlMode.Items.AddRange(new object[] { "Continuous", "Event based" });
            _cboTlMode.SelectedIndex = 0;
            _cboTlFps.Items.AddRange(new object[] { 5, 10, 15, 24, 30 });
            _cboTlFps.SelectedItem = 24;
            BuildLayout();
            WireDirtyTracking();
            BuildEventHelpText();
        }

        // Fill the event help box with plain text and bold the field names so they stand out. The box
        // grows to fit whatever the wrapped text needs.
        private void BuildEventHelpText()
        {
            _rtbTlEventHelp.ContentsResized += (_, e) => _rtbTlEventHelp.Height = e.NewRectangle.Height + 4;
            AppendHelp("Event based walks each recorded sequence on its own.\n", false);
            AppendHelp("Frame interval", true);
            AppendHelp(" is how often a frame is taken inside a sequence.\n", false);
            AppendHelp("Frames per sequence", true);
            AppendHelp(" is the fewest and most frames taken from any one sequence.\n", false);
            AppendHelp("Merge sequences under", true);
            AppendHelp(" joins sequences recorded close together so they count as one sequence.", false);
        }

        private void AppendHelp(string text, bool bold)
        {
            _rtbTlEventHelp.SelectionStart = _rtbTlEventHelp.TextLength;
            _rtbTlEventHelp.SelectionLength = 0;
            _rtbTlEventHelp.SelectionColor = System.Drawing.SystemColors.GrayText;
            _rtbTlEventHelp.SelectionFont = new System.Drawing.Font(
                _rtbTlEventHelp.Font, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
            _rtbTlEventHelp.AppendText(text);
        }

        // Report any user edit so the host can enable Save. The format pick and the mode radios also
        // refresh which options are visible.
        private void WireDirtyTracking()
        {
            _cboFormat.SelectedIndexChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };
            _cboTlMode.SelectedIndexChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };
            _chkTlDaily.CheckedChanged += (_, __) => { UpdateFormatUi(); RaiseChanged(); };

            _txtName.TextChanged += (_, __) => RaiseChanged();
            _chkEnabled.CheckedChanged += (_, __) => RaiseChanged();
            _cboAgent.SelectedIndexChanged += (_, __) => RaiseChanged();
            _chkSign.CheckedChanged += (_, __) => RaiseChanged();
            _chkEncrypt.CheckedChanged += (_, __) => { _txtPassword.Enabled = _chkEncrypt.Checked; RaiseChanged(); };
            _txtPassword.TextChanged += (_, __) => RaiseChanged();
            _chkAviTimestamp.CheckedChanged += (_, __) => RaiseChanged();
            _chkTlTimestamp.CheckedChanged += (_, __) => RaiseChanged();
            _numRangeValue.ValueChanged += (_, __) => RaiseChanged();
            _cboRangeUnit.SelectedIndexChanged += (_, __) => RaiseChanged();
            _numTlInterval.ValueChanged += (_, __) => RaiseChanged();
            _numEvtInterval.ValueChanged += (_, __) => RaiseChanged();
            _numEvtMin.ValueChanged += (_, __) => RaiseChanged();
            _numEvtMax.ValueChanged += (_, __) => RaiseChanged();
            _numEvtMergeGap.ValueChanged += (_, __) => RaiseChanged();
            _cboTlFps.SelectedIndexChanged += (_, __) => RaiseChanged();
            _txtTlDailyStart.TextChanged += (_, __) => RaiseChanged();
            _txtTlDailyEnd.TextChanged += (_, __) => RaiseChanged();
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

            _txtName.Width = 320;

            // General: what the job is and who runs it.
            var general = MakeFormTable();
            AddRow(general, "Name", _txtName);
            AddRow(general, "Enabled", _chkEnabled);
            AddRow(general, "Agent", _cboAgent);

            // Timerange: how far back each run reaches.
            var timerange = MakeFormTable();
            AddRow(timerange, "Last", Inline(_numRangeValue, _cboRangeUnit), centerLabel: true);

            // Cameras: which cameras or groups get exported. No left label, the list fills the group.
            var cameras = BuildTargetsPanel();

            // Output: the format pick and only the options that apply to it. Every option is a normal
            // label-left / control-right row, shown or hidden with the format and the timelapse mode.
            var format = MakeFormTable();
            AddRow(format, "Format", _cboFormat, centerLabel: true);
            _rowSign = AddRow(format, "Sign export", Inline(_chkSign), centerLabel: true);
            _rowEncrypt = AddRow(format, "Encryption", Inline(_chkEncrypt, _txtPassword), centerLabel: true);
            _rowAviTimestamp = AddRow(format, "Burn in timestamp", Inline(_chkAviTimestamp), centerLabel: true);

            _rowTlMode = AddRow(format, "Mode", _cboTlMode, centerLabel: true);
            _rowTlContinuous = AddRow(format, "Sample every", Inline(_numTlInterval, Lbl("seconds of footage")), centerLabel: true);
            _rowTlEvtInterval = AddRow(format, "Frame interval", Inline(_numEvtInterval, Lbl("seconds per sequence")), centerLabel: true);
            _rowTlEvtFrames = AddRow(format, "Frames per sequence", Inline(_numEvtMin, Lbl("to"), _numEvtMax), centerLabel: true);
            _rowTlEvtMerge = AddRow(format, "Merge sequences under", Inline(_numEvtMergeGap, Lbl("seconds apart")), centerLabel: true);
            _rowTlFps = AddRow(format, "Play back at", Inline(_cboTlFps, Lbl("fps")), centerLabel: true);
            _rowTlTimestamp = AddRow(format, "Burn in timestamp", Inline(_chkTlTimestamp), centerLabel: true);
            _rowTlDaily = AddRow(format, "Daily window", Inline(_chkTlDaily, _txtTlDailyStart, Lbl("to"), _txtTlDailyEnd), centerLabel: true);
            _rowTlEvtHelp = AddSpanRow(format, _rtbTlEventHelp);

            // Stack the three groups top to bottom, each filling the width.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddGroup(root, "General", general);
            AddGroup(root, "Timerange", timerange);
            AddGroup(root, "Cameras", cameras);
            AddGroup(root, "Output", format);

            Controls.Add(root);
        }

        // The cameras list with Remove on the left edge and Add on the right edge, both sitting under
        // the list across its width. Docked to the top so it clears the group caption.
        private Control BuildTargetsPanel()
        {
            const int listWidth = 320;

            var btnAdd = new Button { Text = "Add", AutoSize = true, Margin = new Padding(0) };
            var btnRemove = new Button { Text = "Remove", AutoSize = true, Margin = new Padding(0) };
            btnAdd.Click += OnAddTargetsClick;
            btnRemove.Click += OnRemoveTargetClick;

            var buttons = new TableLayoutPanel
            {
                // Two pixels wider than the list and shifted so the buttons bleed 1px past each list
                // edge. That lines the button borders up with the list's 3D border instead of sitting
                // a pixel inside it.
                Width = listWidth + 2,
                // Just enough headroom that the real font does not clip the button bottoms.
                Height = btnAdd.PreferredSize.Height + 3,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 0),   // a small, even gap under the list
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            btnRemove.Anchor = AnchorStyles.Top | AnchorStyles.Left;    // flush to the list left edge
            btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;      // flush to the list right edge
            buttons.Controls.Add(btnRemove, 0, 0);
            buttons.Controls.Add(btnAdd, 1, 0);

            _lstTargets.Width = listWidth;
            _lstTargets.Margin = new Padding(1, 0, 0, 0);   // open 1px on the left so the row can bleed out
            var targetsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,   // keep the buttons stacked under the list, not wrapped beside it
                Dock = DockStyle.Top,
            };
            targetsPanel.Controls.Add(_lstTargets);
            targetsPanel.Controls.Add(buttons);
            return targetsPanel;
        }

        // A two column label/control table used inside each group box.
        private static TableLayoutPanel MakeFormTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        // Wrap a form table in a captioned group box and add it as the next stacked row.
        private static void AddGroup(TableLayoutPanel root, string title, Control content)
        {
            var box = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 6, 10, 10),
                Margin = new Padding(0, 0, 0, 10),
            };
            box.Controls.Add(content);
            int row = root.RowCount;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(box, 0, row);
            root.RowCount = row + 1;
        }

        // Lay the controls out in a single table row and anchor each one to the left
        // only, so the layout engine vertically centres them. A short caption then sits level with a
        // taller spinner or combo box beside it instead of riding above it.
        private static TableLayoutPanel Inline(params Control[] controls)
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                RowCount = 1,
                ColumnCount = controls.Length,
                Margin = new Padding(0),
            };
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < controls.Length; i++)
            {
                var c = controls[i];
                c.Anchor = AnchorStyles.Left;
                // Plainly centre a trailing caption so it sits level with the spinner or combo it
                // follows. (The left-hand row caption uses a small lift instead, see AddRow.)
                if (c is Label lbl) lbl.Padding = new Padding(0);
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.Controls.Add(c, i, 0);
            }
            return table;
        }

        // An inline caption sitting beside controls in a Flow row, padded to line up with them.
        private static Label Lbl(string text)
            => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };

        private static Row AddRow(TableLayoutPanel root, string label, Control control, bool centerLabel = false)
        {
            // Tall rows (a panel, a list) read better with the caption near the top, so the default
            // nudges the label down a little. Single line rows whose field is vertically centred pass
            // centerLabel so the caption shares that centre line instead of riding low.
            var pad = centerLabel ? new Padding(0, 0, 0, 4) : new Padding(0, 6, 0, 0);
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = pad };
            int row = root.RowCount;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(lbl, 0, row);
            root.Controls.Add(control, 1, row);
            root.RowCount = row + 1;
            return new Row(lbl, control);
        }

        // A row whose single control spans both columns (no left caption), for wide content like a
        // help paragraph.
        private static Row AddSpanRow(TableLayoutPanel root, Control control)
        {
            int row = root.RowCount;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(control, 0, row);
            root.SetColumnSpan(control, 2);
            root.RowCount = row + 1;
            return new Row(null, control);
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
                SelectFormat(job.Format);

                _chkSign.Checked = job.Sign;
                _chkEncrypt.Checked = job.Encrypt;
                _txtPassword.Text = job.Password;

                // One stored Timestamp flag, shown in whichever format panel is active.
                _chkAviTimestamp.Checked = job.Timestamp;
                _chkTlTimestamp.Checked = job.Timestamp;

                SelectTlMode(job.TimelapseMode);
                _numTlInterval.Value = Clamp(job.TimelapseIntervalSeconds, _numTlInterval.Minimum, _numTlInterval.Maximum);
                _numEvtInterval.Value = Clamp(job.TimelapseEventIntervalSeconds, _numEvtInterval.Minimum, _numEvtInterval.Maximum);
                _numEvtMin.Value = Clamp(job.TimelapseEventMinFrames, _numEvtMin.Minimum, _numEvtMin.Maximum);
                _numEvtMax.Value = Clamp(job.TimelapseEventMaxFrames, _numEvtMax.Minimum, _numEvtMax.Maximum);
                _numEvtMergeGap.Value = Clamp(job.TimelapseEventMergeGapSeconds, _numEvtMergeGap.Minimum, _numEvtMergeGap.Maximum);
                SelectFps(job.TimelapseFps);
                _chkTlDaily.Checked = job.TimelapseDailyEnabled;
                _txtTlDailyStart.Text = string.IsNullOrWhiteSpace(job.TimelapseDailyStart) ? "08:00" : job.TimelapseDailyStart;
                _txtTlDailyEnd.Text = string.IsNullOrWhiteSpace(job.TimelapseDailyEnd) ? "17:00" : job.TimelapseDailyEnd;

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
                SelectFormat("AVI");
                _chkSign.Checked = false;
                _chkEncrypt.Checked = false;
                _txtPassword.Text = "";
                _chkAviTimestamp.Checked = false;
                _chkTlTimestamp.Checked = false;
                _cboTlMode.SelectedIndex = 0;
                _numTlInterval.Value = 60;
                _numEvtInterval.Value = 10;
                _numEvtMin.Value = 1;
                _numEvtMax.Value = 10;
                _numEvtMergeGap.Value = 2;
                _cboTlFps.SelectedItem = 24;
                _chkTlDaily.Checked = false;
                _txtTlDailyStart.Text = "08:00";
                _txtTlDailyEnd.Text = "17:00";
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
            if (SelectedFormat() == "XProtect" && _chkEncrypt.Checked && string.IsNullOrEmpty(_txtPassword.Text))
                return "Encryption is enabled. Please enter a password.";
            if (SelectedFormat() == "Timelapse")
            {
                if (SelectedTlMode() == "EventBased" && _numEvtMin.Value > _numEvtMax.Value)
                    return "Timelapse: the minimum frames per event cannot exceed the maximum.";
                if (_chkTlDaily.Checked &&
                    (!TimeSpan.TryParse(_txtTlDailyStart.Text.Trim(), out _) || !TimeSpan.TryParse(_txtTlDailyEnd.Text.Trim(), out _)))
                    return "Please enter the daily window times as HH:mm (for example 08:00 and 17:00).";
            }
            return null;
        }

        public JobConfig ToConfig()
        {
            string fmt = SelectedFormat();
            bool tl = fmt == "Timelapse";
            bool avi = fmt == "AVI";
            var job = new JobConfig
            {
                Name = _txtName.Text.Trim(),
                Enabled = _chkEnabled.Checked,
                AgentHostname = (_cboAgent.SelectedItem as AgentChoice)?.Hostname ?? "",
                Format = fmt,
                // Signing and encryption are XProtect-only options.
                Sign = fmt == "XProtect" && _chkSign.Checked,
                Encrypt = fmt == "XProtect" && _chkEncrypt.Checked,
                Password = _txtPassword.Text,
                IncludeAudio = true,   // audio is always exported (the UI option was removed)
                // Timestamp burn-in comes from whichever format panel is active.
                Timestamp = tl ? _chkTlTimestamp.Checked : (avi && _chkAviTimestamp.Checked),
                RangeValue = (int)_numRangeValue.Value,
                RangeUnit = _cboRangeUnit.SelectedItem?.ToString() ?? "Days",
                TimelapseMode = SelectedTlMode(),
                TimelapseFps = _cboTlFps.SelectedItem is int fps ? fps : 24,
                TimelapseIntervalSeconds = (int)_numTlInterval.Value,
                TimelapseEventIntervalSeconds = (int)_numEvtInterval.Value,
                TimelapseEventMaxFrames = (int)_numEvtMax.Value,
                TimelapseEventMinFrames = (int)_numEvtMin.Value,
                TimelapseEventMergeGapSeconds = (int)_numEvtMergeGap.Value,
                TimelapseDailyEnabled = _chkTlDaily.Checked,
                TimelapseDailyStart = _txtTlDailyStart.Text.Trim(),
                TimelapseDailyEnd = _txtTlDailyEnd.Text.Trim(),
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

        // Show only the options that apply to the chosen format, and within timelapse only the
        // fields for the chosen capture mode.
        private void UpdateFormatUi()
        {
            string fmt = SelectedFormat();
            bool xp = fmt == "XProtect";
            bool avi = fmt == "AVI";
            bool tl = fmt == "Timelapse";
            bool evt = tl && SelectedTlMode() == "EventBased";

            if (_rowSign != null) _rowSign.Visible = xp;
            if (_rowEncrypt != null) _rowEncrypt.Visible = xp;
            if (_rowAviTimestamp != null) _rowAviTimestamp.Visible = avi;

            // Timelapse rows: Mode, fps, timestamp and daily window show for any timelapse job. The
            // interval row is for Continuous, the three clip rows are for Event based.
            if (_rowTlMode != null) _rowTlMode.Visible = tl;
            if (_rowTlContinuous != null) _rowTlContinuous.Visible = tl && !evt;
            if (_rowTlEvtInterval != null) _rowTlEvtInterval.Visible = evt;
            if (_rowTlEvtFrames != null) _rowTlEvtFrames.Visible = evt;
            if (_rowTlEvtMerge != null) _rowTlEvtMerge.Visible = evt;
            if (_rowTlFps != null) _rowTlFps.Visible = tl;
            if (_rowTlTimestamp != null) _rowTlTimestamp.Visible = tl;
            if (_rowTlDaily != null) _rowTlDaily.Visible = tl;
            if (_rowTlEvtHelp != null) _rowTlEvtHelp.Visible = evt;

            _txtPassword.Enabled = _chkEncrypt.Checked;

            bool daily = _chkTlDaily.Checked;
            _txtTlDailyStart.Enabled = daily;
            _txtTlDailyEnd.Enabled = daily;
        }

        // The format value behind the dropdown pick (the items carry friendly labels).
        private string SelectedFormat()
        {
            switch (_cboFormat.SelectedIndex)
            {
                case 0: return "XProtect";
                case 2: return "Timelapse";
                default: return "AVI";
            }
        }

        // Point the dropdown at the stored format value.
        private void SelectFormat(string format)
        {
            if (string.Equals(format, "XProtect", StringComparison.OrdinalIgnoreCase)) _cboFormat.SelectedIndex = 0;
            else if (string.Equals(format, "Timelapse", StringComparison.OrdinalIgnoreCase)) _cboFormat.SelectedIndex = 2;
            else _cboFormat.SelectedIndex = 1;
        }

        // The timelapse capture mode behind the Mode dropdown pick.
        private string SelectedTlMode() => _cboTlMode.SelectedIndex == 1 ? "EventBased" : "Continuous";

        // Point the Mode dropdown at the stored capture mode.
        private void SelectTlMode(string mode)
            => _cboTlMode.SelectedIndex = string.Equals(mode, "EventBased", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        // Select the stored fps in the dropdown, adding it if it is not one of the presets.
        private void SelectFps(int fps)
        {
            if (fps <= 0) fps = 24;
            if (!_cboTlFps.Items.Contains(fps)) _cboTlFps.Items.Add(fps);
            _cboTlFps.SelectedItem = fps;
        }

        private static decimal Clamp(int value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // A label-plus-control row in the form, shown or hidden as a unit.
        private sealed class Row
        {
            private readonly Label _label;
            private readonly Control _control;
            public Row(Label label, Control control) { _label = label; _control = control; }
            public bool Visible
            {
                set { if (_label != null) _label.Visible = value; _control.Visible = value; }
            }
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
