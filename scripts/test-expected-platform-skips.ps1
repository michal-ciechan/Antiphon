#requires -Version 5.1
# Expected platform-skip roster for the nightly coverage verdict. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'c487-harness.ps1')
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$repo = Split-Path -Parent $here

function New-EpsRow {
    param([string]$Identity, [string]$Outcome, [string]$DebugTrace)
    return [pscustomobject]@{ Outcome = $Outcome; Identity = $Identity; DebugTrace = $DebugTrace }
}

function Invoke-EpsVerdict {
    param(
        $Rows,
        [int]$ProcessExit = 0,
        $Roster = $null,
        [string]$Platform = 'windows',
        [string[]]$Known = $null,
        [switch]$OmitRoster
    )
    $bound = @{
        TerminalRows = @($Rows)
        ProcessExit = $ProcessExit
        Sha = 'a'
        ExpectedSha = 'a'
        GitRef = 'r'
        ExpectedRef = 'r'
        PolicyHash = 'p'
        ExpectedPolicyHash = 'p'
        Platform = $Platform
    }
    if (-not $OmitRoster) { $bound.ExpectedSkipRoster = $Roster }
    if ($null -ne $Known) { $bound.KnownTestIdentities = $Known }
    return (ConvertTo-NightlyCoverageVerdict @bound)
}

function Test-EpsVerdict {
    $s7 = 'S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.'
    $cleanup = 'Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation'
    $sync = 'Antiphon.Tests.Application.C0824RefOnlySyncTests.Reported_tip_and_mirror_publication_keep_existing_contract'
    $missing = 'Antiphon.Tests.Missing.NoSuchTests.MissingMethod'
    $roster = @(
        [pscustomobject]@{ platform = 'windows'; test = $cleanup; reasonPrefix = $s7 }
        [pscustomobject]@{ platform = 'windows'; test = $sync; reasonPrefix = $s7 }
        [pscustomobject]@{ platform = 'windows'; test = $missing; reasonPrefix = 'gone' }
    )
    $trace = 'Skipped: ' + $s7
    $skip = New-EpsRow -Identity $cleanup -Outcome 'NotExecuted' -DebugTrace $trace
    $accepted = Invoke-EpsVerdict -Rows @($skip) -Roster $roster
    $acceptedReasons = @($accepted.reasons) -join ','
    Assert-C487 -Cond ([bool]$accepted.coverageComplete -and [bool]$accepted.testsPassed -and $acceptedReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS expected-skip-accepted' -Detail $acceptedReasons

    $longer = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $cleanup -Outcome 'NotExecuted' -DebugTrace ($trace + ' extra detail')) -Roster $roster
    $longerReasons = @($longer.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$longer.coverageComplete) -and $longerReasons -match 'unexecuted-outcome') `
        -Name 'EPS prefix-extension-rejected' -Detail $longerReasons

    $tight = 'Skipped:' + $s7
    $noSpace = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $cleanup -Outcome 'NotExecuted' -DebugTrace $tight) -Roster $roster
    $noSpaceReasons = @($noSpace.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$noSpace.coverageComplete) -and $noSpaceReasons -match 'unexecuted-outcome') `
        -Name 'EPS skipped-without-space-rejected' -Detail $noSpaceReasons

    $loaded = Invoke-EpsVerdict -Rows @($skip) -OmitRoster
    $loadedReasons = @($loaded.reasons) -join ','
    Assert-C487 -Cond ([bool]$loaded.coverageComplete -and [bool]$loaded.testsPassed -and $loadedReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS autoload-roster-accepted' -Detail $loadedReasons

    $unexpected = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity 'Antiphon.Tests.Scripts.SampleTests.NotOnTheRoster' -Outcome 'NotExecuted' -DebugTrace $trace) -Roster $roster
    $unexpectedReasons = @($unexpected.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$unexpected.coverageComplete) -and $unexpectedReasons -match 'unexecuted-outcome:Antiphon\.Tests\.Scripts\.SampleTests\.NotOnTheRoster') `
        -Name 'EPS unexpected-skip-rejected' -Detail $unexpectedReasons

    $mismatch = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $cleanup -Outcome 'NotExecuted' -DebugTrace 'Skipped: a different reason') -ProcessExit 8 -Roster $roster
    $mismatchReasons = @($mismatch.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$mismatch.coverageComplete) -and (-not [bool]$mismatch.testsPassed) -and $mismatchReasons -match 'unexecuted-outcome' -and $mismatchReasons -match 'process-exit-8' -and -not [bool]$mismatch.processExitTolerated) `
        -Name 'EPS reason-mismatch-rejected' -Detail $mismatchReasons

    $onlySkip = Invoke-EpsVerdict -Rows @($skip) -ProcessExit 8 -Roster $roster
    $onlyReasons = @($onlySkip.reasons) -join ','
    Assert-C487 -Cond ([bool]$onlySkip.coverageComplete -and [bool]$onlySkip.testsPassed -and [bool]$onlySkip.processExitTolerated -and $onlyReasons -notmatch 'process-exit-8' -and $onlyReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS all-skip-exit-8-tolerated' -Detail $onlyReasons

    $mixed = Invoke-EpsVerdict -Rows @(
        $skip,
        (New-EpsRow -Identity 'Antiphon.Tests.Scripts.SampleTests.Ran' -Outcome 'Passed' -DebugTrace '')
    ) -ProcessExit 8 -Roster $roster
    $mixedReasons = @($mixed.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$mixed.testsPassed) -and $mixedReasons -match 'process-exit-8' -and -not [bool]$mixed.processExitTolerated) `
        -Name 'EPS mixed-exit-8-rejected' -Detail $mixedReasons

    $exitOne = Invoke-EpsVerdict -Rows @($skip) -ProcessExit 1 -Roster $roster
    $exitOneReasons = @($exitOne.reasons) -join ','
    Assert-C487 -Cond ((-not [bool]$exitOne.testsPassed) -and $exitOneReasons -match 'process-exit-1' -and -not [bool]$exitOne.processExitTolerated) `
        -Name 'EPS other-nonzero-still-fails' -Detail $exitOneReasons

    $ran = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $cleanup -Outcome 'Passed' -DebugTrace '') -Roster $roster
    $ranReasons = @($ran.reasons) -join ','
    Assert-C487 -Cond ([bool]$ran.coverageComplete -and [bool]$ran.testsPassed -and $ranReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS ran-roster-entry-stays-complete' -Detail $ranReasons

    $group = Invoke-EpsVerdict -Rows @(
        (New-EpsRow -Identity 'Antiphon.Tests.Scripts.SampleTests.Ran' -Outcome 'Passed' -DebugTrace ''),
        $skip
    ) -Roster $roster
    $groupReasons = @($group.reasons) -join ','
    Assert-C487 -Cond ([bool]$group.coverageComplete -and [bool]$group.testsPassed -and $groupReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS passed-with-expected-skip-complete' -Detail $groupReasons

    $stale = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $cleanup -Outcome 'Passed' -DebugTrace '') -Roster $roster -Known @($cleanup, $sync)
    $staleText = @($stale.warnings) -join ','
    $staleReasons = @($stale.reasons) -join ','
    Assert-C487 -Cond ([bool]$stale.coverageComplete -and [bool]$stale.testsPassed -and $staleText -ceq ('stale-roster:' + $missing) -and $staleReasons -notmatch 'stale-roster' -and $staleReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS stale-roster-warns' -Detail ($staleText + ' | ' + $staleReasons)

    $trx = Join-Path $ResultsDirectory 'expected-skip.trx'
    $xml = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>
    <UnitTest id="t1" name="Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation">
      <TestMethod className="Antiphon.Tests.Application.C0824RefOnlyCleanupTests" name="Remote_cleanup_still_uses_its_binding_and_generation" />
    </UnitTest>
  </TestDefinitions>
  <Results>
    <UnitTestResult testId="t1" testName="Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation" outcome="NotExecuted">
      <Output><DebugTrace>Skipped: S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.</DebugTrace></Output>
    </UnitTestResult>
  </Results>
</TestRun>
'@
    [System.IO.File]::WriteAllText($trx, $xml)
    $parsed = @(Read-NightlyTrxIdentities -TrxPath $trx)
    $parsedTrace = ''
    if ($parsed.Count -eq 1 -and $parsed[0].PSObject.Properties.Name -contains 'DebugTrace') { $parsedTrace = [string]$parsed[0].DebugTrace }
    $fromTrx = $null
    $fromReasons = ''
    if ($parsed.Count -eq 1) {
        $fromTrx = Invoke-EpsVerdict -Rows @($parsed[0]) -ProcessExit 8 -OmitRoster
        $fromReasons = @($fromTrx.reasons) -join ','
    }
    $trxOk = ($parsed.Count -eq 1 -and $parsed[0].Outcome -ceq 'NotExecuted' -and $parsed[0].Identity -ceq $cleanup -and $parsedTrace -ceq $trace -and $null -ne $fromTrx -and [bool]$fromTrx.coverageComplete -and [bool]$fromTrx.testsPassed -and [bool]$fromTrx.processExitTolerated)
    Assert-C487 -Cond $trxOk -Name 'EPS trx-debug-trace' -Detail ($parsedTrace + ' | ' + $fromReasons)

    $committed = @(Get-NightlyExpectedSkipRoster)
    $windows = @($committed | Where-Object { [string]$_.platform -ceq 'windows' })
    $unique = @($windows | ForEach-Object { [string]$_.test } | Select-Object -Unique)
    $gaps = @(Get-NightlyExpectedSkipSourceGaps -RepoRoot $repo -Platform 'windows' -Entries $committed)
    Assert-C487 -Cond ($windows.Count -eq 25 -and $unique.Count -eq 25 -and $gaps.Count -eq 0) `
        -Name 'EPS census-matches-source' -Detail ('windows=' + $windows.Count + ' unique=' + $unique.Count + ' gaps=' + ($gaps -join ','))

    $planted = @($committed) + @([pscustomobject]@{ platform = 'windows'; test = $missing; reasonPrefix = 'gone' })
    $plantedGaps = @(Get-NightlyExpectedSkipSourceGaps -RepoRoot $repo -Platform 'windows' -Entries $planted)
    Assert-C487 -Cond ($plantedGaps.Count -eq 1 -and [string]$plantedGaps[0] -ceq $missing) `
        -Name 'EPS census-reports-missing' -Detail ($plantedGaps -join ',')

    $reasonCmd = Get-Command -Name 'Get-NightlyExpectedSkipReasonGaps' -CommandType Function -ErrorAction SilentlyContinue
    $reasonGaps = @()
    if ($null -ne $reasonCmd) {
        $reasonGaps = @(Get-NightlyExpectedSkipReasonGaps -RepoRoot $repo -Platform 'windows' -Entries $committed)
    }
    Assert-C487 -Cond ($null -ne $reasonCmd -and $reasonGaps.Count -eq 0) `
        -Name 'EPS reason-text-matches-source' -Detail ($(if ($null -eq $reasonCmd) { 'missing-command' } else { $reasonGaps -join ',' }))

    $wrongId = 'Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed'
    $wrongReason = @()
    foreach ($entry in @($committed)) {
        if ([string]$entry.test -ceq $wrongId) {
            $wrongReason += [pscustomobject]@{ platform = 'windows'; test = $wrongId; reasonPrefix = 'not-the-method-skip-text' }
        } else { $wrongReason += $entry }
    }
    $wrongGaps = @()
    if ($null -ne $reasonCmd) {
        $wrongGaps = @(Get-NightlyExpectedSkipReasonGaps -RepoRoot $repo -Platform 'windows' -Entries $wrongReason)
    }
    Assert-C487 -Cond ($null -ne $reasonCmd -and @($wrongGaps) -contains $wrongId) `
        -Name 'EPS reason-text-reports-mismatch' -Detail ($(if ($null -eq $reasonCmd) { 'missing-command' } else { $wrongGaps -join ',' }))

    $driftCmd = Get-Command -Name 'Get-NightlyExpectedSkipRosterDrift' -CommandType Function -ErrorAction SilentlyContinue
    $drift = $null
    if ($null -ne $driftCmd) { $drift = Get-NightlyExpectedSkipRosterDrift -RepoRoot $repo -Entries $committed }
    $driftDetail = 'missing-command'
    $driftStable = $false
    if ($null -ne $drift) {
        $driftDetail = ('overlap=' + @($drift.Overlap).Count + ' mismatches=' + (@($drift.Mismatches) -join ','))
        $driftStable = (@($drift.Overlap).Count -eq 10 -and @($drift.Mismatches).Count -eq 0)
    }
    Assert-C487 -Cond $driftStable -Name 'EPS roster-drift-stable' -Detail $driftDetail

    $drifted = @()
    foreach ($entry in @($committed)) {
        if ([string]$entry.test -ceq $wrongId) {
            $drifted += [pscustomobject]@{ platform = [string]$entry.platform; test = [string]$entry.test; reasonPrefix = 'drifted-reason'; reasonClass = 'platform' }
        } else { $drifted += $entry }
    }
    $driftBad = $null
    if ($null -ne $driftCmd) { $driftBad = Get-NightlyExpectedSkipRosterDrift -RepoRoot $repo -Entries $drifted }
    $driftBadDetail = 'missing-command'
    $driftCaught = $false
    if ($null -ne $driftBad) {
        $driftBadDetail = (@($driftBad.Mismatches) -join ',')
        $driftCaught = (@($driftBad.Mismatches) -contains $wrongId)
    }
    Assert-C487 -Cond $driftCaught -Name 'EPS roster-drift-reports-mismatch' -Detail $driftBadDetail

    $c680 = 'Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs'
    $liveRoster = @([pscustomobject]@{ platform = 'windows'; test = $c680; reasonPrefix = '20 missing-context'; reasonClass = 'live-context' })
    $liveTrace = 'Skipped: 20 missing-context'
    $liveLinux = Invoke-EpsVerdict -Rows @(New-EpsRow -Identity $c680 -Outcome 'NotExecuted' -DebugTrace $liveTrace) -Roster $liveRoster -Platform 'linux'
    $liveReasons = @($liveLinux.reasons) -join ','
    Assert-C487 -Cond ([bool]$liveLinux.coverageComplete -and [bool]$liveLinux.testsPassed -and $liveReasons -notmatch 'unexecuted-outcome') `
        -Name 'EPS live-context-any-platform' -Detail $liveReasons

    $c680Row = @($committed | Where-Object { [string]$_.test -ceq $c680 })
    $c680Class = ''
    if ($c680Row.Count -eq 1 -and $c680Row[0].PSObject.Properties.Name -contains 'reasonClass') { $c680Class = [string]$c680Row[0].reasonClass }
    Assert-C487 -Cond ($c680Row.Count -eq 1 -and $c680Class -ceq 'live-context') `
        -Name 'EPS live-context-labeled' -Detail ('count=' + $c680Row.Count + ' class=' + $c680Class)

    $digest = ''
    $toleratedIds = @()
    $toleratedClass = ''
    if ($accepted.PSObject.Properties.Name -contains 'rosterDigest') { $digest = [string]$accepted.rosterDigest }
    if ($accepted.PSObject.Properties.Name -contains 'toleratedSkips') {
        $toleratedIds = @($accepted.toleratedSkips | ForEach-Object { [string]$_.identity })
        if (@($accepted.toleratedSkips).Count -eq 1 -and $accepted.toleratedSkips[0].PSObject.Properties.Name -contains 'reasonClass') {
            $toleratedClass = [string]$accepted.toleratedSkips[0].reasonClass
        }
    }
    $digestAgain = ''
    $again = Invoke-EpsVerdict -Rows @($skip) -Roster $roster
    if ($again.PSObject.Properties.Name -contains 'rosterDigest') { $digestAgain = [string]$again.rosterDigest }
    $digestOk = ($digest.Length -eq 64 -and $digest -ceq $digestAgain -and @($toleratedIds).Count -eq 1 -and [string]$toleratedIds[0] -ceq $cleanup -and $toleratedClass -ceq 'platform')
    Assert-C487 -Cond $digestOk -Name 'EPS tolerated-list-and-digest' -Detail ('digest=' + $digest + ' ids=' + ($toleratedIds -join ',') + ' class=' + $toleratedClass)

    $summaryPath = Join-Path $ResultsDirectory 'summary.json'
    $summaryDigest = 'a' * 64
    $summaryJson = @{
        startedAt = '2026-11-10T00:00:00Z'
        succeeded = $false
        outcome = 'TESTS'
        sha = 'abc'
        gitRef = 'rc/test'
        profile = 'rc'
        durationSeconds = 1
        builds = @()
        suites = @(
            @{ id = 'antiphon'; name = 'antiphon chunk-a'; result = 'pass'; exitCode = 8; processExitTolerated = $true; skipped = $false; failed = 0 }
            @{ id = 'antiphon'; name = 'antiphon chunk-b'; result = 'FAIL'; exitCode = 1; skipped = $false; failed = 2 }
        )
        expectedSkipRosterDigest = $summaryDigest
        toleratedSkips = @(@{ identity = $cleanup; reasonClass = 'platform' })
    } | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($summaryPath, $summaryJson)
    $shim = {
        param($Method, $Uri, $Headers, $Body)
        if ($Uri -match '/api/boards$') { return [pscustomobject]@{ id = 'board-1'; name = 'Antiphon' } }
        if ($Uri -match '/columns') { return @() }
        if ($Uri -match '/api/cards') { return [pscustomobject]@{ cards = @(); truncated = $false } }
        return $null
    }
    $title = ''
    $body = ''
    $reportError = ''
    try {
        $report = & (Join-Path $here 'nightly-report.ps1') -Summary $summaryPath -DryRun -PassThru -HttpShim $shim -Board 'Antiphon' -Api 'http://nightly.test'
        $title = [string]$report.Title
        $body = [string]$report.Body
    } catch {
        $reportError = $_.Exception.Message
    }
    $titleOk = ($title -match '2 failed' -and $title -notmatch 'exit 8' -and $body.Contains($summaryDigest) -and $body.Contains($cleanup) -and $body.Contains('Tolerated skips'))
    Assert-C487 -Cond $titleOk -Name 'EPS title-omits-tolerated-exit' -Detail ($(if ($reportError) { $reportError } else { $title + ' | ' + $body.Substring(0, [Math]::Min(400, $body.Length)) }))

    $knownCmd = Get-Command -Name 'Get-NightlyDiscoveryKnownIdentities' -CommandType Function -ErrorAction SilentlyContinue
    $staleCmd = Get-Command -Name 'Get-NightlyExpectedSkipStaleWarnings' -CommandType Function -ErrorAction SilentlyContinue
    $implText = [System.IO.File]::ReadAllText((Join-Path $lib 'nightly-tests-impl.ps1'))
    $known = @()
    $knownWarnings = @()
    if ($null -ne $knownCmd) {
        $known = @(Get-NightlyDiscoveryKnownIdentities -Nodes @([pscustomobject]@{ ClassName = 'Antiphon.Tests.Application.C0824RefOnlyCleanupTests'; Method = 'Remote_cleanup_still_uses_its_binding_and_generation' }))
    }
    if ($null -ne $staleCmd) {
        $knownWarnings = @(Get-NightlyExpectedSkipStaleWarnings -Platform 'windows' -Entries $roster -KnownTestIdentities $known)
    }
    $wired = $implText.Contains('Get-NightlyDiscoveryKnownIdentities')
    $discoveryOk = ($null -ne $knownCmd -and $null -ne $staleCmd -and @($known) -contains $cleanup -and @($knownWarnings) -contains ('stale-roster:' + $sync) -and @($knownWarnings) -notcontains ('stale-roster:' + $cleanup) -and $wired)
    Assert-C487 -Cond $discoveryOk -Name 'EPS discovery-known-identities' -Detail ('known=' + ($known -join ',') + ' warnings=' + ($knownWarnings -join ',') + ' wired=' + $wired)
}

$expected = 0
switch -Exact ($Case) {
    'Verdict' { Test-EpsVerdict; $expected = 24 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $expected
