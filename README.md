# Auto Exporter

Standalone Milestone XProtect auto-export system. An agent **service** exports video, a **tray app**
configures it, and a Management Client **plugin** defines jobs and shows status.

Style: no em dashes or semicolons in prose, docs or comments.

## Components

| Project | TFM | What it is |
|---|---|---|
| `AutoExporter.Contracts` | netstandard2.0 | Shared schema and codecs. No SDK refs. Unit tested. |
| `AutoExporter.Agent` | net48 | Windows service. Logs in, self-registers, runs the exports. |
| `AutoExporter.AdminPlugin` | net48 | MIP plugin. Overview + Jobs nodes, rule action, Event Server bridge. |
| `AutoExporter.Tray` | net9.0-windows | Avalonia config UI. Server, account, export folder, limits, service control. |
| `AutoExporter.Installer` | WiX v5 | Per-machine MSI, selectable Agent and Plugin features. |

## How it works

- The agent service is the only thing that logs in to Milestone. The tray saves config and restarts
  the service, then shows the service's own result from `agent.state`. One login path.
- Jobs and agents are stored as MIP config items, the UI is our own (Configuration API). A rule or
  Run now sends a `RunJob` message to the owning agent.
- Config and state live under `%ProgramData%\MSCPlugins\AutoExporter\`.

## Gotchas (read before changing)

- **Login uses OAuth.** `MipTokenCache` + `Environment.AddServerOAuth` + `Environment.Login`. The
  legacy `AddServer(...)` with a `[BASIC]` domain fails with IDP 401 on SDK 26.1. Do not pin
  `System.Text.Json` or `Microsoft.IdentityModel.*`.
- **Plugin deploy needs the Management Client closed** (it locks the DLL). The agent and tray
  unlock by stopping the service / tray first.
- **Password is DPAPI LocalMachine scope**, so the LocalSystem service can read what the tray wrote.
- **Disabled cameras** are hidden in the picker and skipped by the agent. An all-disabled run is
  Skipped, not Failed.
- **Agents cannot be renamed in the admin client** (the server rejects the write). Offline agents
  can be removed (cascades to their jobs) via the Event Server bridge.
- The agent reconnects on its own if Milestone restarts or is slow to boot (retry on connect,
  reconnect after 3 failed heartbeats).

## Build, test, release

```powershell
.\build.ps1                       # plugin + agent + tray (Debug)
.\build.ps1 -Component agent|plugin|tray|installer
dotnet test tests\AutoExporter.Contracts.Tests\AutoExporter.Contracts.Tests.csproj
```

The installer needs the WiX v5 CLI (`dotnet tool install --global wix`). It produces a per-machine
MSI: pick the Agent feature (service + tray, with a registration page that pre-writes config) and/or
the Plugin feature (closes the Management Client and bounces the Event Server). CI runs the tests on
push, and a `v*` tag builds the MSI and attaches it to the GitHub release.

## License

MIT (see `LICENSE`). Independent open source project, not affiliated with Milestone Systems.
XProtect (TM) is a trademark of Milestone Systems A/S.
