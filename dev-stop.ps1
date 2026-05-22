<#
.SYNOPSIS
    Stop the Antiphon dev server and client. Postgres stays up by default.
.PARAMETER IncludePostgres
    Also stop the postgres container (docker compose down).
#>
param([switch]$IncludePostgres)

$ErrorActionPreference = 'SilentlyContinue'

function Stop-OnPort([int]$Port, [string]$Label) {
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($conn) {
        $pid_ = $conn.OwningProcess
        Stop-Process -Id $pid_ -Force -ErrorAction SilentlyContinue
        Write-Host "  Stopped $Label (PID $pid_)" -ForegroundColor Green
    } else {
        Write-Host "  $Label not listening on :$Port" -ForegroundColor DarkGray
    }
}

Write-Host "`nStopping Antiphon services..." -ForegroundColor Cyan
Stop-OnPort 17281 '.NET server'
Stop-OnPort 17282 'React client'

if ($IncludePostgres) {
    $root        = $PSScriptRoot
    $composeFile = "$root\docker-compose.dev.yml"
    Write-Host "  Stopping PostgreSQL..." -ForegroundColor Cyan
    docker compose -f $composeFile down 2>&1 | Out-Null
    Write-Host "  PostgreSQL stopped." -ForegroundColor Green
} else {
    Write-Host "  PostgreSQL left running (pass -IncludePostgres to stop it)." -ForegroundColor DarkGray
}

Write-Host ""
