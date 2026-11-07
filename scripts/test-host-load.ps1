#requires -Version 7.0
# Owned process adapter for the CARD-1514 ScriptHarness. It changes only this child's environment.
param([string]$Case, [string]$ResultsDirectory, [string]$ApiUrl, [string]$TokenFile,
    [string]$HostId, [string]$Project, [string]$Role, [switch]$JsonMode, [switch]$Diagnostic,
    [string]$ProbeConfig, [string]$ProbePath)
$ErrorActionPreference = 'Stop'
if ($Case -eq 'Probe') { & dotnet $ProbePath $ProbeConfig; exit $LASTEXITCODE }
$env:ANTIPHON_API = $ApiUrl
$env:ANTIPHON_TASK_TOKEN = if ($TokenFile) { [IO.File]::ReadAllText($TokenFile) } else { $null }
if ($Diagnostic) { [Console]::Error.WriteLine('c1514-owned-diagnostic') }
$parameters = @{}
if ($HostId) { $parameters.HostId = $HostId }
if ($Project) { $parameters.Project = $Project }
if ($Role) { $parameters.Role = $Role }
if ($JsonMode) { $parameters.Json = $true }
& (Join-Path $PSScriptRoot 'host-load.ps1') @parameters
exit $LASTEXITCODE
