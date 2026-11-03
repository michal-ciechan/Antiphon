#requires -Version 5.1
<#
.SYNOPSIS
CARD-1667: read-only classification of a supervised rc qualification run.
.DESCRIPTION
Reads TaskRoot (supervisor-identity.json, supervisor-heartbeat.json, commission-exit.txt) and
StateRoot (run.lock, last-run.json). last-run.json completedAt is the end signal; a gone
supervisor or stale heartbeat while nightly-run still owns run.lock is
supervisor-lost-run-alive, an observation gap and not a run failure.
Prints one JSON line. Exit 0: completed, running or supervisor-lost-run-alive. Exit 3: run-lost
(nightly-run gone without completedAt; investigate). Exit 2: unknown or unreadable input.
Writes nothing and never stops a process or clears a lock. ASCII-only, Windows PowerShell 5.1 safe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TaskRoot,
    [Parameter(Mandatory = $true)][string]$StateRoot,
    [ValidateRange(1, 120)][double]$HeartbeatStaleMinutes = 5,
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/qualification-supervisor.ps1')
$observation = Get-QualificationSupervision -TaskRoot $TaskRoot -StateRoot $StateRoot -StaleMinutes $HeartbeatStaleMinutes
if ($PassThru) { $observation } else { $observation | ConvertTo-Json -Compress }
switch ($observation.classification) {
    'completed' { exit 0 }
    'running' { exit 0 }
    'supervisor-lost-run-alive' { exit 0 }
    'run-lost' { exit 3 }
    default { exit 2 }
}
