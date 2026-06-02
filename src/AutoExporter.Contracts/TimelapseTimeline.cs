using System;
using System.Collections.Generic;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// A single recorded block on a camera, clipped to the query window. Times are local.
    /// </summary>
    public sealed class RecordingSegment
    {
        public DateTime Start { get; }
        public DateTime End { get; }
        public TimeSpan Duration => End - Start;

        public RecordingSegment(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Pure timestamp-list generators for timelapse sampling. No MIP dependency, so the agent and
    /// the unit tests both use them. Ported from the Timelapse Smart Client plugin, keeping the
    /// gap-skipping and wrapping daily-window behaviour intact. The agent feeds these the recording
    /// segments it gets from the MIP SequenceDataSource, then grabs one frame per returned timestamp.
    /// </summary>
    public static class TimestampGenerator
    {
        /// <summary>
        /// Continuous mode: walk each segment at <paramref name="interval"/> spacing. Gaps between
        /// segments are naturally skipped (a 3 day range with 1 hour of footage yields only the
        /// frames that exist, not thousands of blanks).
        /// </summary>
        public static List<DateTime> GenerateContinuous(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan interval)
        {
            var list = new List<DateTime>();
            if (segments == null || segments.Count == 0) return list;
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);

            foreach (var seg in segments)
            {
                for (var t = seg.Start; t <= seg.End; t += interval)
                    list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Merges segments whose gap is &lt;= <paramref name="mergeGap"/> into one. Assumes input
        /// sorted by Start.
        /// </summary>
        public static List<RecordingSegment> MergeAdjacent(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan mergeGap)
        {
            var result = new List<RecordingSegment>();
            if (segments == null || segments.Count == 0) return result;

            var curStart = segments[0].Start;
            var curEnd = segments[0].End;

            for (int i = 1; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s.Start - curEnd <= mergeGap)
                {
                    if (s.End > curEnd) curEnd = s.End;
                }
                else
                {
                    result.Add(new RecordingSegment(curStart, curEnd));
                    curStart = s.Start;
                    curEnd = s.End;
                }
            }
            result.Add(new RecordingSegment(curStart, curEnd));
            return result;
        }

        /// <summary>
        /// Sum of segment durations.
        /// </summary>
        public static TimeSpan TotalDuration(IReadOnlyList<RecordingSegment> segments)
        {
            if (segments == null) return TimeSpan.Zero;
            long ticks = 0;
            foreach (var s in segments) ticks += s.Duration.Ticks;
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// Clips each segment to a per-day time window. <paramref name="dailyStart"/> and
        /// <paramref name="dailyEnd"/> are wall-clock times-of-day. If dailyEnd &lt;= dailyStart the
        /// window wraps midnight (e.g. 22:00 to 06:00). A segment spanning multiple days is split
        /// into one piece per day.
        /// </summary>
        public static List<RecordingSegment> ClipToDailyWindow(
            IReadOnlyList<RecordingSegment> segments,
            TimeSpan dailyStart,
            TimeSpan dailyEnd)
        {
            var result = new List<RecordingSegment>();
            if (segments == null || segments.Count == 0) return result;
            if (dailyStart < TimeSpan.Zero) dailyStart = TimeSpan.Zero;
            if (dailyEnd < TimeSpan.Zero) dailyEnd = TimeSpan.Zero;
            if (dailyStart >= TimeSpan.FromDays(1)) dailyStart = TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1);
            if (dailyEnd > TimeSpan.FromDays(1)) dailyEnd = TimeSpan.FromDays(1);

            bool wraps = dailyEnd <= dailyStart;

            foreach (var seg in segments)
            {
                // One day before too, to catch a wrapping window that started the previous day.
                var firstDay = seg.Start.Date.AddDays(wraps ? -1 : 0);
                var lastDay = seg.End.Date;

                for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
                {
                    DateTime winStart, winEnd;
                    if (wraps)
                    {
                        winStart = day + dailyStart;
                        winEnd = day.AddDays(1) + dailyEnd;
                    }
                    else
                    {
                        winStart = day + dailyStart;
                        winEnd = day + dailyEnd;
                    }

                    var s = seg.Start > winStart ? seg.Start : winStart;
                    var e = seg.End < winEnd ? seg.End : winEnd;
                    if (e > s) result.Add(new RecordingSegment(s, e));
                }
            }

            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        /// <summary>
        /// True if <paramref name="ts"/> falls inside any segment. O(log n), assumes sorted input.
        /// </summary>
        public static bool Covers(IReadOnlyList<RecordingSegment> segments, DateTime ts)
        {
            if (segments == null || segments.Count == 0) return false;
            int lo = 0, hi = segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var s = segments[mid];
                if (ts < s.Start) hi = mid - 1;
                else if (ts > s.End) lo = mid + 1;
                else return true;
            }
            return false;
        }
    }
}
