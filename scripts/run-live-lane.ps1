#requires -Version 7.0
[CmdletBinding()]
param([hashtable]$Request = @{}, [hashtable]$Boundary = @{})
. (Join-Path $PSScriptRoot 'lib/live-lane-run.ps1')
Invoke-LiveLaneRun -Request $Request -Boundary $Boundary
