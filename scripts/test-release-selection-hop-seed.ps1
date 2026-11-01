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

function Test-HS_SeededRcHopBinds {
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

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    Test-HS_SeededRcHopBinds
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
