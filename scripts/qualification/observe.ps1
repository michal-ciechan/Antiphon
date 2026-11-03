#requires -Version 5.1
<#
.SYNOPSIS
CARD-1667: read-only classification of a supervised rc qualification run.
.DESCRIPTION
Reads TaskRoot (supervisor-identity.json, supervisor-heartbeat.json, launch-child.json,
own-last-run.json, commission-exit.txt) and StateRoot (run.lock, last-run.json). Every verdict
is bound to the launch's run id recorded in supervisor-identity.json: the state root is shared by
every run, so a last-run.json or run.lock naming another run id is never credited to this
launch. last-run.json holds only the latest run; own-last-run.json is supervise-rc's copy of it
while it named the bound run id, and stands in for it once another run has overwritten it.
completedAt for the bound run id (in last-run.json, or else in the copy) is the end signal; a gone
supervisor or stale heartbeat while the bound run still owns run.lock is
supervisor-lost-run-alive, an observation gap and not a run failure.
Prints one JSON line. Exit 0: completed, running or supervisor-lost-run-alive. Exit 3: run-lost
(the bound run's nightly-run gone from its run.lock without completedAt; investigate). Exit 2:
not-started (bound run id not in run.lock, last-run.json or the copy yet, e.g. waiting for a
build slot), launch-failed (commission-exit.txt written, the run id in none of them, and
last-run.json names a run that started before the launch child; see reason and
runLock/lockRunId for another run's lock), or unknown (unbound or unreadable input; a bound run
whose run.lock was released or replaced without completedAt: read last-run.json; or reason
state-superseded: last-run.json now names a later run and no copy proves completion, so the
run may have finished: read its LogRoot run directory before any relaunch).
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
