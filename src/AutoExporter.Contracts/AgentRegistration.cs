using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// The self-registered Agent node (kind = <see cref="Ids.AgentKindId"/>). The running
    /// service creates/updates this item via the in-proc Configuration API so it appears under
    /// Exporter -> Agents in the Management Client. The agent item's ObjectId is derived
    /// deterministically from the hostname so restarts round-trip to the same node.
    /// </summary>
    public sealed class AgentRegistration
    {
        public string Hostname = "";
        public string Version = "";
        public string Status = "Online";   // Online | Offline
        public DateTime LastSeenUtc;
        public string ExportFolder = "";    // informational, for the admin to see
        public int MaxGB;                    // agent-wide export-folder size cap (0 = unlimited)
        public int RetentionDays;            // agent-wide retention (0 = unlimited)
        public string DisplayName = "";      // admin-set friendly name (UI only), preserved by the agent

        public static class Keys
        {
            public const string Hostname = "Hostname";
            public const string Version = "Version";
            public const string Status = "Status";
            public const string LastSeenUtc = "LastSeenUtc";
            public const string ExportFolder = "ExportFolder";
            public const string MaxGB = "MaxGB";
            public const string RetentionDays = "RetentionDays";
            public const string DisplayName = "DisplayName";
        }

        /// <summary>
        /// Deterministic ObjectId for an agent item, derived from the hostname so the
        /// service always writes to the same node (no duplicates on restart).
        /// </summary>
        public static Guid ObjectIdFor(string hostname)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    "AutoExporter.Agent:" + (hostname ?? "").ToLowerInvariant()));
                return new Guid(bytes);
            }
        }

        public IDictionary<string, string> ToProperties() => new Dictionary<string, string>
        {
            [Keys.Hostname] = Hostname ?? "",
            [Keys.Version] = Version ?? "",
            [Keys.Status] = Status ?? "Online",
            [Keys.LastSeenUtc] = LastSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            [Keys.ExportFolder] = ExportFolder ?? "",
            [Keys.MaxGB] = MaxGB.ToString(CultureInfo.InvariantCulture),
            [Keys.RetentionDays] = RetentionDays.ToString(CultureInfo.InvariantCulture),
            [Keys.DisplayName] = DisplayName ?? "",
        };

        public static AgentRegistration FromProperties(IDictionary<string, string> p)
        {
            string Get(string k) => p != null && p.TryGetValue(k, out var v) && v != null ? v : "";
            int GetInt(string k) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            DateTime.TryParse(Get(Keys.LastSeenUtc), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var seen);
            return new AgentRegistration
            {
                Hostname = Get(Keys.Hostname),
                Version = Get(Keys.Version),
                Status = string.IsNullOrEmpty(Get(Keys.Status)) ? "Online" : Get(Keys.Status),
                LastSeenUtc = seen,
                ExportFolder = Get(Keys.ExportFolder),
                MaxGB = GetInt(Keys.MaxGB),
                RetentionDays = GetInt(Keys.RetentionDays),
                DisplayName = Get(Keys.DisplayName),
            };
        }

        /// <summary>Hostname unless a friendly display name was set.</summary>
        public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? Hostname : DisplayName;
    }
}
