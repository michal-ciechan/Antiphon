# Runs the client vitest suite and reports the REAL exit code, visibly and unlosably.
#
# Why this exists (CARD-0069): a merge run reported "Exit code 0" over SIX failing client tests.
# Nothing in vitest or npm converts failures to success - measured 2026-08-19, a failing test
# yields exit 1 from `npx vitest run` and `npm test` in both PowerShell and Bash. The mechanism
# was the caller's own output-capping pipeline: in Bash, `npm test 2>&1 | tail -50` reports the
# exit code of TAIL (always 0), not the test run. PowerShell's `| Select-Object -Last N` does
# not have this problem ($LASTEXITCODE survives), but the Bash habit is common enough that the
# suite needs a runner whose verdict survives any pipe.
#
# So: this script streams vitest's output through, also tees it to logs/client-tests.log, and
# ends with an unambiguous "CLIENT TESTS EXIT CODE: n" line - the verdict is IN the text, so
# even a tail-capped capture shows it. It exits with vitest's real code.
#
# Usage (from repo root or anywhere):
#   pwsh -File scripts/test-client.ps1                 # full suite
#   pwsh -File scripts/test-client.ps1 BoardPage       # vitest filter args pass through
#
# ASCII-only on purpose - must parse under Windows PowerShell 5.1 too.

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $repoRoot 'client'
$logDir = Join-Path $repoRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'client-tests.log'

Push-Location $clientDir
try {
    npx vitest run @args 2>&1 | Tee-Object -FilePath $logFile
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

Write-Output ""
Write-Output "CLIENT TESTS EXIT CODE: $code  ($(if ($code -eq 0) { 'PASS' } else { 'FAIL - do not report this run as green' }))"
Write-Output "Full output: $logFile"
exit $code
