#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts restart-dev-stack` (scripts/lib/Antiphon.Nightly/DevRestart.cs): restart the server
    and the client through the Aspire control endpoint (Mikey.AspireCommand), the session runner first with -RestartRunner,
    then verify the stack (verify-dev-stack) unless -SkipVerify.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\restart.ps1` still
    works. Agents run `scripts/antiphon restart-dev-stack`; Windows runbooks `scripts\antiphon.cmd restart-dev-stack`.

    -Endpoint, -StopPortOwners, -RestartRunner and -SkipVerify are --endpoint (passed when given), --stop-port-owners,
    --restart-runner and --skip-verify. The command's exit code is returned (0 restarted, 1 a step failed); 9 when the
    CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners,
    [switch]$RestartRunner,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('Endpoint')) { $arguments += @('--endpoint', $Endpoint) }
if ($StopPortOwners) { $arguments += '--stop-port-owners' }
if ($RestartRunner) { $arguments += '--restart-runner' }
if ($SkipVerify) { $arguments += '--skip-verify' }
exit (Invoke-AntiphonCli -Command 'restart-dev-stack' -Arguments $arguments)
