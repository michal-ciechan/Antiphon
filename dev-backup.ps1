<#
.SYNOPSIS
    Back up the Antiphon postgres database to C:\Antiphon\backups\.
    Keeps the last 14 backups; older ones are pruned automatically.
.PARAMETER BackupDir
    Override the default backup directory.
.PARAMETER Retain
    Number of backups to keep. Default: 14.
.OUTPUTS
    Path to the created backup file.
#>
param(
    [string]$BackupDir = 'C:\Antiphon\backups',
    [int]$Retain = 14
)

$ErrorActionPreference = 'Stop'
$root        = $PSScriptRoot
$composeFile = "$root\docker-compose.dev.yml"

# Ensure backup dir
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null }

# Check postgres is up
$dockerInfo = docker compose -f $composeFile exec -T postgres pg_isready -U antiphon -d antiphon 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Postgres is not running. Start it with: docker compose -f docker-compose.dev.yml up -d"
}

# Dump
$timestamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$dumpFile   = Join-Path $BackupDir "antiphon-$timestamp.sql"
$zipFile    = "$dumpFile.zip"

Write-Host "Backing up to $dumpFile ..." -ForegroundColor Cyan
docker compose -f $composeFile exec -T postgres `
    pg_dump -U antiphon -d antiphon --no-owner --no-acl > $dumpFile
if ($LASTEXITCODE -ne 0) {
    Remove-Item $dumpFile -ErrorAction SilentlyContinue
    Write-Error "pg_dump failed (exit $LASTEXITCODE)."
}

# Compress
Compress-Archive -Path $dumpFile -DestinationPath $zipFile -Force
Remove-Item $dumpFile

$size = [math]::Round((Get-Item $zipFile).Length / 1KB, 1)
Write-Host "  Created: $zipFile  ($size KB)" -ForegroundColor Green

# Prune old backups
$all = Get-ChildItem $BackupDir -Filter '*.sql.zip' | Sort-Object LastWriteTime -Descending
if ($all.Count -gt $Retain) {
    $toRemove = $all | Select-Object -Skip $Retain
    $toRemove | Remove-Item -Force
    Write-Host "  Pruned $($toRemove.Count) old backup(s), kept $Retain." -ForegroundColor DarkGray
}

Write-Host ""
return $zipFile
