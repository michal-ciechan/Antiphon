#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1886. Record a Postgres JIT window without resetting statistics.

    Default prints the plan and does not call docker.
    -Execute requires -Container and -OutDir. It writes a start snapshot,
    samples docker stats --no-stream for -Minutes (default 10) at
    -IntervalSeconds (5 through 10, default 10), then writes an end snapshot.
    Each snapshot is pg_stat_statements_info plus the current database's
    statement rows: identity, calls, rows, total_exec_time, total_plan_time,
    block counters, and every jit_ column the view actually has.
    This script does not reset statistics.
#>
[CmdletBinding()]
param(
    [switch]$Execute,
    [string]$Container,
    [string]$OutDir,
    [string]$Database = 'antiphon',
    [string]$User = 'postgres',
    [int]$Minutes = 10,
    [int]$IntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'

if (-not $Execute) {
    Write-Output 'CARD-1886 JIT window plan. No docker call.'
    Write-Output 'After run 97150a0b ends: pwsh -NoProfile -File scripts/postgres-jit-window.ps1 -Execute -Container <name> -OutDir <dir>'
    Write-Output 'Records pg_stat_statements_info, total_exec_time, total_plan_time, calls, rows, block counters, and jit_ columns. Does not reset statistics.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Container)) { throw '-Container is required with -Execute' }
if ([string]::IsNullOrWhiteSpace($OutDir)) { throw '-OutDir is required with -Execute' }
if ($Minutes -lt 1 -or $Minutes -gt 30) { throw '-Minutes must be from 1 through 30' }
if ($IntervalSeconds -lt 5 -or $IntervalSeconds -gt 10) { throw '-IntervalSeconds must be from 5 through 10' }
if ($Database -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw '-Database must be a plain identifier' }
if ($User -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw '-User must be a plain identifier' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Invoke-ContainerPsql {
    param([string]$Sql)
    $output = $Sql | & docker exec -i $Container psql -X -A -t -v ON_ERROR_STOP=1 -U $User -d $Database
    if ($LASTEXITCODE -ne 0) { throw "psql failed with exit $LASTEXITCODE" }
    return $output
}

function Get-JitColumnList {
    $listed = Invoke-ContainerPsql @"
SELECT attname
FROM pg_attribute
WHERE attrelid = 'pg_stat_statements'::regclass
  AND attnum > 0
  AND NOT attisdropped
  AND attname LIKE 'jit_%'
ORDER BY attnum;
"@
    $names = @()
    foreach ($line in @($listed)) {
        $trimmed = ([string]$line).Trim()
        if ($trimmed.Length -eq 0) { continue }
        if ($trimmed -notmatch '^jit_[a-z0-9_]+$') { throw 'unexpected jit column name' }
        $names += $trimmed
    }
    return $names
}

function Get-SnapshotSql {
    param([string[]]$JitColumns)
    $columns = @(
        'dbid::text AS dbid',
        'userid::text AS userid',
        'queryid::text AS queryid',
        'toplevel',
        'calls',
        'rows',
        'total_exec_time',
        'total_plan_time',
        'shared_blks_hit',
        'shared_blks_read',
        'shared_blks_dirtied',
        'shared_blks_written',
        'temp_blks_read',
        'temp_blks_written'
    ) + $JitColumns
    $select = $columns -join ', '
    return @"
SELECT json_build_object(
  'recordedAt', clock_timestamp(),
  'postmasterStartedAt', pg_postmaster_start_time(),
  'database', current_database(),
  'info', (SELECT row_to_json(i) FROM pg_stat_statements_info i),
  'statements', COALESCE((SELECT json_agg(row_to_json(s)) FROM (
    SELECT $select
    FROM pg_stat_statements
    WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
  ) s), '[]'::json)
);
"@
}

function Write-Snapshot {
    param([string]$Name, [string[]]$JitColumns)
    $json = Invoke-ContainerPsql (Get-SnapshotSql -JitColumns $JitColumns)
    $path = Join-Path $OutDir $Name
    Set-Content -LiteralPath $path -Value ($json -join "`n") -Encoding ascii
}

$started = [DateTime]::UtcNow
$jitColumns = @(Get-JitColumnList)
Write-Snapshot -Name 'statements-start.json' -JitColumns $jitColumns

$statsPath = Join-Path $OutDir 'docker-stats.txt'
$deadline = (Get-Date).AddMinutes($Minutes)
do {
    $stamp = [DateTime]::UtcNow.ToString('o')
    $sample = & docker stats --no-stream --format '{{.Name}} {{.CPUPerc}} {{.MemPerc}}' $Container
    if ($LASTEXITCODE -ne 0) { throw 'docker stats failed' }
    Add-Content -LiteralPath $statsPath -Value ($stamp + ' ' + ($sample -join ' ')) -Encoding ascii
    if ((Get-Date) -ge $deadline) { break }
    Start-Sleep -Seconds $IntervalSeconds
} while ((Get-Date) -lt $deadline)

Write-Snapshot -Name 'statements-end.json' -JitColumns $jitColumns

$note = @{
    container = $Container
    database = $Database
    minutes = $Minutes
    intervalSeconds = $IntervalSeconds
    startedAt = $started.ToString('o')
    endedAt = [DateTime]::UtcNow.ToString('o')
    jitColumns = @($jitColumns)
}
$note | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir 'window.json') -Encoding ascii
Write-Output ("CARD-1886 JIT window written under " + $OutDir)
