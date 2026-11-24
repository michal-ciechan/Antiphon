#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts stop-server` (scripts/lib/Antiphon.Nightly/DevRestart.cs): stop the server through
    the Aspire control endpoint (Mikey.AspireCommand), then wait 3 s for its file locks.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\stop-server.ps1` still
    works. Agents run `scripts/antiphon stop-server`; Windows runbooks `scripts\antiphon.cmd stop-server`.

    -Endpoint is --endpoint, passed when given. The command's exit code is returned (0); 9 when the CLI cannot be built
    or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$Endpoint = "https://localhost:22025"
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('Endpoint')) { $arguments += @('--endpoint', $Endpoint) }
exit (Invoke-AntiphonCli -Command 'stop-server' -Arguments $arguments)
