#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts check-daemon-build` (scripts/lib/Antiphon.Nightly/DaemonBuildCheck.cs): warn when
    an adopted daemon was built before a source change in its project closure. Advisory by default.

    Unit 30 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because scripts/restart-apphost.ps1 and
    dev-aspire.ps1 run this file by path. Delete it when they are ported. Agents and operators run
    `scripts/antiphon check-daemon-build` (Windows: scripts\antiphon.cmd check-daemon-build) directly.

    -FailOnStale is the command's --fail-on-stale. The command's exit code is returned; 9 when the CLI cannot be built or
    started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param([switch]$FailOnStale)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($FailOnStale) { $arguments = @('--fail-on-stale') }
exit (Invoke-AntiphonCli -Command 'check-daemon-build' -Arguments $arguments)
