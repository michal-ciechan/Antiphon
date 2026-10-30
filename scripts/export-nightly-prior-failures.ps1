#requires -Version 5.1
# Explicit export from closed retained evidence. ASCII-only; no live services.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RunDirectory,
      [Parameter(Mandatory = $true)][string]$OutputPath,
      [switch]$PassThru)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-failure-history.ps1')
Export-NightlyPriorFailures -RunDirectory $RunDirectory -OutputPath $OutputPath -PassThru:$PassThru
