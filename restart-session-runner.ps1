<#
.SYNOPSIS
    Thin forwarder to scripts/restart-session-runner.ps1 (canonical 17204 daemon).

    The old body launched a standalone `dotnet dll` on 17283. That port is retired.
    Do not retarget that launcher onto 17204 — it would fight the Scheduled Task's
    supervised process.
#>
param(
    [switch]$Hard,
    [switch]$KillSessions,
    [int]$TimeoutSec = 60
)

& "$PSScriptRoot\scripts\restart-session-runner.ps1" @PSBoundParameters
exit $LASTEXITCODE
