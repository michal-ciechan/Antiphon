# Lints the client the release gate lints, without running Vitest.
#
# CARD-1393. Production command, working directory client/:
#   node client/node_modules/eslint/bin/eslint.js . --max-warnings 0 --cache --cache-strategy content --cache-location node_modules/.tmp/eslintcache
# The cache file stays under client/node_modules/.tmp, which is gitignored (CARD-1398).
# --cache-strategy content keys the cache on file bytes. The default metadata
# strategy (size + mtime) treats a same-size edit with a restored mtime as a hit.
# -Path <file> runs from that file's directory:
#   node client/node_modules/eslint/bin/eslint.js <file> --max-warnings 0
#
# Does not take a build slot. test-client.ps1 and a checkpoint command row already
# hold one. A direct run outside the checkpoint tool goes through scripts/build-slot.ps1.
# ASCII-only. Parses under Windows PowerShell 5.1.

param(
    [string]$Path = ''
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $repoRoot 'client'
$logDir = Join-Path $repoRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'client-lint.log'
$eslint = Join-Path (Join-Path (Join-Path $clientDir 'node_modules') 'eslint') (Join-Path 'bin' 'eslint.js')

. (Join-Path $PSScriptRoot (Join-Path 'lib' 'client-npm-install.ps1'))

if (-not (Test-Path -LiteralPath $eslint)) {
    Write-Output "eslint is not installed at $eslint - installing client dependencies first."
    $installCode = Install-ClientNpmDependencies -RepoRoot $repoRoot -RequiredPaths @($eslint)
    if ($installCode -ne 0) {
        Write-Output "npm ci exited with code $installCode."
    }
}

if (-not (Test-Path -LiteralPath $eslint)) {
    Write-Output "eslint is still not installed at $eslint; run npm ci in client/ by hand and look at why it failed."
    Write-Output ""
    Write-Output "CLIENT LINT EXIT CODE: 1  (FAIL - do not report this run as green)"
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($Path)) {
    $target = $Path
    if (-not [System.IO.Path]::IsPathRooted($target)) {
        $target = Join-Path $repoRoot $target
    }
    $target = [System.IO.Path]::GetFullPath($target)
    if (-not (Test-Path -LiteralPath $target)) {
        Write-Output "lint target is missing at $target"
        Write-Output ""
        Write-Output "CLIENT LINT EXIT CODE: 1  (FAIL - do not report this run as green)"
        Write-Output "Full output: $logFile"
        exit 1
    }
    $leaf = Split-Path -Leaf $target
    Push-Location (Split-Path -Parent $target)
    try {
        & node $eslint $leaf --max-warnings 0 2>&1 | Tee-Object -FilePath $logFile
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
else {
    $eslintCache = Join-Path (Join-Path (Join-Path $clientDir 'node_modules') '.tmp') 'eslintcache'
    $eslintCacheDir = Split-Path -Parent $eslintCache
    if (-not (Test-Path -LiteralPath $eslintCacheDir)) {
        New-Item -ItemType Directory -Path $eslintCacheDir | Out-Null
    }
    Push-Location $clientDir
    try {
        & node $eslint . --max-warnings 0 --cache --cache-strategy content --cache-location $eslintCache 2>&1 | Tee-Object -FilePath $logFile
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

if ($null -eq $code) { $code = 1 }
Write-Output ""
Write-Output "CLIENT LINT EXIT CODE: $code  ($(if ($code -eq 0) { 'PASS' } else { 'FAIL - do not report this run as green' }))"
Write-Output "Full output: $logFile"
exit $code
