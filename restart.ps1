param(
    [string]$Endpoint = "https://localhost:22025"
)

Write-Host "=== Restarting Antiphon ===" -ForegroundColor Cyan

& "$PSScriptRoot\restart-server.ps1" -Endpoint $Endpoint
& "$PSScriptRoot\restart-client.ps1" -Endpoint $Endpoint

Write-Host "=== Done. Open Aspire dashboard for status. ===" -ForegroundColor Green
