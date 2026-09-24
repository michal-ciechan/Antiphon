#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0651 regression: desktop-server retrieval must use server\logs in Git's primary worktree.

.DESCRIPTION
    Exercises the public helper from this linked checkout and inspects only its emitted
    file path. It never reads log contents or any secret path.
#>
$ErrorActionPreference = 'Continue'
$script:passed = 0
$script:failed = 0

function Pass { param([string]$Name) $script:passed++; Write-Host "PASS $Name" }
function Fail { param([string]$Name, [string]$Detail) $script:failed++; Write-Host "FAIL $Name - $Detail" }
function Assert-True { param([bool]$Value, [string]$Name, [string]$Detail = '') if ($Value) { Pass $Name } else { Fail $Name $Detail } }

$logs = Join-Path $PSScriptRoot 'logs.ps1'
$repositoryRoot = @(& git -C $PSScriptRoot rev-parse --show-toplevel 2>&1 | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) | Select-Object -First 1
$worktrees = @(& git -C $repositoryRoot worktree list --porcelain 2>&1)
$mainRecord = @($worktrees | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^worktree\s+(.+)$' }) | Select-Object -First 1
$mainRoot = [System.IO.Path]::GetFullPath(([regex]::Match($mainRecord, '^worktree\s+(.+)$').Groups[1].Value.Trim()))
$expectedDirectory = Join-Path $mainRoot 'server\logs'

$output = @(& pwsh -NoProfile -File $logs -Source desktop-server -Tail 1 2>&1 | ForEach-Object { $_.ToString() })
$header = @($output | Where-Object { $_ -match '^--- .+ ---$' }) | Select-Object -First 1
Assert-True ($LASTEXITCODE -eq 0) 'desktop-server helper exits successfully' ($output -join "`n")
Assert-True ($null -ne $header) 'desktop-server helper emits a file header' ($output -join "`n")
if ($header) {
    $actualPath = $header.Substring(4, $header.Length - 8)
    $actualDirectory = Split-Path -Parent $actualPath
    Assert-True ([string]::Equals($actualDirectory, $expectedDirectory, [System.StringComparison]::OrdinalIgnoreCase)) 'desktop-server source is server\\logs in the primary worktree' ("expected=$expectedDirectory actual=$actualDirectory")
    Assert-True ($actualPath -match 'antiphon-\d{8}\.log$') 'desktop-server source is a dated Serilog file' $actualPath
}

Write-Host "CARD-0651 logs regression: $script:passed passed, $script:failed failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
