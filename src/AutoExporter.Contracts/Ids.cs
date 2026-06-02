using System;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Stable identifiers shared across the admin plugin, the agent service and the tray.
    /// These are NEW ids (distinct from the legacy event-server AutoExporter plugin) so the
    /// old and new plugins can coexist on a dev box during the migration.
    /// </summary>
    public static class Ids
    {
        // ── Admin plugin ────────────────────────────────────────────────
        public static readonly Guid PluginId           = new Guid("7E2B9C4A-1F3D-4A56-9B07-2C8E5D6F1A30");

        // Thin Event Server bridge that turns rule actions into MIP messages to the agent.
        public static readonly Guid BackgroundPluginId = new Guid("4D6F8A2B-1C3E-4F50-A6B7-8C9D0E1F2A3B");

        // ── Tree item kinds ─────────────────────────────────────────────
        // Storage kinds, not literal tree nodes: one Agent item per agent (the service writes it on
        // self-registration) and one Job item per admin-created job (scoped to an agent by property).
        public static readonly Guid AgentKindId        = new Guid("3C1A7F92-8D4E-4B61-A2F5-6E9D0C3B5471");
        public static readonly Guid JobKindId          = new Guid("9F4D2E18-5A6B-4C7D-8E90-1F2A3B4C5D6E");

        // The single tree node the plugin shows. Clicking it opens our own tabbed page
        // (Executions / Jobs / Agents); everything else is driven by the Configuration API.
        public static readonly Guid OverviewKindId     = new Guid("0D7C2A98-3E14-4B5F-A6C7-D8E9F0A1B2C3");

        // ── Rule action ─────────────────────────────────────────────────
        public static readonly Guid RunExportActionId  = new Guid("5E7A9C1D-2B4F-4068-9A1C-3D5E7F9A1B2C");

        // ── Events raised by the plugin ─────────────────────────────────
        public static readonly Guid EventGroupId       = new Guid("1A2B3C4D-5E6F-4071-8293-A4B5C6D7E8F0");
        public static readonly Guid EvtJobStartedId    = new Guid("2A2B3C4D-5E6F-4071-8293-A4B5C6D7E8F1");
        public static readonly Guid EvtJobSucceededId  = new Guid("3A2B3C4D-5E6F-4071-8293-A4B5C6D7E8F2");
        public static readonly Guid EvtJobFailedId     = new Guid("4A2B3C4D-5E6F-4071-8293-A4B5C6D7E8F3");

        // ── SDK login integration identity (used by the agent) ──────────
        public static readonly Guid IntegrationId      = PluginId;
        public const string IntegrationName            = "Auto Exporter Agent";
        public const string IntegrationManufacturer    = "MSC Community Plugins";

        // The single product version, read from this (Contracts) assembly so it tracks the build
        // version stamped by CI (-p:Version=<tag>). Reported to Milestone at login and shown in the
        // Agents Version column.
        public static readonly string IntegrationVersion =
            typeof(Ids).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>
    /// String message ids exchanged over MIP MessageCommunication between the
    /// Event Server bridge and the agent service.
    /// </summary>
    public static class Messages
    {
        // Event Server bridge -> agent: run a job now (rule trigger). Data = TriggerRequest.
        public const string RunJob   = "AutoExporter.RunJob";

        // agent -> Event Server bridge: raise a MIP job event. Data = JobEventNotice (encoded).
        public const string JobEvent = "AutoExporter.JobEvent";

        // admin Status view -> agents: send your recent executions. Data = empty (broadcast).
        public const string QueryExecutions = "AutoExporter.QueryExecutions";

        // agent -> admin Status view: a batch of recent executions. Data = ExecutionCodec.EncodeList.
        public const string ExecutionsReply = "AutoExporter.ExecutionsReply";

        // admin Status view -> agents: clear the execution history. Data = empty (broadcast).
        public const string ClearExecutions = "AutoExporter.ClearExecutions";

        // admin -> Event Server bridge: remove an offline agent (and its jobs) server-side. The
        // Management Client cannot write agent kind items itself, so the bridge does it. Data = the
        // agent hostname.
        public const string RemoveAgent = "AutoExporter.RemoveAgent";

        // admin Agents view -> agents: are you alive? Data = empty (broadcast).
        public const string AgentPing = "AutoExporter.AgentPing";

        // agent -> admin Agents view: yes, I am alive. Data = the agent hostname. Used for the live
        // Online/Offline status, because the Management Client caches config and its LastSeenUtc
        // read goes stale.
        public const string AgentPong = "AutoExporter.AgentPong";
    }
}
