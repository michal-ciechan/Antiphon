#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts set-apphost-maintenance` (scripts/lib/Antiphon.Nightly/AppHostMaintenance.cs): the
    supported way to leave the desktop AppHost down on purpose. Entering creates logs/apphost.down-on-purpose, then disables
    the "Antiphon AppHost Watchdog" task; -Clear re-enables the watchdog, then removes the marker; both then run the
    watchdog-state observer. It never starts or stops the AppHost.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because scripts/cutover-postgres-server2.ps1
    starts this file by path (-EnterIfAbsent, -Clear). Delete it when the cutover is ported (wave W8). Agents run
    `scripts/antiphon set-apphost-maintenance`; Windows runbooks `scripts\antiphon.cmd set-apphost-maintenance`.

    The parameters are the script's: -Clear, -EnterIfAbsent (create-new; exit 2 when the marker already exists), -Root,
    -WatchdogTaskName. The command's exit code is returned; 9 when the CLI cannot be built or started (one stderr line).
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [switch]$Clear,
    [switch]$EnterIfAbsent,
    [string]$Root,
    [string]$WatchdogTaskName = 'Antiphon AppHost Watchdog'
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($Clear) { $arguments += '--clear' }
if ($EnterIfAbsent) { $arguments += '--enter-if-absent' }
if ($Root) { $arguments += @('--root', $Root) }
if ($PSBoundParameters.ContainsKey('WatchdogTaskName')) { $arguments += @('--watchdog-task-name', $WatchdogTaskName) }
exit (Invoke-AntiphonCli -Command 'set-apphost-maintenance' -Arguments $arguments)
