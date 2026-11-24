#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts run-daemon` (scripts/lib/Antiphon.Nightly/DaemonSupervisor.cs): the long-lived
    supervisor of one Antiphon service (build before each launch, the built executable under cmd.exe with its output
    appended to LogFile, restart on exit, the desired-state file, log rotation at relaunch).

    Unit 30 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the AppHost starts this file by
    path (Antiphon.AppHost/Supervisor/DaemonProcessService.cs, a detached pwsh -File) and the logon task's
    scripts/autostart-session-runner.ps1 calls it. The parameters are the script's; ExeArgs stays one space-joined string
    (PowerShell -File drops extra values from a [string[]]) and the command splits it.

    This process is the supervisor the AppHost and the logon task record in logs/<name>.supervisor.pid, so it passes its
    own PID as --supervisor-pid: the startup records and the session runner name it, and the command stops supervising
    once it is gone. The command's exit code is returned; 9 when the CLI cannot be built or started (one stderr line).
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$Name,
    [string]$WorkDir,
    [string]$Exe,
    [string]$ExeArgs,
    [string]$LogFile,
    [string]$ServicePidFile,
    [string]$StateFile,
    [string]$BuildProjectDir = '',
    [int]$LogMaxMb = 20,
    [int]$LogRetainDays = 5,
    [int]$LogRetainCount = 10
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @('--name', $Name, '--work-dir', $WorkDir, '--exe', $Exe, '--exe-args', $ExeArgs, '--log-file', $LogFile,
    '--service-pid-file', $ServicePidFile, '--state-file', $StateFile, '--build-project-dir', $BuildProjectDir,
    '--log-max-mb', [string]$LogMaxMb, '--log-retain-days', [string]$LogRetainDays, '--log-retain-count', [string]$LogRetainCount,
    '--supervisor-pid', [string]$PID)
exit (Invoke-AntiphonCli -Command 'run-daemon' -Arguments $arguments)
