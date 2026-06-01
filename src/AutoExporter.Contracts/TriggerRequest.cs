using System;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Payload sent over MIP MessageCommunication (<see cref="Messages.RunJob"/>) from the
    /// Event Server bridge to the agent that owns the job, when a rule fires the export action.
    ///
    /// Transmitted as a compact pipe-delimited string (see <see cref="Encode"/>) rather than a
    /// binary-serialized object, so it crosses the process/machine boundary without depending on
    /// serializer type resolution.
    /// </summary>
    public sealed class TriggerRequest
    {
        public Guid JobObjectId;
        public string AgentHostname;     // which agent should run it (exact match)
        public string TriggerSource;     // "Rule" | "Manual"
        public Guid RunId;               // correlation id for status/progress

        private const string Version = "v1";

        public string Encode() => string.Join("|", new[]
        {
            Version,
            JobObjectId.ToString("D"),
            Sanitize(AgentHostname),
            Sanitize(TriggerSource),
            RunId.ToString("D"),
        });

        public static TriggerRequest Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split('|');
            if (p.Length < 5 || p[0] != Version) return null;
            Guid.TryParse(p[1], out var job);
            Guid.TryParse(p[4], out var run);
            return new TriggerRequest
            {
                JobObjectId = job,
                AgentHostname = p[2],
                TriggerSource = p[3],
                RunId = run,
            };
        }

        // Hostnames / trigger sources never legitimately contain '|'; strip just in case.
        private static string Sanitize(string s) => (s ?? "").Replace("|", "");
    }
}
