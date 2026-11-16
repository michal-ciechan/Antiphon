#requires -Version 7.0
# CARD-1836 S5c: opt-in diagnostic only. Never launches or activates qualification.
param([Parameter(Mandatory)][string]$RequestPath,[Parameter(Mandatory)][string]$RequestDigest,
    [Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$ExpectedSha,
    [Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-incremental-plan.ps1"
$request=Read-RcAdmissionJson $RequestPath $RequestDigest
$plan=New-RcIncrementalPlan $request $RepositoryRoot $ExpectedSha
Write-RcIncrementalPlan $PlanPath $plan
Write-Host ('RC PLAN diagnostic=true effective=Full required='+$plan.partition.required.Count+' carried=0 digest='+$plan.digest)
exit 0
