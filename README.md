# Auto Exporter

Standalone Milestone XProtect auto-export system. A Windows **agent service** does the export work,
an Avalonia **tray app** configures and controls it, and a MIP **admin plugin** manages jobs and
shows status inside the Management Client.

This file is the project memory. It describes what exists today, how the pieces fit, and the
non-obvious decisions that took real effort to get right. Read it before changing anything.

Note on style: do not use em dashes or semicolons in prose, docs or comments.

## Components

| Project | TFM | What it is |
|---|---|---|
| `src/AutoExporter.Contracts` | netstandard2.0 | Shared POCO schema and helpers used by all three apps. No SDK references. |
| `src/AutoExporter.Agent` | net48 | Windows service. Logs in to Milestone, self-registers, runs `DBExporter`/`AVIExporter` in-process, records runs. |
| `src/AutoExporter.AdminPlugin` | net48 | MIP plugin. Overview page (Agents, Jobs, Executions), Jobs node for rules, rule action and events, Event Server bridge. |
| `src/AutoExporter.Tray` | net9.0-windows | Avalonia config UI. Sets export folder, retention, log level and the Milestone connection, and controls the service. |
| `src/AutoExporter.Installer` | WiX v5 | Per-machine MSI that installs the agent service plus the tray. |

The agent and plugin are net48 because the Milestone export SDK (`DBExporter`, `AVIExporter`,
`SequenceDataSource`) is net48. The tray is net9 Avalonia.

## How it fits together

- The **agent service** is the only thing that logs in to Milestone. It runs as Local System and
  signs in with the credentials the tray stores. After login it self-registers an agent node and
  listens for run requests.
- The **tray** never logs in itself. On Connect it saves the config, restarts the service, and then
  reads back the service's real login result from `agent.state`. One login path, one source of truth.
- The **admin plugin** is the UI and the rule integration. Jobs and agents are stored as MIP
  configuration items, but the visible UI is our own, built on the Configuration API.
- A rule, or the Run now button, sends a `RunJob` message over MIP MessageCommunication to the
  owning agent, which runs the export and writes an execution record.

### Management Client tree

```
MIP Plug-ins
  Exporter
    Overview      one page, three stacked sections: Agents, Jobs, Executions
    Jobs          jobs as a real node so rules can select a specific job
```

Agents are never their own tree node. They are shown read only in the Overview Agents section. This
avoids the "add new agent" problem entirely. Jobs exist both in the Overview Jobs section (manage
plus Run now) and as the Jobs node (so the rule engine can target them). Both edit the same items.

All three Overview sections refresh themselves on a timer (agents and jobs every 3 seconds, the
executions feed re-queries every 5 seconds), so there are no Refresh buttons. The current row
selection is preserved across a refresh.

An agent that has gone offline (its service was stopped or uninstalled, so no heartbeat for 90
seconds) can be removed with the Remove agent button in the Agents section. Removing an agent also
deletes the jobs assigned to it, so no job is left pointing at a missing agent. A live agent cannot
be removed because it would just re-register itself.

## Login (the important part)

The agent uses the modern OAuth login, exactly like the official MIP SDK samples:

```csharp
var idpUri = new Uri(serverUri, "idp");
var tokenCache = new VideoOS.Platform.SDK.OAuth.MipTokenCache(idpUri, networkCredential, isBasicUser);
VideoOS.Platform.SDK.Environment.AddServerOAuth(secureOnly, serverUri, tokenCache, false);
VideoOS.Platform.SDK.Environment.Login(serverUri, integrationId, name, version, manufacturer, false);
```

- Basic user uses `isBasicUser: true`, Windows user uses `false`, both with a plain
  `NetworkCredential(user, password)` and no domain marker.
- The legacy `Environment.AddServer(secureOnly, uri, credential, masterOnly)` with a `[BASIC]`
  domain does NOT perform the IDP token grant on SDK 26.1. It fails with `IDP returned 401` inside
  `FormatToken`. Do not go back to it.
- `secureOnly` is true when the server URL is https.
- This was misdiagnosed twice. It is not System.Text.Json and not the IdentityModel version. Do not
  pin `System.Text.Json` or the `Microsoft.IdentityModel.*` packages. The SDK declares IdentityModel
  8.14.0 and that is correct. The only build noise is MSB3277, demoted with
  `MSBuildWarningsAsMessages` in the agent project.

### Authentication modes

Two modes only, both credential based: Basic user and Windows user. There is no current-user mode,
because a Local System service has no interactive user and would fall back to the machine account
(VMO61008). Credentials are stored DPAPI protected with **LocalMachine** scope so the Local System
service can decrypt what the tray (an admin user) wrote.

## Self-registration and visibility

The running agent writes its node as a configuration item (kind `AgentKindId`, ObjectId derived
from the hostname via MD5) using `Configuration.Instance.SaveItemConfiguration`. This works from the
standalone agent process and the Management Client reads it back.

Note: agents are listed by hostname. There is no rename in the Management Client because the
management server rejects a PUT (Bad Request) when the admin client tries to save an agent kind item
(that kind is not a registered tree node, so the config REST layer will not accept an admin side
write to it). The agent process itself can write its own node, the admin client cannot. The
`DisplayName` plumbing is still present in the code and the agent preserves it, but there is no UI to
set it. Do not re-add a rename button unless the write path is solved.

## Recording server reachability

After a successful login the service probes every enabled recording server to confirm this machine
can actually reach it. A common field problem is a recorder the management server knows about but
that this host cannot resolve or reach (bad DNS, a missing hosts entry, a firewall), which would
later show up as a confusing export failure for that recorder's cameras.

The probe is a plain TCP connect to each recorder's web service host and port (from `WebServerUri`,
falling back to `HostName` and `PortNumber`). It does not log in or read media. The result is written
to `agent.state` as `RecorderWarnings` (empty when all reachable, otherwise one line per unreachable
server). After a Connect the tray shows the unreachable list in a modal and also keeps it visible as
a warning block on the Registration page while connected.

## Disabled cameras

A camera target that is disabled must not fail a job.

- The job editor camera picker hides disabled cameras with `ItemPickerWpfWindow.IsVisibleCallback`
  (folders and groups still show, disabled camera leaves do not).
- The agent skips disabled cameras during resolution (direct targets and group members) and lists
  them as skipped. The job runs with the enabled cameras. If every selected camera is disabled or
  inaccessible, the run is recorded as Skipped, not Failed, with the names in the detail.
- `Item.Enabled` is the flag used for this.

## Jobs

A job stores name, enabled, the agent hostname that runs it, format (XProtect or AVI), encryption
plus password, include player and audio, and a time range (last N minutes/hours/days/months) plus
the camera and group targets. Storage folder, max size and retention are agent wide settings now
(set in the tray), not per job. The Jobs section also shows Last run and Last status per job, fed
from the execution records.

## Cross-process messaging

MIP MessageCommunication on the management server channel.

- `RunJob` request: the rule action bridge or the Run now button broadcasts it. The agent filters by
  hostname and runs the job.
- `JobEvent`: the agent tells the Event Server bridge to raise the registered rule events
  (JobStarted, JobSucceeded, JobFailed).
- `QueryExecutions` and `ExecutionsReply`: the Executions section broadcasts a query, every agent
  replies with its recent run history (encoded with `ExecutionCodec`), merged newest first.

## Data and logs

Everything lives under `%ProgramData%\MSCPlugins\AutoExporter\`.

- `agent.config` machine config written by the tray (password DPAPI protected, LocalMachine scope).
- `agent.state` the service's last login result (LoggedIn, Identity, LoginUser, LastError). The tray
  reads this to show the connection status and to drive the tray icon error state.
- `executions.log` one `ExecutionCodec` line per run, capped at 1000, read back over messaging.
- `agent.log`, `plugin.log`, `tray.log` rotated at 2 MB with 2 backups (`LogRotation`).
- Agent log verbosity is Error, Info or Debug, set in the tray General section.

## Tray UI

A single fixed-size dark window with a left navigation (General, Registration, Control).

- General: export folder with a Browse picker and an Open folder button, Max size (GB), Retention
  (days), Log level, Save. Saving restarts the service so the change takes effect.
- Registration: management server URL, Basic user or Windows user, credentials with a show password
  toggle, Connect. After a successful login the editor collapses to a Connected summary with a
  Change Server Registration button. Connect saves the config and restarts the service, then waits
  for the service to report its own login result.
- Control: service status and Start/Stop/Restart, plus Open service log and Open tray log.

The tray lives in the notification area. The tray icon turns red and the tooltip shows the reason
when the service login failed or the service is not installed. An always visible error footer shows
the same problem on any page. The tray context menu has Start/Stop/Restart, Open export folder, and
the log openers.

## Build and deploy

```powershell
.\build.ps1                  # plugin + agent + tray (Debug)
.\build.ps1 -Component agent
.\build.ps1 -Component plugin
.\build.ps1 -Component tray
.\build.ps1 -Component installer    # needs the WiX v5 CLI: dotnet tool install --global wix
.\build.ps1 -NoRestart       # build and copy only, do not bounce services
```

`dotnet build AutoExporter.sln` compiles everything for inner-loop dev.

Deploy gotchas:

- The **plugin** deploy stops the Event Server, copies the DLL to
  `C:\Program Files\Milestone\MIPPlugins\AutoExporterV2`, then starts the Event Server. The
  **Management Client must be closed** first, otherwise it locks the plugin DLL and the copy fails.
  Reopen the Management Client afterwards to pick up tree or UI changes.
- The **agent** deploy stops the `AutoExporterAgent` service, copies to
  `C:\Program Files\MSCPlugins\AutoExporterAgent`, then starts it. The service re-logs in and
  re-registers on start.
- The **tray** publishes a single file self-contained exe. Stop a running tray first or the copy is
  locked.

## Export pipeline notes

- All SDK work runs on a dedicated STA thread that pumps the Win32 message loop (`AgentHost`). The
  export pipeline and the recorder online status need a pumped loop, otherwise the recorder reads as
  offline and `StartExport` fails.
- `MilestoneSession.Login` order is Environment, UI, Export, then AddServerOAuth and Login, then
  Media.
- Login is retried every 20 seconds, so a transient failure recovers on its own. Each attempt
  writes `agent.state` so the tray reflects reality.
- Cameras with no recorded data in the range are skipped, not failed.

## Reference

- Legacy plugin (ported from): `G:\mscp\mscp\Admin Plugins\AutoExporter`.
- Tray look and feel reference: `G:\mscp\mscp\installer\Mscp.PkiCertInstaller`.
- Official MIP SDK login samples that the agent follows:
  `https://github.com/milestonesys/mipsdk-samples-component` (ConfigAddCameras, StatusDemoConsole).
