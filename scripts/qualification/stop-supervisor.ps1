#requires -Version 5.1
<#
.SYNOPSIS
CARD-1507: stop one recorded Windows rc qualification supervisor, then its driver.
.DESCRIPTION
Without -Confirm, prints a dry run of the recorded pids and identities and exits 2.
Nothing is killed. With -Confirm, reads TaskRoot\supervisor-identity.json and
launch-child.json, checks the recorded pid, start time, and command line when the
record has one, and on Windows runs taskkill /PID <supervisor> /T /F. A second
taskkill /PID <driver> /T /F runs only when that driver outlived the supervisor tree.
A driver already absent after the tree kill is not a failed stop. Moves
CoordinationRoot\native-run.lock into TaskRoot\stop-evidence only when that lock's
owner pid is dead, the run id matches, and the preserved bytes are unchanged.
Appends the stop reason to TaskRoot\supervisor.log. A log write failure still
returns the JSON result. Does not edit last-run.json and does not remove run.lock.
A non-Windows host exits 5 and does not stop a process or change a file.
Do not point this at a live qualification unless the operator has decided to stop that run.
ASCII-only, Windows PowerShell 5.1 safe.
#>
param(
    [Parameter(Mandatory = $true)][string]$TaskRoot,
    [Parameter(Mandatory = $true)][string]$CoordinationRoot,
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$Reason,
    [string]$EvidenceRoot,
    [switch]$Confirm
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'qualification-stop.ps1')
if (-not $Confirm) {
    Write-Output (Get-QualificationStopDryRunText -TaskRoot $TaskRoot -CoordinationRoot $CoordinationRoot -RunId $RunId)
    exit 2
}
$params = @{
    TaskRoot = $TaskRoot
    CoordinationRoot = $CoordinationRoot
    RunId = $RunId
    Reason = $Reason
    CensusMilliseconds = 30000
}
if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) { $params.EvidenceRoot = $EvidenceRoot }
$result = $null
try { $result = Invoke-QualificationRecordedStop @params }
catch { $result = New-QualificationStopResult 3 'incomplete' 'log-unwritten' $null 'not-attempted' $null $null }
Write-Output ($result | ConvertTo-Json -Compress -Depth 6)
exit $result.exitCode
