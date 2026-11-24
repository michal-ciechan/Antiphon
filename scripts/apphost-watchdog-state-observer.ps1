#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts apphost-watchdog-state-observer` (scripts/lib/Antiphon.Nightly/AppHostMaintenance.cs):
    independently samples the "Antiphon AppHost Watchdog" Scheduled Task (Enabled, Disabled, Missing or Unknown) into
    logs/apphost-watchdog-state.json, so a recovered AppHost can raise Critical attention for a disabled, missing or
    unreadable watchdog. Detection only: it never restarts, re-enables, or writes Antiphon's database.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the per-user Scheduled Task
    "Antiphon AppHost Watchdog State Observer" (scripts/install-autostart.ps1) starts this file by path every two minutes.
    Delete it when that registration names the command. Agents run `scripts/antiphon apphost-watchdog-state-observer`.

    The parameters are the script's, passed when given: -TaskName, -Root, -StateFile, -MarkerFile, -LogFile. The command's
    exit code is returned; 9 when the CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows
    PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'Antiphon AppHost Watchdog',
    [string]$Root,
    [string]$StateFile,
    [string]$MarkerFile,
    [string]$LogFile
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('TaskName')) { $arguments += @('--task-name', $TaskName) }
if ($Root) { $arguments += @('--root', $Root) }
if ($StateFile) { $arguments += @('--state-file', $StateFile) }
if ($MarkerFile) { $arguments += @('--marker-file', $MarkerFile) }
if ($LogFile) { $arguments += @('--log-file', $LogFile) }
exit (Invoke-AntiphonCli -Command 'apphost-watchdog-state-observer' -Arguments $arguments)
