using System;
using System.Collections.Generic;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class ExecutionCodecTests
    {
        private static ExecutionRecord Sample() => new ExecutionRecord
        {
            RunId = Guid.NewGuid(),
            JobObjectId = Guid.NewGuid(),
            JobName = "Nightly",
            AgentHostname = "ACS01",
            StartedUtc = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc),
            FinishedUtc = new DateTime(2026, 6, 1, 3, 5, 0, DateTimeKind.Utc),
            RangeStartUtc = new DateTime(2026, 5, 31, 3, 0, 0, DateTimeKind.Utc),
            RangeEndUtc = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc),
            Format = "XProtect",
            Trigger = "Manual",
            Success = true,
            Outcome = "Partial",
            Error = "",
            CameraCount = 3,
            BytesWritten = 1234567890,
            OutputFolder = @"C:\Exports\Nightly\01.06.2026_0300",
            CameraNames = new List<string> { "Front", "Back" },
            SkippedCameras = new List<string> { "Disabled cam" },
        };

        [Fact]
        public void RoundTrips_AllFields()
        {
            var r = Sample();
            var decoded = ExecutionCodec.Decode(ExecutionCodec.Encode(r));

            Assert.NotNull(decoded);
            Assert.Equal(r.RunId, decoded.RunId);
            Assert.Equal(r.JobObjectId, decoded.JobObjectId);
            Assert.Equal(r.JobName, decoded.JobName);
            Assert.Equal(r.AgentHostname, decoded.AgentHostname);
            Assert.Equal(r.StartedUtc, decoded.StartedUtc);
            Assert.Equal(r.FinishedUtc, decoded.FinishedUtc);
            Assert.Equal(r.Format, decoded.Format);
            Assert.Equal(r.Trigger, decoded.Trigger);
            Assert.True(decoded.Success);
            Assert.Equal("Partial", decoded.Outcome);
            Assert.Equal(3, decoded.CameraCount);
            Assert.Equal(1234567890, decoded.BytesWritten);
            Assert.Equal(r.OutputFolder, decoded.OutputFolder);
            Assert.Equal(new[] { "Front", "Back" }, decoded.CameraNames);
            Assert.Equal(new[] { "Disabled cam" }, decoded.SkippedCameras);
        }

        [Fact]
        public void EncodesToSingleLine_EvenWithNewlinesAndSeparators()
        {
            var r = Sample();
            r.Error = "line1\nline2\twithsepand\\backslash";
            var encoded = ExecutionCodec.Encode(r);
            Assert.DoesNotContain("\n", encoded);

            var decoded = ExecutionCodec.Decode(encoded);
            Assert.Equal("line1\nline2\twithsepand\\backslash", decoded.Error);
        }

        [Fact]
        public void List_RoundTrips_NewestKept()
        {
            var batch = new List<ExecutionRecord> { Sample(), Sample(), Sample() };
            var payload = ExecutionCodec.EncodeList(batch);
            var back = ExecutionCodec.DecodeList(payload);
            Assert.Equal(3, back.Count);
        }

        [Fact]
        public void Decode_ReturnsNull_OnGarbageOrVersionMismatch()
        {
            Assert.Null(ExecutionCodec.Decode(null));
            Assert.Null(ExecutionCodec.Decode(""));
            Assert.Null(ExecutionCodec.Decode("not a record"));
            Assert.Null(ExecutionCodec.Decode("v0only"));
        }

        [Fact]
        public void DecodeList_SkipsBadLines()
        {
            var good = ExecutionCodec.Encode(Sample());
            var payload = good + "\nrubbish\n" + ExecutionCodec.Encode(Sample());
            Assert.Equal(2, ExecutionCodec.DecodeList(payload).Count);
        }

        [Fact]
        public void EmptyLists_RoundTripAsEmpty()
        {
            var r = Sample();
            r.CameraNames = new List<string>();
            r.SkippedCameras = new List<string>();
            var decoded = ExecutionCodec.Decode(ExecutionCodec.Encode(r));
            Assert.Empty(decoded.CameraNames);
            Assert.Empty(decoded.SkippedCameras);
        }
    }
}
