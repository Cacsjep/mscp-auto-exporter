using System;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Sent by the agent to the Event Server bridge (<see cref="Messages.JobEvent"/>) so the
    /// bridge raises the registered MIP event (JobStarted / JobSucceeded / JobFailed) with the
    /// job item as the source. The agent cannot raise these itself because the EventSource lives
    /// in the Event Server context.
    ///
    /// Transmitted as a pipe-delimited string (see <see cref="Encode"/>).
    /// </summary>
    public sealed class JobEventNotice
    {
        public const string KindStarted   = "Started";
        public const string KindSucceeded = "Succeeded";
        public const string KindFailed    = "Failed";

        public Guid JobObjectId;
        public string Kind;     // Started | Succeeded | Failed
        public string Detail;   // human readable detail (becomes the event CustomTag)

        private const string Version = "v1";

        public string Encode() => string.Join("|", new[]
        {
            Version,
            JobObjectId.ToString("D"),
            Sanitize(Kind),
            Sanitize(Detail),
        });

        public static JobEventNotice Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split('|');
            if (p.Length < 4 || p[0] != Version) return null;
            Guid.TryParse(p[1], out var job);
            return new JobEventNotice { JobObjectId = job, Kind = p[2], Detail = p[3] };
        }

        private static string Sanitize(string s) => (s ?? "").Replace("|", "/");
    }
}
