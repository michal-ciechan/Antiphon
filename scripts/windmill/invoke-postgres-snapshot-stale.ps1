#requires -Version 5.1
<#
.SYNOPSIS
    08:00 check. Posts one STALE line when no plain success is newer than 26 hours.
    A fresh success prints JOB STATUS fresh and sends nothing.

    Thin launcher for `antiphon-scripts postgres-snapshot-stale` (scripts/lib/Antiphon.Nightly/PostgresSnapshotJob.cs), unit 53
    of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. Kept because Windmill's
    u/lndcobra/antiphon_postgres_snapshot_stale (08:00, desktop tag, SSH bridge) starts the runner copy,
    C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot-stale.ps1, by path; the job definition is unchanged. It reads
    C:\Users\lndco\antiphon-backups\snapshot-status.json and the plain dumps beside it, writes job-status\db-backup-stale.json
    when stale and returns the send's exit (0 when fresh); 9 when the CLI cannot be built or started (one stderr line).
    The checkout's scripts/lib/antiphon-cli.ps1 runs the command; the runner copy runs C:\src\Antiphon's.
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'
$cli = Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'lib') 'antiphon-cli.ps1'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { $cli = 'C:\src\Antiphon\scripts\lib\antiphon-cli.ps1' }
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    [Console]::Error.WriteLine('antiphon-cli: no scripts\lib\antiphon-cli.ps1 above this file or in C:\src\Antiphon')
    exit 9
}
. $cli
exit (Invoke-AntiphonCli -Command 'postgres-snapshot-stale')
