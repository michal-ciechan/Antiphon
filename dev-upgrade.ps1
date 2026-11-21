<#
.SYNOPSIS
    Upgrade Antiphon: backup → git pull → dotnet build → npm install → restart.
.PARAMETER NoBrowserOpen
    Pass through to dev-start.ps1.
#>
param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "`nAntiphon upgrade" -ForegroundColor Cyan

# 1. Backup first
Write-Host "`n▶ Backing up database..." -ForegroundColor Cyan
& "$root\dev-backup.ps1"

# 2. Stop server + client (keep postgres)
Write-Host "`n▶ Stopping server and client..." -ForegroundColor Cyan
& "$root\dev-stop.ps1"

# 3. Pull latest code
Write-Host "`n▶ git pull..." -ForegroundColor Cyan
git -C $root pull
if ($LASTEXITCODE -ne 0) { Write-Error "git pull failed — resolve conflicts and retry." }

# 4. Build .NET server
Write-Host "`n▶ Building .NET server..." -ForegroundColor Cyan
$sourceRevision = ((& git -C $root rev-parse HEAD 2>$null) | Out-String).Trim().ToLowerInvariant()
if ($sourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'dev-upgrade: git rev-parse HEAD failed; refusing an unstamped server build.' }
dotnet build "$root\server" "-p:SourceRevisionId=$sourceRevision" --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    dotnet build "$root\server" "-p:SourceRevisionId=$sourceRevision" --configuration Release
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed." }
}

# 5. Install npm deps
Write-Host "`n▶ npm install..." -ForegroundColor Cyan
Push-Location "$root\client"
npm install
Pop-Location
if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed." }

# 6. Restart
Write-Host "`n▶ Starting services..." -ForegroundColor Cyan
$startArgs = @()
if ($NoBrowser) { $startArgs += '-NoBrowser' }
& "$root\dev-start.ps1" @startArgs

Write-Host "`nUpgrade complete." -ForegroundColor Green
