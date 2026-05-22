<#
.SYNOPSIS
    Nuclear reset: drop the database volume, wipe worktrees, start fresh.
    ALL DATA WILL BE LOST. Requires explicit confirmation.
.PARAMETER Yes
    Skip the confirmation prompt (for scripted use).
#>
param([switch]$Yes)

$ErrorActionPreference = 'Stop'
$root        = $PSScriptRoot
$composeFile = "$root\docker-compose.dev.yml"

Write-Host "`nAntiphon FRESH START" -ForegroundColor Red
Write-Host "This will:" -ForegroundColor Yellow
Write-Host "  • Stop the server and client" -ForegroundColor Yellow
Write-Host "  • Destroy the postgres volume (ALL DATABASE DATA LOST)" -ForegroundColor Yellow
Write-Host "  • Delete all worktrees under C:\Antiphon\worktrees\ (outside repo)" -ForegroundColor Yellow
Write-Host "  • Re-start everything from scratch" -ForegroundColor Yellow
Write-Host ""

if (-not $Yes) {
    $confirm = Read-Host "Type 'yes' to continue, anything else to abort"
    if ($confirm -ne 'yes') {
        Write-Host "Aborted." -ForegroundColor DarkGray
        exit 0
    }
}

# 1. Stop server + client
Write-Host "`n▶ Stopping services..." -ForegroundColor Cyan
& "$root\dev-stop.ps1"

# 2. Drop postgres volume
Write-Host "▶ Dropping postgres volume..." -ForegroundColor Cyan
docker compose -f $composeFile down -v
if ($LASTEXITCODE -ne 0) { Write-Warning "docker compose down -v returned $LASTEXITCODE — continuing." }

# 3. Wipe worktrees
$worktrees = 'C:\Antiphon\worktrees'
if (Test-Path $worktrees) {
    Write-Host "▶ Wiping worktrees..." -ForegroundColor Cyan
    Remove-Item $worktrees -Recurse -Force
}

# 4. Start fresh
Write-Host "`n▶ Starting fresh..." -ForegroundColor Green
& "$root\dev-start.ps1"
