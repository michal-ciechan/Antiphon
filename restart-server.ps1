#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts restart-server` (scripts/lib/Antiphon.Nightly/DevRestart.cs): the toolchain floor
    warnings (CARD-1698, never a refusal), then stop the server through the Aspire control endpoint (Mikey.AspireCommand),
    check port 17281, build server/Antiphon.Server.csproj stamped with HEAD and start it.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\restart-server.ps1` still
    works. Agents run `scripts/antiphon restart-server`; Windows runbooks `scripts\antiphon.cmd restart-server`.

    -Endpoint and -StopPortOwners are --endpoint (passed when given) and --stop-port-owners. The command's exit code is
    returned (0 restarted, 1 a step failed); 9 when the CLI cannot be built or started (one stderr line). ASCII-only:
    parseable under Windows PowerShell 5.1.
#>
param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('Endpoint')) { $arguments += @('--endpoint', $Endpoint) }
if ($StopPortOwners) { $arguments += '--stop-port-owners' }
exit (Invoke-AntiphonCli -Command 'restart-server' -Arguments $arguments)
