#requires -Version 7.0
<#
.SYNOPSIS
    Run a native Windows phone-home session runner (a laptop or a second Windows host).
    pwsh -NoProfile -File scripts/start-windows-phone-home-runner.ps1 -RunnerId laptop
        [-ServerOrigin https://antiphon.desktop.codeperf.net] [-Root C:\Antiphon\runner]
        [-Port 17214] [-Capacity 2] [-NoBuild]
.DESCRIPTION
    The runner connects OUT to the desktop server (POST /api/session-runners/register, then a
    WebSocket to /api/session-runners/<id>/connect) and opens no inbound port beyond its local
    /health and /build-slots on -Port. Every runner path lives under -Root in drive-rooted,
    forward-slash form (C:/Antiphon/runner/work), which is the spelling the runner reports back
    and the spelling the desktop's PhoneHomeRunner:Runners:<id> entry must use:

        RunnerWorkspace  = <Root>/work
        RunnerRepository = <Root>/work/repos/antiphon
        ChildClaudeHome / ChildCodexHome / ChildGrokHome = this host's own homes (printed below)

    The shared secret is read from <Root>\secrets\phone-home (one line, no other content). It must
    equal that entry's SharedSecret. claude, codex and grok are resolved from PATH once at start
    and passed as PhoneHome:Executables, so a launch never depends on the child's PATH.
    Herdr is off: the phone-home protocol refuses Herdr launches.

    Runs in the foreground until Ctrl+C. Run it from a normal terminal, not an agent shell whose
    Job Object would kill the runner's detached pty hosts when the command ends.
    The desktop side (secret entry and AppHost restart) is described in docs/docker-stack.md
    under "Native Windows phone-home runner". ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)][string]$RunnerId,
    [string]$ServerOrigin = 'https://antiphon.desktop.codeperf.net',
    [string]$Root = 'C:\Antiphon\runner',
    [int]$Port = 17214,
    [int]$Capacity = 2,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'

if ($RunnerId -in @('local', 'desktop')) { throw "RunnerId '$RunnerId' is reserved for the desktop runner." }
if (-not $IsWindows) { throw 'This script starts a native Windows runner; use docker-compose.server2-runner.yml on Linux.' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Antiphon.SessionRunner'
$exe = Join-Path $project 'bin\Debug\net9.0\Antiphon.SessionRunner.exe'

function Slash([string]$path) { return $path.Replace('\', '/').TrimEnd('/') }

$rootSlash = Slash ([IO.Path]::GetFullPath($Root))
$work = "$rootSlash/work"
$state = "$rootSlash/state"
$secret = Join-Path $Root 'secrets\phone-home'
foreach ($dir in @($work, "$work/repos", $state, "$state/session-runner", "$state/logs", "$state/pty-hosts", (Split-Path -Parent $secret))) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
if (-not (Test-Path -LiteralPath $secret)) {
    throw "Missing shared secret file $secret. Write the value of PhoneHomeRunner:Runners:${RunnerId}:SharedSecret into it."
}

$executables = [ordered]@{}
foreach ($name in @('claude', 'codex', 'grok')) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { $executables[$name] = $command.Source }
}

$claudeHome = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $HOME '.claude' }
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
$grokHome = if ($env:GROK_HOME) { $env:GROK_HOME } else { Join-Path $HOME '.grok' }

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-slot.ps1') -Label "phone-home-runner-$RunnerId" -- dotnet build $project -nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "Runner build failed ($LASTEXITCODE)." }
}
if (-not (Test-Path -LiteralPath $exe)) { throw "Runner not built: $exe" }

$envs = [ordered]@{
    'ASPNETCORE_URLS'                       = "http://localhost:$Port"
    'PhoneHome__Enabled'                    = 'true'
    'PhoneHome__RunnerId'                   = $RunnerId
    'PhoneHome__ServerOrigin'               = $ServerOrigin
    'PhoneHome__SecretPath'                 = $secret
    'PhoneHome__StoreIdPath'                = "$state/runner-store-id"
    'PhoneHome__CapacityStatePath'          = "$state/runner-capacity"
    'PhoneHome__AllowedCwd'                 = $work
    'PhoneHome__RunnerRepository'           = "$work/repos/antiphon"
    'PhoneHome__Capacity'                   = "$Capacity"
    'PhoneHome__ClaudeHome'                 = $claudeHome
    'PhoneHome__CodexHome'                  = $codexHome
    'PhoneHome__GrokHome'                   = $grokHome
    'PhoneHome__RawExeAllowList__0'         = (Get-Command pwsh).Source
    'SessionRunner__SessionLogPath'         = "$state/session-runner"
    'SessionRunner__PtyHostDir'             = "$state/pty-hosts"
    'SessionRunner__Herdr__Enabled'         = 'false'
    'SessionRunner__HostStats__Volumes__0'  = $work
    'Serilog__LogPath'                      = "$state/logs"
}
foreach ($name in $executables.Keys) { $envs["PhoneHome__Executables__$name"] = $executables[$name] }
foreach ($key in $envs.Keys) { [Environment]::SetEnvironmentVariable($key, $envs[$key], 'Process') }

Write-Host "Phone-home runner '$RunnerId' -> $ServerOrigin (local http://localhost:$Port)"
Write-Host "Desktop entry PhoneHomeRunner:Runners:${RunnerId} must use:"
Write-Host "  RunnerWorkspace  = $work"
Write-Host "  RunnerRepository = $work/repos/antiphon"
Write-Host "  ChildClaudeHome  = $claudeHome"
Write-Host "  ChildCodexHome   = $codexHome"
Write-Host "  ChildGrokHome    = $grokHome"
Write-Host "  RawExeAllowList  = $((Get-Command pwsh).Source)"
foreach ($name in $executables.Keys) { Write-Host "  executable $name = $($executables[$name])" }

Push-Location $project
try { & $exe --urls "http://localhost:$Port"; exit $LASTEXITCODE }
finally { Pop-Location }
