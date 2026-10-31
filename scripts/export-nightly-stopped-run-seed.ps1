#requires -Version 5.1
<#
.SYNOPSIS
Build an ordering-only seed from an operator-stopped run's retained logs.
.DESCRIPTION
Read-only source directory; output must be outside it. No liveness inference,
process operations, receipt reuse or execution credit. Supply source metadata
when absent from owner JSON; conflicting planned/owner identity refuses export.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory,
      [Parameter(Mandatory=$true)][string]$OutputPath,
      [string]$SourceRunId='', [string]$SourceCandidateId='', [string]$SourceSha='',
      [switch]$PassThru)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-failure-history.ps1')
try {Export-NightlyStoppedRunSeed @PSBoundParameters}
catch {Write-Error $_.Exception.Message;exit 2}
