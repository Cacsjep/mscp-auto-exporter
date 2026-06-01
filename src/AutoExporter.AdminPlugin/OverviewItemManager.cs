using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Forms;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Data;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// The plugin's single tree node. It is a singleton (one auto-created item, so no "add"
    /// option), its detail panel is the tabbed <see cref="OverviewUserControl"/>, and it also
    /// declares the rule events (JobStarted / JobSucceeded / JobFailed) so rules can react.
    /// </summary>
    public class OverviewItemManager : ItemManager
    {
        private readonly Guid _kind;
        private OverviewUserControl _userControl;

        private static readonly Guid SingletonId = new Guid("A1B2C3D4-E5F6-4709-8A1B-2C3D4E5F6071");

        public OverviewItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        // ----- Rule events (sourced from job items) -----
        public override Collection<EventGroup> GetKnownEventGroups(CultureInfo culture)
            => new Collection<EventGroup> { new EventGroup { ID = Ids.EventGroupId, Name = "Auto Exporter" } };

        public override Collection<EventType> GetKnownEventTypes(CultureInfo culture)
        {
            var src = new List<Guid> { Ids.JobKindId };
            EventType Evt(Guid id, string msg) => new EventType
            {
                ID = id, Message = msg, GroupID = Ids.EventGroupId,
                DefaultSourceKind = Ids.JobKindId, SourceKinds = src
            };
            return new Collection<EventType>
            {
                Evt(Ids.EvtJobStartedId,   "Auto Export: Job Started"),
                Evt(Ids.EvtJobSucceededId, "Auto Export: Job Succeeded"),
                Evt(Ids.EvtJobFailedId,    "Auto Export: Job Failed"),
            };
        }

        // ----- Detail page -----
        public override UserControl GenerateDetailUserControl() => _userControl = new OverviewUserControl();
        public override void ReleaseUserControl() { _userControl?.Dispose(); _userControl = null; }
        public override void FillUserControl(Item item) => CurrentItem = item;
        public override void ClearUserControl() => CurrentItem = null;
        public override bool ValidateAndSaveUserControl() => true;

        public override string GetItemName() => "Overview";
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = "Overview"; }

        // ----- Singleton plumbing -----
        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, _kind) ?? new List<Item>();

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(Ids.PluginId, parentItem, _kind) ?? new List<Item>();

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(Ids.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            var fqid = new FQID(suggestedFQID.ServerId, suggestedFQID.ParentId, SingletonId, FolderType.No, _kind);
            CurrentItem = new Item(fqid, "Overview");
            Configuration.Instance.SaveItemConfiguration(Ids.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null) Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, item);
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;
        public override string GetItemStatusDetails(Item item, string language) => "Auto export overview";
    }
}
