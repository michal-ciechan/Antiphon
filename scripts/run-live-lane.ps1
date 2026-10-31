#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Native')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Fixture')][hashtable]$Request,
    [Parameter(Mandatory, ParameterSetName = 'Fixture')][hashtable]$Boundary,
    [Parameter(ParameterSetName = 'Native')][string]$Lane,
    [Parameter(ParameterSetName = 'Native')][string]$CandidateRef,
    [Parameter(ParameterSetName = 'Native')][string]$ExpectedSha,
    [Parameter(ParameterSetName = 'Native')][string]$Manifest,
    [Parameter(ParameterSetName = 'Native')][string]$CoordinationRoot,
    [Parameter(ParameterSetName = 'Native')][string]$ResultsRoot
)
. (Join-Path $PSScriptRoot 'lib/live-lane-run.ps1')
if ($PSCmdlet.ParameterSetName -eq 'Native') {
    $result = Invoke-LiveLaneRun -Request @{ Mode = 'Native' }
    $result | ConvertTo-Json -Compress
    exit $result.ExitCode
}
Invoke-LiveLaneRun -Request $Request -Boundary $Boundary
