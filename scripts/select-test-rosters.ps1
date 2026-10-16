#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineSha,
    [Parameter(Mandatory)][string]$CandidateSha,
    [string]$Profile='rc',
    [string]$RepositoryRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath,
    [switch]$Json,
    [string[]]$TimingRoots
)
$ErrorActionPreference='Stop'
# The JSON pipe is UTF-8 even when a Windows child has no console code page.
if ($Json) { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) }
try {
    . "$PSScriptRoot/lib/nightly-common.ps1"
    . "$PSScriptRoot/lib/nightly-policy.ps1"
    . "$PSScriptRoot/lib/test-selection.ps1"
    $selection=Get-TestSelection $RepositoryRoot $BaselineSha $CandidateSha $Profile
    if ($TimingRoots) {
        try {
            . "$PSScriptRoot/lib/test-timings.ps1"
            $timingReport = Get-TestTimingReport -Roots $TimingRoots -GroupBy roster
            $selection = Add-TestTimingEstimates -Selection $selection -Report $timingReport
        } catch { }
    }
    $text=ConvertTo-TestSelectionCanonicalJson $selection
    if($OutputPath){
        # Never overwrite an older intent and leave it looking like the requested run.
        $stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$bytes=[Text.Encoding]::UTF8.GetBytes($text+"`n");$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    }
    if($Json){Write-Output $text}else{
        Write-Output ("mode=$($selection.mode) activation=unqualified executionEvidence=false digest=$($selection.digest)")
        foreach($set in $selection.selectedSets){Write-Output ("$($set.id) eligibility=$($set.eligibility) platforms=$($set.platforms -join ',')")}
        foreach($change in $selection.changedPaths){Write-Output ("$($change.role) $($change.path): $($change.ruleIds -join ',') edges=$($change.edges -join ',')")}
        foreach($reason in $selection.fallbackReasons){Write-Output ("fallback: $reason")}
        Write-Output ("prerequisites: "+($selection.prerequisites -join ','))
    }
    exit 0
}catch{[Console]::Error.WriteLine("selection refused: "+$_.Exception.Message);exit 2}
