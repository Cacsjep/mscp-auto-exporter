using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Jobs as a Milestone tree node so the rule engine can target a specific job (rule action +
    /// JobStarted/Succeeded/Failed events use kind <see cref="Ids.JobKindId"/>). Jobs can also be
    /// managed from the Auto Exporter > Jobs tab. Both read and write the same configuration items.
    /// </summary>
    public class JobItemManager : ItemManager
    {
        private readonly Guid _kind;
        private JobUserControl _editor;

        public JobItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        public override UserControl GenerateDetailUserControl()
        {
            _editor = new JobUserControl();
            // Forward user edits to the base handler so the Management Client enables Save / Cancel.
            _editor.ContentChanged += ConfigurationChangedByUserHandler;
            return _editor;
        }

        public override void ReleaseUserControl()
        {
            if (_editor != null) _editor.ContentChanged -= ConfigurationChangedByUserHandler;
            _editor?.Dispose();
            _editor = null;
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _editor?.SetAgents(Agents());
            _editor?.FillContent(item?.Properties != null ? JobConfig.FromProperties(item.Properties) : new JobConfig());
        }

        public override void ClearUserControl() { CurrentItem = null; _editor?.ClearContent(); }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem == null || _editor == null) return true;
            var error = _editor.ValidateInput();
            if (error != null)
            {
                MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var job = _editor.ToConfig();
            CurrentItem.Name = job.Name;
            CurrentItem.Properties.Clear();
            foreach (var kv in job.ToProperties()) CurrentItem.Properties[kv.Key] = kv.Value;
            Configuration.Instance.SaveItemConfiguration(Ids.PluginId, CurrentItem);
            return true;
        }

        public override string GetItemName() => _editor?.JobName ?? CurrentItem?.Name ?? "Job";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = name; }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, _kind) ?? new List<Item>();

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(Ids.PluginId, parentItem, _kind) ?? new List<Item>();

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(Ids.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New Job");
            foreach (var kv in new JobConfig().ToProperties())
                CurrentItem.Properties[kv.Key] = kv.Value;
            Configuration.Instance.SaveItemConfiguration(Ids.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null) Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item)
        {
            if (item?.Properties == null) return OperationalState.Disabled;
            return JobConfig.FromProperties(item.Properties).Enabled ? OperationalState.Ok : OperationalState.Disabled;
        }

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item?.Properties == null) return "";
            var j = JobConfig.FromProperties(item.Properties);
            return j.Enabled ? $"{j.Format} on '{AgentsUserControl.FriendlyName(j.AgentHostname)}'" : "Disabled";
        }

        private static List<AgentRegistration> Agents()
            => AgentsUserControl.ReadAgents().Where(a => !string.IsNullOrWhiteSpace(a.Hostname)).ToList();
    }
}
