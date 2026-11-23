#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts keep-six` (scripts/lib/Antiphon.Nightly/KeepSix.cs).

    Unit 23 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept only because
    scripts/cutover-postgres-server2.ps1 starts this file by path (-Hold and -ClearHold around the
    cutover window). Delete it when the cutover is ported (wave W8). Agents and operators run
    `scripts/antiphon keep-six` (Windows: scripts\antiphon.cmd keep-six) directly.

    The parameters are the script's: no verb reads the status; -Hold -Agent -Reason -Until,
    -ClearHold -Agent and -Ack -Agent. The command's exit code is returned; 9 when the CLI cannot
    be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [switch]$Hold,

    [Parameter(Mandatory = $true, ParameterSetName = 'Ack')]
    [switch]$Ack,

    [Parameter(Mandatory = $true, ParameterSetName = 'ClearHold')]
    [switch]$ClearHold,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Ack')]
    [Parameter(Mandatory = $true, ParameterSetName = 'ClearHold')]
    [string]$Agent,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [string]$Reason,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [string]$Until
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($Hold) { $arguments = @('--hold', '--agent', $Agent, '--reason', $Reason, '--until', $Until) }
elseif ($ClearHold) { $arguments = @('--clear-hold', '--agent', $Agent) }
elseif ($Ack) { $arguments = @('--ack', '--agent', $Agent) }
exit (Invoke-AntiphonCli -Command 'keep-six' -Arguments $arguments)
