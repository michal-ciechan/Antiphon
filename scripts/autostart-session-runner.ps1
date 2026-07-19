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
#>
$ErrorActionPreference = 'Continue'

$root   = Split-Path $PSScriptRoot -Parent          # scripts/ -> repo root
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

$stateFile      = Join-Path $logDir 'session-runner.state'
$supervisorPid  = Join-Path $logDir 'session-runner.supervisor.pid'
$servicePidFile = Join-Path $logDir 'session-runner.service.pid'
$logFile        = Join-Path $logDir 'session-runner.log'
$runnerDir      = Join-Path $root 'src\Antiphon.SessionRunner'

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
    -ExeArgs         '--urls http://localhost:17204' `
    -LogFile         $logFile `
    -ServicePidFile  $servicePidFile `
    -StateFile       $stateFile `
    -BuildProjectDir $runnerDir
