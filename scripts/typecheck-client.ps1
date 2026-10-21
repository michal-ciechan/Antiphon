# Typechecks the client the release build typechecks, without running Vite.
#
# CARD-1387. Production command, working directory client/:
#   node client/node_modules/typescript/bin/tsc -b --pretty false
# client/tsconfig.app.json and tsconfig.node.json set incremental and noEmit,
# with tsBuildInfoFile under client/node_modules/.tmp (CARD-1394).
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

. (Join-Path $PSScriptRoot (Join-Path 'lib' 'client-npm-install.ps1'))

if (-not (Test-Path -LiteralPath $tsc)) {
    Write-Output "typescript is not installed at $tsc - installing client dependencies first."
    $installCode = Install-ClientNpmDependencies -RepoRoot $repoRoot -RequiredPaths @($tsc)
    if ($installCode -ne 0) {
        Write-Output "npm ci exited with code $installCode."
    }
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
    # tsc -b with incremental and noEmit returns 1 for a source diagnostic.
    # Without incremental the same diagnostic returns 2. Keep that historical
    # gate code (CARD-1394). A compiler failure that is not a source diagnostic
    # stays 1.
    if ($code -eq 1 -and (Test-Path -LiteralPath $logFile)) {
        $logged = [System.IO.File]::ReadAllText($logFile)
        if ($logged -match '\(\d+,\d+\): error TS') { $code = 2 }
    }
}

if ($null -eq $code) { $code = 1 }
Write-Output ""
Write-Output "CLIENT TYPECHECK EXIT CODE: $code  ($(if ($code -eq 0) { 'PASS' } else { 'FAIL - do not report this run as green' }))"
Write-Output "Full output: $logFile"
exit $code
