#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts autostart-apphost` (scripts/lib/Antiphon.Nightly/AppHostAutostart.cs): the logon
    auto-start of the desktop Aspire AppHost (server 17202, client 17203, dashboard 17205, control API 17207). It waits for
    Docker Desktop, no-ops when 17202 already listens, brings up the dev compose services (ANTIPHON_SKIP_DEV_POSTGRES=1 keeps
    the desktop Postgres stopped), runs dev-aspire.ps1 -NoBrowser and waits for /health. Logs to logs/autostart-apphost.log.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the per-user Scheduled Task
    "Antiphon AppHost" (`scripts\antiphon.cmd install-autostart`) starts this file by path at logon. Delete it when that registration
    names the command.

    The parameters are the script's, passed when given: -NoBuild, -DockerTimeoutSec, -HealthTimeoutSec. The command's exit
    code is returned (0 healthy or already up, 1 otherwise); 9 when the CLI cannot be built or started (one stderr line).
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$DockerTimeoutSec = 300,
    [int]$HealthTimeoutSec = 180
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($NoBuild) { $arguments += '--no-build' }
if ($PSBoundParameters.ContainsKey('DockerTimeoutSec')) { $arguments += @('--docker-timeout-sec', [string]$DockerTimeoutSec) }
if ($PSBoundParameters.ContainsKey('HealthTimeoutSec')) { $arguments += @('--health-timeout-sec', [string]$HealthTimeoutSec) }
exit (Invoke-AntiphonCli -Command 'autostart-apphost' -Arguments $arguments)
