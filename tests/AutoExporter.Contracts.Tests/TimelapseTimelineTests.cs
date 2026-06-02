using System;
using System.Collections.Generic;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class TimelapseTimelineTests
    {
        private static RecordingSegment Seg(string start, string end)
            => new RecordingSegment(DateTime.Parse(start), DateTime.Parse(end));

        [Fact]
        public void GenerateContinuous_WalksEachSegmentAtInterval()
        {
            var segments = new List<RecordingSegment> { Seg("2026-06-01 10:00:00", "2026-06-01 10:00:30") };

            var stamps = TimestampGenerator.GenerateContinuous(segments, TimeSpan.FromSeconds(10));

            // 10:00:00, :10, :20, :30
            Assert.Equal(4, stamps.Count);
            Assert.Equal(DateTime.Parse("2026-06-01 10:00:00"), stamps[0]);
            Assert.Equal(DateTime.Parse("2026-06-01 10:00:30"), stamps[3]);
        }

        [Fact]
        public void GenerateContinuous_SkipsGapsBetweenSegments()
        {
            var segments = new List<RecordingSegment>
            {
                Seg("2026-06-01 10:00:00", "2026-06-01 10:00:20"),
                Seg("2026-06-01 14:00:00", "2026-06-01 14:00:10"),
            };

            var stamps = TimestampGenerator.GenerateContinuous(segments, TimeSpan.FromSeconds(10));

            // 3 from the first block, 2 from the second; nothing in the 4 hour gap.
            Assert.Equal(5, stamps.Count);
            Assert.DoesNotContain(DateTime.Parse("2026-06-01 12:00:00"), stamps);
        }

        [Fact]
        public void GenerateContinuous_EmptyOrZeroInterval_IsSafe()
        {
            Assert.Empty(TimestampGenerator.GenerateContinuous(new List<RecordingSegment>(), TimeSpan.FromSeconds(10)));
            Assert.Empty(TimestampGenerator.GenerateContinuous(null, TimeSpan.FromSeconds(10)));

            // Zero interval is coerced to 1 second rather than looping forever.
            var stamps = TimestampGenerator.GenerateContinuous(
                new List<RecordingSegment> { Seg("2026-06-01 10:00:00", "2026-06-01 10:00:02") },
                TimeSpan.Zero);
            Assert.Equal(3, stamps.Count);
        }

        [Fact]
        public void MergeAdjacent_MergesWithinGap_AndKeepsSeparateBeyondIt()
        {
            var segments = new List<RecordingSegment>
            {
                Seg("2026-06-01 10:00:00", "2026-06-01 10:00:10"),
                Seg("2026-06-01 10:00:12", "2026-06-01 10:00:20"),  // 2s gap -> merged
                Seg("2026-06-01 11:00:00", "2026-06-01 11:00:05"),  // far -> separate
            };

            var merged = TimestampGenerator.MergeAdjacent(segments, TimeSpan.FromSeconds(5));

            Assert.Equal(2, merged.Count);
            Assert.Equal(DateTime.Parse("2026-06-01 10:00:00"), merged[0].Start);
            Assert.Equal(DateTime.Parse("2026-06-01 10:00:20"), merged[0].End);
        }

        [Fact]
        public void ClipToDailyWindow_KeepsOnlyTheInWindowPortion()
        {
            // A full day of footage, window 08:00 to 17:00.
            var segments = new List<RecordingSegment> { Seg("2026-06-01 00:00:00", "2026-06-01 23:59:59") };

            var clipped = TimestampGenerator.ClipToDailyWindow(
                segments, TimeSpan.FromHours(8), TimeSpan.FromHours(17));

            Assert.Single(clipped);
            Assert.Equal(DateTime.Parse("2026-06-01 08:00:00"), clipped[0].Start);
            Assert.Equal(DateTime.Parse("2026-06-01 17:00:00"), clipped[0].End);
        }

        [Fact]
        public void ClipToDailyWindow_WrappingWindow_SpansMidnight()
        {
            // Footage across two days, night window 22:00 to 06:00.
            var segments = new List<RecordingSegment> { Seg("2026-06-01 00:00:00", "2026-06-02 23:59:59") };

            var clipped = TimestampGenerator.ClipToDailyWindow(
                segments, TimeSpan.FromHours(22), TimeSpan.FromHours(6));

            // Expect an early-morning piece (..06:00) and an evening piece (22:00..) on the covered days.
            Assert.Contains(clipped, s => s.End == DateTime.Parse("2026-06-01 06:00:00"));
            Assert.Contains(clipped, s => s.Start == DateTime.Parse("2026-06-01 22:00:00"));
            Assert.All(clipped, s => Assert.True(s.End > s.Start));
        }

        [Fact]
        public void Covers_FindsTimestampInsideASegment()
        {
            var segments = new List<RecordingSegment>
            {
                Seg("2026-06-01 10:00:00", "2026-06-01 10:30:00"),
                Seg("2026-06-01 12:00:00", "2026-06-01 12:30:00"),
            };

            Assert.True(TimestampGenerator.Covers(segments, DateTime.Parse("2026-06-01 12:15:00")));
            Assert.False(TimestampGenerator.Covers(segments, DateTime.Parse("2026-06-01 11:00:00")));
        }

        [Fact]
        public void TotalDuration_SumsSegments()
        {
            var segments = new List<RecordingSegment>
            {
                Seg("2026-06-01 10:00:00", "2026-06-01 10:10:00"),
                Seg("2026-06-01 11:00:00", "2026-06-01 11:05:00"),
            };

            Assert.Equal(TimeSpan.FromMinutes(15), TimestampGenerator.TotalDuration(segments));
        }
    }
}
