using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoExporter.Contracts;
using FontAwesome5;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;
using VideoOS.Platform.RuleAction;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Tree: Exporter (this plugin) -> Agents -> Agent &lt;Hostname&gt; -> Jobs -> Job-A/B,
    /// plus a Status and Executions node. Agent nodes are written by the running agent service
    /// (self-registration); Job nodes are created here by the admin, scoped under an agent.
    /// </summary>
    public class AutoExporterPluginDefinition : PluginDefinition
    {
        // Read/Edit permission pair per item kind (surfaces under Security > Roles > MIP).
        private static readonly List<SecurityAction> KindSecurity = new List<SecurityAction>
        {
            new SecurityAction("GENERIC_READ",  "Read"),
            new SecurityAction("GENERIC_WRITE", "Edit"),
        };

        private List<ItemNode> _itemNodes;
        private readonly List<BackgroundPlugin> _backgroundPlugins = new List<BackgroundPlugin>();
        private RunExportActionManager _actionManager;

        // Font Awesome tree icon (rendered lazily in the Administration environment).
        private Image _pluginIcon = PluginIcon.Fallback;

        public override Guid Id => Ids.PluginId;
        public override string Name => "Exporter";
        public override string Manufacturer => Ids.IntegrationManufacturer;
        public override Image Icon => _pluginIcon;

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            PluginFileLog.Info(Diagnostics.Banner("Auto Exporter Plugin", typeof(AutoExporterPluginDefinition).Assembly)
                               + " env=" + env);

            // Render the icon only in the Management Client (the Service env has no need for it).
            if (env != EnvironmentType.Service)
                _pluginIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_FileExport);

            // Event Server: host the thin bridge that turns rule actions into MIP messages.
            if (env == EnvironmentType.Service)
                _backgroundPlugins.Add(new BridgeBackgroundPlugin());

            _actionManager = new RunExportActionManager();
        }

        public override void Close()
        {
            _itemNodes = null;
            _backgroundPlugins.Clear();
        }

        public override List<ItemNode> ItemNodes
        {
            get
            {
                var env = EnvironmentManager.Instance.EnvironmentType;
                if (env != EnvironmentType.Administration && env != EnvironmentType.Service)
                    return null;
                if (_itemNodes != null) return _itemNodes;

                // The overview node hosts our tabbed page (Executions / Jobs / Agents). Agents are
                // never a tree node (no "add" problem). Singleton (ItemsAllowed.One) = no "add".
                var overviewNode = new ItemNode(
                    Ids.OverviewKindId, Guid.Empty,
                    "Overview", _pluginIcon, "Overview", _pluginIcon,
                    Category.Text, true, ItemsAllowed.One,
                    new OverviewItemManager(Ids.OverviewKindId),
                    null,
                    new List<SecurityAction>(KindSecurity));

                // Jobs ALSO exist as a node so the rule engine can target a specific job (the rule
                // action + events use JobKindId). The Jobs tab and this node edit the same items.
                var jobsNode = new ItemNode(
                    Ids.JobKindId, Guid.Empty,
                    "Job", _pluginIcon, "Jobs", _pluginIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new JobItemManager(Ids.JobKindId),
                    null,
                    new List<SecurityAction>(KindSecurity));

                _itemNodes = new List<ItemNode> { overviewNode, jobsNode };
                return _itemNodes;
            }
        }

        public override List<BackgroundPlugin> BackgroundPlugins => _backgroundPlugins;
        public override ActionManager ActionManager => _actionManager;

        // Help page shown when the plugin root node ("Exporter") is selected in the tree, matching
        // the help style of the other admin plugins. The operational UI lives under the Overview node.
        public override UserControl GenerateUserControl() => new HtmlHelpUserControl();
    }
}
