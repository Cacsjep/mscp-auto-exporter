using System;
using System.Collections.Generic;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Thin Event Server bridge between rules and agents:
    ///  - forwards rule-fired triggers to the owning agent (RunJob), and
    ///  - receives JobEvent notices from agents and raises the registered MIP events
    ///    (JobStarted / JobSucceeded / JobFailed) with the job item as source.
    /// No heavy lifting happens here.
    /// </summary>
    public class BridgeBackgroundPlugin : BackgroundPlugin
    {
        internal static BridgeBackgroundPlugin Instance { get; private set; }

        private ServerId _serverId;
        private MessageCommunication _mc;
        private object _jobEventFilter;
        private object _removeAgentFilter;

        public override Guid Id => Ids.BackgroundPluginId;
        public override string Name => "Auto Exporter Bridge";

        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            Instance = this;
            try
            {
                _serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(_serverId);
                _mc = MessageCommunicationManager.Get(_serverId);
                _jobEventFilter = _mc.RegisterCommunicationFilter(
                    OnJobEvent, new CommunicationIdFilter(Messages.JobEvent));
                _removeAgentFilter = _mc.RegisterCommunicationFilter(
                    OnRemoveAgent, new CommunicationIdFilter(Messages.RemoveAgent));
                PluginFileLog.Info("Bridge started, MessageCommunication ready.");
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("Bridge init failed: " + ex.Message);
            }
        }

        public override void Close()
        {
            Instance = null;
            try { if (_jobEventFilter != null) _mc?.UnRegisterCommunicationFilter(_jobEventFilter); } catch { }
            try { if (_removeAgentFilter != null) _mc?.UnRegisterCommunicationFilter(_removeAgentFilter); } catch { }
            try { if (_serverId != null) MessageCommunicationManager.Stop(_serverId); } catch { }
            _mc = null;
        }

        /// <summary>Broadcast a RunJob trigger. The agent whose hostname matches runs it.</summary>
        public void SendRunJob(TriggerRequest req)
        {
            if (req.RunId == Guid.Empty) req.RunId = Guid.NewGuid();

            if (_mc == null)
            {
                PluginFileLog.Error($"Cannot send RunJob (no MessageCommunication): job={req.JobObjectId}");
                return;
            }

            var msg = new Message(Messages.RunJob, req.Encode());
            _mc.TransmitMessage(msg, null, null, null);
            PluginFileLog.Info($"Sent RunJob: job={req.JobObjectId} agent='{req.AgentHostname}' run={req.RunId}");
        }

        // ----- Incoming RemoveAgent -> delete the agent item and its jobs (server-side) -----
        //
        // The Management Client cannot write agent kind items (the config REST layer rejects an
        // admin-side write to a kind that is not a registered tree node). The Event Server runs
        // server-side, where the deletion is accepted, so the admin asks the bridge to do it.
        private object OnRemoveAgent(Message message, FQID destination, FQID sender)
        {
            try
            {
                var hostname = (message?.Data as string)?.Trim();
                if (string.IsNullOrEmpty(hostname)) return null;

                int jobs = 0;
                var jobItems = Configuration.Instance.GetItemConfigurations(Ids.PluginId, null, Ids.JobKindId);
                if (jobItems != null)
                    foreach (var job in jobItems)
                    {
                        if (job?.Properties == null) continue;
                        if (!string.Equals(JobConfig.FromProperties(job.Properties).AgentHostname, hostname, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, job); jobs++; }
                        catch (Exception ex) { PluginFileLog.Error($"RemoveAgent: deleting job '{job.Name}' failed: {ex.Message}"); }
                    }

                var objectId = AgentRegistration.ObjectIdFor(hostname);
                var agentItem = Configuration.Instance.GetItemConfiguration(Ids.PluginId, Ids.AgentKindId, objectId);
                if (agentItem != null)
                    Configuration.Instance.DeleteItemConfiguration(Ids.PluginId, agentItem);

                PluginFileLog.Info($"RemoveAgent: removed agent '{hostname}' and {jobs} job(s).");
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("OnRemoveAgent failed: " + ex.Message);
            }
            return null;
        }

        // ----- Incoming JobEvent -> raise MIP event -----

        private object OnJobEvent(Message message, FQID destination, FQID sender)
        {
            try
            {
                var notice = JobEventNotice.Decode(message?.Data as string);
                if (notice == null) return null;

                var job = Configuration.Instance.GetItemConfiguration(Ids.PluginId, Ids.JobKindId, notice.JobObjectId);
                if (job == null)
                {
                    PluginFileLog.Error($"JobEvent: job {notice.JobObjectId} not found.");
                    return null;
                }

                switch (notice.Kind)
                {
                    case JobEventNotice.KindStarted:
                        FireRuleEvent(job, "AutoExportJobStarted", "Auto Export: Job Started", notice.Detail, 4);
                        break;
                    case JobEventNotice.KindSucceeded:
                        FireRuleEvent(job, "AutoExportJobSucceeded", "Auto Export: Job Succeeded", notice.Detail, 5);
                        break;
                    case JobEventNotice.KindFailed:
                        FireRuleEvent(job, "AutoExportJobFailed", "Auto Export: Job Failed", notice.Detail, 3);
                        break;
                }
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("OnJobEvent failed: " + ex.Message);
            }
            return null;
        }

        // Raises a user-defined event so rules can react. Mirrors the proven legacy plugin.
        private void FireRuleEvent(Item job, string type, string message, string customTag, ushort priority)
        {
            try
            {
                var header = new EventHeader
                {
                    ID = Guid.NewGuid(),
                    Class = "Operational",
                    Type = type,
                    Timestamp = DateTime.Now,
                    Name = job.Name,
                    Message = message,
                    CustomTag = customTag ?? "",
                    Priority = priority,
                    Source = new EventSource { Name = job.Name, FQID = job.FQID }
                };

                var ev = new AnalyticsEvent { EventHeader = header };
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.Server.NewEventCommand) { Data = ev, RelatedFQID = job.FQID });

                PluginFileLog.Info($"Raised event {type} for job '{job.Name}'.");
            }
            catch (Exception ex)
            {
                PluginFileLog.Error($"FireRuleEvent {type} failed: {ex.Message}");
            }
        }
    }
}
