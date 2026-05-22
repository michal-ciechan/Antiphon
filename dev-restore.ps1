<#
.SYNOPSIS
    Restore the Antiphon database from a backup.
    If no file is given, uses the most recent backup in <repo>/backups/.
.PARAMETER BackupFile
    Path to a .sql or .sql.zip backup file. Defaults to latest in BackupDir.
.PARAMETER BackupDir
    Where to look for the latest backup. Default: <repo>/backups/.
.PARAMETER Yes
    Skip the confirmation prompt.
#>
param(
    [string]$BackupFile,
    [string]$BackupDir = (Join-Path $PSScriptRoot 'backups'),
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$root        = $PSScriptRoot
$composeFile = "$root\docker-compose.dev.yml"

# Resolve backup file
if (-not $BackupFile) {
    $latest = Get-ChildItem $BackupDir -Filter '*.sql.zip' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) { Write-Error "No backups found in $BackupDir. Run .\dev-backup.ps1 first." }
    $BackupFile = $latest.FullName
    Write-Host "Using latest backup: $BackupFile" -ForegroundColor Cyan
}

if (-not (Test-Path $BackupFile)) { Write-Error "File not found: $BackupFile" }

Write-Host "`nThis will DROP and recreate the 'antiphon' database, then restore from backup." -ForegroundColor Yellow
if (-not $Yes) {
    $confirm = Read-Host "Type 'yes' to continue"
    if ($confirm -ne 'yes') { Write-Host "Aborted."; exit 0 }
}

# Stop server (keep postgres)
Write-Host "`n▶ Stopping .NET server..." -ForegroundColor Cyan
& "$root\dev-stop.ps1"

# Ensure postgres is running
Write-Host "▶ Ensuring PostgreSQL is running..." -ForegroundColor Cyan
docker compose -f $composeFile up -d
Start-Sleep 5

# Extract .zip if needed
$sqlFile = $BackupFile
if ($BackupFile -like '*.zip') {
    $tmpDir  = Join-Path $env:TEMP "antiphon-restore-$(Get-Date -Format 'yyyyMMddHHmmss')"
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    Expand-Archive -Path $BackupFile -DestinationPath $tmpDir -Force
    $sqlFile = Get-ChildItem $tmpDir -Filter '*.sql' | Select-Object -First 1 -ExpandProperty FullName
    if (-not $sqlFile) { Write-Error "No .sql file found inside $BackupFile" }
}

# Drop + recreate database
Write-Host "▶ Recreating database..." -ForegroundColor Cyan
docker compose -f $composeFile exec -T postgres psql -U antiphon -d postgres `
    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='antiphon';" 2>&1 | Out-Null
docker compose -f $composeFile exec -T postgres psql -U antiphon -d postgres `
    -c "DROP DATABASE IF EXISTS antiphon;" 2>&1 | Out-Null
docker compose -f $composeFile exec -T postgres psql -U antiphon -d postgres `
    -c "CREATE DATABASE antiphon OWNER antiphon;" 2>&1 | Out-Null

# Restore
Write-Host "▶ Restoring from $BackupFile ..." -ForegroundColor Cyan
Get-Content $sqlFile | docker compose -f $composeFile exec -T postgres `
    psql -U antiphon -d antiphon 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "psql restore failed (exit $LASTEXITCODE)." }

# Cleanup temp
if ($BackupFile -like '*.zip') { Remove-Item $tmpDir -Recurse -Force }

Write-Host "`nRestore complete." -ForegroundColor Green
Write-Host "Run .\dev-start.ps1 to restart the server." -ForegroundColor DarkGray
