<#
.SYNOPSIS
    Build + (re)install the Auto Exporter components: admin plugin, agent service, tray app.

.DESCRIPTION
    One script to rebuild on change. Builds with CIBuild=true so the per-project dev
    deploy targets (Directory.Build.targets) stay out of the way, then this script does
    the install/restart steps explicitly.

      - plugin    : build AdminPlugin -> copy to MIPPlugins\AutoExporter -> restart Event Server
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

    # Product version stamped into the assemblies and the MSI. CI passes the release tag
    # (without the leading v). Local builds default to 1.0.0.
    [string]$Version = '1.0.0',

    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Install targets
$MipPluginDir   = 'C:\Program Files\Milestone\MIPPlugins\AutoExporter'
$AgentInstallDir = 'C:\Program Files\MSCPlugins\AutoExporter'
$EventServerSvc = 'MilestoneEventServerService'
$AgentSvc       = 'AutoExporterAgent'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Invoke-Build($project) {
    Write-Step "build $project ($Configuration, v$Version)"
    dotnet build (Join-Path $root $project) -c $Configuration -p:CIBuild=true -p:Version=$Version --nologo
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
    else {
        # Always repoint the existing service at the current exe. Without this, an install-dir change
        # (the AutoExporterAgent -> AutoExporter rename) leaves the service launching the OLD exe, so a
        # redeploy looks like it did nothing (stale version, missing features).
        Write-Step "ensure service $AgentSvc runs $exe"
        & sc.exe config $AgentSvc binPath= "`"$exe`"" | Out-Null
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
        -c $Configuration -r win-x64 --nologo -p:CIBuild=true -p:Version=$Version
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

    # The UI (feature tree + license) and Util (WixQuietExec64) extensions. Pin them to major 5 so
    # they match the WiX v5 CLI (an unversioned add can resolve a mismatched extension on a fresh CI
    # runner and then 'wix build -ext ...' fails with WIX0144 not found). Adding is idempotent. Fail
    # loudly if an add does not succeed, instead of discovering it later as a confusing build error.
    Write-Step "ensure WiX extensions (UI, Util)"
    & wix extension add -g WixToolset.UI.wixext/5
    if ($LASTEXITCODE -ne 0) { throw "wix extension add WixToolset.UI.wixext/5 failed" }
    & wix extension add -g WixToolset.Util.wixext/5
    if ($LASTEXITCODE -ne 0) { throw "wix extension add WixToolset.Util.wixext/5 failed" }

    # Build all payloads (no service install / restart needed just to package). The custom-actions
    # project packs itself into AutoExporterCustomActions.CA.dll via the WixToolset.Dtf build target.
    Invoke-Build 'src\AutoExporter.Agent\AutoExporter.Agent.csproj'
    Invoke-Build 'src\AutoExporter.AdminPlugin\AutoExporter.AdminPlugin.csproj'
    Invoke-Build 'src\AutoExporter.Installer.CustomActions\AutoExporter.Installer.CustomActions.csproj'
    Build-Tray

    $agentDir  = Join-Path $root "src\AutoExporter.Agent\bin\$Configuration\net48"
    $pluginDir = Join-Path $root "src\AutoExporter.AdminPlugin\bin\$Configuration\net48"
    $trayExe   = Join-Path $root "src\AutoExporter.Tray\bin\$Configuration\net9.0-windows\win-x64\publish\AutoExporter.Tray.exe"
    $caDll     = Join-Path $root "src\AutoExporter.Installer.CustomActions\bin\$Configuration\net48\AutoExporterCustomActions.CA.dll"
    $installerDir = Join-Path $root 'src\AutoExporter.Installer'
    $wxs       = Join-Path $installerDir 'Package.wxs'
    $outDir    = Join-Path $installerDir 'bin'
    $msi       = Join-Path $outDir 'AutoExporter.msi'

    if (-not (Test-Path $trayExe)) { throw "tray publish not found: $trayExe" }
    if (-not (Test-Path $caDll)) { throw "custom actions not packed (expected $caDll). The WixToolset.Dtf.CustomAction MakeSfxCA target did not run." }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Step "package installer -> $msi"
    # -bindpath lets wix resolve License.rtf (and other relative payload) from the installer folder.
    & wix build $wxs -arch x64 -bindpath $installerDir `
        -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d "Version=$Version" `
        -d "AgentDir=$agentDir" -d "TrayExe=$trayExe" -d "PluginDir=$pluginDir" `
        -d "CustomActionsDll=$caDll" -o $msi
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
