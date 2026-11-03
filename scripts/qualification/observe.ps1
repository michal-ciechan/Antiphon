#requires -Version 5.1
<#
.SYNOPSIS
CARD-1667: read-only classification of a supervised rc qualification run.
.DESCRIPTION
Reads TaskRoot (supervisor-identity.json, supervisor-heartbeat.json, commission-exit.txt) and
StateRoot (run.lock, last-run.json). Every verdict is bound to the launch's run id recorded in
supervisor-identity.json: the state root is shared by every run, so a last-run.json or run.lock
naming another run id is never credited to this launch. last-run.json completedAt for the bound
run id is the end signal; a gone supervisor or stale heartbeat while the bound run still owns
run.lock is supervisor-lost-run-alive, an observation gap and not a run failure.
Prints one JSON line. Exit 0: completed, running or supervisor-lost-run-alive. Exit 3: run-lost
(the bound run's nightly-run gone from its run.lock without completedAt; investigate). Exit 2:
not-started (bound run id not in run.lock or last-run.json yet, e.g. waiting for a build slot),
launch-failed (commission-exit.txt written and the run id never seen; see reason and
runLock/lockRunId for another run's lock), or unknown (unbound or unreadable input, or a bound
run whose run.lock was released or replaced without completedAt: read last-run.json).
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
    'not-started' { exit 2 }
    'launch-failed' { exit 2 }
    default { exit 2 }
}
