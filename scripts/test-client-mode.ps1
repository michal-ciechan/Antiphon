#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0216 S1 smoke test for scripts/client-mode.ps1 -Status: asserts it always prints one
    of its known verdict shapes and exits 0, whether or not the shim has ever run and whether
    or not anything is listening on the probed port.

    Deliberately never calls -Mode: this test must not be able to flip the real client between
    built and dev while it runs. -Port points at an address nothing listens on, so the "NOT
    reachable" branch is exercised deterministically instead of depending on machine state.
#>
$ErrorActionPreference = 'Continue'

$scriptPath = Join-Path $PSScriptRoot 'client-mode.ps1'

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass([string]$Name) {
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail([string]$Name, [string]$Detail) {
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True([bool]$Cond, [string]$Name, [string]$Detail = '') {
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

# An unreachable port (nothing binds 65500 in this stack) so this test's verdict never depends
# on whether the real client happens to be up right now.
$unreachablePort = 65500

$output = & pwsh -NoLogo -NoProfile -File $scriptPath -Status -Port $unreachablePort 2>&1 | Out-String
$exitCode = $LASTEXITCODE

Assert-True ($exitCode -eq 0) '-Status exits 0 regardless of reachability' "exit code $exitCode"
Assert-True ($output -match 'State file|No state file') '-Status prints a state-file verdict' $output
Assert-True ($output -match 'Live probe') '-Status prints a live-probe verdict' $output
Assert-True ($output -match 'NOT reachable') 'unreachable port reports NOT reachable' $output

$noArgsOutput = & pwsh -NoLogo -NoProfile -File $scriptPath 2>&1 | Out-String
$noArgsExit = $LASTEXITCODE
Assert-True ($noArgsExit -eq 1) 'no arguments exits 1' "exit code $noArgsExit"
Assert-True ($noArgsOutput -match 'Specify -Mode') 'no arguments explains the usage' $noArgsOutput

Write-Host ''
Write-Host ("CARD-0216 client-mode smoke: {0} passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'CLIENT MODE SMOKE TEST EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'CLIENT MODE SMOKE TEST EXIT CODE: 0  (PASS)'
exit 0
