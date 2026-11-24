#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-backup` (scripts/lib/Antiphon.Nightly/DevStack.cs): a pg_dump of the antiphon
    database zipped into <repo>/backups/antiphon-<yyyyMMdd-HHmmss>.sql.zip (or -BackupDir), keeping the newest 14 (-Retain).
    The zip's path is the last line of output.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-backup.ps1` still
    works. Agents run `scripts/antiphon dev-backup`; Windows runbooks `scripts\antiphon.cmd dev-backup`.

    -BackupDir and -Retain are --backup-dir and --retain, passed when given. The command's exit code is returned (0
    backed up, 1 failed); 9 when the CLI cannot be built or started (one stderr line). ASCII-only: parseable under
    Windows PowerShell 5.1.
#>
param(
    [string]$BackupDir = (Join-Path $PSScriptRoot 'backups'),
    [int]$Retain = 14
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('BackupDir')) { $arguments += @('--backup-dir', $BackupDir) }
if ($PSBoundParameters.ContainsKey('Retain')) { $arguments += @('--retain', [string]$Retain) }
exit (Invoke-AntiphonCli -Command 'dev-backup' -Arguments $arguments)
