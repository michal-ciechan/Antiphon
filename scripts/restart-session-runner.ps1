<#
.SYNOPSIS
    Restart the runner, or continue observing without another restart.
.DESCRIPTION
    Supervisor delay, incremental build, launch and complete adoption precede HTTP.
    Detached sessions survive. Exit 0 healthy; 2 wait-expired; 1 action-failed or wait-failed.
    Stops the recorded service wrapper and runners at this checkout's Debug executable
    path, not arbitrary port 17204 listeners. Other build paths remain untouched even
    with -Hard; expiry output identifies the observed port owner's PID and path.
.PARAMETER TimeoutSec
    Positive observation budget after restart actions, default 180 seconds.
.PARAMETER WaitOnly
    No desired-state writes, process stops, PID deletion or Scheduled Task calls.
.PARAMETER Hard
    Refresh the supervisor for a planned deployment.
.PARAMETER KillSessions
    Also kill detached sessions.
.EXAMPLE
    pwsh -NoProfile -File scripts/restart-session-runner.ps1
.EXAMPLE
    pwsh -NoProfile -File scripts/restart-session-runner.ps1 -WaitOnly -TimeoutSec 180
#>
param([switch]$Hard, [switch]$KillSessions, [switch]$WaitOnly, [int]$TimeoutSec = 180)
$ErrorActionPreference = 'Stop'
$commandClock = [Diagnostics.Stopwatch]::StartNew()
$commandEnteredAtUtc = [DateTime]::UtcNow.ToString('o')
. (Join-Path $PSScriptRoot 'session-runner-restart-health.ps1')
$platform = New-RunnerRestartPlatform -Root (Split-Path $PSScriptRoot -Parent) -CommandClock $commandClock
$result = Invoke-RunnerRestart -Platform $platform -Hard:$Hard -KillSessions:$KillSessions -WaitOnly:$WaitOnly -TimeoutSec $TimeoutSec -CommandEnteredAtUtc $commandEnteredAtUtc
Write-Host ('RUNNER RESTART RESULT: ' + ($result | ConvertTo-Json -Compress -Depth 6))
exit $result.exitCode
