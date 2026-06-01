using System;
using System.IO;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class AgentStateTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ae-state-" + Guid.NewGuid().ToString("N") + ".state");

        public void Dispose() { try { if (File.Exists(_path)) File.Delete(_path); } catch { } }

        [Fact]
        public void RoundTrips_AllFields()
        {
            var st = new AgentState
            {
                LoggedIn = true,
                Identity = "NT AUTHORITY\\SYSTEM",
                ServerUrl = "https://mgmt.local",
                AuthMode = "Basic",
                LoginUser = "acs",
                LastError = "",
                RecorderWarnings = "Recorder17 (r17:7563) is not reachable: timed out\nRecorder3 ...",
                UpdatedUtc = DateTime.UtcNow,
            };
            st.Save(_path);

            var loaded = AgentState.Load(_path);
            Assert.True(loaded.LoggedIn);
            Assert.Equal(st.Identity, loaded.Identity);
            Assert.Equal(st.ServerUrl, loaded.ServerUrl);
            Assert.Equal(st.LoginUser, loaded.LoginUser);
            Assert.Equal(st.RecorderWarnings, loaded.RecorderWarnings);
            Assert.NotEqual(default, loaded.UpdatedUtc);
        }

        [Fact]
        public void MissingFile_HasDefaultUpdatedUtc()
        {
            var loaded = AgentState.Load(Path.Combine(Path.GetTempPath(), "no-state-" + Guid.NewGuid().ToString("N")));
            Assert.Equal(default, loaded.UpdatedUtc);
            Assert.False(loaded.LoggedIn);
        }

        [Fact]
        public void Clear_RemovesFile()
        {
            new AgentState { LoggedIn = true, UpdatedUtc = DateTime.UtcNow }.Save(_path);
            Assert.True(File.Exists(_path));
            AgentState.Clear(_path);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void MultilineRecorderWarnings_SurviveAsOnePhysicalLineFile()
        {
            new AgentState { RecorderWarnings = "a\nb\nc", UpdatedUtc = DateTime.UtcNow }.Save(_path);
            Assert.Equal("a\nb\nc", AgentState.Load(_path).RecorderWarnings);
        }
    }
}
