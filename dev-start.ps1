<#
.SYNOPSIS
    Start the Antiphon dev stack: PostgreSQL (Docker), .NET API server, React/Vite client.
.PARAMETER NoClient
    Skip the React client window.
.PARAMETER NoBrowser
    Skip auto-opening the browser.
.PARAMETER ServerOnly
    Start postgres + server only (implies NoClient).
#>
param(
    [switch]$NoClient,
    [switch]$NoBrowser,
    [switch]$ServerOnly
)

$ErrorActionPreference = 'Stop'
$root          = $PSScriptRoot
$composeFile   = "$root\docker-compose.dev.yml"
$settingsFile  = "$root\server\appsettings.json"

# Persistent storage directories — created if absent
$dirs = @(
    'C:\Antiphon\backups',
    'C:\Antiphon\worktrees',
    'C:\MavLog\Antiphon',
    'C:\MavLog\Antiphon\sessions'
)
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

# Prerequisites
if (-not (Test-Path $settingsFile)) {
    Write-Error "server\appsettings.json not found.`nCopy appsettings.json.example and fill in your API keys."
}

$dockerInfo = docker info 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker Desktop is not running.`nStart Docker Desktop, wait for the tray icon to stabilise, then re-run."
}

# ── PostgreSQL ───────────────────────────────────────────────────────────────
Write-Host "`n▶ Starting PostgreSQL..." -ForegroundColor Cyan
docker compose -f $composeFile up -d
if ($LASTEXITCODE -ne 0) { Write-Error "docker compose up failed." }

Write-Host "  Waiting for postgres to be healthy..." -ForegroundColor DarkGray
$timeout = 90; $elapsed = 0
do {
    Start-Sleep 3; $elapsed += 3
    docker compose -f $composeFile exec -T postgres `
        pg_isready -U antiphon -d antiphon 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "  ...($elapsed s)" -ForegroundColor DarkGray
} while ($elapsed -lt $timeout)

if ($LASTEXITCODE -ne 0) {
    Write-Warning "Postgres didn't confirm ready within ${timeout}s — continuing; server will retry."
} else {
    Write-Host "  Postgres ready." -ForegroundColor Green
}

# Helper — launch a command in a new PowerShell window
function Open-Window([string]$Cwd, [string]$Cmd) {
    $full = "Set-Location '$Cwd'; $Cmd"
    $enc  = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($full))
    Start-Process pwsh -ArgumentList @('-NoLogo', '-NoExit', '-EncodedCommand', $enc)
}

# ── .NET API server ──────────────────────────────────────────────────────────
Write-Host "▶ Launching .NET server (port 17281)..." -ForegroundColor Cyan
Open-Window "$root\server" "dotnet run --urls 'http://localhost:17281'"

# ── React / Vite client ──────────────────────────────────────────────────────
if (-not $NoClient -and -not $ServerOnly) {
    Write-Host "▶ Launching React client (port 17282)..." -ForegroundColor Cyan
    Open-Window "$root\client" "npm run dev"
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Antiphon is starting up (may take ~15s on first run):" -ForegroundColor Green
Write-Host "  http://localhost:17282  ← app" -ForegroundColor White
Write-Host "  http://localhost:17281  ← API" -ForegroundColor White
Write-Host "  localhost:17280         ← PostgreSQL" -ForegroundColor White
Write-Host ""
Write-Host "  Logs  : C:\MavLog\Antiphon\" -ForegroundColor DarkGray
Write-Host "  Data  : Docker volume 'pgdata' (docker volume inspect antiphon_pgdata)" -ForegroundColor DarkGray
Write-Host "  Backup: .\dev-backup.ps1" -ForegroundColor DarkGray

if (-not $NoBrowser -and -not $ServerOnly) {
    Write-Host "`n  Opening browser in 15s..." -ForegroundColor DarkGray
    Start-Sleep 15
    Start-Process 'http://localhost:17282'
}
