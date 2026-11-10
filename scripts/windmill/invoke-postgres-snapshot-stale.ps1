#requires -Version 5.1
<#
.SYNOPSIS
    08:00 check. Posts one STALE line when no plain success is newer than 26 hours.
    A fresh success prints JOB STATUS fresh and sends nothing.
    ASCII-only.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'job-status.ps1')

$now = [datetime]::UtcNow
$statusPath = 'C:\Users\lndco\antiphon-backups\snapshot-status.json'
$dumpDir = 'C:\Users\lndco\antiphon-backups'
$fresh = (Test-SnapshotStatusFresh -Path $statusPath -NowUtc $now -MaxAgeHours 26) -or (Test-SnapshotDumpFresh -Dir $dumpDir -NowUtc $now -MaxAgeHours 26)
if ($fresh) {
    Write-Output 'JOB STATUS fresh'
    exit 0
}
$record = New-JobStatusRecord -Job 'db-backup' -Icon 'STALE' -Status 'stale'
$recordDir = 'C:\Users\lndco\antiphon-backups\job-status'
if (-not (Test-Path -LiteralPath $recordDir)) { New-Item -ItemType Directory -Path $recordDir | Out-Null }
[void](Write-JobStatusRecord -Record $record -Path (Join-Path $recordDir 'db-backup-stale.json'))
$line = Format-JobStatusLine $record
Write-Output $line
exit (Send-JobStatusLine -Line $line)
