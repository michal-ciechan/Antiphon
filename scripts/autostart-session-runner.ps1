<#
.SYNOPSIS
    Logon auto-start for the Antiphon session-runner daemon (http://localhost:17204).

    Registered as a per-user Scheduled Task by scripts/install-autostart.ps1 so the
    session-runner - which spawns the agent PTY processes - is always running, even
    before you launch the Aspire AppHost.

    It reuses scripts/run-daemon.ps1 (the same supervisor the AppHost uses) and writes
    the SAME pid/state files under <repo>/logs/. Because of that, when you later run
    dev-aspire.ps1 the AppHost sees port 17204 already listening and ADOPTS this
    running session-runner instead of spawning a duplicate (see DaemonProcessService).
.NOTES
    Runs under the interactive user account so spawned agents inherit your PATH/profile
    (dotnet, npm, cl.bat, etc.). This process IS the supervisor: it blocks in
    run-daemon.ps1's restart loop and only exits when the desired state becomes 'stopped'.
    When C:\Antiphon\runner\phone-home.json exists, the same task runs the phone-home
    runner. A bad, unreadable, or unloadable config is refused and nothing is started.
    Pass -PhoneHomeConfigPath for the phase 1 file (phone-home.phase1.json); that path
    may use port 17214.
#>
param(
    [string]$PhoneHomeConfigPath = 'C:\Antiphon\runner\phone-home.json'
)
$ErrorActionPreference = 'Continue'

$root   = Split-Path $PSScriptRoot -Parent          # scripts/ -> repo root
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

$stateFile      = Join-Path $logDir 'session-runner.state'
$supervisorPid  = Join-Path $logDir 'session-runner.supervisor.pid'
$servicePidFile = Join-Path $logDir 'session-runner.service.pid'
$logFile        = Join-Path $logDir 'session-runner.log'
$runnerDir      = Join-Path $root 'src\Antiphon.SessionRunner'

function Write-AutostartRefusal([string]$Reason) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Add-Content -LiteralPath $logFile -Value ($stamp + ' [autostart] phone-home config refused: ' + $Reason) -Encoding UTF8 -ErrorAction SilentlyContinue
}

$configPresent = Test-Path -LiteralPath $PhoneHomeConfigPath
$helperPath = Join-Path $PSScriptRoot 'lib/windows-phone-home-env.ps1'
$helperLoaded = $false
$savedPreference = $ErrorActionPreference
$ErrorActionPreference = 'Stop'
try {
    . $helperPath
    $helperLoaded = $true
} catch {
    $helperLoaded = $false
}
$ErrorActionPreference = $savedPreference
if (-not $helperLoaded) {
    Write-AutostartRefusal 'helper-load-failed'
    exit 2
}

$configText = $null
$explicitConfigPath = $PSBoundParameters.ContainsKey('PhoneHomeConfigPath')
$secretPresent = $true
if ($configPresent) {
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        $configText = [System.IO.File]::ReadAllText($PhoneHomeConfigPath)
        $requirePort = 0
        if (-not $explicitConfigPath) { $requirePort = 17204 }
        $parsedConfig = ConvertFrom-WindowsPhoneHomeConfig -Text $configText -RequirePort $requirePort
        if ($parsedConfig.ok) {
            $secretFile = Join-Path ([string]$parsedConfig.root) 'secrets\phone-home'
            $secretPresent = Test-Path -LiteralPath $secretFile
        }
    } catch {
        $ErrorActionPreference = $savedPreference
        Write-AutostartRefusal 'config-unreadable'
        exit 2
    }
    $ErrorActionPreference = $savedPreference
}
$pwshCommand = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$pwshPath = ''
if ($null -ne $pwshCommand) { $pwshPath = [string]$pwshCommand.Source }
$executables = [ordered]@{}
foreach ($name in @('claude', 'codex', 'grok')) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { $executables[$name] = [string]$command.Source }
}
$plan = $null
$savedPreference = $ErrorActionPreference
$ErrorActionPreference = 'Stop'
try {
    $plan = Resolve-SessionRunnerAutostartPlan -ConfigText $configText -ExplicitConfigPath $explicitConfigPath -SecretPresent $secretPresent -HomeDir $HOME -PwshPath $pwshPath -Executables $executables -RepoRoot $root
} catch {
    $ErrorActionPreference = $savedPreference
    Write-AutostartRefusal 'plan-resolve-failed'
    exit 2
}
$ErrorActionPreference = $savedPreference
$mode = ''
if ($null -ne $plan) { $mode = [string]$plan.mode }
if ($mode -ceq 'refused') {
    Write-AutostartRefusal ([string]$plan.reason)
    exit 2
}
if ($mode -cne 'direct' -and $mode -cne 'phone-home') {
    Write-AutostartRefusal 'plan-mode-invalid'
    exit 2
}
if ($mode -ceq 'phone-home') {
    foreach ($key in @($plan.env.Keys)) {
        [Environment]::SetEnvironmentVariable([string]$key, [string]$plan.env[$key], 'Process')
    }
}

# Force desired-state 'running' on every logon: a prior dashboard "Stop" wrote
# 'stopped' to this file, which would otherwise make run-daemon.ps1 exit immediately.
Set-Content -LiteralPath $stateFile -Value 'running' -Encoding UTF8

# Record THIS process as the supervisor so the AppHost can track/restart/kill it.
$PID | Set-Content -LiteralPath $supervisorPid -Encoding UTF8

Add-Content -LiteralPath $logFile -Value `
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [autostart] logon supervisor starting (PID $PID)" `
    -Encoding UTF8 -ErrorAction SilentlyContinue

# Blocks in the supervise/restart loop until desired-state = 'stopped'.
# Launch the BUILT exe directly (not 'dotnet run'): 'dotnet run' wraps the app in a
# kill-on-close Job Object that captures our detached pty-hosts and kills them on restart.
# run-daemon rebuilds via -BuildProjectDir before each (re)launch, so soft restarts still
# pick up new code. See docs/superpowers/specs/2026-07-19-pty-host-split.md.
$runnerExe = Join-Path $runnerDir 'bin\Debug\net9.0\Antiphon.SessionRunner.exe'
& (Join-Path $PSScriptRoot 'run-daemon.ps1') `
    -Name            'session-runner' `
    -WorkDir         $runnerDir `
    -Exe             $runnerExe `
    -ExeArgs         $plan.exeArgs `
    -LogFile         $logFile `
    -ServicePidFile  $servicePidFile `
    -StateFile       $stateFile `
    -BuildProjectDir $runnerDir
