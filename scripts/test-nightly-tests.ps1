#requires -Version 5.1
# CARD-0487 E harness: nightly-tests coverage/evidence. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$repo = Split-Path -Parent $here
$policyPath = Join-Path $repo (Join-Path 'tests' 'test-execution-policy.json')

# W2-nightly S15: the other cases of this driver (C487_G026-G058 but G056, G144, G1341, C1698, PrerequisiteGate and the
# C1044 discovery cases) run in-process against Antiphon.Nightly/NightlyTests.cs: NightlyTestsRunTests,
# NightlyDiscoveryParserTests, NightlyPrerequisiteTests and ToolchainFloorWarningTests (tests/Antiphon.Tests). G056 runs
# there too (NightlyTestsRunTests.C487_G056); this copy of its inventory stays until S17 deletes the driver.

# CARD-1626: the census stays an exact inventory. Each row is pinned by id and path, so a
# dropped, added or renamed row fails G056; a deliberate census edit moves this pin with it.
$script:C487ScriptCensusInventory = @(
    'test-apphost-graceful-stop scripts/test-apphost-graceful-stop.ps1',
    'test-apphost-lock-age scripts/test-apphost-lock-age.ps1',
    'test-apphost-main-worktree-guard scripts/test-apphost-main-worktree-guard.ps1',
    'test-apphost-probe-class scripts/test-apphost-probe-class.ps1',
    'test-cleanup-claude-sessions scripts/test-cleanup-claude-sessions.ps1',
    'test-cleanup-codex-test-residue scripts/test-cleanup-codex-test-residue.ps1',
    'test-client scripts/test-client.ps1',
    'test-client-mode scripts/test-client-mode.ps1',
    'test-deploy-am-service scripts/test-deploy-am-service.ps1',
    'test-deploy-nightly-watchdog scripts/test-deploy-nightly-watchdog.ps1',
    'test-duration-tripwire scripts/test-duration-tripwire.ps1',
    'test-hooks scripts/test-hooks.ps1',
    'test-reap-zombie-agents scripts/test-reap-zombie-agents.ps1',
    'test-stage-value-report scripts/test-stage-value-report.ps1',
    'test-qualification-graph scripts/test-qualification-graph.ps1'
)

function Get-C487ScriptCensusDrift {
    param([object[]]$Census, [string[]]$Expected)
    $actual = @($Census | ForEach-Object { '{0} {1}' -f [string]$_.id, [string]$_.path })
    $drift = @()
    foreach ($row in $Expected) {
        $n = @($actual | Where-Object { $_ -ceq $row }).Count
        if ($n -eq 0) { $drift += ('missing ' + $row) } elseif ($n -gt 1) { $drift += ('duplicate ' + $row) }
    }
    foreach ($row in $actual) {
        if (@($Expected | Where-Object { $_ -ceq $row }).Count -eq 0) { $drift += ('extra ' + $row) }
    }
    if ($actual.Count -ne $Expected.Count) { $drift += ('count {0}/{1}' -f $actual.Count, $Expected.Count) }
    return $drift
}

function Test-C487_G056 {
    $pol = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
    $census = @([Antiphon.Nightly.NightlyPolicy]::ScriptCensus($pol))
    $expected = $script:C487ScriptCensusInventory
    $drift = @(Get-C487ScriptCensusDrift -Census $census -Expected $expected)
    # The comparison must see a dropped row and an unlisted added row, or a green proves nothing.
    $dropped = @($census | Select-Object -Skip 1)
    $added = @($census) + @([pscustomobject]@{ id = 'test-unlisted'; path = 'scripts/test-unlisted.ps1'; disposition = 'unattended'; wildcard = $false })
    $seesDrop = @(Get-C487ScriptCensusDrift -Census $dropped -Expected $expected).Count -gt 0
    $seesAdd = @(Get-C487ScriptCensusDrift -Census $added -Expected $expected).Count -gt 0
    Assert-C487 -Cond ($drift.Count -eq 0 -and $census.Count -eq $expected.Count -and $seesDrop -and $seesAdd) `
        -Name ('G056 {0} census rows' -f $expected.Count) `
        -Detail ('count=' + $census.Count + ' drop=' + $seesDrop + ' add=' + $seesAdd + ' ' + ($drift -join '; '))
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    Test-C487_G056
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'tests-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 1 })
