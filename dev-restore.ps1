#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-restore` (scripts/lib/Antiphon.Nightly/DevStack.cs): after a typed 'yes' (or
    -Yes), drop and recreate the antiphon database and restore it from -BackupFile (.sql or .sql.zip), by default the newest
    *.sql.zip in <repo>/backups/ (or -BackupDir).

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-restore.ps1` still
    works. Agents run `scripts/antiphon dev-restore`; Windows runbooks `scripts\antiphon.cmd dev-restore`.

    -BackupFile, -BackupDir and -Yes are --backup-file, --backup-dir and --yes, passed when given; the prompt reads this
    console. The command's exit code is returned (0 restored or aborted, 1 failed); 9 when the CLI cannot be built or
    started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$BackupFile,
    [string]$BackupDir = (Join-Path $PSScriptRoot 'backups'),
    [switch]$Yes
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($PSBoundParameters.ContainsKey('BackupFile')) { $arguments += @('--backup-file', $BackupFile) }
if ($PSBoundParameters.ContainsKey('BackupDir')) { $arguments += @('--backup-dir', $BackupDir) }
if ($Yes) { $arguments += '--yes' }
exit (Invoke-AntiphonCli -Command 'dev-restore' -Arguments $arguments)
