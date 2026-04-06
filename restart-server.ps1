param(
    [string]$Endpoint = "https://localhost:22025"
)

$RepoRoot = $PSScriptRoot
$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"
$ServerProject = "$RepoRoot\server\Antiphon.Server.csproj"

Write-Host "Stopping server..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-server stop --endpoint $Endpoint

Write-Host "Waiting for file locks to release..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "Building server..." -ForegroundColor Yellow
dotnet build $ServerProject
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Starting server..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-server start --endpoint $Endpoint

Write-Host "Server restarted." -ForegroundColor Green
