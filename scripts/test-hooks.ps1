#requires -Version 5.1
<#
.SYNOPSIS
    Runs the CARD-0247 Node hook-classifier tests (scripts/hooks/__tests__).

    Sibling of test-client.ps1: streams output, tees to logs/hooks-tests.log, and
    prints an unmissable HOOKS TESTS EXIT CODE line so a piped tail cannot hide
    a failure (CARD-0069).

    Usage (from repo root or anywhere):
      pwsh -File scripts/test-hooks.ps1

    Runs every file under scripts/hooks/__tests__/*.test.mjs. The live claude -p
    probe (live-s2-probe.mjs) is not a .test.mjs and is not part of this suite.
#>
$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$hooksDir = Join-Path $PSScriptRoot 'hooks'
$logDir = Join-Path $repoRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logFile = Join-Path $logDir 'hooks-tests.log'

Push-Location $hooksDir
try {
    node --test --test-reporter=spec ./__tests__/orchestrator-investigation.test.mjs ./__tests__/orchestrator-investigation-hook.test.mjs 2>&1 | Tee-Object -FilePath $logFile
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

Write-Output ""
Write-Output "HOOKS TESTS EXIT CODE: $code  ($(if ($code -eq 0) { 'PASS' } else { 'FAIL - do not report this run as green' }))"
Write-Output "Full output: $logFile"
exit $code
