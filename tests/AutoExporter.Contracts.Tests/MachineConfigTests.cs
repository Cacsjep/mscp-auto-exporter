using System;
using System.IO;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class MachineConfigTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "ae-test-" + Guid.NewGuid().ToString("N") + ".config");

        public void Dispose() { try { if (File.Exists(_path)) File.Delete(_path); } catch { } }

        [Fact]
        public void RoundTrips_AllFields()
        {
            var cfg = new MachineConfig
            {
                ServerUrl = "https://mgmt.local:8443",
                AuthMode = AuthMode.WindowsOtherUser,
                Username = "DOMAIN\\svc",
                Password = "p@ss w0rd!",
                ExportFolder = @"C:\Exports\Auto",
                LogLevel = "Debug",
                MaxGB = 42,
                RetentionDays = 7,
                Registered = true,
            };
            cfg.Save(_path);

            var loaded = MachineConfig.Load(_path);
            Assert.Equal(cfg.ServerUrl, loaded.ServerUrl);
            Assert.Equal(cfg.AuthMode, loaded.AuthMode);
            Assert.Equal(cfg.Username, loaded.Username);
            Assert.Equal(cfg.Password, loaded.Password);
            Assert.Equal(cfg.ExportFolder, loaded.ExportFolder);
            Assert.Equal(cfg.LogLevel, loaded.LogLevel);
            Assert.Equal(cfg.MaxGB, loaded.MaxGB);
            Assert.Equal(cfg.RetentionDays, loaded.RetentionDays);
            Assert.True(loaded.Registered);
        }

        [Fact]
        public void Password_IsEncryptedAtRest_NotPlaintext()
        {
            var cfg = new MachineConfig { ServerUrl = "h", Password = "SuperSecret123" };
            cfg.Save(_path);

            var raw = File.ReadAllText(_path);
            Assert.DoesNotContain("SuperSecret123", raw);
            Assert.Equal("SuperSecret123", MachineConfig.Load(_path).Password);
        }

        [Fact]
        public void EmptyPassword_RoundTripsAsEmpty()
        {
            new MachineConfig { ServerUrl = "h", Password = "" }.Save(_path);
            Assert.Equal("", MachineConfig.Load(_path).Password);
        }

        [Fact]
        public void MissingFile_ReturnsDefaults()
        {
            var loaded = MachineConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")));
            Assert.Equal("", loaded.ServerUrl);
            Assert.Equal(AuthMode.Basic, loaded.AuthMode);
            Assert.Equal("Info", loaded.LogLevel);
            Assert.Equal(0, loaded.MaxGB);
            Assert.False(loaded.Registered);
        }

        [Fact]
        public void UnknownAuthMode_FallsBackToBasic()
        {
            File.WriteAllText(_path, "ServerUrl=h\nAuthMode=Garbage\n");
            Assert.Equal(AuthMode.Basic, MachineConfig.Load(_path).AuthMode);
        }

        [Fact]
        public void Escapes_BackslashAndNewlineInValues()
        {
            var cfg = new MachineConfig { ServerUrl = "h", ExportFolder = "C:\\a\\b", Username = "line1\nline2" };
            cfg.Save(_path);
            var loaded = MachineConfig.Load(_path);
            Assert.Equal("C:\\a\\b", loaded.ExportFolder);
            Assert.Equal("line1\nline2", loaded.Username);
        }
    }
}
