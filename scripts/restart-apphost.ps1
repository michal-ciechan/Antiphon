#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts restart-apphost` (scripts/lib/Antiphon.Nightly/AppHostRestart.cs): kill and restart
    the desktop Aspire stack (AppHost, server, client, dashboard), preserving the session runner on 17204, then wait for it
    to come back healthy with the intended server SHA loaded.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because scripts/cutover-postgres-server2.ps1
    starts this file by path and the desktop-win-phase1-secrets command (unit 51) tells the operator to run it. Delete it when
    they no longer do (wave W8); the deploy-local command (unit 49) runs restart-apphost in its process. Agents run
    `scripts/antiphon restart-apphost`; Windows runbooks `scripts\antiphon.cmd restart-apphost`.

    The parameters are the script's; only the ones given are passed (the command's defaults are the script's): -NoBuild,
    -TimeoutSec, -MaxWaitSec, -LockMaxAgeMinutes, -AllowWorktree, -ExpectedServerSha, -GracefulStopTimeoutSec,
    -SkipGracefulStop, -StopOnly, -WhatIf. Exit codes are the command's: 0 restarted (or -StopOnly done), 1 failed, 3 refused
    (nothing killed), 4 DCP dependency timeout, 5 server build unverified; 9 when the CLI cannot be built or started (one
    stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150,
    [int]$MaxWaitSec = 600,
    [int]$LockMaxAgeMinutes,
    [switch]$AllowWorktree,
    [string]$ExpectedServerSha,
    [int]$GracefulStopTimeoutSec = 20,
    [switch]$SkipGracefulStop,
    [switch]$StopOnly,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($NoBuild) { $arguments += '--no-build' }
if ($PSBoundParameters.ContainsKey('TimeoutSec')) { $arguments += @('--timeout-sec', [string]$TimeoutSec) }
if ($PSBoundParameters.ContainsKey('MaxWaitSec')) { $arguments += @('--max-wait-sec', [string]$MaxWaitSec) }
if ($PSBoundParameters.ContainsKey('LockMaxAgeMinutes')) { $arguments += @('--lock-max-age-minutes', [string]$LockMaxAgeMinutes) }
if ($AllowWorktree) { $arguments += '--allow-worktree' }
if ($ExpectedServerSha) { $arguments += @('--expected-server-sha', $ExpectedServerSha) }
if ($PSBoundParameters.ContainsKey('GracefulStopTimeoutSec')) { $arguments += @('--graceful-stop-timeout-sec', [string]$GracefulStopTimeoutSec) }
if ($SkipGracefulStop) { $arguments += '--skip-graceful-stop' }
if ($StopOnly) { $arguments += '--stop-only' }
if ($WhatIf) { $arguments += '--what-if' }
exit (Invoke-AntiphonCli -Command 'restart-apphost' -Arguments $arguments)
