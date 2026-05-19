param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners
)

$RepoRoot = $PSScriptRoot
$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"
$ServerProject = "$RepoRoot\server\Antiphon.Server.csproj"

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
dotnet build $ServerProject
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
