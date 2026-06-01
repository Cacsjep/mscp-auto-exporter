using System;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class AgentRegistrationTests
    {
        [Fact]
        public void RoundTrips_AllFields()
        {
            var reg = new AgentRegistration
            {
                Hostname = "ACS01",
                Version = "1.2",
                Status = "Online",
                LastSeenUtc = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                ExportFolder = @"C:\Exports",
                MaxGB = 100,
                RetentionDays = 30,
                DisplayName = "Lobby agent",
            };

            var loaded = AgentRegistration.FromProperties(reg.ToProperties());
            Assert.Equal("ACS01", loaded.Hostname);
            Assert.Equal("1.2", loaded.Version);
            Assert.Equal("Online", loaded.Status);
            Assert.Equal(reg.LastSeenUtc, loaded.LastSeenUtc);
            Assert.Equal(@"C:\Exports", loaded.ExportFolder);
            Assert.Equal(100, loaded.MaxGB);
            Assert.Equal(30, loaded.RetentionDays);
            Assert.Equal("Lobby agent", loaded.DisplayName);
        }

        [Fact]
        public void ObjectIdFor_IsDeterministic_AndCaseInsensitive()
        {
            Assert.Equal(AgentRegistration.ObjectIdFor("ACS01"), AgentRegistration.ObjectIdFor("ACS01"));
            Assert.Equal(AgentRegistration.ObjectIdFor("ACS01"), AgentRegistration.ObjectIdFor("acs01"));
            Assert.NotEqual(AgentRegistration.ObjectIdFor("ACS01"), AgentRegistration.ObjectIdFor("ACS02"));
        }

        [Fact]
        public void FriendlyName_PrefersDisplayName_FallsBackToHostname()
        {
            Assert.Equal("Nice", new AgentRegistration { Hostname = "ACS01", DisplayName = "Nice" }.FriendlyName);
            Assert.Equal("ACS01", new AgentRegistration { Hostname = "ACS01", DisplayName = "" }.FriendlyName);
            Assert.Equal("ACS01", new AgentRegistration { Hostname = "ACS01", DisplayName = "   " }.FriendlyName);
        }

        [Fact]
        public void Status_DefaultsToOnline_WhenMissing()
        {
            var loaded = AgentRegistration.FromProperties(new System.Collections.Generic.Dictionary<string, string>());
            Assert.Equal("Online", loaded.Status);
            Assert.Equal(0, loaded.MaxGB);
        }
    }
}
