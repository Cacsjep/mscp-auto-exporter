using System;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class MessageCodecTests
    {
        [Fact]
        public void TriggerRequest_RoundTrips()
        {
            var req = new TriggerRequest
            {
                JobObjectId = Guid.NewGuid(),
                AgentHostname = "ACS01",
                TriggerSource = "Manual",
                RunId = Guid.NewGuid(),
            };
            var back = TriggerRequest.Decode(req.Encode());
            Assert.NotNull(back);
            Assert.Equal(req.JobObjectId, back.JobObjectId);
            Assert.Equal(req.AgentHostname, back.AgentHostname);
            Assert.Equal(req.TriggerSource, back.TriggerSource);
            Assert.Equal(req.RunId, back.RunId);
        }

        [Fact]
        public void TriggerRequest_StripsPipes()
        {
            var req = new TriggerRequest { AgentHostname = "a|b", TriggerSource = "x|y", RunId = Guid.NewGuid() };
            var back = TriggerRequest.Decode(req.Encode());
            Assert.Equal("ab", back.AgentHostname);
            Assert.Equal("xy", back.TriggerSource);
        }

        [Fact]
        public void TriggerRequest_Decode_NullOnGarbage()
        {
            Assert.Null(TriggerRequest.Decode(null));
            Assert.Null(TriggerRequest.Decode(""));
            Assert.Null(TriggerRequest.Decode("nope"));
        }

        [Fact]
        public void JobEventNotice_RoundTrips()
        {
            var n = new JobEventNotice { JobObjectId = Guid.NewGuid(), Kind = JobEventNotice.KindSucceeded, Detail = "all good" };
            var back = JobEventNotice.Decode(n.Encode());
            Assert.NotNull(back);
            Assert.Equal(n.JobObjectId, back.JobObjectId);
            Assert.Equal("Succeeded", back.Kind);
            Assert.Equal("all good", back.Detail);
        }

        [Fact]
        public void JobEventNotice_ReplacesPipesInDetail()
        {
            var n = new JobEventNotice { JobObjectId = Guid.NewGuid(), Kind = "Failed", Detail = "a|b|c" };
            var back = JobEventNotice.Decode(n.Encode());
            Assert.Equal("a/b/c", back.Detail);
        }

        [Fact]
        public void JobEventNotice_Decode_NullOnGarbage()
        {
            Assert.Null(JobEventNotice.Decode(null));
            Assert.Null(JobEventNotice.Decode("bad"));
        }
    }
}
