<#
.SYNOPSIS
    Build + (re)install the Auto Exporter components: admin plugin, agent service, tray app.

.DESCRIPTION
    One script to rebuild on change. Builds with CIBuild=true so the per-project dev
    deploy targets (Directory.Build.targets) stay out of the way, then this script does
    the install/restart steps explicitly.

      - plugin    : build AdminPlugin -> copy to MIPPlugins\AutoExporterV2 -> restart Event Server
      - agent     : build Agent -> stop service -> copy to install dir -> (re)install + start
      - tray      : publish Tray single-file self-contained exe
      - installer : build agent + publish tray -> package the V2 WiX MSI (agent service + tray)

    'all' builds the three dev-loop components (plugin, agent, tray). The installer is a separate
    packaging step (build it explicitly with -Component installer) so the inner dev loop stays fast.

.PARAMETER Component
    Which component(s) to build/install: all (default), plugin, agent, tray, installer.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER NoRestart
    Skip stopping/starting the Event Server and the agent service (build + copy only).

.EXAMPLE
    .\build.ps1                      # everything, Debug
    .\build.ps1 -Component agent     # just the service
    .\build.ps1 -Component plugin -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'plugin', 'agent', 'tray', 'installer')]
    [string]$Component = 'all',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Install targets
$MipPluginDir   = 'C:\Program Files\Milestone\MIPPlugins\AutoExporterV2'
$AgentInstallDir = 'C:\Program Files\MSCPlugins\AutoExporterAgent'
$EventServerSvc = 'MilestoneEventServerService'
$AgentSvc       = 'AutoExporterAgent'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Invoke-Build($project) {
    Write-Step "build $project ($Configuration)"
    dotnet build (Join-Path $root $project) -c $Configuration -p:CIBuild=true --nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}

function Stop-Svc($name) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Write-Step "stop service $name"
        Stop-Service -Name $name -Force
    }
}

function Copy-Tree($from, $to) {
    if (-not (Test-Path $to)) { New-Item -ItemType Directory -Force -Path $to | Out-Null }
    Copy-Item -Path (Join-Path $from '*') -Destination $to -Recurse -Force
}

# ── Admin plugin ────────────────────────────────────────────────────────
function Build-Plugin {
    Invoke-Build 'src\AutoExporter.AdminPlugin\AutoExporter.AdminPlugin.csproj'
    $out = Join-Path $root "src\AutoExporter.AdminPlugin\bin\$Configuration\net48"
    if (-not $NoRestart) { Stop-Svc $EventServerSvc }
    Write-Step "deploy plugin -> $MipPluginDir"
    Copy-Tree $out $MipPluginDir
    if (-not $NoRestart) {
        Write-Step "start $EventServerSvc"
        Start-Service -Name $EventServerSvc -ErrorAction SilentlyContinue
    }
}

# ── Agent service ───────────────────────────────────────────────────────
function Build-Agent {
    Invoke-Build 'src\AutoExporter.Agent\AutoExporter.Agent.csproj'
    $out = Join-Path $root "src\AutoExporter.Agent\bin\$Configuration\net48"
    if (-not $NoRestart) { Stop-Svc $AgentSvc }
    Write-Step "deploy agent -> $AgentInstallDir"
    Copy-Tree $out $AgentInstallDir

    $exe = Join-Path $AgentInstallDir 'AutoExporter.Agent.exe'
    $existing = Get-Service -Name $AgentSvc -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Step "install service $AgentSvc"
        & sc.exe create $AgentSvc binPath= "`"$exe`"" start= auto DisplayName= "Auto Exporter Agent" | Out-Null
    }
    if (-not $NoRestart) {
        Write-Step "start $AgentSvc"
        Start-Service -Name $AgentSvc -ErrorAction SilentlyContinue
    }
}

# ── Tray app ────────────────────────────────────────────────────────────
function Build-Tray {
    Write-Step "publish tray ($Configuration, single-file win-x64)"
    dotnet publish (Join-Path $root 'src\AutoExporter.Tray\AutoExporter.Tray.csproj') `
        -c $Configuration -r win-x64 --nologo -p:CIBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'publish failed: tray' }
    $pub = Join-Path $root "src\AutoExporter.Tray\bin\$Configuration\net9.0-windows\win-x64\publish"
    Write-Step "tray published to: $pub"
}

# ── V2 WiX installer (agent service + tray) ──────────────────────────────
function Build-Installer {
    $wix = Get-Command wix -ErrorAction SilentlyContinue
    if (-not $wix) {
        throw "WiX v5 CLI not found. Install it with: dotnet tool install --global wix"
    }

    # The UI (feature tree + license) and Util (WixQuietExec64) extensions. Adding is idempotent.
    Write-Step "ensure WiX extensions (UI, Util)"
    & wix extension add -g WixToolset.UI.wixext | Out-Null
    & wix extension add -g WixToolset.Util.wixext | Out-Null

    # Build all three payloads (no service install / restart needed just to package).
    Invoke-Build 'src\AutoExporter.Agent\AutoExporter.Agent.csproj'
    Invoke-Build 'src\AutoExporter.AdminPlugin\AutoExporter.AdminPlugin.csproj'
    Build-Tray

    $agentDir  = Join-Path $root "src\AutoExporter.Agent\bin\$Configuration\net48"
    $pluginDir = Join-Path $root "src\AutoExporter.AdminPlugin\bin\$Configuration\net48"
    $trayExe   = Join-Path $root "src\AutoExporter.Tray\bin\$Configuration\net9.0-windows\win-x64\publish\AutoExporter.Tray.exe"
    $installerDir = Join-Path $root 'src\AutoExporter.Installer'
    $wxs       = Join-Path $installerDir 'Package.wxs'
    $outDir    = Join-Path $installerDir 'bin'
    $msi       = Join-Path $outDir 'AutoExporter.msi'

    if (-not (Test-Path $trayExe)) { throw "tray publish not found: $trayExe" }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Step "package installer -> $msi"
    # -bindpath lets wix resolve License.rtf (and other relative payload) from the installer folder.
    & wix build $wxs -arch x64 -bindpath $installerDir `
        -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d "AgentDir=$agentDir" -d "TrayExe=$trayExe" -d "PluginDir=$pluginDir" -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
    Write-Step "installer built: $msi"
}

switch ($Component) {
    'plugin'    { Build-Plugin }
    'agent'     { Build-Agent }
    'tray'      { Build-Tray }
    'installer' { Build-Installer }
    'all'       { Build-Plugin; Build-Agent; Build-Tray }
}

Write-Host "Done." -ForegroundColor Green
