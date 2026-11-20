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
            duration = 1; passed = $true; completelyCarryable = $true
        })
    }
    foreach ($i in 16..30) {
        $items.Add(@{
            id = ('c{0:d2}' -f $i); suite = 'alpha'; boundary = 'south'
            duration = 100000; passed = $false; completelyCarryable = $true
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
            completelyCarryable = $true
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
    $executed = $Sample -ceq 'executed'
    return @{
        cutId = $CutId; platform = $Platform; sample = $Sample; shadowPilot = $executed
        discrepancy = $false; openFailure = $false; nativeReceipts = 'current'; fullOverdue = $false
        contractDigest = $script:Digest
        n = $(if ($executed) { 30 } else { 0 })
        nExecuted = $(if ($executed) { 30 } else { 0 })
        cchunk = 600
        stratumCount = 1
    }
}

function New-CountedPilot {
    param(
        [string]$CutId,
        [int]$Planned = 30,
        [int]$Executed = 30,
        [string]$Digest = 'USE',
        [int]$Cchunk = 600,
        [int]$Strata = 1,
        [switch]$OmitPlan
    )
    if ($Digest -ceq 'USE') { $Digest = $script:Digest }
    $pilot = @{
        cutId = $CutId; platform = 'windows'; sample = 'executed'; shadowPilot = $true
        discrepancy = $false; openFailure = $false; nativeReceipts = 'current'; fullOverdue = $false
        contractDigest = $Digest; nExecuted = $Executed
    }
    if (-not $OmitPlan) {
        $pilot.n = $Planned
        $pilot.cchunk = $Cchunk
        $pilot.stratumCount = $Strata
    }
    return $pilot
}

function New-ClosedFlagEvidence {
    param([string]$Field, $Value)
    $row = [ordered]@{
        chunkId = 'c1'
        outcome = 'passed'
        provenance = 'verified'
    }
    # Assign even when the value is null so the key is present.
    $row[$Field] = $Value
    return @{ sampled = @('c1'); results = @($row) }
}

function Assert-DerivedFailure {
    param([string]$Label, $Evidence, [string]$Kind, [string]$ChunkId, [string]$TrustPath)
    $decision = Get-RcShadowTrustDecision -Carries @('c2', 'c1') -Evidence $Evidence -CurrentSample 'roster' -TrustRecordPath $TrustPath
    Note ($decision.carriesInvalidated -eq $true) ($Label + ' left carries valid')
    Note ((Join-Ids $decision.invalidatedCarries) -ceq 'c1|c2') ($Label + ' carries=' + (Join-Ids $decision.invalidatedCarries))
    Note ((Join-Ids $decision.reasons) -ceq 'sample-failed') ($Label + ' reasons=' + (Join-Ids $decision.reasons))
    Note ($decision.trustGranted -eq $false -and $decision.gateCredit -eq $false -and $decision.effectiveMode -ceq 'Full' -and $decision.incrementalValidation -ceq 'Shadow') ($Label + ' shadow flags')
    Note ($decision.expandedToFull -eq $false -and $decision.trustRecordWritten -eq $false) ($Label + ' wrote trust or expanded')
    Note (-not [IO.File]::Exists($TrustPath)) ($Label + ' created a trust record')
    $foundKind = [string](Get-RcShadowField $decision.preservedSampleFailure 'kind')
    $foundChunk = [string](Get-RcShadowField $decision.preservedSampleFailure 'chunkId')
    Note ($foundKind -ceq $Kind) ($Label + ' kind=' + $foundKind)
    if (-not [string]::IsNullOrEmpty($ChunkId)) { Note ($foundChunk -ceq $ChunkId) ($Label + ' chunk=' + $foundChunk) }
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
        'OneExecutedSampleIsNotAPilot' {
            $bare = New-CountedPilot 'bare-one' -Executed 1 -OmitPlan
            Note (-not (Test-RcShadowPilotEligible -Pilot $bare -ContractDigest $script:Digest)) 'one executed sample counted as a pilot'
            $partial = New-CountedPilot 'partial' -Planned 30 -Executed 1 -Cchunk 600 -Strata 1
            Note (-not (Test-RcShadowPilotEligible -Pilot $partial -ContractDigest $script:Digest)) 'one of thirty planned samples counted as a pilot'
            $short = New-CountedPilot 'short' -Planned 1 -Executed 1 -Cchunk 600 -Strata 1
            Note (-not (Test-RcShadowPilotEligible -Pilot $short -ContractDigest $script:Digest)) 'planned N below the D-8 minimum counted as a pilot'
            $under = New-CountedPilot 'under' -Planned 30 -Executed 30 -Cchunk 601 -Strata 1
            Note (-not (Test-RcShadowPilotEligible -Pilot $under -ContractDigest $script:Digest)) 'thirty samples counted when D-8 requires 31'
            $legalOne = New-CountedPilot 'legal-one' -Planned 1 -Executed 1 -Cchunk 1 -Strata 1
            Note (Test-RcShadowPilotEligible -Pilot $legalOne -ContractDigest $script:Digest) 'bound plan of one was refused'
            $legal = New-CountedPilot 'legal-31' -Planned 31 -Executed 31 -Cchunk 601 -Strata 1
            Note (Test-RcShadowPilotEligible -Pilot $legal -ContractDigest $script:Digest) 'D-8 plan of 31 was refused'
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                $decision = Get-RcShadowTrustDecision -Pilots @($bare, $partial) -CurrentSample 'roster' -ContractDigest $script:Digest -TrustRecordPath $trustPath
                Note ($decision.observedPilots -eq 0) ('one-sample observed=' + $decision.observedPilots)
                Note ($decision.reason -ceq 'pilot-count') ('one-sample reason=' + $decision.reason)
                Note ($decision.trustGranted -eq $false -and $decision.gateCredit -eq $false -and $decision.effectiveMode -ceq 'Full' -and $decision.incrementalValidation -ceq 'Shadow') 'one-sample granted trust'
                Note ($decision.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) 'one-sample wrote a trust record'
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'EmptyDigestIsUnproven' {
            $empty = New-CountedPilot 'empty' -Digest ''
            Note (-not (Test-RcShadowPilotEligible -Pilot $empty -ContractDigest '')) 'empty digest matched an empty pilot digest'
            $missing = New-CountedPilot 'missing' -Digest ''
            $missing.Remove('contractDigest')
            Note (-not (Test-RcShadowPilotEligible -Pilot $missing -ContractDigest '')) 'missing digest matched'
            $upper = New-CountedPilot 'upper' -Digest ('A' * 64)
            Note (-not (Test-RcShadowPilotEligible -Pilot $upper -ContractDigest ('A' * 64))) 'uppercase digest matched'
            Note (Test-RcShadowPilotEligible -Pilot (New-Pilot 'canonical') -ContractDigest $script:Digest) 'canonical digest was refused'
            $blankContract = New-CountedPilot 'blank-contract'
            Note (-not (Test-RcShadowPilotEligible -Pilot $blankContract -ContractDigest '')) 'empty contract digest matched a real pilot'
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                $first = New-CountedPilot 'cut-1' -Digest ''
                $second = New-CountedPilot 'cut-2' -Digest ''
                $two = Get-RcShadowTrustDecision -Pilots @($first, $second) -CurrentSample 'roster' -ContractDigest '' -TrustRecordPath $trustPath
                Note ($two.observedPilots -eq 0) ('empty-digest observed=' + $two.observedPilots)
                Note ($two.reason -ceq 'pilot-count') ('empty-digest reason=' + $two.reason)
                Note ($two.trustGranted -eq $false -and $two.gateCredit -eq $false -and $two.effectiveMode -ceq 'Full' -and $two.incrementalValidation -ceq 'Shadow') 'empty digest granted trust'
                Note ($two.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) 'empty digest wrote a trust record'
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'MissingCarryableIsDirty' {
            $unknown = New-RcShadowProposal -Chunks @(@{ id = 'unknown'; suite = 'alpha'; boundary = 'north' }) -Seed $script:SeedA
            Assert-ProposalShape $unknown 'unknown'
            Note ($unknown.cchunk -eq 0) ('unknown cchunk=' + $unknown.cchunk)
            Note ($unknown.n -eq 0) ('unknown n=' + $unknown.n)
            Note ($unknown.sample -ceq 'not-applicable') ('unknown sample=' + $unknown.sample)
            Note ((Join-Ids $unknown.selected) -ceq '') ('missing completelyCarryable entered the sample: ' + (Join-Ids $unknown.selected))
            $mixed = New-RcShadowProposal -Chunks @(
                @{ id = 'unknown'; suite = 'alpha'; boundary = 'north' }
                @{ id = 'carry'; suite = 'alpha'; boundary = 'north'; completelyCarryable = $true }
                @{ id = 'dirty'; suite = 'beta'; boundary = 'south'; completelyCarryable = $false }
            ) -Seed $script:SeedA
            Assert-ProposalShape $mixed 'mixed'
            Note ($mixed.cchunk -eq 1) ('missing completelyCarryable diluted the sample cchunk=' + $mixed.cchunk)
            Note ($mixed.n -eq 1) ('mixed n=' + $mixed.n)
            Note ((Join-Ids $mixed.selected) -ceq 'carry') ('mixed selected=' + (Join-Ids $mixed.selected))
        }
        'EvidenceDerivesSampleFailure' {
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                Assert-DerivedFailure 'empty results' @{ sampled = @('c1'); results = @() } 'empty-results' 'c1' $trustPath
                Assert-DerivedFailure 'missing outcome' @{
                    sampled = @('c1'); results = @(@{ chunkId = 'c1'; provenance = 'verified' })
                } 'missing-outcome' 'c1' $trustPath
                Assert-DerivedFailure 'empty outcome' @{
                    sampled = @('c1'); results = @(@{ chunkId = 'c1'; outcome = ''; provenance = 'verified' })
                } 'missing-outcome' 'c1' $trustPath
                Assert-DerivedFailure 'absent chunk' @{
                    sampled = @('c1', 'c2')
                    results = @(@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified' })
                } 'missing-outcome' 'c2' $trustPath
                Assert-DerivedFailure 'unexpected skip' @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; unexpectedSkip = $true })
                } 'unexpected-skip' 'c1' $trustPath
                Assert-DerivedFailure 'skipped outcome' @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'skipped'; provenance = 'verified' })
                } 'unexpected-skip' 'c1' $trustPath
                Assert-DerivedFailure 'provenance' @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'failed' })
                } 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'provenance flag' @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; provenanceFailure = $true })
                } 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'failed outcome' @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'failed'; provenance = 'verified' })
                } 'sample-outcome' 'c1' $trustPath
                $cleanEvidence = @{
                    sampled = @('c1')
                    results = @(@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; unexpectedSkip = $false })
                }
                $clean = Get-RcShadowTrustDecision -Carries @('c1') -Evidence $cleanEvidence -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($clean.carriesInvalidated -eq $false) 'clean sample invalidated carries'
                Note ($clean.reason -ceq 'pilot-count') ('clean reason=' + $clean.reason)
                Note ($clean.trustGranted -eq $false -and $clean.gateCredit -eq $false -and $clean.effectiveMode -ceq 'Full' -and $clean.incrementalValidation -ceq 'Shadow') 'clean sample granted trust'
                Note ($clean.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) 'clean sample wrote a trust record'
                $caller = @{ chunkId = 'c1'; outcome = 'failed'; receipt = 'receipt-1' }
                $flag = Get-RcShadowTrustDecision -Carries @('c9', 'c8') -SampleFailure $caller -Evidence $cleanEvidence -TrustRecordPath $trustPath
                Note ($flag.carriesInvalidated -eq $true) 'caller failure left carries valid'
                Note ((Join-Ids $flag.reasons) -ceq 'sample-failed') ('caller reasons=' + (Join-Ids $flag.reasons))
                Note ((Join-Ids $flag.invalidatedCarries) -ceq 'c8|c9') ('caller carries=' + (Join-Ids $flag.invalidatedCarries))
                Note ((Get-RcShadowField $flag.preservedSampleFailure 'outcome') -ceq 'failed') 'caller failure was replaced'
                Note ($flag.trustGranted -eq $false -and $flag.effectiveMode -ceq 'Full' -and $flag.gateCredit -eq $false -and $flag.incrementalValidation -ceq 'Shadow') 'caller failure granted trust'
                $both = Get-RcShadowTrustDecision -Carries @('c1') -SampleFailure $caller -Evidence @{ sampled = @('c1'); results = @() }
                Note ((Join-Ids $both.reasons) -ceq 'sample-failed') ('duplicate sample-failed reasons=' + (Join-Ids $both.reasons))
                Note ((Get-RcShadowField $both.preservedSampleFailure 'outcome') -ceq 'failed') 'derived failure replaced the caller flag'
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'NonBooleanProvenanceFailureInvalidates' {
            # D-1. Only boolean false is a clean flag. A present string, number,
            # null, or object is a failure, including after a JSON round-trip.
            # unexpectedSkip uses the same rule.
            # B-1. A one-element array is not boolean false. [false] and @($false)
            # must fail closed, and so must [true], [], and [[false]]. An array
            # outcome, provenance, or chunkId is not the scalar type.
            # A-1. A wrong-case flag key fails closed. It is not a missing flag.
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                Assert-DerivedFailure 'string provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' 'true') 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'string false provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' 'false') 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'number provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' 1) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'number zero provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' 0) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'null provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' $null) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'object provenanceFailure' (New-ClosedFlagEvidence 'provenanceFailure' ([pscustomobject]@{ bad = $true })) 'provenance-failure' 'c1' $trustPath
                $jsonString = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":"true"}]}'
                $jsonNumber = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":1}]}'
                $jsonNull = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":null}]}'
                $jsonObject = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":{"bad":true}}]}'
                Assert-DerivedFailure 'json string provenanceFailure' ($jsonString | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'json number provenanceFailure' ($jsonNumber | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'json null provenanceFailure' ($jsonNull | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'json object provenanceFailure' ($jsonObject | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'string unexpectedSkip' (New-ClosedFlagEvidence 'unexpectedSkip' 'true') 'unexpected-skip' 'c1' $trustPath
                Assert-DerivedFailure 'number unexpectedSkip' (New-ClosedFlagEvidence 'unexpectedSkip' 1) 'unexpected-skip' 'c1' $trustPath
                Assert-DerivedFailure 'null unexpectedSkip' (New-ClosedFlagEvidence 'unexpectedSkip' $null) 'unexpected-skip' 'c1' $trustPath
                Assert-DerivedFailure 'object unexpectedSkip' (New-ClosedFlagEvidence 'unexpectedSkip' ([pscustomobject]@{ bad = $true })) 'unexpected-skip' 'c1' $trustPath
                $clean = Get-RcShadowTrustDecision -Carries @('c1') -Evidence (New-ClosedFlagEvidence 'provenanceFailure' $false) -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($clean.carriesInvalidated -eq $false) 'boolean false provenanceFailure invalidated carries'
                Note ($clean.reason -ceq 'pilot-count') ('boolean false provenance reason=' + $clean.reason)
                Note ($clean.trustGranted -eq $false -and $clean.gateCredit -eq $false -and $clean.effectiveMode -ceq 'Full' -and $clean.incrementalValidation -ceq 'Shadow') 'boolean false provenance granted trust'
                $jsonFalse = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":false}]}'
                $cleanJson = Get-RcShadowTrustDecision -Carries @('c1') -Evidence ($jsonFalse | ConvertFrom-Json) -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($cleanJson.carriesInvalidated -eq $false) 'json boolean false provenanceFailure invalidated carries'
                Note ($cleanJson.reason -ceq 'pilot-count') ('json false provenance reason=' + $cleanJson.reason)
                Note ($cleanJson.trustGranted -eq $false -and $cleanJson.gateCredit -eq $false -and $cleanJson.effectiveMode -ceq 'Full' -and $cleanJson.incrementalValidation -ceq 'Shadow') 'json false provenance granted trust'
                foreach ($flagName in @('provenanceFailure', 'unexpectedSkip')) {
                    $flagKind = $(if ($flagName -ceq 'unexpectedSkip') { 'unexpected-skip' } else { 'provenance-failure' })
                    Assert-DerivedFailure ('hashtable [false] ' + $flagName) (New-ClosedFlagEvidence $flagName @($false)) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('hashtable [true] ' + $flagName) (New-ClosedFlagEvidence $flagName @($true)) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('hashtable [] ' + $flagName) (New-ClosedFlagEvidence $flagName @()) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('hashtable [[false]] ' + $flagName) (New-ClosedFlagEvidence $flagName @(,@($false))) $flagKind 'c1' $trustPath
                    $jsonFalseArray = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","' + $flagName + '":[false]}]}'
                    $jsonTrueArray = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","' + $flagName + '":[true]}]}'
                    $jsonEmptyArray = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","' + $flagName + '":[]}]}'
                    $jsonNestedArray = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","' + $flagName + '":[[false]]}]}'
                    Assert-DerivedFailure ('json [false] ' + $flagName) ($jsonFalseArray | ConvertFrom-Json) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('json [true] ' + $flagName) ($jsonTrueArray | ConvertFrom-Json) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('json [] ' + $flagName) ($jsonEmptyArray | ConvertFrom-Json) $flagKind 'c1' $trustPath
                    Assert-DerivedFailure ('json [[false]] ' + $flagName) ($jsonNestedArray | ConvertFrom-Json) $flagKind 'c1' $trustPath
                }
                Assert-DerivedFailure 'outcome [passed]' (New-ClosedFlagEvidence 'outcome' @('passed')) 'missing-outcome' 'c1' $trustPath
                Assert-DerivedFailure 'provenance [verified]' (New-ClosedFlagEvidence 'provenance' @('verified')) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'chunkId [c1]' (New-ClosedFlagEvidence 'chunkId' @('c1')) 'unreadable-chunk-id' '' $trustPath
                $jsonOutcome = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":["passed"],"provenance":"verified"}]}'
                $jsonProvenance = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":["verified"]}]}'
                $jsonChunk = '{"sampled":["c1"],"results":[{"chunkId":["c1"],"outcome":"passed","provenance":"verified"}]}'
                Assert-DerivedFailure 'json outcome [passed]' ($jsonOutcome | ConvertFrom-Json) 'missing-outcome' 'c1' $trustPath
                Assert-DerivedFailure 'json provenance [verified]' ($jsonProvenance | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'json chunkId [c1]' ($jsonChunk | ConvertFrom-Json) 'unreadable-chunk-id' '' $trustPath
                $wrongTrue = [pscustomobject]@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; ProvenanceFailure = $true }
                $wrongFalse = [pscustomobject]@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; ProvenanceFailure = $false }
                $wrongSkip = [pscustomobject]@{ chunkId = 'c1'; outcome = 'passed'; provenance = 'verified'; UnexpectedSkip = $true }
                Assert-DerivedFailure 'wrong-case ProvenanceFailure true' @{ sampled = @('c1'); results = @($wrongTrue) } 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'wrong-case ProvenanceFailure false' @{ sampled = @('c1'); results = @($wrongFalse) } 'provenance-failure' 'c1' $trustPath
                Assert-DerivedFailure 'wrong-case UnexpectedSkip true' @{ sampled = @('c1'); results = @($wrongSkip) } 'unexpected-skip' 'c1' $trustPath
                $jsonWrong = '{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","ProvenanceFailure":true}]}'
                Assert-DerivedFailure 'json wrong-case ProvenanceFailure' ($jsonWrong | ConvertFrom-Json) 'provenance-failure' 'c1' $trustPath
                $bothFlags = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                $bothFlags['chunkId'] = 'c1'
                $bothFlags['outcome'] = 'passed'
                $bothFlags['provenance'] = 'verified'
                $bothFlags['provenanceFailure'] = $false
                $bothFlags['ProvenanceFailure'] = $true
                Assert-DerivedFailure 'ordinal both flag cases' @{ sampled = @('c1'); results = @($bothFlags) } 'provenance-failure' 'c1' $trustPath
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'CarriedChunkResultInvalidatesCarries' {
            # D-2. A carry is not launched unless sampled names it. Any result
            # row for another carry is a contradiction, including a passed row,
            # and invalidates every carry. A dirty unsampled row stays dropped.
            # B-2. Once sampled or results is present, a row whose chunkId is
            # missing, null, empty, whitespace, a number, an object, or an array
            # fails closed as unreadable-chunk-id, whatever its outcome.
            # A-3. A non-string -Carries entry fails closed. It is not skipped.
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                $passedSample = @{ chunkId = 's1'; outcome = 'passed'; provenance = 'verified' }
                $failedCarry = @{
                    sampled = @('s1')
                    results = @(
                        $passedSample
                        @{ chunkId = 'c2'; outcome = 'failed'; provenance = 'verified' }
                    )
                }
                $failed = Get-RcShadowTrustDecision -Carries @('c1', 'c2') -Evidence $failedCarry -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($failed.carriesInvalidated -eq $true) ('failed carried row left carries valid reason=' + $failed.reason)
                Note ((Join-Ids $failed.invalidatedCarries) -ceq 'c1|c2') ('failed carried carries=' + (Join-Ids $failed.invalidatedCarries))
                Note ((Join-Ids $failed.reasons) -ceq 'sample-failed') ('failed carried reasons=' + (Join-Ids $failed.reasons))
                Note ((Get-RcShadowField $failed.preservedSampleFailure 'kind') -ceq 'carried-result') ('failed carried kind=' + (Get-RcShadowField $failed.preservedSampleFailure 'kind'))
                Note ((Get-RcShadowField $failed.preservedSampleFailure 'chunkId') -ceq 'c2') ('failed carried chunk=' + (Get-RcShadowField $failed.preservedSampleFailure 'chunkId'))
                Note ($failed.trustGranted -eq $false -and $failed.gateCredit -eq $false -and $failed.effectiveMode -ceq 'Full' -and $failed.incrementalValidation -ceq 'Shadow') 'failed carried row granted trust'
                Note ($failed.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) 'failed carried row wrote a trust record'
                $passedCarry = @{
                    sampled = @('s1')
                    results = @(
                        $passedSample
                        @{ chunkId = 'c2'; outcome = 'passed'; provenance = 'verified' }
                    )
                }
                $passed = Get-RcShadowTrustDecision -Carries @('c1', 'c2') -Evidence $passedCarry -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($passed.carriesInvalidated -eq $true) ('passed carried row left carries valid reason=' + $passed.reason)
                Note ((Join-Ids $passed.invalidatedCarries) -ceq 'c1|c2') ('passed carried carries=' + (Join-Ids $passed.invalidatedCarries))
                Note ((Join-Ids $passed.reasons) -ceq 'sample-failed') ('passed carried reasons=' + (Join-Ids $passed.reasons))
                Note ((Get-RcShadowField $passed.preservedSampleFailure 'kind') -ceq 'carried-result') ('passed carried kind=' + (Get-RcShadowField $passed.preservedSampleFailure 'kind'))
                Note ((Get-RcShadowField $passed.preservedSampleFailure 'chunkId') -ceq 'c2') ('passed carried chunk=' + (Get-RcShadowField $passed.preservedSampleFailure 'chunkId'))
                Note ($passed.trustGranted -eq $false -and $passed.gateCredit -eq $false -and $passed.effectiveMode -ceq 'Full' -and $passed.incrementalValidation -ceq 'Shadow') 'passed carried row granted trust'
                $dirty = @{
                    sampled = @('s1')
                    results = @(
                        $passedSample
                        @{ chunkId = 'dirty'; outcome = 'failed'; provenance = 'verified' }
                    )
                }
                $dropped = Get-RcShadowTrustDecision -Carries @('c1', 'c2') -Evidence $dirty -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($dropped.carriesInvalidated -eq $false) ('dirty unsampled row invalidated carries reason=' + $dropped.reason)
                Note ($dropped.reason -ceq 'pilot-count') ('dirty unsampled reason=' + $dropped.reason)
                $launched = @{
                    sampled = @('s1')
                    results = @($passedSample)
                }
                $kept = Get-RcShadowTrustDecision -Carries @('s1', 'c2') -Evidence $launched -CurrentSample 'roster' -TrustRecordPath $trustPath
                Note ($kept.carriesInvalidated -eq $false) ('launched carry sample invalidated carries reason=' + $kept.reason)
                Note ($kept.reason -ceq 'pilot-count') ('launched carry reason=' + $kept.reason)
                Note ($kept.trustGranted -eq $false -and $kept.gateCredit -eq $false -and $kept.effectiveMode -ceq 'Full' -and $kept.incrementalValidation -ceq 'Shadow') 'launched carry sample granted trust'
                $badRows = @(
                    @{ label = 'missing chunkId'; row = '{"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'null chunkId'; row = '{"chunkId":null,"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'empty chunkId'; row = '{"chunkId":"","outcome":"failed","provenance":"verified"}' }
                    @{ label = 'whitespace chunkId'; row = '{"chunkId":" ","outcome":"failed","provenance":"verified"}' }
                    @{ label = 'number chunkId'; row = '{"chunkId":1,"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'object chunkId'; row = '{"chunkId":{"bad":true},"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'array chunkId'; row = '{"chunkId":["other"],"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'multi array chunkId'; row = '{"chunkId":["c2","c3"],"outcome":"failed","provenance":"verified"}' }
                    @{ label = 'passed number chunkId'; row = '{"chunkId":2,"outcome":"passed","provenance":"verified"}' }
                    @{ label = 'flag with null chunkId'; row = '{"chunkId":null,"outcome":"passed","provenance":"verified","provenanceFailure":true}' }
                )
                foreach ($bad in $badRows) {
                    $json = '{"sampled":["s1"],"results":[{"chunkId":"s1","outcome":"passed","provenance":"verified"},' + $bad.row + ']}'
                    Assert-DerivedFailure ('json ' + $bad.label) ($json | ConvertFrom-Json) 'unreadable-chunk-id' '' $trustPath
                }
                $resultsOnly = '{"results":[{"chunkId":1,"outcome":"passed","provenance":"verified"}]}'
                Assert-DerivedFailure 'results-only passed number chunkId' ($resultsOnly | ConvertFrom-Json) 'unreadable-chunk-id' '' $trustPath
                $resultsOnlyFailed = '{"results":[{"outcome":"failed","provenance":"verified"}]}'
                Assert-DerivedFailure 'results-only missing chunkId' ($resultsOnlyFailed | ConvertFrom-Json) 'unreadable-chunk-id' '' $trustPath
                $hashtableMissing = @{
                    sampled = @('s1')
                    results = @(
                        @{ chunkId = 's1'; outcome = 'passed'; provenance = 'verified' }
                        @{ outcome = 'failed'; provenance = 'verified' }
                    )
                }
                Assert-DerivedFailure 'hashtable missing chunkId' $hashtableMissing 'unreadable-chunk-id' '' $trustPath
                foreach ($carryCase in @(
                    @{ label = 'integer carry'; carries = @('c1', 2) }
                    @{ label = 'object carry'; carries = @([pscustomobject]@{ id = 'c9' }) }
                )) {
                    $threw = $false
                    $throwMessage = ''
                    try {
                        $null = Get-RcShadowTrustDecision -Carries $carryCase.carries -Evidence $launched -CurrentSample 'roster' -TrustRecordPath $trustPath
                    } catch {
                        $threw = $true
                        $throwMessage = [string]$_.Exception.Message
                    }
                    Note $threw ($carryCase.label + ' was skipped')
                    Note ($throwMessage -ceq 'shadow-carry-id') ($carryCase.label + ' message=' + $throwMessage)
                }
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        'AbsentEvidenceKeepsCarriesUntilWired' {
            # F-1 precondition, current behavior. S6c/S6d must flip this case
            # before the library decides a carry: absent evidence stays valid,
            # and a pilot with no cchunk or stratumCount stays eligible at 30.
            $root = New-TempRoot
            try {
                $trustPath = Join-Path $root 'trust-record.json'
                $absent = @(
                    @{ label = 'null evidence'; evidence = $null; sample = 'roster' }
                    @{ label = 'empty evidence'; evidence = @{}; sample = 'roster' }
                    @{ label = 'string evidence'; evidence = 'not-a-record'; sample = 'executed' }
                    @{ label = 'wrong-case keys'; evidence = @{ Sampled = @('c1'); Results = @() }; sample = 'executed' }
                )
                foreach ($row in $absent) {
                    $decision = Get-RcShadowTrustDecision -Carries @('c1', 'c2') -Evidence $row.evidence -CurrentSample $row.sample -TrustRecordPath $trustPath
                    Note ($decision.carriesInvalidated -eq $false) ($row.label + ' invalidated carries reason=' + $decision.reason)
                    Note ((Join-Ids $decision.invalidatedCarries) -ceq '') ($row.label + ' carries=' + (Join-Ids $decision.invalidatedCarries))
                    Note ($decision.reason -ceq 'pilot-count') ($row.label + ' reason=' + $decision.reason)
                    Note ($null -eq $decision.preservedSampleFailure) ($row.label + ' preserved a sample failure')
                    Note ($decision.trustGranted -eq $false -and $decision.gateCredit -eq $false -and $decision.effectiveMode -ceq 'Full' -and $decision.incrementalValidation -ceq 'Shadow') ($row.label + ' granted trust')
                    Note ($decision.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) ($row.label + ' wrote a trust record')
                }
                $floor = @{
                    cutId = 'floor-1'; platform = 'windows'; sample = 'executed'; shadowPilot = $true
                    discrepancy = $false; openFailure = $false; nativeReceipts = 'current'; fullOverdue = $false
                    contractDigest = $script:Digest; n = 30; nExecuted = 30
                }
                Note (Test-RcShadowPilotEligible -Pilot $floor -ContractDigest $script:Digest) 'incomplete pilot without cchunk or stratumCount was ineligible'
                $second = @{
                    cutId = 'floor-2'; platform = 'windows'; sample = 'executed'; shadowPilot = $true
                    discrepancy = $false; openFailure = $false; nativeReceipts = 'current'; fullOverdue = $false
                    contractDigest = $script:Digest; n = 30; nExecuted = 30
                }
                $counted = Get-RcShadowTrustDecision -Pilots @($floor, $second) -Carries @('c1') -Evidence $null -CurrentSample 'executed' -ContractDigest $script:Digest -TrustRecordPath $trustPath
                Note ($counted.observedPilots -eq 2) ('incomplete pilots observed=' + $counted.observedPilots)
                Note ($counted.carriesInvalidated -eq $false) ('incomplete pilots invalidated carries reason=' + $counted.reason)
                Note ($counted.reason -ceq 'operator-trust-required') ('incomplete pilots reason=' + $counted.reason)
                Note ($counted.trustGranted -eq $false -and $counted.gateCredit -eq $false -and $counted.effectiveMode -ceq 'Full' -and $counted.incrementalValidation -ceq 'Shadow') 'incomplete pilots granted trust'
                Note ($counted.trustRecordWritten -eq $false -and -not [IO.File]::Exists($trustPath)) 'incomplete pilots wrote a trust record'
            } finally {
                if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
            }
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch { Note $false ('unexpected harness error: ' + $_.Exception.Message) }
Assert-C487 ($script:Errors.Count -eq 0) ('RS ' + $Case + '.contract') ($script:Errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
