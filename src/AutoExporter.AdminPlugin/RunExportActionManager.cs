using System;
using System.Collections.ObjectModel;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.RuleAction;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Registers the "Execute Auto Export Job" rule action. When a rule fires it, the action
    /// runs in the Event Server and hands off to the bridge, which sends a MIP RunJob message
    /// to the agent that owns the job (resolved from the job item's parent agent node).
    /// </summary>
    public class RunExportActionManager : ActionManager
    {
        public override Collection<ActionDefinition> GetActionDefinitions()
        {
            return new Collection<ActionDefinition>
            {
                new ActionDefinition
                {
                    Id = Ids.RunExportActionId,
                    Name = "Execute Auto Export Job",
                    SelectionText = "Execute <Auto Export Job>",
                    DescriptionText = "Execute {0}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "Auto Export Job",
                        ItemKinds = new Collection<Guid> { Ids.JobKindId }
                    }
                }
            };
        }

        public override void ExecuteAction(Guid actionId, Collection<FQID> actionItems, BaseEvent sourceEvent)
        {
            if (actionId != Ids.RunExportActionId) return;

            foreach (var fqid in actionItems)
            {
                var host = ResolveAgentHostname(fqid);
                if (string.IsNullOrEmpty(host))
                {
                    PluginFileLog.Error($"Rule action: could not resolve owning agent for job {fqid.ObjectId}; skipped.");
                    continue;
                }

                var bridge = BridgeBackgroundPlugin.Instance;
                if (bridge == null)
                {
                    PluginFileLog.Error($"Rule action: the Event Server bridge is not available; job {fqid.ObjectId} was not sent.");
                    continue;
                }

                bridge.SendRunJob(new TriggerRequest
                {
                    JobObjectId = fqid.ObjectId,
                    AgentHostname = host,
                    TriggerSource = "Rule",
                });
            }
        }

        /// <summary>The job stores which agent runs it in its AgentHostname property.</summary>
        private static string ResolveAgentHostname(FQID jobFqid)
        {
            try
            {
                var jobItem = Configuration.Instance.GetItemConfiguration(
                    Ids.PluginId, Ids.JobKindId, jobFqid.ObjectId);
                if (jobItem?.Properties == null) return null;
                return JobConfig.FromProperties(jobItem.Properties).AgentHostname;
            }
            catch (Exception ex)
            {
                PluginFileLog.Error("ResolveAgentHostname failed: " + ex.Message);
                return null;
            }
        }
    }
}
