using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// The single Overview page. One scrollable view with three stacked sections (top to bottom):
    /// Agents, Jobs, Executions. All are driven by the Configuration API and (for executions) MIP
    /// messaging. The executions feed also updates each job's Last run / Last status.
    /// </summary>
    internal sealed class OverviewUserControl : UserControl
    {
        private readonly AgentsUserControl _agents = new AgentsUserControl { Dock = DockStyle.Fill };
        private readonly JobsUserControl _jobs = new JobsUserControl { Dock = DockStyle.Fill };
        private readonly StatusDashboardUserControl _executions = new StatusDashboardUserControl { Dock = DockStyle.Fill };

        public OverviewUserControl()
        {
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Window;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 26));   // Agents
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 37));   // Jobs
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 37));   // Executions

            grid.Controls.Add(Section("Agents", _agents), 0, 0);
            grid.Controls.Add(Section("Jobs", _jobs), 0, 1);
            grid.Controls.Add(Section("Executions", _executions), 0, 2);

            Controls.Add(grid);

            // The executions query (over messaging) also drives each job's Last run / Last status.
            _executions.ExecutionsUpdated += recs =>
            {
                try { _jobs.SetExecutions(recs); } catch { }
            };

            // Run now shows a Pending row in the Executions section immediately.
            _jobs.RunRequested += rec =>
            {
                try { _executions.ShowPending(rec); } catch { }
            };
        }

        private static Control Section(string title, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
            content.Dock = DockStyle.Fill;
            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = title,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Padding = new Padding(4, 4, 0, 0),
                ForeColor = Color.FromArgb(60, 60, 60),
            };
            panel.Controls.Add(content);
            panel.Controls.Add(header);
            return panel;
        }
    }
}
