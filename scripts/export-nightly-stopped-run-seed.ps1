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
      [string]$TimingRoot='',
      [switch]$PassThru)
$ErrorActionPreference='Stop'
# One compiled call (lib/Antiphon.Nightly/StoppedSeedFiles.cs Export). A refusal is reported with exit code 2.
try {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    Assert-NightlyCoreAvailable 'export-nightly-stopped-run-seed'
    $seed=[Antiphon.Nightly.StoppedSeedFiles]::Export($RunDirectory,$OutputPath,$SourceRunId,$SourceCandidateId,$SourceSha,$TimingRoot)
} catch {
    $failure=$_.Exception
    if($failure -is [Management.Automation.MethodInvocationException] -and $null -ne $failure.InnerException){$failure=$failure.InnerException}
    Write-Error $failure.Message;exit 2
}
if($PassThru){return $seed}
Write-Host ('SEED '+$seed.Path+' SHA256 '+$seed.Digest)
