<#
.SYNOPSIS
    Start Antiphon via the Aspire AppHost (dashboard mode).
    - Postgres: Aspire-managed container (persistent named volume)
    - Server + Client: true daemons — survive AppHost exit, log to <repo>/logs/
    - Dashboard: URL parsed from log (Aspire assigns it; opens automatically)
    - OTLP:      http://localhost:17206  (fixed)
    - Control API: http://localhost:17207/control/{name}/start|stop|restart
    - Script exits after dashboard is ready (AppHost continues in background).
.PARAMETER NoBuild    Skip dotnet restore/build before starting.
.PARAMETER NoBrowser  Do not open the dashboard in a browser (used by the logon auto-start).
.PARAMETER AllowWorktree  Admit a linked worktree at its current HEAD when it is not origin/master (warning).
.PARAMETER ExpectedSha    Full commit SHA the source root HEAD must equal; default is the local
                          refs/remotes/origin/master (CARD-0644; main and linked roots alike).
.PARAMETER RestartOwnerPid  Set by restart-apphost.ps1 for the child it launches under its restart lock.
#>
param([switch]$NoBuild, [switch]$NoBrowser, [switch]$AllowWorktree, [string]$ExpectedSha, [int]$RestartOwnerPid)

$ErrorActionPreference = 'Stop'
$root         = $PSScriptRoot
. (Join-Path $root 'scripts\apphost-common.ps1')
$appHostTestSeams = $env:ANTIPHON_APPHOST_TEST_SEAMS
if (-not [string]::IsNullOrWhiteSpace($appHostTestSeams) -and (Test-Path -LiteralPath $appHostTestSeams)) {
    . $appHostTestSeams
    Write-Host 'TEST SEAMS ACTIVE'
}

# CARD-0644: admit the source root by exact commit (restart-apphost.ps1 passes the SHA
# it froze) before any lock, Docker, build or launch activity.
$admission = Get-AppHostSourceAdmission -SourceRoot $root -ExpectedSha $ExpectedSha -AllowWorktree:$AllowWorktree
if (-not $admission.Admitted) {
    $admission.Lines | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    exit 3
}
$admissionColor = if ($admission.Override) { 'Yellow' } else { 'DarkGray' }
$admission.Lines | ForEach-Object { Write-Host $_ -ForegroundColor $admissionColor }

$appHostDir   = "$root\Antiphon.AppHost"
$settingsFile = "$root\server\appsettings.json"

# Shared-stack coordination state (locks, wrapper PID, dashboard URL, wrapper log)
# lives in the MAIN worktree's logs/ whichever checkout launches (CARD-0644 D-7).
$stateLogDir = Join-Path $admission.StateRoot 'logs'
$launchLock  = Join-Path $stateLogDir 'apphost.launch.lock'
$restartLock = Join-Path $stateLogDir 'apphost.restart.lock'

# A restart owns teardown + launch. Only the child it launched (RestartOwnerPid = the
# live restart-lock holder) may launch under it; a direct launch waits for it.
$restartInFlight = Test-AppHostLockActive -Path $restartLock -Label 'restart lock'
if ($restartInFlight) {
    $restartHolder = Get-AppHostLock -Path $restartLock
    $ownedByParent = ($RestartOwnerPid -gt 0) -and ($restartHolder.ProcessId -eq $RestartOwnerPid) -and $restartHolder.HolderAlive
    if (-not $ownedByParent) {
        Write-Host "REFUSED: a restart is in flight - $restartInFlight" -ForegroundColor Yellow
        Write-Host "  restart-apphost.ps1 launches its own dev-aspire.ps1; wait for it, then check http://localhost:17202/health." -ForegroundColor DarkGray
        Write-Host "  Nothing was started. If the holder is genuinely gone, delete $restartLock." -ForegroundColor DarkGray
        exit 3
    }
}

# CARD-0011: tell the watchdog a launch is in flight so a 2-minute fire does not
# call restart-apphost.ps1 over the top of us. Taken atomically (CARD-0644: a second
# direct launch refuses instead of overwriting it) and released in finally even on
# Write-Error ($ErrorActionPreference Stop). A leftover lock stamped before the last
# boot is stale (CARD-0644 R4), so a crash-and-reboot does not block the logon launch.
$launch = New-AppHostLock -Path $launchLock -Label 'launch lock'
if (-not $launch.Acquired) {
    Write-Host "REFUSED: another launch is in flight - $($launch.Reason)" -ForegroundColor Yellow
    Write-Host "  Nothing was started. If the holder is genuinely gone, delete $launchLock." -ForegroundColor DarkGray
    exit 3
}
try {

# CARD-0644 test seam: an inert fixture records the admitted launch here instead of
# touching Docker, npm, dotnet or processes. Only defined when test seams are loaded.
if (Get-Command Invoke-AppHostDevLaunchSeam -ErrorAction SilentlyContinue) {
    Invoke-AppHostDevLaunchSeam -Root $root -LaunchLock $launchLock -Bound $PSBoundParameters
    return
}

if (-not (Test-Path $settingsFile)) {
    Write-Error "server\appsettings.json not found. Copy appsettings.json.example first."
}

docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker Desktop is not running. Start it, wait for the tray icon, then re-run."
}

# ── Pre-flight: test Docker network creation ───────────────────────────────
Write-Host "`n▶ Testing Docker network health..." -ForegroundColor Cyan
$dockerNetworkHealthy = $false
$testNetName = "aspire-preflight-$(Get-Random)"
$netJob = Start-Job { param($n) & docker network create $n 2>&1; $LASTEXITCODE } -ArgumentList $testNetName
$netCompleted = $netJob | Wait-Job -Timeout 8
if ($netCompleted -and $netCompleted.State -eq 'Completed') {
    $dockerNetworkHealthy = $true
    $rmJob = Start-Job { param($n) & docker network rm $n 2>&1 } -ArgumentList $testNetName
    $rmJob | Wait-Job -Timeout 5 | Out-Null; Remove-Job $rmJob -Force
    Remove-Job $netJob -Force
    Write-Host "  Docker networking: OK" -ForegroundColor DarkGray
} else {
    Remove-Job $netJob -Force -ErrorAction SilentlyContinue
    Write-Host "" -ForegroundColor Yellow
    Write-Host "  WARNING: Docker network creation is unresponsive (>8s)." -ForegroundColor Yellow
    Write-Host "  Postgres will NOT start. Restart Docker Desktop, then re-run this script." -ForegroundColor Red
    Write-Host ""
}

# ── Pre-flight: ensure the always-on Postgres container is up ──────────────
# Postgres is an EXTERNAL standalone container (docker-compose.dev.yml, restart:
# unless-stopped) — not Aspire-managed. The AppHost references it via
# AddConnectionString. Bring it up here in case it isn't already (idempotent).
Write-Host "`n▶ Ensuring Postgres container 'antiphon-postgres' is up..." -ForegroundColor Cyan
docker compose -f "$root\docker-compose.dev.yml" up -d 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  WARNING: could not start Postgres via docker compose." -ForegroundColor Yellow
} else {
    Write-Host "  Postgres container ensured" -ForegroundColor DarkGray
}

# Advisory only: AppHost adoption preserves long-lived daemons, so surface a relevant source-build
# difference without disrupting their live pty-host pipes.
& "$root\scripts\check-daemon-build.ps1"

# ── Pre-flight: clean up old Aspire DCP temp state dirs ───────────────────
$aspireDirs = Get-ChildItem 'C:\Users\lndco\AppData\Local\Temp' -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'aspire.*' -and $_.LastWriteTime -lt (Get-Date).AddHours(-2) }
if ($aspireDirs) {
    foreach ($d in $aspireDirs) {
        Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "  Cleaned $($aspireDirs.Count) old DCP temp dir(s)" -ForegroundColor DarkGray
}

foreach ($d in @("$root\logs", $stateLogDir, "$root\server\logs", "$root\server\logs\sessions", "$root\backups")) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
}

$worktreeDir = 'C:\Antiphon\worktrees'
if (-not (Test-Path $worktreeDir)) { New-Item -ItemType Directory -Force $worktreeDir | Out-Null }

if (-not $NoBuild) {
    Write-Host "`n▶ Restoring AppHost dependencies..." -ForegroundColor Cyan
    dotnet restore $appHostDir
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet restore failed." }

    Write-Host "`n▶ Installing client npm packages..." -ForegroundColor Cyan
    Push-Location "$root\client"
    npm install
    if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed." }
    Pop-Location
}

$logFile = Join-Path $stateLogDir 'apphost.log'
$pidFile = Join-Path $stateLogDir 'apphost.pid'

Write-Host "`n▶ Starting Aspire AppHost (background)..." -ForegroundColor Cyan
Write-Host "  OTLP    : http://localhost:17206" -ForegroundColor DarkGray
Write-Host "  Log     : $logFile" -ForegroundColor DarkGray
Write-Host ""

# Truncate old log so we don't match stale lines
'' | Set-Content $logFile

# Launch AppHost detached in a hidden window so this script can exit cleanly
$encodedCmd = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
    "Set-Location '$appHostDir'; dotnet run 2>&1 | Tee-Object -FilePath '$logFile'"
))
$appHostProc = Start-Process pwsh -ArgumentList @(
    '-NonInteractive', '-NoLogo', '-EncodedCommand', $encodedCmd
) -WindowStyle Hidden -PassThru

$appHostProc.Id | Set-Content $pidFile
Write-Host "  AppHost PID : $($appHostProc.Id)" -ForegroundColor DarkGray
Write-Host "  Waiting for dashboard URL in log (up to 90s)..." -ForegroundColor DarkGray
Write-Host ""

# Parse dashboard URL from log — Aspire prints:
#   "Login to the dashboard at http://..." or "Now listening on: http://..."
# The dashboard process writes its URL back into the AppHost log.
$timeout = 90; $elapsed = 0; $dashboardUrl = $null
while ($elapsed -lt $timeout) {
    Start-Sleep 3; $elapsed += 3

    if (Test-Path $logFile) {
        $lines = Get-Content $logFile -ErrorAction SilentlyContinue
        # Look for dashboard login URL line
        $match = $lines | Select-String 'Login to the dashboard at (https?://\S+)' | Select-Object -First 1
        if ($match) {
            $dashboardUrl = $match.Matches[0].Groups[1].Value -replace '/login.*',''
            break
        }
        # Fallback: find the dashboard process and get its HTTP port
        $dash = Get-Process 'Aspire.Dashboard' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($dash) {
            $otlpPort = 17206
            $uiPort = Get-NetTCPConnection -State Listen -OwningProcess $dash.Id -ErrorAction SilentlyContinue |
                Where-Object { $_.LocalPort -ne $otlpPort } |
                Select-Object -ExpandProperty LocalPort -First 1
            if ($uiPort) {
                $dashboardUrl = "http://localhost:$uiPort"
                break
            }
        }
    }
    Write-Host "  ...($elapsed s)" -ForegroundColor DarkGray
}

if ($dashboardUrl) {
    Write-Host ""
    Write-Host "  Dashboard : $dashboardUrl" -ForegroundColor Green
    $dashboardUrl | Set-Content (Join-Path $stateLogDir 'apphost-dashboard-url.txt')
    if (-not $NoBrowser) { Start-Process $dashboardUrl }
} else {
    Write-Host ""
    Write-Host "  Dashboard URL not found after ${timeout}s." -ForegroundColor Yellow
    Write-Host "  Check log: Get-Content '$logFile' -Tail 30" -ForegroundColor DarkGray
}

# ── Post-launch: verify Postgres is up ─────────────────────────────────────
Write-Host "`n▶ Checking Postgres..." -ForegroundColor Cyan
$pgDeadline = (Get-Date).AddSeconds(45)
$pgOK = $false
while ((Get-Date) -lt $pgDeadline) {
    Start-Sleep 5
    $runningPg = docker ps --filter name=antiphon-postgres --format "{{.Status}}" 2>&1 | Where-Object { $_ -like "Up*" }
    if ($runningPg) { $pgOK = $true; Write-Host "  Postgres: Up" -ForegroundColor Green; break }
}
if (-not $pgOK) {
    Write-Host ""
    Write-Host "  WARNING: Postgres ('antiphon-postgres') is not up after 45s." -ForegroundColor Yellow
    $state = docker ps -a --filter name=antiphon-postgres --format "{{.Names}}`t{{.Status}}" 2>&1
    if ($state) { Write-Host "  Container state: $state" -ForegroundColor DarkGray }
    Write-Host "  Fix: docker compose -f docker-compose.dev.yml up -d   (restart Docker Desktop if it hangs)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "AppHost running in background (PID $($appHostProc.Id))." -ForegroundColor Green
Start-Sleep 3
} finally {
    Remove-AppHostLock $launch
}
