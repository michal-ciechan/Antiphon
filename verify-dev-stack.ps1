#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts verify-dev-stack` (scripts/lib/Antiphon.Nightly/DevStackVerify.cs): the one-shot
    health check of the local dev stack. Aspire mode (default, Windows: ports 1720x) or -SimpleMode (the compose stack: API
    17281, client 17282, runner 17204). Every check is reported before the verdict.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because scripts/deploy-local.ps1, restart.ps1
    and scripts/bootstrap-check.ps1 run this file by path. Agents run `scripts/antiphon verify-dev-stack`; Windows runbooks
    `scripts\antiphon.cmd verify-dev-stack`.

    The parameters are the script's, passed when given: -SimpleMode, -SkipBrowser, -TimeoutSec, -ProbeListeningPorts. The
    command's exit code is returned (0 verified, 1 a core check failed); 9 when the CLI cannot be built or started (one
    stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [switch]$SimpleMode,
    [switch]$SkipBrowser,
    [int]$TimeoutSec = 10,
    [switch]$ProbeListeningPorts
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($SimpleMode) { $arguments += '--simple-mode' }
if ($SkipBrowser) { $arguments += '--skip-browser' }
if ($PSBoundParameters.ContainsKey('TimeoutSec')) { $arguments += @('--timeout-sec', [string]$TimeoutSec) }
if ($ProbeListeningPorts) { $arguments += '--probe-listening-ports' }
exit (Invoke-AntiphonCli -Command 'verify-dev-stack' -Arguments $arguments)
