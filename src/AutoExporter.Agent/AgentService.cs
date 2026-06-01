using System.ServiceProcess;

namespace AutoExporter.Agent
{
    /// <summary>Thin Windows-service shell around <see cref="AgentHost"/>.</summary>
    internal sealed class AgentService : ServiceBase
    {
        private readonly AgentHost _host = new AgentHost();

        public AgentService()
        {
            ServiceName = Program.ServiceName;
            CanShutdown = true;
            CanStop = true;
        }

        protected override void OnStart(string[] args) => _host.Start();
        protected override void OnStop() => _host.Stop();
        protected override void OnShutdown() => _host.Stop();
    }
}
