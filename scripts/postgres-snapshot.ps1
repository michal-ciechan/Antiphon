#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts postgres-snapshot` (scripts/lib/Antiphon.Nightly/PostgresSnapshot*.cs): the CARD-1405
    daily Postgres snapshot and scratch restore drill.

    Unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the Windmill job
    u/lndcobra/antiphon_postgres_snapshot starts this file by path (the runner copy of scripts/windmill/invoke-postgres-snapshot.ps1
    resolves it by its four source literals and the runner's SOURCE-SHA pin), and the cutover runbooks and
    deploy-server2-postgres.ps1 name it. Agents and operators may run `scripts\antiphon.cmd postgres-snapshot` directly.

    The parameters are the script's. -Plain is the operator decision of 2026-11-10 (SnapshotPlainDecision): the unencrypted
    compressed dump outside OneDrive; -OneDriveCopy copies the verified dump into OneDrive. -Target stays case-sensitive
    (desktop or server2) and -Container is passed only when it is bound (-Target server2 refuses it). Every plan line, refusal,
    retention rule and exit code is the command's; its exit code is returned, and 9 when the CLI cannot be built or started (one
    stderr line). The strings are passed as given (an empty value and a trailing backslash survive). The checkout's
    scripts/lib/antiphon-cli.ps1 runs the command; a copy of this file outside a checkout runs C:\src\Antiphon's.
    ASCII-only: parseable under Windows PowerShell 5.1. Do not pass a password or connection string.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [switch]$Plain,
    [switch]$OneDriveCopy,
    [int]$OneDriveRetain = 7,
    [switch]$RestoreDrill,
    [string]$Container = 'antiphon-postgres',
    [string]$Database = 'antiphon',
    [string]$StagingPath = '',
    [string]$Destination = '',
    [string]$SyncRoot = '',
    [string]$RecipientFile = '',
    [string]$RecipientRef = '',
    [string]$IdentityFile = '',
    [string]$IdentityRef = '',
    [int]$Retain = 14,
    [int64]$MinimumDumpBytes = 1048576,
    [switch]$KeepScratch,
    [string]$ScratchContainer = 'antiphon-postgres-drill',
    [string]$ScratchDatabase = 'antiphon_drill',
    [string]$ScratchVolume = 'antiphon-postgres-drill-pgdata',
    [string]$SourceVolume = 'antiphon_pgdata',
    [string]$ScratchImage = 'postgres:16',
    [switch]$AllowRemoteDocker,
    [int]$StaleRunMinutes = 360,
    [ValidateSet('desktop', 'server2', IgnoreCase = $false)]
    [string]$Target = 'desktop'
)

$ErrorActionPreference = 'Continue'
$cli = Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { $cli = 'C:\src\Antiphon\scripts\lib\antiphon-cli.ps1' }
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    [Console]::Error.WriteLine('antiphon-cli: no scripts\lib\antiphon-cli.ps1 beside this file or in C:\src\Antiphon')
    exit 9
}
. $cli
$arguments = @(
    '--one-drive-retain', [string]$OneDriveRetain,
    '--retain', [string]$Retain,
    '--minimum-dump-bytes', [string]$MinimumDumpBytes,
    '--stale-run-minutes', [string]$StaleRunMinutes,
    '--target', $Target
)
if ($WhatIf) { $arguments += '--what-if' }
if ($Execute) { $arguments += '--execute' }
if ($Plain) { $arguments += '--plain' }
if ($OneDriveCopy) { $arguments += '--one-drive-copy' }
if ($RestoreDrill) { $arguments += '--restore-drill' }
if ($KeepScratch) { $arguments += '--keep-scratch' }
if ($AllowRemoteDocker) { $arguments += '--allow-remote-docker' }
$verbatim = @(
    '--database', $Database,
    '--staging-path', $StagingPath,
    '--destination', $Destination,
    '--sync-root', $SyncRoot,
    '--recipient-file', $RecipientFile,
    '--recipient-ref', $RecipientRef,
    '--identity-file', $IdentityFile,
    '--identity-ref', $IdentityRef,
    '--scratch-container', $ScratchContainer,
    '--scratch-database', $ScratchDatabase,
    '--scratch-volume', $ScratchVolume,
    '--source-volume', $SourceVolume,
    '--scratch-image', $ScratchImage
)
if ($PSBoundParameters.ContainsKey('Container')) { $verbatim += @('--container', $Container) }
exit (Invoke-AntiphonCli -Command 'postgres-snapshot' -Arguments $arguments -Verbatim $verbatim)
