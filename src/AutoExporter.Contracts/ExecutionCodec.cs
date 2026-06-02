using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Compact, dependency-free codec for <see cref="ExecutionRecord"/>. One record encodes to a
    /// single line so the agent can both persist records (one per line) and ship a batch of them
    /// to the admin Status view over messaging. No JSON parser needed on either net48 side.
    ///
    /// Layout: fields are joined by the unit separator (0x1F), list items inside a field by the
    /// record separator (0x1E). Any separator, backslash or newline inside a value is backslash
    /// escaped, so a record never spans more than one line.
    /// </summary>
    public static class ExecutionCodec
    {
        private const char Fs = '\u001F';   // field separator
        private const char Ls = '\u001E';   // list item separator
        private const string Version = "v1";

        public static string Encode(ExecutionRecord r)
        {
            var sb = new StringBuilder(256);
            sb.Append(Version);
            F(sb, r.RunId.ToString("D"));
            F(sb, r.JobObjectId.ToString("D"));
            F(sb, r.JobName);
            F(sb, r.AgentHostname);
            F(sb, Iso(r.StartedUtc));
            F(sb, Iso(r.FinishedUtc));
            F(sb, Iso(r.RangeStartUtc));
            F(sb, Iso(r.RangeEndUtc));
            F(sb, r.Format);
            F(sb, r.Trigger);
            F(sb, r.Success ? "1" : "0");
            F(sb, r.Outcome);
            F(sb, r.Error);
            F(sb, r.CameraCount.ToString(CultureInfo.InvariantCulture));
            F(sb, r.BytesWritten.ToString(CultureInfo.InvariantCulture));
            F(sb, r.OutputFolder);
            F(sb, JoinList(r.CameraNames));
            F(sb, JoinList(r.SkippedCameras));
            F(sb, r.Progress.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static ExecutionRecord Decode(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var p = line.Split(Fs);
            // version + 18 payload fields = 19 segments (Progress is an optional 20th, appended later
            // so records written before it still decode with Progress = 0).
            if (p.Length < 19 || p[0] != Version) return null;
            try
            {
                return new ExecutionRecord
                {
                    RunId = ParseGuid(Un(p[1])),
                    JobObjectId = ParseGuid(Un(p[2])),
                    JobName = Un(p[3]),
                    AgentHostname = Un(p[4]),
                    StartedUtc = ParseIso(Un(p[5])),
                    FinishedUtc = ParseIso(Un(p[6])),
                    RangeStartUtc = ParseIso(Un(p[7])),
                    RangeEndUtc = ParseIso(Un(p[8])),
                    Format = Un(p[9]),
                    Trigger = Un(p[10]),
                    Success = Un(p[11]) == "1",
                    Outcome = Un(p[12]),
                    Error = Un(p[13]),
                    CameraCount = ParseInt(Un(p[14])),
                    BytesWritten = ParseLong(Un(p[15])),
                    OutputFolder = Un(p[16]),
                    CameraNames = SplitList(p[17]),
                    SkippedCameras = SplitList(p[18]),
                    Progress = p.Length > 19 ? ParseInt(Un(p[19])) : 0,
                };
            }
            catch { return null; }
        }

        /// <summary>Encode a batch as newline-joined record lines (for a messaging payload).</summary>
        public static string EncodeList(IEnumerable<ExecutionRecord> records)
        {
            var sb = new StringBuilder();
            if (records != null)
                foreach (var r in records)
                {
                    if (r == null) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(Encode(r));
                }
            return sb.ToString();
        }

        public static List<ExecutionRecord> DecodeList(string payload)
        {
            var list = new List<ExecutionRecord>();
            if (string.IsNullOrEmpty(payload)) return list;
            foreach (var line in payload.Split('\n'))
            {
                var rec = Decode(line.TrimEnd('\r'));
                if (rec != null) list.Add(rec);
            }
            return list;
        }

        // ── helpers ─────────────────────────────────────────────────────
        private static void F(StringBuilder sb, string raw) { sb.Append(Fs); sb.Append(Esc(raw)); }

        private static string JoinList(List<string> items)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(Ls);
                sb.Append(items[i] ?? "");
            }
            return sb.ToString();
        }

        // The list field is escaped/unescaped as a whole by Esc/Un, so by the time we split here
        // the separators are back to literal Ls characters.
        private static List<string> SplitList(string field)
        {
            var list = new List<string>();
            var raw = Un(field);
            if (raw.Length == 0) return list;
            foreach (var item in raw.Split(Ls)) list.Add(item);
            return list;
        }

        // Field-level escaping: backslash, both separators, CR and LF.
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case Fs:   sb.Append("\\u"); break;
                    case Ls:   sb.Append("\\l"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Un(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    var n = s[++i];
                    switch (n)
                    {
                        case '\\': sb.Append('\\'); break;
                        case 'u':  sb.Append(Fs); break;
                        case 'l':  sb.Append(Ls); break;
                        case 'n':  sb.Append('\n'); break;
                        default:   sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Iso(DateTime d) => d.ToString("o", CultureInfo.InvariantCulture);
        private static DateTime ParseIso(string s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
        private static Guid ParseGuid(string s) => Guid.TryParse(s, out var g) ? g : Guid.Empty;
        private static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        private static long ParseLong(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0L;
    }
}
