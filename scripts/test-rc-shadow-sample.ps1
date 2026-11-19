#requires -Version 7.0
# CARD-1836 S8b: fixed-seed sample, contract mismatch, and trust refusal.
# No qualification launch, publish, promote, or external service.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    [IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
}
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
. (Join-Path $PSScriptRoot 'lib/rc-shadow-sample.ps1')
. (Join-Path $PSScriptRoot 'lib/release-gate.ps1')
Set-StrictMode -Version Latest
$script:Errors = [Collections.Generic.List[string]]::new()
function Note([bool]$Ok, [string]$Why) { if (-not $Ok) { $script:Errors.Add($Why) } }
$script:SeedA = '0123456789abcdef' * 4
$script:SeedB = 'fedcba9876543210' * 4
$script:Now = [DateTimeOffset]'2026-11-13T12:00:00Z'
$script:Digest = 'a' * 64

function Join-Ids($Value) {
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Value)) {
        if ($null -ne $item) { $parts.Add([string]$item) }
    }
    return ($parts -join '|')
}

function New-PinnedChunks {
    $items = [Collections.Generic.List[object]]::new()
    foreach ($i in 0..15) {
        $items.Add(@{
            id = ('c{0:d2}' -f $i); suite = 'alpha'; boundary = 'north'
            duration = 1; passed = $true
        })
    }
    foreach ($i in 16..30) {
        $items.Add(@{
            id = ('c{0:d2}' -f $i); suite = 'alpha'; boundary = 'south'
            duration = 100000; passed = $false
        })
    }
    $items.Add(@{ id = 'skip-a'; suite = 'alpha'; boundary = 'north'; expectedSkipOnly = $true; duration = 1; passed = $true })
    $items.Add(@{ id = 'skip-b'; suite = 'beta'; boundary = 'west'; expectedSkipOnly = $true; duration = 1; passed = $true })
    return ,$items.ToArray()
}

function New-Population([int]$Count, [int]$Strata) {
    $items = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $Count; $i++) {
        $items.Add(@{
            id = ('p{0:d4}' -f $i)
            suite = 'suite'
            boundary = ('b{0:d2}' -f ($i % $Strata))
            duration = $(if ($i % 2 -eq 0) { 1 } else { 100000 })
            passed = ($i % 3 -eq 0)
        })
    }
    return ,$items.ToArray()
}

function New-ExpectedIds([string]$Omitted) {
    $ids = [Collections.Generic.List[string]]::new()
    foreach ($i in 0..30) {
        $id = 'c{0:d2}' -f $i
        if ($id -cne $Omitted) { $ids.Add($id) }
    }
    return ,$ids.ToArray()
}

function New-TempRoot {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('rc-shadow-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    return $root
}

function New-Contract {
    $contract = [ordered]@{
        format = 'antiphon-rc-input-contract-v1'; inputMapVersion = '1'
        sdkVersion = '10.0.204'; runtimeVersion = '9.0.10'
        platform = 'linux'; executionImplementation = 'compiled'
    }
    foreach ($field in @('policyHash','catalogHash','selectorHash','skipResolverHash','discoveryHash','parserHash',
        'coverageHash','launcherHash','custodyHash','buildHash','harnessHash','inputMapHash','sdkHash','runtimeHash')) {
        $contract[$field] = 'a' * 64
    }
    return $contract
}

function New-Evidence {
    return @{
        baselineState = 'admitted-ancestor'; admissionChainState = 'verified'; historyState = 'complete'
        globalOwnershipState = 'resolved'; shadowState = 'clean'
    }
}

function New-Anchor {
    return @{
        kind = 'executed-full'; admissionState = 'verified'; completedAtUtc = '2026-11-12T12:00:00Z'
        sourceSha = '1' * 40; closureDigest = 'b' * 64
    }
}

function New-Pilot([string]$CutId, [string]$Platform = 'windows', [string]$Sample = 'executed') {
    return @{
        cutId = $CutId; platform = $Platform; sample = $Sample; shadowPilot = ($Sample -ceq 'executed')
        discrepancy = $false; openFailure = $false; nativeReceipts = 'current'; fullOverdue = $false
        contractDigest = $script:Digest; nExecuted = $(if ($Sample -ceq 'executed') { 30 } else { 0 })
    }
}

function Assert-ProposalShape($Result, [string]$Label) {
    Note ($Result.format -ceq 'antiphon-rc-shadow-proposal-v1') ($Label + ': format')
    Note ($Result.effectiveMode -ceq 'Full') ($Label + ': effectiveMode')
    Note ($Result.gateCredit -is [bool] -and $Result.gateCredit -eq $false) ($Label + ': gateCredit')
    Note ($Result.bindingState -ceq 'proposal-only') ($Label + ': bindingState')
    Note ($Result.incrementalValidation -ceq 'Shadow') ($Label + ': incrementalValidation')
    Note ($Result.trustGranted -is [bool] -and $Result.trustGranted -eq $false) ($Label + ': trustGranted')
    Note ($Result.trustRecordWritten -is [bool] -and $Result.trustRecordWritten -eq $false) ($Label + ': trustRecordWritten')
    Note ($Result.pilotClaim -is [bool] -and $Result.pilotClaim -eq $false) ($Label + ': pilotClaim')
    Note ($Result.launched -is [bool] -and $Result.launched -eq $false) ($Label + ': launched')
    Note ($Result.expandedToFull -is [bool] -and -not $Result.expandedToFull) ($Label + ': expandedToFull')
}

function Assert-OffSwitch {
    $modeLists = [Collections.Generic.List[object]]::new()
    [void]$modeLists.Add([string[]]@('release-cut.ps1', '    [ValidateSet(''Full'', ''Shadow'', ''ChangeAware'')][string]$SelectionMode = ''Full'','))
    [void]$modeLists.Add([string[]]@('nightly-run.ps1', '    [ValidateSet(''Full'', ''Shadow'', ''ChangeAware'')][string]$SelectionMode = ''Full'','))
    [void]$modeLists.Add([string[]]@('nightly-tests.ps1', '    [ValidateSet(''Full'', ''Shadow'', ''ChangeAware'')][string]$SelectionMode = ''Full'','))
    foreach ($row in $modeLists) {
        $lines = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot $row[0]))
        $found = $false
        foreach ($line in $lines) { if ($line -ceq $row[1]) { $found = $true } }
        Note $found ('mode list ' + $row[0])
    }
    $sha = 'a' * 40
    $base = @{
        profile = 'rc'; trigger = 'rc'; ref = 'rc/2026-11-19'; sha = $sha; expectedSha = $sha
        candidateId = 'rc-2026-11-19-aaaaaaaaaaaa'; coverageComplete = $true; testsPassed = $true
        reportDelivered = $true; exitCode = 0; runId = 'off-switch'; policyHash = 'ab'
    }
    $full = [ordered]@{}
    foreach ($key in $base.Keys) { $full[$key] = $base[$key] }
    $full['coverageScope'] = 'Full'
    $fullVerdict = Get-ReleaseGateCreditVerdict $full
    Note ($fullVerdict.Ok -eq $true -and $fullVerdict.CreditKind -ceq 'rc-release') ('Full gate credit ' + ($fullVerdict.Reasons -join '|'))
    $incremental = [ordered]@{}
    foreach ($key in $base.Keys) { $incremental[$key] = $base[$key] }
    $incremental['coverageScope'] = 'Incremental'
    $incrementalVerdict = Get-ReleaseGateCreditVerdict $incremental
    Note ($incrementalVerdict.Ok -eq $false -and (@($incrementalVerdict.Reasons) -ccontains 'full-credit-scope-required')) ('Incremental gate ' + ($incrementalVerdict.Reasons -join '|'))
}

try {
    switch ($Case) {
        'SampleSizeStrataAndReplay' {
            $root = New-TempRoot
            try {
                Assert-OffSwitch
                $forward = New-PinnedChunks
                $reverse = [object[]]::new($forward.Count)
                [Array]::Copy($forward, $reverse, $forward.Count)
                [Array]::Reverse($reverse)
                $pathA = Join-Path $root 'seed-a.json'
                $first = New-RcShadowProposal -Chunks $reverse -Seed $script:SeedA -ProposalPath $pathA
                $expectedA = New-ExpectedIds 'c06'
                Assert-ProposalShape $first 'seed-a'
                Note ($first.sample -ceq 'roster') ('seed-a sample=' + $first.sample)
                Note ($first.seed -ceq $script:SeedA) 'seed-a stored seed'
                Note ($first.cchunk -eq 31) ('seed-a cchunk=' + $first.cchunk)
                Note ($first.stratumCount -eq 2) ('seed-a strata=' + $first.stratumCount)
                Note ($first.n -eq 30) ('seed-a n=' + $first.n)
                Note ($first.shadowPilot -eq $true) 'seed-a shadow pilot plan'
                Note ((Join-Ids $first.selected) -ceq (Join-Ids $expectedA)) ('seed-a roster=' + (Join-Ids $first.selected))
                Note ((Join-Ids $first.expectedSkipOnly) -ceq 'skip-a|skip-b') ('seed-a skips=' + (Join-Ids $first.expectedSkipOnly))
                Note ((Join-Ids $first.selected) -cnotlike '*skip-*') 'skip-only entered the executed sample'
                $stored = Get-Content -LiteralPath $pathA -Raw -Encoding utf8 | ConvertFrom-Json
                Note ($stored.seed -ceq $script:SeedA) 'file seed'
                Note ($stored.effectiveMode -ceq 'Full' -and $stored.gateCredit -eq $false -and $stored.incrementalValidation -ceq 'Shadow') 'file off switch'
                Note ($stored.launched -eq $false -and $stored.trustGranted -eq $false -and $stored.pilotClaim -eq $false) 'file does not launch or trust'
                Note ((Join-Ids $stored.selected) -ceq (Join-Ids $expectedA)) 'file roster'
                $replay = New-RcShadowProposal -Chunks $forward -Seed $script:SeedA -ProposalPath (Join-Path $root 'seed-a-replay.json')
                Note ((Join-Ids $replay.selected) -ceq (Join-Ids $first.selected)) 'replay roster'
                Note ($replay.seed -ceq $first.seed -and $replay.n -eq $first.n) 'replay seed and n'
                $second = New-RcShadowProposal -Chunks $forward -Seed $script:SeedB -ProposalPath (Join-Path $root 'seed-b.json')
                $expectedB = New-ExpectedIds 'c10'
                Note ((Join-Ids $second.selected) -ceq (Join-Ids $expectedB)) ('seed-b roster=' + (Join-Ids $second.selected))
                Note ((Join-Ids $second.selected) -cne (Join-Ids $first.selected)) 'distinct seeds collapsed'
                $wroteAgain = $false
                try { New-RcShadowProposal -Chunks $forward -Seed $script:SeedA -ProposalPath $pathA } catch { $wroteAgain = $_.Exception.Message -ceq 'write-once' }
                Note $wroteAgain 'second write refused'
                $zero = New-RcShadowProposal -Chunks @(@{ id = 'skip-only'; suite = 'alpha'; boundary = 'north'; expectedSkipOnly = $true }) -Seed $script:SeedA -ProposalPath (Join-Path $root 'zero.json')
                Assert-ProposalShape $zero 'zero'
                Note ($zero.sample -ceq 'not-applicable') ('zero sample=' + $zero.sample)
                Note ($zero.cchunk -eq 0 -and $zero.n -eq 0 -and $zero.shadowPilot -eq $false -and $zero.pilotClaim -eq $false) ('zero counts cchunk=' + $zero.cchunk + ' n=' + $zero.n)
                Note ((Join-Ids $zero.selected) -ceq '') 'zero selected'
                Note ((Join-Ids $zero.expectedSkipOnly) -ceq 'skip-only') 'zero skip report'
                Note (-not (Test-RcShadowPilotEligible -Pilot (New-Pilot 'zero' -Sample 'not-applicable') -ContractDigest $script:Digest)) 'zero-carry record counted as a pilot'
                $budgets = [Collections.Generic.List[object]]::new()
                [void]$budgets.Add([int[]](1, 1, 1, 1))
                [void]$budgets.Add([int[]](30, 1, 30, 30))
                [void]$budgets.Add([int[]](600, 1, 600, 30))
                [void]$budgets.Add([int[]](601, 1, 601, 31))
                [void]$budgets.Add([int[]](1000, 1, 1000, 50))
                [void]$budgets.Add([int[]](32, 32, 32, 32))
                foreach ($row in $budgets) {
                    $population = New-Population $row[0] $row[1]
                    $budget = New-RcShadowProposal -Chunks $population -Seed $script:SeedA -ProposalPath (Join-Path $root ('n-' + $row[0] + '.json'))
                    Assert-ProposalShape $budget ('n' + $row[0])
                    Note ($budget.cchunk -eq $row[2] -and $budget.n -eq $row[3] -and @($budget.selected).Count -eq $row[3]) ('budget ' + $row[0] + ' cchunk=' + $budget.cchunk + ' n=' + $budget.n + ' selected=' + @($budget.selected).Count)
                    if ($row[0] -eq 32) {
                        $all = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($item in @($population)) { [void]$all.Add([string]$item.id) }
                        $got = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($id in @($budget.selected)) { [void]$got.Add([string]$id) }
                        Note ($all.SetEquals($got)) 'strata increase dropped a stratum'
                    }
                }
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'MismatchInvalidatesCarries' {
            $beforeCurrent = New-Contract
            $beforeBaseline = New-Contract
            $beforeEvidence = New-Evidence
            $beforeAnchor = New-Anchor
            $snapshot = @{
                current = ($beforeCurrent | ConvertTo-Json -Compress -Depth 6)
                baseline = ($beforeBaseline | ConvertTo-Json -Compress -Depth 6)
                evidence = ($beforeEvidence | ConvertTo-Json -Compress -Depth 6)
                anchor = ($beforeAnchor | ConvertTo-Json -Compress -Depth 6)
            }
            $failure = [ordered]@{ chunkId = 'c01'; outcome = 'failed'; receipt = 'receipt-1' }
            $failureJson = $failure | ConvertTo-Json -Compress -Depth 6
            $carries = @('c2','c1','c3')
            $changed = New-Contract
            $changed.policyHash = 'b' * 64
            $mismatch = Get-RcShadowTrustDecision -CurrentContract $changed -BaselineContract $beforeBaseline -Evidence $beforeEvidence -FullAnchor $beforeAnchor -UtcNow $script:Now -Carries $carries -SampleFailure $failure -ContractDigest $script:Digest
            Note ($mismatch.trustGranted -eq $false) 'mismatch granted trust'
            Note ($mismatch.effectiveMode -ceq 'Full') ('mismatch mode=' + $mismatch.effectiveMode)
            Note ($mismatch.gateCredit -is [bool] -and $mismatch.gateCredit -eq $false) 'mismatch gate credit'
            Note ($mismatch.incrementalValidation -ceq 'Shadow') ('mismatch validation=' + $mismatch.incrementalValidation)
            Note ($mismatch.expandedToFull -eq $false) 'mismatch expanded into Full'
            Note ($mismatch.carriesInvalidated -eq $true) 'mismatch left carries authorized'
            Note ((Join-Ids $mismatch.invalidatedCarries) -ceq 'c1|c2|c3') ('mismatch carries=' + (Join-Ids $mismatch.invalidatedCarries))
            Note ((Join-Ids $mismatch.reasons) -ceq 'contract-changed:policyHash|sample-failed') ('mismatch reasons=' + (Join-Ids $mismatch.reasons))
            Note (($mismatch.preservedSampleFailure | ConvertTo-Json -Compress -Depth 6) -ceq $failureJson) 'failed sample erased'
            Note ($mismatch.trustRecordWritten -eq $false) 'mismatch wrote trust'
            $sampleOnly = Get-RcShadowTrustDecision -CurrentContract (New-Contract) -BaselineContract (New-Contract) -Evidence (New-Evidence) -FullAnchor (New-Anchor) -UtcNow $script:Now -Carries $carries -SampleFailure $failure -ContractDigest $script:Digest
            Note ($sampleOnly.carriesInvalidated -eq $true -and $sampleOnly.trustGranted -eq $false -and $sampleOnly.expandedToFull -eq $false) 'sample failure kept carries'
            Note ((Join-Ids $sampleOnly.reasons) -ceq 'sample-failed') ('sample reasons=' + (Join-Ids $sampleOnly.reasons))
            Note (($sampleOnly.preservedSampleFailure | ConvertTo-Json -Compress -Depth 6) -ceq $failureJson) 'sample-only failure erased'
            $roster = Get-RcShadowTrustDecision -CurrentContract (New-Contract) -BaselineContract (New-Contract) -Evidence (New-Evidence) -FullAnchor (New-Anchor) -UtcNow $script:Now -Carries @('c1') -RosterChanged $true -ContractDigest $script:Digest
            Note ($roster.carriesInvalidated -eq $true -and (Join-Ids $roster.reasons) -ceq 'sample-roster-changed' -and $roster.expandedToFull -eq $false) ('roster reasons=' + (Join-Ids $roster.reasons))
            $missing = New-Contract
            $missing.Remove('policyHash')
            $unknown = Get-RcShadowTrustDecision -CurrentContract $missing -BaselineContract (New-Contract) -Evidence (New-Evidence) -FullAnchor (New-Anchor) -UtcNow $script:Now -Carries @('c9') -ContractDigest $script:Digest
            Note ($unknown.carriesInvalidated -eq $true -and (Join-Ids $unknown.reasons) -ceq 'contract-current-unknown:policyHash') ('unknown reasons=' + (Join-Ids $unknown.reasons))
            Note (($beforeCurrent | ConvertTo-Json -Compress -Depth 6) -ceq $snapshot.current) 'current contract mutated'
            Note (($beforeBaseline | ConvertTo-Json -Compress -Depth 6) -ceq $snapshot.baseline) 'baseline contract mutated'
            Note (($beforeEvidence | ConvertTo-Json -Compress -Depth 6) -ceq $snapshot.evidence) 'evidence mutated'
            Note (($beforeAnchor | ConvertTo-Json -Compress -Depth 6) -ceq $snapshot.anchor) 'anchor mutated'
            Note (($failure | ConvertTo-Json -Compress -Depth 6) -ceq $failureJson) 'failure receipt mutated'
        }
        'NoCarryAndOnePilotDoNotGrantTrust' {
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                $none = Get-RcShadowTrustDecision -Pilots @() -CurrentSample 'not-applicable' -ContractDigest $script:Digest -TrustRecordPath $trustPath
                Note ($none.reason -ceq 'not-a-shadow-pilot') ('none reason=' + $none.reason)
                Note ($none.observedPilots -eq 0 -and $none.requiredPilots -eq 2) ('none pilots=' + $none.observedPilots)
                Note ($none.trustGranted -eq $false -and $none.incrementalValidation -ceq 'Shadow' -and $none.effectiveMode -ceq 'Full') 'none flipped validation'
                Note ($none.gateCredit -is [bool] -and $none.gateCredit -eq $false -and $none.trustRecordWritten -eq $false) 'none granted credit'
                Note (-not [IO.File]::Exists($trustPath)) 'none created a trust record'
                $one = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1')) -CurrentSample 'not-applicable' -ContractDigest $script:Digest -TrustRecordPath $trustPath
                Note ($one.reason -ceq 'pilot-count' -and $one.observedPilots -eq 1 -and $one.requiredPilots -eq 2 -and $one.trustGranted -eq $false) ('one reason=' + $one.reason + ' observed=' + $one.observedPilots)
                $mixed = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1'), (New-Pilot 'cut-zero' -Sample 'not-applicable')) -CurrentSample 'roster' -ContractDigest $script:Digest
                Note ($mixed.reason -ceq 'pilot-count' -and $mixed.observedPilots -eq 1 -and $mixed.trustGranted -eq $false) ('mixed observed=' + $mixed.observedPilots + ' reason=' + $mixed.reason)
                $duplicate = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1'), (New-Pilot 'cut-1')) -CurrentSample 'roster' -ContractDigest $script:Digest
                Note ($duplicate.observedPilots -eq 1 -and $duplicate.reason -ceq 'pilot-count') ('duplicate observed=' + $duplicate.observedPilots)
                $linux = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1'), (New-Pilot 'cut-linux' -Platform 'linux')) -CurrentSample 'roster' -ContractDigest $script:Digest
                Note ($linux.observedPilots -eq 1 -and $linux.reason -ceq 'pilot-count' -and $linux.trustGranted -eq $false) ('linux observed=' + $linux.observedPilots)
                $two = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1'), (New-Pilot 'cut-2')) -CurrentSample 'roster' -ContractDigest $script:Digest -TrustRecordPath $trustPath
                Note ($two.observedPilots -eq 2 -and $two.reason -ceq 'operator-trust-required' -and $two.trustGranted -eq $false) ('two reason=' + $two.reason)
                Note ($two.incrementalValidation -ceq 'Shadow' -and $two.effectiveMode -ceq 'Full' -and $two.gateCredit -eq $false) 'two promoted itself'
                Note (-not [IO.File]::Exists($trustPath) -and $two.trustRecordWritten -eq $false) 'two wrote a trust record'
                $record = @{ approved = $true; contractDigest = $script:Digest; platform = 'windows' }
                $held = Get-RcShadowTrustDecision -Pilots @((New-Pilot 'cut-1'), (New-Pilot 'cut-2')) -CurrentSample 'roster' -ContractDigest $script:Digest -OperatorTrustRecord $record -TrustRecordPath $trustPath
                Note ($held.observedPilots -eq 2 -and $held.reason -ceq 'trust-transition-disabled' -and $held.trustGranted -eq $false) ('held reason=' + $held.reason)
                Note ($held.incrementalValidation -ceq 'Shadow' -and $held.effectiveMode -ceq 'Full' -and $held.expandedToFull -eq $false) 'held flipped IncrementalValidation'
                Note (-not [IO.File]::Exists($trustPath) -and $held.trustRecordWritten -eq $false) 'held persisted the operator record'
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch { Note $false ('unexpected harness error: ' + $_.Exception.Message) }
Assert-C487 ($script:Errors.Count -eq 0) ('RS ' + $Case + '.contract') ($script:Errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
