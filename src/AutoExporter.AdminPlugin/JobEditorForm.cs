using System.Collections.Generic;
using System.Windows.Forms;
using AutoExporter.Contracts;

namespace AutoExporter.AdminPlugin
{
    /// <summary>Modal dialog that hosts the <see cref="JobUserControl"/> editor for add/edit.</summary>
    internal sealed class JobEditorForm : Form
    {
        private readonly JobUserControl _editor = new JobUserControl { Dock = DockStyle.Fill };

        public JobConfig Result { get; private set; }

        public JobEditorForm(string title, JobConfig job, IEnumerable<AgentRegistration> agents)
        {
            Text = title;
            Width = 560;
            Height = 660;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;   // not resizable
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            _editor.SetAgents(agents);
            _editor.FillContent(job);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(8),
            };
            var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            Controls.Add(_editor);
            Controls.Add(buttons);
            CancelButton = cancel;
        }

        private void OnOk(object sender, System.EventArgs e)
        {
            var error = _editor.ValidateInput();
            if (error != null)
            {
                MessageBox.Show(this, error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = _editor.ToConfig();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
