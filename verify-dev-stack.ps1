<#
.SYNOPSIS
    One-shot health check for the Antiphon dev stack. Run this INSTEAD of
    manually probing ports / reading logs every time you want to know if the
    stack is up.

    Defaults to Aspire AppHost mode (dev-aspire.ps1 — ports 1720x).
    Use -SimpleMode for the docker-compose stack (dev-start.ps1 — ports 1728x).

    Reports a full status table and only throws at the end if something core
    is down — so you get the whole picture in one run, not a death on the
    first failure.
.PARAMETER SimpleMode   Check the docker-compose stack (17281/82/83) instead of Aspire.
.PARAMETER SkipBrowser  Skip the Playwright browser smoke test.
.PARAMETER TimeoutSec   Per-request HTTP timeout (default 10).
#>
param(
    [switch]$SimpleMode,
    [switch]$SkipBrowser,
    [int]$TimeoutSec = 10
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

# ── Port / URL scheme ──────────────────────────────────────────────────────
if ($SimpleMode) {
    $BackendUrl  = "http://localhost:17281"
    $FrontendUrl = "http://localhost:17282"
    $RunnerUrl   = "http://localhost:17283"
    $corePorts   = 17280, 17281, 17282
    $dashboardUrl = $null
} else {
    $BackendUrl  = "http://localhost:17202"
    $FrontendUrl = "http://localhost:17203"
    $RunnerUrl   = "http://localhost:17204"
    $corePorts   = 17200, 17202, 17203, 17204, 17205, 17206
    $dashboardUrl = "http://localhost:17205"
}

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result { param($Name, $Ok, $Detail)
    $results.Add([pscustomobject]@{ Check = $Name; Status = ($Ok ? 'OK' : 'FAIL'); Detail = $Detail })
    $color = $Ok ? 'Green' : 'Red'
    Write-Host ("  {0,-28} {1,-5} {2}" -f $Name, ($Ok ? 'OK' : 'FAIL'), $Detail) -ForegroundColor $color
}

Write-Host "`n=== Verifying Antiphon dev stack ($($SimpleMode ? 'simple/compose' : 'Aspire')) ===" -ForegroundColor Cyan

# ── 1. Docker daemon + network health (Aspire only — it needs containers) ──
if (-not $SimpleMode) {
    $j = Start-Job { docker version 2>&1 | Select-String 'Server:' }
    $done = $j | Wait-Job -Timeout 12
    $dockerUp = $done -and (Receive-Job $j)
    Remove-Job $j -Force
    Add-Result "Docker daemon" ([bool]$dockerUp) ($dockerUp ? "reachable" : "unreachable — run docker-desktop skill / restart.cmd")

    if ($dockerUp) {
        # HNS health: docker network create must not hang
        $testNet = "verify-hns-$(Get-Random)"
        $nj = Start-Job { param($n) docker network create $n 2>&1 } -ArgumentList $testNet
        $netDone = $nj | Wait-Job -Timeout 8
        $hnsOk = $netDone -and $netDone.State -eq 'Completed'
        Remove-Job $nj -Force -ErrorAction SilentlyContinue
        if ($hnsOk) { docker network rm $testNet 2>&1 | Out-Null }
        Add-Result "Docker network (HNS)" $hnsOk ($hnsOk ? "create OK" : "HANGS — restart Docker Desktop (Windows HNS broken)")

        # Postgres container up
        $pg = docker ps --filter name=DefaultConnection --format "{{.Status}}" 2>&1 | Where-Object { $_ -like "Up*" }
        Add-Result "Postgres container" ([bool]$pg) ($pg ? "$pg" : "not Up — check 'docker ps -a --filter name=DefaultConnection'")
    }
}

# ── 2. Listening ports ──────────────────────────────────────────────────────
$listening = (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).LocalPort | Sort-Object -Unique
foreach ($p in $corePorts) {
    $open = $listening -contains $p
    Add-Result "Port $p" $open ($open ? "listening" : "closed")
}

# ── 3. HTTP health endpoints ────────────────────────────────────────────────
function Test-Endpoint { param($Name, $Uri, $Method = 'GET', $Expect = '')
    try {
        $req = @{ Uri = $Uri; Method = $Method; UseBasicParsing = $true; TimeoutSec = $TimeoutSec }
        if ($Method -eq 'POST') { $req.ContentType = 'application/json'; $req.Body = '' }
        $r = Invoke-WebRequest @req
        $ok = $r.StatusCode -ge 200 -and $r.StatusCode -lt 300 -and (-not $Expect -or $r.Content -like "*$Expect*")
        Add-Result $Name $ok "HTTP $($r.StatusCode)"
    } catch {
        Add-Result $Name $false $_.Exception.Message
    }
}

Test-Endpoint "Session runner health" "$($RunnerUrl.TrimEnd('/'))/health"  'GET'  'Healthy'
Test-Endpoint "Backend health"        "$($BackendUrl.TrimEnd('/'))/health" 'GET'  'Healthy'
Test-Endpoint "Frontend API proxy"    "$($FrontendUrl.TrimEnd('/'))/api/projects"
Test-Endpoint "Frontend SignalR"      "$($FrontendUrl.TrimEnd('/'))/hubs/antiphon/negotiate?negotiateVersion=1" 'POST'
if ($dashboardUrl) { Test-Endpoint "Aspire dashboard" $dashboardUrl }

# ── 4. Optional browser smoke ───────────────────────────────────────────────
if (-not $SkipBrowser) {
    try {
        $debugScript = Join-Path $RepoRoot "tests\Antiphon.E2E\bin\Debug\net9.0\playwright.ps1"
        if (-not (Test-Path $debugScript)) {
            dotnet build (Join-Path $RepoRoot "tests\Antiphon.E2E\Antiphon.E2E.csproj") --nologo | Out-Null
        }
        if (Test-Path $debugScript) {
            $shotDir = Join-Path $RepoRoot "tests\Antiphon.E2E\TestOutput\Screenshots"
            New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
            $shot = Join-Path $shotDir "dev-stack-smoke.png"
            & $debugScript screenshot $FrontendUrl.TrimEnd('/') $shot --wait-for-selector "text=Workflows" --wait-for-timeout 5000 --viewport-size "1280,720"
            Add-Result "Browser smoke" ($LASTEXITCODE -eq 0) ($LASTEXITCODE -eq 0 ? $shot : "playwright exit $LASTEXITCODE")
        } else {
            Add-Result "Browser smoke" $false "playwright.ps1 not found"
        }
    } catch { Add-Result "Browser smoke" $false $_.Exception.Message }
}

# ── Summary ─────────────────────────────────────────────────────────────────
$failed = $results | Where-Object { $_.Status -eq 'FAIL' }
Write-Host ""
if ($failed) {
    Write-Host "Stack NOT healthy — $($failed.Count) check(s) failed:" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  - $($_.Check): $($_.Detail)" -ForegroundColor Red }
    exit 1
} else {
    Write-Host "Antiphon dev stack verified — all checks passed." -ForegroundColor Green
    exit 0
}
