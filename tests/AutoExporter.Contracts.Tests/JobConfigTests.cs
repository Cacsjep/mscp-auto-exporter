using System;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class JobConfigTests
    {
        [Fact]
        public void RoundTrips_AllFieldsAndTargets()
        {
            var job = new JobConfig
            {
                Name = "Nightly",
                Enabled = false,
                AgentHostname = "ACS01",
                Format = "AVI",
                Encrypt = true,
                Sign = true,
                Password = "pw",
                IncludeAudio = false,
                Timestamp = true,
                RangeValue = 12,
                RangeUnit = "Hours",
                TimelapseMode = "EventBased",
                TimelapseIntervalSeconds = 30,
                TimelapseFps = 15,
                TimelapseEventIntervalSeconds = 5,
                TimelapseEventMaxFrames = 20,
                TimelapseEventMinFrames = 2,
                TimelapseEventMergeGapSeconds = 4,
                TimelapseDailyEnabled = true,
                TimelapseDailyStart = "06:30",
                TimelapseDailyEnd = "21:45",
            };
            job.Targets.Add(new JobTarget { Kind = "Camera", ObjectId = Guid.NewGuid(), Name = "Front" });
            job.Targets.Add(new JobTarget { Kind = "Group", ObjectId = Guid.NewGuid(), Name = "Lobby" });

            var loaded = JobConfig.FromProperties(job.ToProperties());

            Assert.Equal("Nightly", loaded.Name);
            Assert.False(loaded.Enabled);
            Assert.Equal("ACS01", loaded.AgentHostname);
            Assert.Equal("AVI", loaded.Format);
            Assert.True(loaded.Encrypt);
            Assert.True(loaded.Sign);
            Assert.Equal("pw", loaded.Password);
            Assert.False(loaded.IncludeAudio);
            Assert.True(loaded.Timestamp);
            Assert.Equal(12, loaded.RangeValue);
            Assert.Equal("Hours", loaded.RangeUnit);
            Assert.Equal("EventBased", loaded.TimelapseMode);
            Assert.Equal(30, loaded.TimelapseIntervalSeconds);
            Assert.Equal(15, loaded.TimelapseFps);
            Assert.Equal(5, loaded.TimelapseEventIntervalSeconds);
            Assert.Equal(20, loaded.TimelapseEventMaxFrames);
            Assert.Equal(2, loaded.TimelapseEventMinFrames);
            Assert.Equal(4, loaded.TimelapseEventMergeGapSeconds);
            Assert.True(loaded.TimelapseDailyEnabled);
            Assert.Equal("06:30", loaded.TimelapseDailyStart);
            Assert.Equal("21:45", loaded.TimelapseDailyEnd);
            Assert.Equal(2, loaded.Targets.Count);
            Assert.Equal("Camera", loaded.Targets[0].Kind);
            Assert.Equal("Front", loaded.Targets[0].Name);
            Assert.Equal(job.Targets[1].ObjectId, loaded.Targets[1].ObjectId);
        }

        [Fact]
        public void Defaults_WhenPropertiesEmpty()
        {
            var loaded = JobConfig.FromProperties(new System.Collections.Generic.Dictionary<string, string>());
            Assert.Equal("New Job", loaded.Name);
            Assert.True(loaded.Enabled);
            Assert.Equal("XProtect", loaded.Format);
            Assert.False(loaded.Encrypt);
            Assert.False(loaded.Sign);
            Assert.Equal(1, loaded.RangeValue);
            Assert.Equal("Days", loaded.RangeUnit);
            Assert.Equal("Continuous", loaded.TimelapseMode);
            Assert.Equal(60, loaded.TimelapseIntervalSeconds);
            Assert.Equal(24, loaded.TimelapseFps);
            Assert.False(loaded.TimelapseDailyEnabled);
            Assert.Empty(loaded.Targets);
        }

        [Fact]
        public void EnabledFlag_UsesYesNo()
        {
            Assert.Equal("Yes", new JobConfig { Enabled = true }.ToProperties()[JobConfig.Keys.Enabled]);
            Assert.Equal("No", new JobConfig { Enabled = false }.ToProperties()[JobConfig.Keys.Enabled]);
        }

        [Fact]
        public void TargetsCount_MatchesSerializedTargets()
        {
            var job = new JobConfig();
            job.Targets.Add(new JobTarget { Kind = "Camera", ObjectId = Guid.NewGuid(), Name = "A" });
            var props = job.ToProperties();
            Assert.Equal("1", props[JobConfig.Keys.TargetsCount]);
            Assert.Equal("A", props["Targets_0_Name"]);
        }
    }
}
