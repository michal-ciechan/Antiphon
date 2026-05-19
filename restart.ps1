param(
    [string]$Endpoint = "https://localhost:22025",
    [switch]$StopPortOwners,
    [switch]$RestartRunner,
    [switch]$SkipVerify
)

Write-Host "=== Restarting Antiphon ===" -ForegroundColor Cyan

if ($RestartRunner) {
    & "$PSScriptRoot\restart-session-runner.ps1" -StopPortOwners:$StopPortOwners
    if ($LASTEXITCODE -ne 0) {
        exit 1
    }
}
else {
    Write-Host "Leaving session runner running. Use -RestartRunner to restart it too." -ForegroundColor Gray
}

& "$PSScriptRoot\restart-server.ps1" -Endpoint $Endpoint -StopPortOwners:$StopPortOwners
if ($LASTEXITCODE -ne 0) {
    exit 1
}

& "$PSScriptRoot\restart-client.ps1" -Endpoint $Endpoint -StopPortOwners:$StopPortOwners
if ($LASTEXITCODE -ne 0) {
    exit 1
}

if (-not $SkipVerify) {
    & "$PSScriptRoot\verify-dev-stack.ps1"
    if ($LASTEXITCODE -ne 0) {
        exit 1
    }
}

Write-Host "=== Done. Open http://localhost:17282. ===" -ForegroundColor Green
