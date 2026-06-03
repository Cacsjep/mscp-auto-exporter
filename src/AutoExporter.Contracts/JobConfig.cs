using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoExporter.Contracts
{
    /// <summary>What a job exports against.</summary>
    public sealed class JobTarget
    {
        public string Kind;      // "Camera" | "Group"
        public Guid ObjectId;
        public string Name;      // cached display name at config time
    }

    /// <summary>
    /// Job definition. Persisted as an MIP configuration Item (kind = <see cref="Ids.JobKindId"/>),
    /// child of an Agent item. Property keys are kept identical to the legacy plugin where it
    /// makes sense so existing export logic ports cleanly.
    /// </summary>
    public sealed class JobConfig
    {
        public string Name = "New Job";
        public bool Enabled = true;
        public string AgentHostname = "";     // which registered agent runs this job
        public string Format = "XProtect";   // "XProtect" | "AVI" | "Timelapse"
        public bool Encrypt;
        public bool Sign;                      // XProtect: sign the export (off by default)
        public string Password = "";
        // Audio is always exported. The Smart Client ™ Player ("include player") is intentionally
        // not offered: the SDK can only bundle the player when the export runs inside the Smart
        // Client itself, so a standalone agent can never produce it. See the plugin help page.
        public bool IncludeAudio = true;
        public bool Timestamp;                 // AVI and Timelapse: burn the recording time into the video frames
        public int RangeValue = 1;
        public string RangeUnit = "Days";     // Minutes | Hours | Days | Months
        public List<JobTarget> Targets = new List<JobTarget>();

        // ----- Timelapse options (Format = "Timelapse") -----
        // One MP4 per camera, played back at TimelapseFps. Recording gaps are always skipped. When
        // the daily window is enabled, only footage inside DailyStart..DailyEnd (local time of day)
        // is used. Two capture modes:
        //   Continuous  - sample one frame every TimelapseIntervalSeconds of footage.
        //   EventBased  - per recorded clip (segments merged when closer than the merge gap) take
        //                 the first frame plus one every TimelapseEventIntervalSeconds, between
        //                 TimelapseEventMinFrames and TimelapseEventMaxFrames frames.
        public string TimelapseMode = "Continuous";   // "Continuous" | "EventBased"
        public int TimelapseFps = 24;
        public int TimelapseIntervalSeconds = 60;      // Continuous: seconds of footage per frame
        public int TimelapseEventIntervalSeconds = 10; // EventBased: seconds between frames in a clip
        public int TimelapseEventMaxFrames = 10;
        public int TimelapseEventMinFrames = 1;
        public int TimelapseEventMergeGapSeconds = 2;  // clips closer than this count as one event
        public bool TimelapseDailyEnabled;
        public string TimelapseDailyStart = "08:00";
        public string TimelapseDailyEnd = "17:00";

        public static class Keys
        {
            public const string Name = "Name";
            public const string Enabled = "Enabled";
            public const string AgentHostname = "AgentHostname";
            public const string Format = "Format";
            public const string Encrypt = "Encrypt";
            public const string Sign = "Sign";
            public const string Password = "Password";
            public const string IncludeAudio = "IncludeAudio";
            public const string Timestamp = "Timestamp";
            public const string RangeValue = "RangeValue";
            public const string RangeUnit = "RangeUnit";
            public const string TimelapseMode = "TimelapseMode";
            public const string TimelapseFps = "TimelapseFps";
            public const string TimelapseIntervalSeconds = "TimelapseIntervalSeconds";
            public const string TimelapseEventIntervalSeconds = "TimelapseEventIntervalSeconds";
            public const string TimelapseEventMaxFrames = "TimelapseEventMaxFrames";
            public const string TimelapseEventMinFrames = "TimelapseEventMinFrames";
            public const string TimelapseEventMergeGapSeconds = "TimelapseEventMergeGapSeconds";
            public const string TimelapseDailyEnabled = "TimelapseDailyEnabled";
            public const string TimelapseDailyStart = "TimelapseDailyStart";
            public const string TimelapseDailyEnd = "TimelapseDailyEnd";
            public const string TargetsCount = "Targets_Count";
            // Per-target: Targets_{i}_Kind / _ObjectId / _Name
        }

        public IDictionary<string, string> ToProperties()
        {
            var p = new Dictionary<string, string>
            {
                [Keys.Name] = Name ?? "",
                [Keys.Enabled] = Enabled ? "Yes" : "No",
                [Keys.AgentHostname] = AgentHostname ?? "",
                [Keys.Format] = Format ?? "XProtect",
                [Keys.Encrypt] = Encrypt ? "Yes" : "No",
                [Keys.Sign] = Sign ? "Yes" : "No",
                [Keys.Password] = Password ?? "",
                [Keys.IncludeAudio] = IncludeAudio ? "Yes" : "No",
                [Keys.Timestamp] = Timestamp ? "Yes" : "No",
                [Keys.RangeValue] = RangeValue.ToString(CultureInfo.InvariantCulture),
                [Keys.RangeUnit] = RangeUnit ?? "Days",
                [Keys.TimelapseMode] = TimelapseMode ?? "Continuous",
                [Keys.TimelapseFps] = TimelapseFps.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseIntervalSeconds] = TimelapseIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseEventIntervalSeconds] = TimelapseEventIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseEventMaxFrames] = TimelapseEventMaxFrames.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseEventMinFrames] = TimelapseEventMinFrames.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseEventMergeGapSeconds] = TimelapseEventMergeGapSeconds.ToString(CultureInfo.InvariantCulture),
                [Keys.TimelapseDailyEnabled] = TimelapseDailyEnabled ? "Yes" : "No",
                [Keys.TimelapseDailyStart] = TimelapseDailyStart ?? "08:00",
                [Keys.TimelapseDailyEnd] = TimelapseDailyEnd ?? "17:00",
                [Keys.TargetsCount] = Targets.Count.ToString(CultureInfo.InvariantCulture),
            };
            for (int i = 0; i < Targets.Count; i++)
            {
                p[$"Targets_{i}_Kind"] = Targets[i].Kind ?? "Camera";
                p[$"Targets_{i}_ObjectId"] = Targets[i].ObjectId.ToString();
                p[$"Targets_{i}_Name"] = Targets[i].Name ?? "";
            }
            return p;
        }

        public static JobConfig FromProperties(IDictionary<string, string> p)
        {
            var j = new JobConfig
            {
                Name = Get(p, Keys.Name, "New Job"),
                Enabled = IsYes(Get(p, Keys.Enabled, "Yes")),
                AgentHostname = Get(p, Keys.AgentHostname, ""),
                Format = Get(p, Keys.Format, "XProtect"),
                Encrypt = IsYes(Get(p, Keys.Encrypt, "No")),
                Sign = IsYes(Get(p, Keys.Sign, "No")),
                Password = Get(p, Keys.Password, ""),
                IncludeAudio = IsYes(Get(p, Keys.IncludeAudio, "Yes")),
                Timestamp = IsYes(Get(p, Keys.Timestamp, "No")),
                RangeValue = GetInt(p, Keys.RangeValue, 1),
                RangeUnit = Get(p, Keys.RangeUnit, "Days"),
                TimelapseMode = Get(p, Keys.TimelapseMode, "Continuous"),
                TimelapseFps = GetInt(p, Keys.TimelapseFps, 24),
                TimelapseIntervalSeconds = GetInt(p, Keys.TimelapseIntervalSeconds, 60),
                TimelapseEventIntervalSeconds = GetInt(p, Keys.TimelapseEventIntervalSeconds, 10),
                TimelapseEventMaxFrames = GetInt(p, Keys.TimelapseEventMaxFrames, 10),
                TimelapseEventMinFrames = GetInt(p, Keys.TimelapseEventMinFrames, 1),
                TimelapseEventMergeGapSeconds = GetInt(p, Keys.TimelapseEventMergeGapSeconds, 2),
                TimelapseDailyEnabled = IsYes(Get(p, Keys.TimelapseDailyEnabled, "No")),
                TimelapseDailyStart = Get(p, Keys.TimelapseDailyStart, "08:00"),
                TimelapseDailyEnd = Get(p, Keys.TimelapseDailyEnd, "17:00"),
            };
            int count = GetInt(p, Keys.TargetsCount, 0);
            for (int i = 0; i < count; i++)
            {
                Guid.TryParse(Get(p, $"Targets_{i}_ObjectId", ""), out var oid);
                j.Targets.Add(new JobTarget
                {
                    Kind = Get(p, $"Targets_{i}_Kind", "Camera"),
                    ObjectId = oid,
                    Name = Get(p, $"Targets_{i}_Name", ""),
                });
            }
            return j;
        }

        static string Get(IDictionary<string, string> p, string k, string dflt)
            => p != null && p.TryGetValue(k, out var v) && v != null ? v : dflt;

        static bool IsYes(string v) => string.Equals(v, "Yes", StringComparison.OrdinalIgnoreCase);

        static int GetInt(IDictionary<string, string> p, string k, int dflt)
            => int.TryParse(Get(p, k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : dflt;
    }
}
