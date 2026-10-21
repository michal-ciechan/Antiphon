# Typechecks the client the release build typechecks, without running Vite.
#
# CARD-1387. Production command, working directory client/:
#   node client/node_modules/typescript/bin/tsc -b --pretty false
# -Project <tsconfig> runs from the repo root:
#   node client/node_modules/typescript/bin/tsc --pretty false --noEmit -p <tsconfig>
#
# Does not take a build slot. test-client.ps1 and a checkpoint command row already
# hold one. A direct run outside the checkpoint tool goes through scripts/build-slot.ps1.
# ASCII-only. Parses under Windows PowerShell 5.1.

param(
    [string]$Project = ''
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $repoRoot 'client'
$logDir = Join-Path $repoRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'client-typecheck.log'
$tsc = Join-Path (Join-Path (Join-Path $clientDir 'node_modules') 'typescript') (Join-Path 'bin' 'tsc')

function Install-ClientDependencies {
    Write-Output "typescript is not installed at $tsc - installing client dependencies first."
    $npmExe = if (Get-Command npm.cmd -ErrorAction SilentlyContinue) { 'npm.cmd' } else { 'npm' }
    $installVerb = if (Test-Path (Join-Path $clientDir 'package-lock.json')) { 'ci' } else { 'install' }
    Push-Location $clientDir
    try {
        & $npmExe $installVerb
        $installCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($installCode -ne 0) {
        Write-Output "npm $installVerb exited with code $installCode."
    }
}

if (-not (Test-Path -LiteralPath $tsc)) {
    Install-ClientDependencies
}

if (-not (Test-Path -LiteralPath $tsc)) {
    Write-Output "typescript is still not installed at $tsc; run npm ci in client/ by hand and look at why it failed."
    Write-Output ""
    Write-Output "CLIENT TYPECHECK EXIT CODE: 1  (FAIL - do not report this run as green)"
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($Project)) {
    & node $tsc --pretty false --noEmit -p $Project 2>&1 | Tee-Object -FilePath $logFile
    $code = $LASTEXITCODE
}
else {
    Push-Location $clientDir
    try {
        & node $tsc -b --pretty false 2>&1 | Tee-Object -FilePath $logFile
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

if ($null -eq $code) { $code = 1 }
Write-Output ""
Write-Output "CLIENT TYPECHECK EXIT CODE: $code  ($(if ($code -eq 0) { 'PASS' } else { 'FAIL - do not report this run as green' }))"
Write-Output "Full output: $logFile"
exit $code
