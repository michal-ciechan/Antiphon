#requires -Version 7.0
# CARD-1836 S8b. Shadow proposal only.
# Persists a fixed-seed sample roster before launch. Does not launch, publish,
# promote, write a trust record, or change IncrementalValidation from Shadow.
# effectiveMode stays Full and gateCredit stays false.
. (Join-Path $PSScriptRoot 'rc-forced-full.ps1')

function Get-RcShadowField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        foreach ($key in @($Object.Keys)) {
            if ($key -is [string] -and [string]::Equals($key, $Name, [StringComparison]::Ordinal)) {
                return $Object[$key]
            }
        }
        return $null
    }
    if ($Object -is [pscustomobject]) {
        foreach ($property in @($Object.PSObject.Properties)) {
            if ([string]::Equals($property.Name, $Name, [StringComparison]::Ordinal)) {
                return $property.Value
            }
        }
    }
    return $null
}

function Test-RcShadowText {
    param([string]$Value)
    return -not ($Value.Contains([char]10) -or $Value.Contains([char]13))
}

function ConvertTo-RcShadowChunk {
    param($Raw)
    $id = Get-RcShadowField $Raw 'id'
    $suite = Get-RcShadowField $Raw 'suite'
    $boundary = Get-RcShadowField $Raw 'boundary'
    if ($id -isnot [string] -or [string]::IsNullOrEmpty($id) -or -not (Test-RcShadowText $id)) { throw 'shadow-chunk-id' }
    if ($suite -isnot [string] -or [string]::IsNullOrEmpty($suite) -or -not (Test-RcShadowText $suite)) { throw 'shadow-chunk-suite' }
    if ($boundary -isnot [string] -or [string]::IsNullOrEmpty($boundary) -or -not (Test-RcShadowText $boundary)) { throw 'shadow-chunk-boundary' }
    $skip = Get-RcShadowField $Raw 'expectedSkipOnly'
    $carry = Get-RcShadowField $Raw 'completelyCarryable'
    if ($null -ne $skip -and $skip -isnot [bool]) { throw 'shadow-chunk-skip' }
    if ($null -ne $carry -and $carry -isnot [bool]) { throw 'shadow-chunk-carryable' }
    return [pscustomobject]@{
        id = $id
        suite = $suite
        boundary = $boundary
        expectedSkipOnly = ($skip -is [bool] -and $skip)
        completelyCarryable = ($null -eq $carry -or ($carry -is [bool] -and $carry))
    }
}

function Get-RcShadowSampleCount {
    param([int]$Cchunk, [int]$StratumCount)
    if ($Cchunk -le 0) { return 0 }
    # ceil(0.05 * Cchunk) in integer arithmetic, then D-8's max(30, that) cap.
    $fraction = [int][decimal]::Floor(([decimal]$Cchunk * 5 + 99) / 100)
    $base = [Math]::Min($Cchunk, [Math]::Max(30, $fraction))
    return [Math]::Min($Cchunk, [Math]::Max($base, $StratumCount))
}

function Get-RcShadowRank {
    param([string]$Seed, [string]$Purpose, [string]$Suite, [string]$Boundary, [string]$Id)
    $text = $Seed + "`n" + $Purpose + "`n" + $Suite + "`n" + $Boundary + "`n" + $Id
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Invoke-RcShadowSort {
    param([object[]]$Items, [string]$Seed, [string]$Purpose)
    $list = [Collections.Generic.List[object]]::new()
    foreach ($item in @($Items)) {
        if ($null -ne $item) { $list.Add($item) }
    }
    $list.Sort([Comparison[object]]{
        param($Left, $Right)
        $leftRank = Get-RcShadowRank $Seed $Purpose $Left.suite $Left.boundary $Left.id
        $rightRank = Get-RcShadowRank $Seed $Purpose $Right.suite $Right.boundary $Right.id
        $compared = [string]::CompareOrdinal($leftRank, $rightRank)
        if ($compared -ne 0) { return $compared }
        return [string]::CompareOrdinal([string]$Left.id, [string]$Right.id)
    })
    return ,$list.ToArray()
}

function Sort-RcShadowIdentity {
    param([object[]]$Items)
    $list = [Collections.Generic.List[object]]::new()
    foreach ($item in @($Items)) {
        if ($null -ne $item) { $list.Add($item) }
    }
    $list.Sort([Comparison[object]]{
        param($Left, $Right)
        $compared = [string]::CompareOrdinal([string]$Left.suite, [string]$Right.suite)
        if ($compared -ne 0) { return $compared }
        $compared = [string]::CompareOrdinal([string]$Left.boundary, [string]$Right.boundary)
        if ($compared -ne 0) { return $compared }
        return [string]::CompareOrdinal([string]$Left.id, [string]$Right.id)
    })
    return ,$list.ToArray()
}

function Assert-RcShadowExternalPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $prefix = $checkout + [IO.Path]::DirectorySeparatorChar
    if ($full.Equals($checkout, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'proposal-output-inside-checkout'
    }
    $current = $full
    for ($depth = 0; $current; $depth++) {
        if ($depth -ge 128) { throw 'proposal-path-depth' }
        if (([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and
            (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'proposal-linked-path'
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $full
}

function Write-RcShadowProposalFile {
    param([string]$Path, $Proposal)
    $full = Assert-RcShadowExternalPath $Path
    if ([IO.File]::Exists($full)) { throw 'write-once' }
    $directory = [IO.Path]::GetDirectoryName($full)
    if (-not [string]::IsNullOrEmpty($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    $json = ConvertTo-Json -InputObject $Proposal -Depth 20 -Compress
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream = [IO.File]::Open($full, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $read = [IO.File]::ReadAllBytes($full)
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($read)).ToLowerInvariant() -cne $digest) {
        throw 'proposal-readback'
    }
    return $digest
}

function New-RcShadowSeed {
    param([string]$Seed)
    if ([string]::IsNullOrEmpty($Seed)) {
        $bytes = [byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        return [Convert]::ToHexString($bytes).ToLowerInvariant()
    }
    if ($Seed -cnotmatch '\A[0-9a-f]{64}\z') { throw 'shadow-seed' }
    return $Seed
}

function New-RcShadowProposal {
    param(
        [object[]]$Chunks = @(),
        [string]$Seed = '',
        [string]$ProposalPath = ''
    )
    $parsed = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($raw in @($Chunks)) {
        if ($null -eq $raw) { continue }
        $chunk = ConvertTo-RcShadowChunk $raw
        if (-not $seen.Add($chunk.id)) { throw 'shadow-chunk-duplicate' }
        $parsed.Add($chunk)
    }
    $population = [Collections.Generic.List[object]]::new()
    $skips = [Collections.Generic.List[object]]::new()
    foreach ($chunk in $parsed) {
        if ($chunk.expectedSkipOnly) { $skips.Add($chunk) }
        elseif ($chunk.completelyCarryable) { $population.Add($chunk) }
    }
    $seed = New-RcShadowSeed $Seed
    $strata = [Collections.Generic.SortedDictionary[string, System.Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
    foreach ($chunk in $population) {
        $key = $chunk.suite + "`n" + $chunk.boundary
        if (-not $strata.ContainsKey($key)) { $strata[$key] = [Collections.Generic.List[object]]::new() }
        $strata[$key].Add($chunk)
    }
    $cchunk = $population.Count
    $stratumCount = $strata.Count
    $sampleCount = Get-RcShadowSampleCount $cchunk $stratumCount
    $selected = [Collections.Generic.List[object]]::new()
    if ($cchunk -gt 0) {
        $pickedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($key in @($strata.Keys)) {
            $ordered = Invoke-RcShadowSort -Items $strata[$key].ToArray() -Seed $seed -Purpose 'stratum'
            $best = $ordered[0]
            $selected.Add($best)
            [void]$pickedIds.Add([string]$best.id)
        }
        if ($selected.Count -lt $sampleCount) {
            $rest = [Collections.Generic.List[object]]::new()
            foreach ($chunk in $population) {
                if (-not $pickedIds.Contains([string]$chunk.id)) { $rest.Add($chunk) }
            }
            foreach ($chunk in (Invoke-RcShadowSort -Items $rest.ToArray() -Seed $seed -Purpose 'fill')) {
                if ($selected.Count -ge $sampleCount) { break }
                $selected.Add($chunk)
            }
        }
    }
    $canonical = Sort-RcShadowIdentity -Items $selected.ToArray()
    $selectedIds = [Collections.Generic.List[string]]::new()
    foreach ($chunk in $canonical) { $selectedIds.Add([string]$chunk.id) }
    $skipCanonical = Sort-RcShadowIdentity -Items $skips.ToArray()
    $skipIds = [Collections.Generic.List[string]]::new()
    foreach ($chunk in $skipCanonical) { $skipIds.Add([string]$chunk.id) }
    $applicable = $cchunk -gt 0
    $proposal = [ordered]@{
        format = 'antiphon-rc-shadow-proposal-v1'
        effectiveMode = 'Full'
        gateCredit = $false
        bindingState = 'proposal-only'
        incrementalValidation = 'Shadow'
        trustGranted = $false
        trustRecordWritten = $false
        pilotClaim = $false
        launched = $false
        expandedToFull = $false
        carriesInvalidated = $false
        sample = $(if ($applicable) { 'roster' } else { 'not-applicable' })
        shadowPilot = [bool]$applicable
        seed = $seed
        n = $sampleCount
        cchunk = $cchunk
        stratumCount = $stratumCount
        selected = $selectedIds.ToArray()
        expectedSkipOnly = $skipIds.ToArray()
    }
    if (-not [string]::IsNullOrWhiteSpace($ProposalPath)) {
        [void](Write-RcShadowProposalFile -Path $ProposalPath -Proposal $proposal)
    }
    return [pscustomobject]$proposal
}

function Test-RcShadowPilotEligible {
    param($Pilot, [string]$ContractDigest = '')
    if ($null -eq $Pilot) { return $false }
    $cut = Get-RcShadowField $Pilot 'cutId'
    $platform = Get-RcShadowField $Pilot 'platform'
    $sample = Get-RcShadowField $Pilot 'sample'
    $shadow = Get-RcShadowField $Pilot 'shadowPilot'
    $discrepancy = Get-RcShadowField $Pilot 'discrepancy'
    $open = Get-RcShadowField $Pilot 'openFailure'
    $receipts = Get-RcShadowField $Pilot 'nativeReceipts'
    $overdue = Get-RcShadowField $Pilot 'fullOverdue'
    $digest = Get-RcShadowField $Pilot 'contractDigest'
    $executed = Get-RcShadowField $Pilot 'nExecuted'
    if ($cut -isnot [string] -or [string]::IsNullOrEmpty($cut)) { return $false }
    if ($platform -cne 'windows') { return $false }
    if ($sample -cne 'executed') { return $false }
    if ($shadow -isnot [bool] -or -not $shadow) { return $false }
    if ($discrepancy -isnot [bool] -or $discrepancy) { return $false }
    if ($open -isnot [bool] -or $open) { return $false }
    if ($receipts -cne 'current') { return $false }
    if ($overdue -isnot [bool] -or $overdue) { return $false }
    if ($digest -cne $ContractDigest) { return $false }
    if (($executed -isnot [int] -and $executed -isnot [long]) -or [int]$executed -lt 1) { return $false }
    return $true
}

function Get-RcShadowCarryIds {
    param([object[]]$Carries)
    $ids = [Collections.Generic.List[string]]::new()
    foreach ($carry in @($Carries)) {
        if ($null -eq $carry) { continue }
        if ($carry -isnot [string] -or [string]::IsNullOrEmpty($carry)) { throw 'shadow-carry-id' }
        $ids.Add($carry)
    }
    $ids.Sort([StringComparer]::Ordinal)
    return ,$ids.ToArray()
}

function Get-RcShadowTrustDecision {
    param(
        $CurrentContract = $null,
        $BaselineContract = $null,
        $Evidence = $null,
        $FullAnchor = $null,
        [DateTimeOffset]$UtcNow = [DateTimeOffset]::MinValue,
        [object[]]$Pilots = @(),
        [object[]]$Carries = @(),
        $SampleFailure = $null,
        [bool]$RosterChanged = $false,
        $OperatorTrustRecord = $null,
        [string]$CurrentSample = '',
        [string]$ContractDigest = '',
        [string]$TrustRecordPath = ''
    )
    # TrustRecordPath is accepted so a caller can name where a record would go.
    # This library never creates it and never reads an operator record as approval.
    $reasons = [Collections.Generic.List[string]]::new()
    if ($null -ne $CurrentContract -or $null -ne $BaselineContract) {
        $clock = $UtcNow
        if ($clock -eq [DateTimeOffset]::MinValue) { $clock = [DateTimeOffset]::UtcNow }
        $decision = Get-RcForcedFullDecision -CurrentContract $CurrentContract -BaselineContract $BaselineContract -Evidence $Evidence -FullAnchor $FullAnchor -UtcNow $clock
        foreach ($reason in @($decision.reasons)) {
            if ([string]$reason -clike 'contract-*') { [void]$reasons.Add([string]$reason) }
        }
    }
    if ($null -ne $SampleFailure) { [void]$reasons.Add('sample-failed') }
    if ($RosterChanged) { [void]$reasons.Add('sample-roster-changed') }
    $reasons.Sort([StringComparer]::Ordinal)
    $invalidate = $reasons.Count -gt 0
    $invalidated = [string[]]::new(0)
    if ($invalidate) {
        # Assignment keeps the comma-returned array. @() would nest it, and
        # Join-Ids would then stringify the whole roster with spaces.
        $sorted = Get-RcShadowCarryIds -Carries $Carries
        $invalidated = [string[]]::new($sorted.Count)
        for ($i = 0; $i -lt $sorted.Count; $i++) { $invalidated[$i] = [string]$sorted[$i] }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $observed = 0
    foreach ($pilot in @($Pilots)) {
        if (-not (Test-RcShadowPilotEligible -Pilot $pilot -ContractDigest $ContractDigest)) { continue }
        $cut = [string](Get-RcShadowField $pilot 'cutId')
        if ($seen.Add($cut)) { $observed++ }
    }
    if ($invalidate) {
        $reason = ($reasons -join '|')
    } elseif ($observed -eq 0 -and ($CurrentSample -ceq 'not-applicable' -or [string]::IsNullOrEmpty($CurrentSample))) {
        $reason = 'not-a-shadow-pilot'
        [void]$reasons.Add($reason)
    } elseif ($observed -lt 2) {
        $reason = 'pilot-count'
        [void]$reasons.Add($reason)
    } elseif ($null -eq $OperatorTrustRecord) {
        $reason = 'operator-trust-required'
        [void]$reasons.Add($reason)
    } else {
        $reason = 'trust-transition-disabled'
        [void]$reasons.Add($reason)
    }
    return [pscustomobject]@{
        trustGranted = $false
        incrementalValidation = 'Shadow'
        effectiveMode = 'Full'
        gateCredit = $false
        reason = $reason
        reasons = $reasons.ToArray()
        observedPilots = $observed
        requiredPilots = 2
        carriesInvalidated = [bool]$invalidate
        invalidatedCarries = $invalidated
        preservedSampleFailure = $SampleFailure
        expandedToFull = $false
        trustRecordWritten = $false
        shadowPilot = $false
    }
}
