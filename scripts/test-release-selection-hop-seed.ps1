#requires -Version 5.1
# Seeded rc selection-hop witness. The operator list is the Windows Full launch
# (Profile rc, Trigger rc, Ref, ExpectedSha, CandidateId, PriorFailuresPath,
# PriorFailuresDigest, AcceptCrossCandidateSeed, SeedSourceCandidateId) plus the
# selection arguments the rc profile adds before nightly-tests.ps1.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1')

$script:HsPriorPath = 'C:\Users\lndco\antiphon-seeds\d47848cc.json'
$script:HsPriorDigest = 'bb3c427c280930fba3c372fed4bf8a8416b0726e1c23e33c63717e40fee0b72e'
$script:HsSeedSource = 'rc-2026-10-27-cde8def41638'
$script:HsCandidate = 'rc-2026-11-01-87f4a868a86c'
$script:HsSha = '87f4a868a86ce594d6dc9d1f1365315cb3f86df9'
$script:HsRunId = '8dc9e695e8f445498bdb7fdd660028c2'

function Test-HsSequence([string[]]$List, [string[]]$Expected) {
    if ($null -eq $List -or $Expected.Count -eq 0 -or $List.Count -lt $Expected.Count) { return $false }
    for ($i = 0; $i -le ($List.Count - $Expected.Count); $i++) {
        $same = $true
        for ($j = 0; $j -lt $Expected.Count; $j++) {
            if ([string]$List[$i + $j] -cne [string]$Expected[$j]) { $same = $false; break }
        }
        if ($same) { return $true }
    }
    return $false
}

function New-HsFakeTestsTarget([string]$Directory) {
    $path = Join-Path $Directory 'nightly-tests.ps1'
    $text = @'
param(
    [string]$RepoRoot,
    [string]$LogRoot,
    [string]$Sha,
    [string]$GitRef,
    [string]$Trigger,
    [string]$Profile,
    [string]$RunId,
    [string]$Repository,
    [string]$SelectionMode,
    [string]$BaselineSha,
    [string]$BaselineReceiptPath,
    [string]$QualificationPath,
    [string]$TimingRoot,
    [string]$TrainManifestPath,
    [string]$ReleaseIntentPath,
    [string]$SelectionAttemptPath,
    [string]$SelectionAttemptDigest,
    [string]$SelectionPath,
    [string]$SelectionDigest,
    [int]$Attempt,
    [string]$ReleaseRoot,
    [string]$CandidateId,
    [string]$ExpectedSha,
    [string]$PriorFailuresPath,
    [string]$PriorFailuresDigest,
    [switch]$AcceptCrossCandidateSeed,
    [string]$SeedSourceCandidateId
)
$out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'bound.txt'
$lines = @(
    ('PriorFailuresPath=' + $PriorFailuresPath),
    ('PriorFailuresDigest=' + $PriorFailuresDigest),
    ('AcceptCrossCandidateSeed=' + [bool]$AcceptCrossCandidateSeed),
    ('SeedSourceCandidateId=' + $SeedSourceCandidateId),
    ('Profile=' + $Profile),
    ('Trigger=' + $Trigger),
    ('CandidateId=' + $CandidateId),
    ('ExpectedSha=' + $ExpectedSha),
    ('GitRef=' + $GitRef)
)
[System.IO.File]::WriteAllText($out, ($lines -join "`n"))
exit 0
'@
    [System.IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"))
    return $path
}

function New-HsRcTestsArguments([string]$TestsScript, [string]$Root) {
    $built = New-NightlyTestsHopArgumentList -TestsScript $TestsScript -CheckoutRoot 'C:\Antiphon\releases\checkout-250a07fb' -RunDir 'C:\Antiphon\releases\logs\2026-11-01-1025-8dc9e695' -Sha $script:HsSha -GitRefLabel 'origin/rc/2026-11-01' -Trigger 'rc' -Profile 'rc' -RunId $script:HsRunId -Repository 'michal-ciechan/Antiphon' -SelectionMode 'Full' -BaselineSha '' -BaselineReceiptPath (Join-Path $Root 'baseline.json') -QualificationPath (Join-Path $Root 'qualification.json') -TimingRoot (Join-Path $Root 'timing') -TrainManifestPath (Join-Path $Root 'train.json') -ReleaseIntentPath (Join-Path $Root 'intent.json') -SelectionAttemptPath (Join-Path $Root 'selection-attempt.json') -SelectionAttemptDigest ('a' * 64) -SelectionPath (Join-Path $Root 'selection.json') -SelectionDigest ('b' * 64) -Attempt 1 -ReleaseRoot 'C:\Antiphon\releases' -CandidateId $script:HsCandidate -ExpectedSha $script:HsSha -PriorFailuresPath $script:HsPriorPath -PriorFailuresDigest $script:HsPriorDigest -AcceptCrossCandidateSeed -SeedSourceCandidateId $script:HsSeedSource
    return ,$built
}

function Test-SeededRcHopBinds {
    $root = New-C487Root $ResultsDirectory
    $fake = New-HsFakeTestsTarget $root
    $testArgs = New-HsRcTestsArguments -TestsScript $fake -Root $root
    $shape = (Test-HsSequence $testArgs @('-Trigger', 'rc', '-Profile', 'rc')) -and
        (Test-HsSequence $testArgs @('-ExpectedSha', $script:HsSha)) -and
        (Test-HsSequence $testArgs @('-CandidateId', $script:HsCandidate)) -and
        (Test-HsSequence $testArgs @('-GitRef', 'origin/rc/2026-11-01')) -and
        (Test-HsSequence $testArgs @(
            '-PriorFailuresPath', $script:HsPriorPath,
            '-PriorFailuresDigest', $script:HsPriorDigest,
            '-AcceptCrossCandidateSeed',
            '-SeedSourceCandidateId', $script:HsSeedSource))
    Assert-C487 $shape 'HS SeededRcHopBinds argument shape' ($testArgs -join ' | ')
    if (-not $shape) { return }
    $threw = ''
    $result = $null
    try {
        $result = Invoke-ReleaseSelectionHop -WorkingDirectory $root -ArgumentList $testArgs
    } catch {
        $threw = [string]$_.Exception.Message
    }
    if ($threw) {
        Assert-C487 $false 'HS SeededRcHopBinds' $threw
        return
    }
    $boundPath = Join-Path $root 'bound.txt'
    $bound = @{}
    if (Test-Path -LiteralPath $boundPath) {
        foreach ($line in @( [System.IO.File]::ReadAllLines($boundPath) )) {
            $eq = $line.IndexOf('=')
            if ($eq -gt 0) { $bound[$line.Substring(0, $eq)] = $line.Substring($eq + 1) }
        }
    }
    Assert-C487 ($null -ne $result -and [int]$result.ExitCode -eq 0) 'HS SeededRcHopBinds child exit' ([string]$result.ExitCode)
    Assert-C487 ($bound['PriorFailuresPath'] -ceq $script:HsPriorPath) 'HS SeededRcHopBinds PriorFailuresPath' ([string]$bound['PriorFailuresPath'])
    Assert-C487 ($bound['PriorFailuresDigest'] -ceq $script:HsPriorDigest) 'HS SeededRcHopBinds PriorFailuresDigest' ([string]$bound['PriorFailuresDigest'])
    Assert-C487 ($bound['AcceptCrossCandidateSeed'] -ceq 'True') 'HS SeededRcHopBinds AcceptCrossCandidateSeed' ([string]$bound['AcceptCrossCandidateSeed'])
    Assert-C487 ($bound['SeedSourceCandidateId'] -ceq $script:HsSeedSource) 'HS SeededRcHopBinds SeedSourceCandidateId' ([string]$bound['SeedSourceCandidateId'])
    Assert-C487 ($bound['Profile'] -ceq 'rc' -and $bound['Trigger'] -ceq 'rc' -and $bound['CandidateId'] -ceq $script:HsCandidate -and $bound['ExpectedSha'] -ceq $script:HsSha -and $bound['GitRef'] -ceq 'origin/rc/2026-11-01') 'HS SeededRcHopBinds rc identity' (($bound['Profile'] + '/' + $bound['Trigger'] + '/' + $bound['CandidateId']))
}

function Test-SwitchDriftGuard {
    $scripts = @(
        (Join-Path $PSScriptRoot 'nightly-tests.ps1'),
        (Join-Path $PSScriptRoot 'nightly-run.ps1')
    )
    $selection = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/release-selection.ps1'))
    $derived = $selection.Contains('Get-ReleaseSelectionDeclaredSwitches') -and $selection.Contains('Assert-ReleaseSelectionHopSwitchDrift')
    Assert-C487 $derived 'HS SwitchDriftGuard classifier is derived'
    $handRefused = $true
    $handDetail = ''
    foreach ($script in $scripts) {
        try {
            Assert-ReleaseSelectionHopSwitchDrift -ScriptPath $script -ForwardedAsSwitch @('NoSync', 'NoReport', 'WhatIf')
            $handRefused = $false
            $handDetail = (Split-Path -Leaf $script) + ' accepted the hand list'
        } catch {
            if ([string]$_.Exception.Message -cne 'selection-hop-switch-drift:AcceptCrossCandidateSeed') {
                $handRefused = $false
                $handDetail = (Split-Path -Leaf $script) + ' ' + $_.Exception.Message
            }
        }
    }
    Assert-C487 $handRefused 'HS SwitchDriftGuard refuses the hand list' $handDetail
    $walkOk = $true
    $walkDetail = ''
    foreach ($script in $scripts) {
        $declared = [string[]](Get-ReleaseSelectionDeclaredSwitches -ScriptPath $script)
        try { Assert-ReleaseSelectionHopSwitchDrift -ScriptPath $script -ForwardedAsSwitch $declared }
        catch { $walkOk = $false; $walkDetail = $_.Exception.Message }
        foreach ($name in [string[]](Get-ReleaseSelectionAstSwitches -ScriptPath $script)) {
            $parsed = $null
            try {
                $parsed = ConvertTo-ReleaseSelectionHopParameters -ArgumentList @('-File', $script, ('-' + $name), '-RepoRoot', 'SENTINEL')
            } catch {
                $walkOk = $false
                $walkDetail = $name + ' ' + $_.Exception.Message
                continue
            }
            $repo = ''
            if ($parsed.ContainsKey('RepoRoot')) {
                $repo = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$parsed['RepoRoot']))
            }
            $bound = $parsed[$name]
            if (-not (($bound -is [bool]) -and $bound) -or $repo -cne 'SENTINEL') {
                $walkOk = $false
                $walkDetail = (Split-Path -Leaf $script) + ' ' + $name + ' repo=' + $repo
            }
        }
    }
    Assert-C487 $walkOk 'HS SwitchDriftGuard every target switch keeps its value' $walkDetail
}

function Test-HopFailureRecord {
    $root = New-C487Root $ResultsDirectory
    $log = Join-Path $root 'empty-log'
    $statePath = Join-Path $root 'last-run.json'
    $state = [ordered]@{ phase = 'tests'; runId = $script:HsRunId; exitCode = 1 }
    $message = 'Exception calling "GetBytes" with "1" argument(s): "Value cannot be null. (Parameter ''chars'')"'
    $command = 'pwsh -File C:\Antiphon\releases\checkout-250a07fb\scripts\nightly-tests.ps1 -AcceptCrossCandidateSeed -SeedSourceCandidateId ' + $script:HsSeedSource
    [void](Write-NightlyHopFailure -State $state -StatePath $statePath -LogDir $log -ErrorMessage $message -Command $command)
    $failurePath = Join-Path $log 'hop-failure.txt'
    $text = ''
    if (Test-Path -LiteralPath $failurePath) { $text = [System.IO.File]::ReadAllText($failurePath) }
    Assert-C487 ($text.Contains($message)) 'HS HopFailureRecord log has the exception' $text
    Assert-C487 ($text.Contains($command)) 'HS HopFailureRecord log has the command' $text
    $json = $null
    if (Test-Path -LiteralPath $statePath) { $json = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json }
    Assert-C487 ($null -ne $json -and [string]$json.error -ceq $message) 'HS HopFailureRecord last-run error' ([string]$json.error)
    Assert-C487 ($null -ne $json -and [string]$json.command -ceq $command) 'HS HopFailureRecord last-run command' ([string]$json.command)
    Assert-C487 ($null -ne $json -and [int]$json.exitCode -eq 1) 'HS HopFailureRecord last-run exit' ([string]$json.exitCode)
    $impl = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1'))
    Assert-C487 ($impl.Contains('Write-NightlyHopFailure -State $stateObj')) 'HS HopFailureRecord catch writes the failure'
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    Test-SeededRcHopBinds
    Test-SwitchDriftGuard
    Test-HopFailureRecord
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
