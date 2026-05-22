<#
.SYNOPSIS
    Start the Antiphon dev stack (docker-compose path, no Aspire).
    Use dev-aspire.ps1 for the AppHost / dashboard mode.
.PARAMETER NoClient   Skip the React client tab.
.PARAMETER NoBrowser  Skip auto-opening the browser.
.PARAMETER ServerOnly Start postgres + server only.
#>
param([switch]$NoClient, [switch]$NoBrowser, [switch]$ServerOnly)

$ErrorActionPreference = 'Stop'
$root         = $PSScriptRoot
$composeFile  = "$root\docker-compose.dev.yml"
$settingsFile = "$root\server\appsettings.json"
$logDir       = "$root\server\logs"
$backupDir    = "$root\backups"
$worktreeDir  = 'C:\Antiphon\worktrees'   # outside repo — git worktrees can't live inside the repo

foreach ($d in @($logDir, "$logDir\sessions", $backupDir, $worktreeDir)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

if (-not (Test-Path $settingsFile)) {
    Write-Error "server\appsettings.json not found. Copy appsettings.json.example and configure it."
}
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker Desktop is not running. Start it, wait for the tray icon, then re-run."
}

# ── PostgreSQL ────────────────────────────────────────────────────────────────
Write-Host "`n▶ Starting PostgreSQL..." -ForegroundColor Cyan
docker compose -f $composeFile up -d
if ($LASTEXITCODE -ne 0) { Write-Error "docker compose up failed." }

Write-Host "  Waiting for postgres..." -ForegroundColor DarkGray
$timeout = 90; $elapsed = 0
do {
    Start-Sleep 3; $elapsed += 3
    docker compose -f $composeFile exec -T postgres pg_isready -U antiphon -d antiphon 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "  ...($elapsed s)" -ForegroundColor DarkGray
} while ($elapsed -lt $timeout)
if ($LASTEXITCODE -ne 0) { Write-Warning "Postgres not yet healthy — continuing." }
else { Write-Host "  Postgres ready." -ForegroundColor Green }

# ── Open a new tab in existing Windows Terminal, or fall back to new window ───
function Open-Tab([string]$Title, [string]$Cwd, [string]$Cmd) {
    $full = "Set-Location '$Cwd'; $Cmd"
    $enc  = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($full))
    if (Get-Command wt.exe -ErrorAction SilentlyContinue) {
        # --window 0 = add tab to the currently-focused Windows Terminal window
        Start-Process wt -ArgumentList @('--window', '0', 'new-tab', '--title', $Title,
            '--', 'pwsh', '-NoLogo', '-NoExit', '-EncodedCommand', $enc)
    } else {
        Start-Process pwsh -ArgumentList @('-NoLogo', '-NoExit', '-EncodedCommand', $enc)
    }
}

# ── .NET API server ───────────────────────────────────────────────────────────
Write-Host "▶ Launching .NET server (port 17281)..." -ForegroundColor Cyan
Open-Tab 'Antiphon Server' "$root\server" "dotnet run --urls 'http://localhost:17281'"

# ── React / Vite client ───────────────────────────────────────────────────────
if (-not $NoClient -and -not $ServerOnly) {
    Write-Host "▶ Launching React client (port 17282)..." -ForegroundColor Cyan
    Open-Tab 'Antiphon Client' "$root\client" "npm run dev"
}

Write-Host ""
Write-Host "Antiphon starting up (first run ~15s):" -ForegroundColor Green
Write-Host "  http://localhost:17282  ← app" -ForegroundColor White
Write-Host "  http://localhost:17281  ← API" -ForegroundColor White
Write-Host "  localhost:17280         ← PostgreSQL" -ForegroundColor White
Write-Host ""
Write-Host "  Logs  : $root\server\logs\" -ForegroundColor DarkGray
Write-Host "  Backup: .\dev-backup.ps1" -ForegroundColor DarkGray
Write-Host "  Aspire: .\dev-aspire.ps1  (dashboard + daemon mode)" -ForegroundColor DarkGray

if (-not $NoBrowser -and -not $ServerOnly) {
    Start-Sleep 15
    Start-Process 'http://localhost:17282'
}
