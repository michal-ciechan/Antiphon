#requires -Version 5.1
<#
.SYNOPSIS
CARD-1507: stop one recorded Windows rc qualification supervisor, then its driver.
.DESCRIPTION
Requires -Confirm. Reads TaskRoot\supervisor-identity.json and launch-child.json, checks the
recorded pid and start time, and on Windows runs taskkill /PID <supervisor> /T /F and then
taskkill /PID <driver> /T /F. Moves CoordinationRoot\native-run.lock into TaskRoot\stop-evidence
only when that lock's owner pid is dead, the run id matches, and the preserved bytes are
unchanged. Appends the stop reason to TaskRoot\supervisor.log. Does not edit last-run.json and
does not remove run.lock. A non-Windows host exits 5 and does not stop a process or change a file.
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
if (-not $Confirm) {
    Write-Output 'stop-supervisor: refused, -Confirm is required; no process was stopped'
    exit 2
}
. (Join-Path $PSScriptRoot 'qualification-stop.ps1')
$params = @{
    TaskRoot = $TaskRoot
    CoordinationRoot = $CoordinationRoot
    RunId = $RunId
    Reason = $Reason
    CensusMilliseconds = 30000
}
if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) { $params.EvidenceRoot = $EvidenceRoot }
$result = Invoke-QualificationRecordedStop @params
Write-Output ($result | ConvertTo-Json -Compress -Depth 6)
exit $result.exitCode
