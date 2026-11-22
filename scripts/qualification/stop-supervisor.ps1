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
For a task-directory copy, pass -CheckoutRoot to load qualification-stop.ps1 and its library from the checkout.
The checkout root (-CheckoutRoot, or two levels above this script when omitted) must be an
existing directory with a .git file or directory at its root (a checkout or linked worktree)
that holds scripts/qualification/qualification-stop.ps1 and scripts/lib/qualification-supervisor.ps1.
A supplied blank or whitespace -CheckoutRoot is refused, not defaulted. Otherwise the script exits 6 before it loads any helper, and does not stop a process or change a file.
Exit codes: 0 stopped, 2 dry run or refused, 3 incomplete, 4 lock retained, 5 non-Windows,
6 checkout root refused.
Do not point this at a live qualification unless the operator has decided to stop that run.
ASCII-only, Windows PowerShell 5.1 safe.
#>
param(
    [Parameter(Mandatory = $true)][string]$TaskRoot,
    [Parameter(Mandatory = $true)][string]$CoordinationRoot,
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$Reason,
    [string]$EvidenceRoot,
    [switch]$Confirm,
    [string]$CheckoutRoot
)
$ErrorActionPreference = 'Stop'
$checkoutRefusal = $null
if ($PSBoundParameters.ContainsKey('CheckoutRoot')) {
    if ([string]::IsNullOrWhiteSpace($CheckoutRoot)) { $checkoutRefusal = 'blank value' }
}
else { $CheckoutRoot = Join-Path $PSScriptRoot '../..' }
if ($null -ne $checkoutRefusal) { }
elseif (-not (Test-Path -LiteralPath $CheckoutRoot -PathType Container)) { $checkoutRefusal = 'not an existing directory' }
else {
    $CheckoutRoot = (Resolve-Path -LiteralPath $CheckoutRoot).ProviderPath
    $dotGit = Join-Path $CheckoutRoot '.git'
    if (-not ([IO.Directory]::Exists($dotGit) -or [IO.File]::Exists($dotGit))) { $checkoutRefusal = 'no .git file or directory at its root' }
    else {
        foreach ($required in @('scripts/qualification/qualification-stop.ps1', 'scripts/lib/qualification-supervisor.ps1')) {
            if ($null -eq $checkoutRefusal -and -not [IO.File]::Exists((Join-Path $CheckoutRoot $required))) { $checkoutRefusal = 'missing ' + $required }
        }
    }
}
if ($null -ne $checkoutRefusal) {
    [Console]::Error.WriteLine('stop-supervisor: refused; -CheckoutRoot ' + $CheckoutRoot + ' is not a checkout (' + $checkoutRefusal + '); pass the checkout or linked worktree root; no helper was loaded and no process was stopped')
    exit 6
}
. (Join-Path $CheckoutRoot 'scripts/qualification/qualification-stop.ps1')
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
catch { $result = New-QualificationStopResult 3 'incomplete' 'stop-fault' $null 'not-attempted' $null $null }
Write-Output ($result | ConvertTo-Json -Compress -Depth 6)
exit $result.exitCode
