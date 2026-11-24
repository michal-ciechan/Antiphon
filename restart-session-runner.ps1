<#
.SYNOPSIS
    Thin forwarder to `antiphon-scripts restart-session-runner` (canonical 17204 daemon;
    scripts/lib/Antiphon.Nightly/RunnerRestart.cs). scripts/restart-session-runner.ps1 is gone (unit 30 of
    docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); run scripts\antiphon.cmd restart-session-runner
    (or scripts/antiphon restart-session-runner) directly.

    -Hard and -KillSessions are --hard and --kill-sessions; -TimeoutSec is --timeout-sec only when given (the command's
    default is 180 seconds), as the forwarder passed only its bound parameters before. The command's exit code is returned;
    9 when the CLI cannot be built or started.

    The old body launched a standalone `dotnet dll` on 17283. That port is retired.
    Do not retarget that launcher onto 17204 - it would fight the Scheduled Task's
    supervised process.
#>
param(
    [switch]$Hard,
    [switch]$KillSessions,
    [int]$TimeoutSec = 60
)

. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($Hard) { $arguments += '--hard' }
if ($KillSessions) { $arguments += '--kill-sessions' }
if ($PSBoundParameters.ContainsKey('TimeoutSec')) { $arguments += @('--timeout-sec', [string]$TimeoutSec) }
exit (Invoke-AntiphonCli -Command 'restart-session-runner' -Arguments $arguments)
