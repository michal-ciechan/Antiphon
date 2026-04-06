param(
    [string]$Endpoint = "https://localhost:22025"
)

$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"

Write-Host "Stopping server..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-server stop --endpoint $Endpoint

Write-Host "Waiting for file locks to release..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "Server stopped." -ForegroundColor Green
