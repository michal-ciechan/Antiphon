param(
    [string]$BackendUrl = "http://localhost:17281",
    [string]$FrontendUrl = "http://localhost:17282",
    [string]$RunnerUrl = "http://localhost:17283",
    [int]$TimeoutSec = 10,
    [switch]$SkipBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

function Invoke-RequiredWebRequest {
    param(
        [string]$Name,
        [string]$Uri,
        [string]$Method = "GET",
        [string]$ExpectedContent = ""
    )

    try {
        $request = @{
            Uri = $Uri
            Method = $Method
            UseBasicParsing = $true
            TimeoutSec = $TimeoutSec
        }

        if ($Method -eq "POST") {
            $request.ContentType = "application/json"
            $request.Body = ""
        }

        $response = Invoke-WebRequest @request
    }
    catch {
        Write-Host "$Name failed: $($_.Exception.Message)" -ForegroundColor Red
        throw
    }

    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
        throw "$Name returned HTTP $($response.StatusCode)."
    }

    if ($ExpectedContent -and -not ($response.Content -like "*$ExpectedContent*")) {
        throw "$Name did not contain expected content '$ExpectedContent'."
    }

    Write-Host "$Name OK: HTTP $($response.StatusCode)" -ForegroundColor Green
}

function Get-PlaywrightScript {
    $debugScript = Join-Path $RepoRoot "tests\Antiphon.E2E\bin\Debug\net9.0\playwright.ps1"
    if (Test-Path $debugScript) {
        return $debugScript
    }

    Write-Host "Building E2E project to make Playwright CLI available..." -ForegroundColor Yellow
    dotnet build (Join-Path $RepoRoot "tests\Antiphon.E2E\Antiphon.E2E.csproj") --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build Antiphon.E2E, so browser smoke verification cannot run."
    }

    if (Test-Path $debugScript) {
        return $debugScript
    }

    throw "Playwright CLI script was not found at $debugScript."
}

Write-Host "=== Verifying Antiphon dev stack ===" -ForegroundColor Cyan

$backendBase = $BackendUrl.TrimEnd("/")
$frontendBase = $FrontendUrl.TrimEnd("/")
$runnerBase = $RunnerUrl.TrimEnd("/")

Invoke-RequiredWebRequest `
    -Name "Session runner health" `
    -Uri "$runnerBase/health" `
    -ExpectedContent "Healthy"

Invoke-RequiredWebRequest `
    -Name "Backend health" `
    -Uri "$backendBase/health" `
    -ExpectedContent "Healthy"

Invoke-RequiredWebRequest `
    -Name "Frontend API proxy" `
    -Uri "$frontendBase/api/projects"

Invoke-RequiredWebRequest `
    -Name "Frontend SignalR negotiate" `
    -Uri "$frontendBase/hubs/antiphon/negotiate?negotiateVersion=1" `
    -Method "POST"

if (-not $SkipBrowser) {
    $playwrightScript = Get-PlaywrightScript
    $screenshotDir = Join-Path $RepoRoot "tests\Antiphon.E2E\TestOutput\Screenshots"
    New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $screenshot = Join-Path $screenshotDir "dev-stack-smoke-$timestamp.png"

    & $playwrightScript screenshot `
        $frontendBase `
        $screenshot `
        --wait-for-selector "text=Workflows" `
        --wait-for-timeout 5000 `
        --viewport-size "1280,720"

    if ($LASTEXITCODE -ne 0) {
        throw "Browser smoke verification failed."
    }

    Write-Host "Browser render OK: $screenshot" -ForegroundColor Green
}

Write-Host "Antiphon dev stack verified." -ForegroundColor Green
