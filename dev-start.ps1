#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-start` (scripts/lib/Antiphon.Nightly/DevStack.cs): start the dev stack in simple
    mode (docker-compose path, no Aspire): Postgres from docker-compose.dev.yml, the server (port 17281, stamped with HEAD)
    and the Vite client (17282) in Windows Terminal tabs, then the browser. dev-aspire.ps1 is the AppHost / dashboard mode.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-start.ps1` still
    works. Agents run `scripts/antiphon dev-start`; Windows runbooks `scripts\antiphon.cmd dev-start`.

    -NoClient, -NoBrowser and -ServerOnly are --no-client, --no-browser and --server-only. The command's exit code is
    returned (0 started, 1 failed); 9 when the CLI cannot be built or started (one stderr line). ASCII-only: parseable
    under Windows PowerShell 5.1.
#>
param([switch]$NoClient, [switch]$NoBrowser, [switch]$ServerOnly)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($NoClient) { $arguments += '--no-client' }
if ($NoBrowser) { $arguments += '--no-browser' }
if ($ServerOnly) { $arguments += '--server-only' }
exit (Invoke-AntiphonCli -Command 'dev-start' -Arguments $arguments)
