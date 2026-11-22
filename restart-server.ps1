param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners
)

$RepoRoot = $PSScriptRoot
$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"
$ServerProject = "$RepoRoot\server\Antiphon.Server.csproj"
$sourceRevision = ((& git -C $RepoRoot rev-parse HEAD 2>$null) | Out-String).Trim().ToLowerInvariant()
if ($sourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'restart-server: git rev-parse HEAD failed; refusing an unstamped server build.' }

# CARD-1698: warn about a tool below its floor (antiphon-scripts toolchain-floor); never refuse, never change the exit code.
try {
    . (Join-Path $RepoRoot (Join-Path "scripts" (Join-Path "lib" "antiphon-cli.ps1")))
    $floorExit = Invoke-AntiphonCli -Command 'toolchain-floor' -Arguments @('--repo-root', $RepoRoot)
    if ($floorExit -ne 0) { Write-Host ("WARNING: the toolchain floor check failed: antiphon-scripts toolchain-floor exited " + $floorExit) -ForegroundColor Yellow }
} catch {
    Write-Host ("WARNING: the toolchain floor check failed: " + $_.Exception.Message) -ForegroundColor Yellow
}

Write-Host "Stopping server..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-server stop --endpoint $Endpoint
if ($LASTEXITCODE -ne 0) {
    Write-Host "Server stop failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Waiting for file locks to release..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "Checking backend dev port..." -ForegroundColor Yellow
& "$RepoRoot\assert-dev-ports-clear.ps1" -Ports 17281 -StopPortOwners:$StopPortOwners
if ($LASTEXITCODE -ne 0) {
    Write-Host "Backend dev port check failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Building server..." -ForegroundColor Yellow
dotnet build $ServerProject "-p:SourceRevisionId=$sourceRevision"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Starting server..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-server start --endpoint $Endpoint
if ($LASTEXITCODE -ne 0) {
    Write-Host "Server start failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Server restarted." -ForegroundColor Green
