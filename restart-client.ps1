param(
    [string]$Endpoint = "https://localhost:22025"
)

$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"

Write-Host "Restarting client..." -ForegroundColor Yellow
dotnet run --project $AspireCommand -- antiphon-client restart --endpoint $Endpoint

Write-Host "Client restarted." -ForegroundColor Green
