param(
    [switch]$StopPortOwners,
    [string]$Url = "http://localhost:17283"
)

$RepoRoot = $PSScriptRoot
$RunnerProject = "$RepoRoot\src\Antiphon.SessionRunner\Antiphon.SessionRunner.csproj"
$RunnerDll = "$RepoRoot\src\Antiphon.SessionRunner\bin\Debug\net9.0\Antiphon.SessionRunner.dll"
$LogDir = Join-Path $env:TEMP "antiphon-session-runner"
$StdOutLog = Join-Path $LogDir "session-runner.out.log"
$StdErrLog = Join-Path $LogDir "session-runner.err.log"
$PidFile = Join-Path $LogDir "session-runner.pid"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

Write-Host "Stopping standalone session runner..." -ForegroundColor Yellow
if (Test-Path $PidFile) {
    $existingPidText = Get-Content $PidFile -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existingPidText -match '^\d+$') {
        Stop-Process -Id ([int]$existingPidText) -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Waiting for runner port to release..." -ForegroundColor Gray
Start-Sleep -Seconds 2

Write-Host "Checking session runner dev port..." -ForegroundColor Yellow
& "$RepoRoot\assert-dev-ports-clear.ps1" -Ports 17283 -StopPortOwners:$StopPortOwners
if ($LASTEXITCODE -ne 0) {
    Write-Host "Session runner dev port check failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Building session runner..." -ForegroundColor Yellow
dotnet build $RunnerProject
if ($LASTEXITCODE -ne 0) {
    Write-Host "Session runner build failed! Aborting." -ForegroundColor Red
    exit 1
}

Remove-Item $StdOutLog, $StdErrLog -Force -ErrorAction SilentlyContinue

Write-Host "Starting standalone session runner at $Url..." -ForegroundColor Yellow
$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @($RunnerDll, "--urls", $Url) `
    -WorkingDirectory $RepoRoot `
    -RedirectStandardOutput $StdOutLog `
    -RedirectStandardError $StdErrLog `
    -WindowStyle Hidden `
    -PassThru

$process.Id | Set-Content -Path $PidFile

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        Write-Host "Session runner exited with code $($process.ExitCode)." -ForegroundColor Red
        if (Test-Path $StdOutLog) { Get-Content $StdOutLog -Tail 40 }
        if (Test-Path $StdErrLog) { Get-Content $StdErrLog -Tail 40 }
        exit 1
    }

    try {
        $response = Invoke-WebRequest -Uri "$Url/health" -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
            Write-Host "Session runner restarted. Logs: $StdOutLog" -ForegroundColor Green
            exit 0
        }
    }
    catch {
        Start-Sleep -Seconds 1
    }
}

Write-Host "Timed out waiting for session runner health at $Url." -ForegroundColor Red
exit 1
