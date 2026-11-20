#requires -Version 7.0
# CARD-1836 S8b. Shadow proposal only.
# Persists a fixed-seed sample roster before launch. Does not launch, publish,
# promote, write a trust record, or change IncrementalValidation from Shadow.
# effectiveMode stays Full and gateCredit stays false.
# Port this library to compiled C# before it is wired into S6c/S6d. Strict types remove this PowerShell coercion class.
# A present provenanceFailure or unexpectedSkip that is not the single boolean
# false is a failure. That includes a string, a number, null, an object, and
# any array, including a one-element array of false. Boolean false is clean,
# and a missing flag is clean. A case-variant of either flag name fails closed.
# A carry is not launched unless sampled names it. A result row for a carry id
# outside sampled fails closed as carried-result, including a passed row, and
# invalidates every carry. A carry id listed in sampled is the launched sample
# and follows the ordinary row rules. An unsampled row that is not a carry
# stays dropped. Once sampled or results is present, a row whose chunkId is
# not a non-empty string fails closed as unreadable-chunk-id, whatever its
# outcome. A non-string carry entry fails closed. It is not skipped.
# PRECONDITION for S6c/S6d: before this library feeds any carry decision, absent
# or unrecognised sample evidence must invalidate every carry when Carries is
# nonempty or CurrentSample is roster or executed. A pilot without cchunk and
# stratumCount must be ineligible. This slice does not do that.
# AbsentEvidenceKeepsCarriesUntilWired records the current fail-open so a later
# slice must flip it deliberately. A present sampled or results key still fails
# closed. Do not describe absent evidence as fail-closed.
. (Join-Path $PSScriptRoot 'rc-forced-full.ps1')

function Get-RcShadowField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        # get_Keys: a dictionary entry named Keys hides the Keys property.
        foreach ($key in @($Object.get_Keys())) {
            if ($key -is [string] -and [string]::Equals($key, $Name, [StringComparison]::Ordinal)) {
                # The comma keeps a one-element array from unrolling to its element.
                $value = $Object[$key]
                return ,$value
            }
        }
        return $null
    }
    if ($Object -is [pscustomobject]) {
        foreach ($property in @($Object.PSObject.Properties)) {
            if ([string]::Equals($property.Name, $Name, [StringComparison]::Ordinal)) {
                $value = $property.Value
                return ,$value
            }
        }
    }
    return $null
}

function Get-RcShadowNames {
    param($Object)
    $names = [Collections.Generic.List[string]]::new()
    if ($Object -is [Collections.IDictionary]) {
        foreach ($key in @($Object.get_Keys())) {
            if ($key -is [string]) { $names.Add($key) }
        }
    } elseif ($Object -is [pscustomobject]) {
        foreach ($property in @($Object.PSObject.Properties)) {
            $names.Add([string]$property.Name)
        }
    }
    return ,$names.ToArray()
}

function Test-RcShadowEmptyCollection {
    param($Value)
    if ($null -eq $Value) { return $true }
    # A blank string yields no ids or rows. Any other string is one scalar.
    if ($Value -is [string]) { return [string]::IsNullOrWhiteSpace($Value) }
    # A dictionary is not an id list. Its Count key must not decide emptiness.
    if ($Value -is [Collections.IDictionary]) { return $false }
    # List, ArrayList, array, and any other non-dictionary collection.
    if ($Value -is [Collections.ICollection]) { return $Value.get_Count() -eq 0 }
    return $false
}

function Test-RcShadowReadableChunkId {
    param($Value)
    return ($Value -is [string]) -and -not [string]::IsNullOrWhiteSpace($Value)
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
        # Unknown carryability is dirty. Only an explicit true is sampled.
        completelyCarryable = ($carry -is [bool] -and $carry)
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
    $stratumCount = $strata.get_Count()
    $sampleCount = Get-RcShadowSampleCount $cchunk $stratumCount
    $selected = [Collections.Generic.List[object]]::new()
    if ($cchunk -gt 0) {
        $pickedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($key in @($strata.get_Keys())) {
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

function Test-RcShadowDigest {
    param($Value)
    return ($Value -is [string]) -and ($Value -cmatch '\A[0-9a-f]{64}\z')
}

function Test-RcShadowWholeNumber {
    param($Value)
    if ($Value -isnot [int] -and $Value -isnot [long]) { return $false }
    $number = [long]$Value
    return $number -ge 0 -and $number -le [int]::MaxValue
}

function Test-RcShadowClosedFlag {
    param($Row, [string]$Name)
    # Missing is clean. The single boolean false is clean. Any other present
    # value fails, including an array whose only element is false. A case-variant
    # of the name fails closed even when the exact key is boolean false.
    $exact = $false
    $variant = $false
    # Assign the name array. Wrapping the call in @() keeps that array as one item.
    $flagNames = Get-RcShadowNames $Row
    foreach ($key in $flagNames) {
        if ([string]::Equals($key, $Name, [StringComparison]::Ordinal)) {
            $exact = $true
        } elseif ([string]::Equals($key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            $variant = $true
        }
    }
    if ($variant) { return $true }
    if (-not $exact) { return $false }
    $value = Get-RcShadowField $Row $Name
    return -not ($value -is [bool] -and -not $value)
}

function Test-RcShadowResultRow {
    param($Row, [string]$ChunkId)
    $outcome = Get-RcShadowField $Row 'outcome'
    if ((Test-RcShadowClosedFlag $Row 'unexpectedSkip') -or ($outcome -is [string] -and $outcome -ceq 'skipped')) {
        return [pscustomobject]@{ kind = 'unexpected-skip'; chunkId = $ChunkId }
    }
    if ($outcome -isnot [string] -or [string]::IsNullOrEmpty($outcome)) {
        return [pscustomobject]@{ kind = 'missing-outcome'; chunkId = $ChunkId }
    }
    $provenance = Get-RcShadowField $Row 'provenance'
    if ((Test-RcShadowClosedFlag $Row 'provenanceFailure') -or $provenance -isnot [string] -or $provenance -cne 'verified') {
        return [pscustomobject]@{ kind = 'provenance-failure'; chunkId = $ChunkId }
    }
    if ($outcome -cne 'passed') {
        return [pscustomobject]@{ kind = 'sample-outcome'; chunkId = $ChunkId }
    }
    return $null
}

function Get-RcShadowSampleEvidenceFailure {
    param($Evidence, [object[]]$Carries = @())
    $carried = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($carry in @($Carries)) {
        if ($null -ne $carry -and $carry -isnot [string]) { throw 'shadow-carry-id' }
        if ($carry -is [string] -and -not [string]::IsNullOrEmpty($carry)) {
            [void]$carried.Add($carry)
        }
    }
    $sawSampled = Test-RcShadowKey $Evidence 'sampled'
    $sawResults = Test-RcShadowKey $Evidence 'results'
    if (-not $sawSampled -and -not $sawResults) { return $null }
    $ids = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($sawSampled) {
        $sampled = Get-RcShadowField $Evidence 'sampled'
        if (Test-RcShadowEmptyCollection $sampled) {
            return [pscustomobject]@{ kind = 'empty-results'; chunkId = '' }
        }
        foreach ($id in @($sampled)) {
            if ($id -isnot [string] -or [string]::IsNullOrEmpty($id)) {
                return [pscustomobject]@{ kind = 'missing-outcome'; chunkId = '' }
            }
            if (-not $seen.Add($id)) {
                return [pscustomobject]@{ kind = 'provenance-failure'; chunkId = $id }
            }
            $ids.Add($id)
        }
        # A present sampled key that produced no ids is empty, whatever the container.
        if ($ids.Count -eq 0) {
            return [pscustomobject]@{ kind = 'empty-results'; chunkId = '' }
        }
    }
    $rows = [Collections.Generic.List[object]]::new()
    if ($sawResults) {
        $results = Get-RcShadowField $Evidence 'results'
        if (Test-RcShadowEmptyCollection $results) {
            $chunkId = ''
            if ($ids.Count -gt 0) { $chunkId = [string]$ids[0] }
            return [pscustomobject]@{ kind = 'empty-results'; chunkId = $chunkId }
        }
        foreach ($row in @($results)) {
            if ($null -eq $row) {
                return [pscustomobject]@{ kind = 'missing-outcome'; chunkId = '' }
            }
            $rowId = Get-RcShadowField $row 'chunkId'
            if (-not (Test-RcShadowReadableChunkId $rowId)) {
                return [pscustomobject]@{ kind = 'unreadable-chunk-id'; chunkId = '' }
            }
            $rows.Add($row)
        }
        # A present results key that produced no rows is empty, whatever the container.
        if ($rows.Count -eq 0) {
            $chunkId = ''
            if ($ids.Count -gt 0) { $chunkId = [string]$ids[0] }
            return [pscustomobject]@{ kind = 'empty-results'; chunkId = $chunkId }
        }
    }
    # A result for a carry that this roster did not launch is a contradiction.
    # A passed row fails closed too. A carry listed in sampled stays ordinary.
    $launched = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in $ids) { [void]$launched.Add($id) }
    foreach ($row in $rows) {
        $rowId = Get-RcShadowField $row 'chunkId'
        if (-not (Test-RcShadowReadableChunkId $rowId)) {
            return [pscustomobject]@{ kind = 'unreadable-chunk-id'; chunkId = '' }
        }
        if (-not $carried.Contains($rowId)) { continue }
        if ($sawSampled -and $launched.Contains($rowId)) { continue }
        return [pscustomobject]@{ kind = 'carried-result'; chunkId = $rowId }
    }
    if ($ids.Count -gt 0 -and $rows.Count -eq 0) {
        return [pscustomobject]@{ kind = 'empty-results'; chunkId = [string]$ids[0] }
    }
    if ($ids.Count -eq 0) {
        # Reaching here with a present key and nothing collected is still empty-results.
        if ($rows.Count -eq 0) {
            return [pscustomobject]@{ kind = 'empty-results'; chunkId = '' }
        }
        foreach ($row in $rows) {
            $rowId = Get-RcShadowField $row 'chunkId'
            $label = ''
            if ($rowId -is [string]) { $label = $rowId }
            $failure = Test-RcShadowResultRow $row $label
            if ($null -ne $failure) { return $failure }
        }
        return $null
    }
    foreach ($id in $ids) {
        $matches = [Collections.Generic.List[object]]::new()
        foreach ($row in $rows) {
            $rowId = Get-RcShadowField $row 'chunkId'
            if ($rowId -is [string] -and [string]::Equals($rowId, $id, [StringComparison]::Ordinal)) {
                $matches.Add($row)
            }
        }
        if ($matches.Count -eq 0) {
            return [pscustomobject]@{ kind = 'missing-outcome'; chunkId = $id }
        }
        if ($matches.Count -gt 1) {
            return [pscustomobject]@{ kind = 'provenance-failure'; chunkId = $id }
        }
        $failure = Test-RcShadowResultRow $matches[0] $id
        if ($null -ne $failure) { return $failure }
    }
    return $null
}

function Test-RcShadowKey {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $false }
    if ($Object -is [Collections.IDictionary]) {
        foreach ($key in @($Object.get_Keys())) {
            if ($key -is [string] -and [string]::Equals($key, $Name, [StringComparison]::Ordinal)) { return $true }
        }
        return $false
    }
    if ($Object -is [pscustomobject]) {
        foreach ($property in @($Object.PSObject.Properties)) {
            if ([string]::Equals($property.Name, $Name, [StringComparison]::Ordinal)) { return $true }
        }
    }
    return $false
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
    # An empty or missing digest is unproven. Only a 64-character lowercase hex digest can match.
    if (-not (Test-RcShadowDigest $digest) -or -not (Test-RcShadowDigest $ContractDigest) -or ($digest -cne $ContractDigest)) { return $false }
    $planned = Get-RcShadowField $Pilot 'n'
    $population = Get-RcShadowField $Pilot 'cchunk'
    $strata = Get-RcShadowField $Pilot 'stratumCount'
    if (-not (Test-RcShadowWholeNumber $planned) -or -not (Test-RcShadowWholeNumber $executed)) { return $false }
    $plannedN = [int]$planned
    $executedN = [int]$executed
    # Without a recorded population the D-8 floor is 30. A recorded population recomputes N.
    $minimum = 30
    if ($null -ne $population -or $null -ne $strata) {
        if (-not (Test-RcShadowWholeNumber $population) -or -not (Test-RcShadowWholeNumber $strata)) { return $false }
        $populationN = [int]$population
        $strataN = [int]$strata
        $minimum = Get-RcShadowSampleCount $populationN $strataN
    }
    if ($minimum -lt 1 -or $plannedN -lt $minimum -or $executedN -lt $plannedN -or $executedN -lt $minimum) { return $false }
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
    # The caller flag can only add a failure. A present sampled or results key
    # fails closed. Absent evidence still leaves carries valid until S6c/S6d.
    $derivedFailure = Get-RcShadowSampleEvidenceFailure -Evidence $Evidence -Carries $Carries
    if ($null -ne $SampleFailure -or $null -ne $derivedFailure) { [void]$reasons.Add('sample-failed') }
    if ($RosterChanged) { [void]$reasons.Add('sample-roster-changed') }
    $preservedFailure = $SampleFailure
    if ($null -eq $preservedFailure) { $preservedFailure = $derivedFailure }
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
        preservedSampleFailure = $preservedFailure
        expandedToFull = $false
        trustRecordWritten = $false
        shadowPilot = $false
    }
}
