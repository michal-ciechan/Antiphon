#requires -Version 5.1
<#
.SYNOPSIS
Read one explicitly selected qualification without changing files or processes.
.DESCRIPTION
Run on the execution host. Exit 0 is a coherent observation (including a failed
run); exit 2 is incomplete or invalid input. Neither exit grants release credit
or permission to stop a process or clear a lock. PassThru returns the object.
#>
[CmdletBinding()]
param([string]$CandidateId, [string]$RunId, [string]$StateRoot, [string]$CoordinationRoot,
    [string]$ReleaseRoot, [switch]$PassThru)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/rc-qualification-status.ps1')
$status = Get-RcQualificationStatus -CandidateId $CandidateId -RunId $RunId -StateRoot $StateRoot `
    -CoordinationRoot $CoordinationRoot -ReleaseRoot $ReleaseRoot
if ($PassThru) { $status } else { $status | ConvertTo-Json -Depth 12 -Compress }
if ($status.observationComplete) { exit 0 }
exit 2
