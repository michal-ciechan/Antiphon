param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners
)

$RepoRoot = $PSScriptRoot
$AspireCommand = "D:\src\Mikeys.Tools\Mikey.AspireCommand"

Write-Host "Restarting client..." -ForegroundColor Yellow

dotnet run --project $AspireCommand -- antiphon-client stop --endpoint $Endpoint
if ($LASTEXITCODE -ne 0) {
    Write-Host "Client stop failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Waiting for client port to release..." -ForegroundColor Gray
Start-Sleep -Seconds 2

Write-Host "Checking frontend dev port..." -ForegroundColor Yellow
& "$RepoRoot\assert-dev-ports-clear.ps1" -Ports 17282 -StopPortOwners:$StopPortOwners
if ($LASTEXITCODE -ne 0) {
    Write-Host "Frontend dev port check failed! Aborting." -ForegroundColor Red
    exit 1
}

dotnet run --project $AspireCommand -- antiphon-client start --endpoint $Endpoint
if ($LASTEXITCODE -ne 0) {
    Write-Host "Client start failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "Client restarted." -ForegroundColor Green
