#requires -Version 5.1
# CARD-1836 S1. Schema antiphon-rc-ledger-v1: read, write, validate, and carry decisions.
# CARD-1842 S6. antiphon-rc-ledger-v2 is v1 plus optional, nullable chunk hostId and os.
# Set-RcLedgerDurations fills durations on either format; Set-RcLedgerHost needs v2.
# Read-RcLedger is default-deny. Duplicate names (case-insensitive), names outside the
# closed schema, and JSON values of the wrong type throw ledger-invalid before any carry.
# ASCII-only. Pure: no network, no git, no process launch. Windows PowerShell 5.1.

if ($script:RcLedgerLoaded) { return }
$script:RcLedgerLoaded = $true

$script:RcLedgerFormat = 'antiphon-rc-ledger-v1'
$script:RcLedgerFormatV2 = 'antiphon-rc-ledger-v2'
$script:RcLedgerHostFields = @('hostId', 'os')
$script:RcLedgerOutcomes = @('passed', 'failed', 'interrupted', 'not-run', 'unexpected-skip', 'expected-skip', 'unproven')

function ConvertTo-RcList {
    param($Value)
    $list = New-Object System.Collections.Generic.List[object]
    if ($null -eq $Value) { return $list }
    if ($Value -is [string]) { $list.Add($Value); return $list }
    $asList = $false
    if ($Value -is [System.Array]) { $asList = $true }
    elseif (($Value -is [System.Collections.IList]) -and -not ($Value -is [string])) { $asList = $true }
    if ($asList) {
        foreach ($item in $Value) { $list.Add($item) }
        return $list
    }
    $list.Add($Value)
    return $list
}

function Get-RcProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Test-RcHasProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $false }
    if ($Object -is [System.Collections.IDictionary]) { return [bool]$Object.Contains($Name) }
    return $null -ne $Object.PSObject.Properties[$Name]
}

function Test-RcAsciiText {
    param([string]$Text)
    if ($null -eq $Text) { return $false }
    foreach ($ch in $Text.ToCharArray()) {
        if ([int]$ch -gt 127) { return $false }
    }
    return $true
}

function Test-RcRequiredText {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    return (Test-RcAsciiText $Text)
}

function Test-RcDigestText {
    param([string]$Digest)
    if ([string]::IsNullOrEmpty($Digest) -or $Digest.Length -ne 64) { return $false }
    foreach ($ch in $Digest.ToCharArray()) {
        $hex = ($ch -ge '0' -and $ch -le '9') -or ($ch -ge 'a' -and $ch -le 'f')
        if (-not $hex) { return $false }
    }
    return $true
}

function Test-RcStoredDigest {
    param([string]$Digest)
    if ($Digest -ceq 'unknown') { return $true }
    return (Test-RcDigestText $Digest)
}

function Test-RcNumber {
    param($Value)
    if ($null -eq $Value) { return $false }
    return ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64] -or $Value -is [decimal] -or $Value -is [double] -or $Value -is [single])
}

function Test-RcDuration {
    param($Value)
    if ($null -eq $Value) { return $true }
    if (-not (Test-RcNumber $Value)) { return $false }
    return ([double]$Value -ge 0)
}

function Test-RcWholeNumber {
    param($Value, [int]$Min)
    if (-not (Test-RcNumber $Value)) { return $false }
    $number = [double]$Value
    if ($number -ne [math]::Truncate($number)) { return $false }
    if ($number -lt $Min -or $number -gt [int]::MaxValue) { return $false }
    return $true
}

function Test-RcUnknownBoundary {
    param([string]$Boundary)
    if ([string]::IsNullOrWhiteSpace($Boundary)) { return $true }
    if ($Boundary -ceq 'unknown' -or $Boundary -ceq 'unassigned') { return $true }
    return $false
}

function Get-RcIdentityKey {
    param($Identity)
    return ([string](Get-RcProp $Identity 'className')) + '/' + ([string](Get-RcProp $Identity 'method'))
}

function Get-RcOutcomeRank {
    param([string]$Outcome)
    switch ($Outcome) {
        'passed' { return 0 }
        'expected-skip' { return 1 }
        'not-run' { return 2 }
        'interrupted' { return 3 }
        'unproven' { return 4 }
        'unexpected-skip' { return 5 }
        'failed' { return 6 }
        default { return 100 }
    }
}

function Get-RcWorstOutcome {
    param($Outcomes)
    $best = $null
    $rank = -1
    foreach ($outcome in @(ConvertTo-RcList $Outcomes)) {
        $text = [string]$outcome
        $next = Get-RcOutcomeRank $text
        if ($next -gt $rank) { $rank = $next; $best = $text }
    }
    if ($null -eq $best) { return 'not-run' }
    return $best
}

function ConvertTo-RcJsonString {
    param([string]$Text)
    if ($null -eq $Text) { return '""' }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($code -gt 127 -or ($code -lt 32 -and $ch -ne "`n" -and $ch -ne "`r" -and $ch -ne "`t")) { throw 'rc-ledger-non-ascii' }
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($ch -eq "`t") { [void]$sb.Append('\t') }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

function ConvertTo-RcJsonNumber {
    param($Value)
    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { throw 'rc-ledger-bad-number' }
    $whole = [math]::Truncate($number)
    if ($number -eq $whole) {
        return ([long]$whole).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    return $number.ToString('G17', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-RcJsonStringArray {
    param($Values)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($value in @(ConvertTo-RcList $Values)) {
        $parts.Add((ConvertTo-RcJsonString ([string]$value)))
    }
    return '[' + ($parts.ToArray() -join ',') + ']'
}

function Sort-RcText {
    param($Values)
    $list = New-Object 'System.Collections.Generic.List[string]'
    $seen = @{}
    foreach ($value in @(ConvertTo-RcList $Values)) {
        $text = [string]$value
        if ($seen.ContainsKey($text)) { continue }
        $seen[$text] = $true
        $list.Add($text)
    }
    $list.Sort([System.StringComparer]::Ordinal)
    # An empty or one-item return would otherwise collapse on the way out.
    Write-Output -NoEnumerate -InputObject $list.ToArray()
}

function Compare-RcInputDigest {
    param([string]$Left, [string]$Right)
    $leftOk = Test-RcDigestText $Left
    $rightOk = Test-RcDigestText $Right
    if (-not $leftOk -or -not $rightOk) {
        return [pscustomobject]@{ relation = 'invalid'; equal = $false }
    }
    if ([string]::Equals($Left, $Right, [StringComparison]::Ordinal)) {
        return [pscustomobject]@{ relation = 'same'; equal = $true }
    }
    return [pscustomobject]@{ relation = 'different'; equal = $false }
}

function New-RcDecision {
    param([string]$Decision, [string]$Reason, [string]$Key, [string]$Outcome, $CarriedFrom)
    return [pscustomobject]@{
        decision = $Decision
        reason = $Reason
        identityKey = $Key
        outcome = $Outcome
        carriedFrom = $CarriedFrom
    }
}

function Test-RcRosterName {
    param([string]$Key, $Roster)
    foreach ($item in @(ConvertTo-RcList $Roster)) {
        if ([string]$item -ceq $Key) { return $true }
    }
    return $false
}

function Resolve-RcOutcome {
    param([string]$Outcome, [string]$Key, $Roster)
    if ($Outcome -ceq 'NotExecuted' -or $Outcome -ceq 'Skipped') {
        if (Test-RcRosterName $Key $Roster) { return 'expected-skip' }
        return 'unexpected-skip'
    }
    return $Outcome
}

function New-RcCarriedFrom {
    param($SourceLedger, $SourceIdentity, $SourceChunk)
    $existing = Get-RcProp $SourceIdentity 'carriedFrom'
    $runId = [string](Get-RcProp $existing 'runId')
    if (-not [string]::IsNullOrWhiteSpace($runId)) {
        return [pscustomobject]@{
            candidateId = [string](Get-RcProp $existing 'candidateId')
            runId = $runId
            candidateSha = [string](Get-RcProp $existing 'candidateSha')
            chunkId = [string](Get-RcProp $existing 'chunkId')
            inputDigest = [string](Get-RcProp $existing 'inputDigest')
        }
    }
    return [pscustomobject]@{
        candidateId = [string](Get-RcProp $SourceLedger 'candidateId')
        runId = [string](Get-RcProp $SourceLedger 'runId')
        candidateSha = [string](Get-RcProp $SourceLedger 'candidateSha')
        chunkId = [string](Get-RcProp $SourceChunk 'chunkId')
        inputDigest = [string](Get-RcProp $SourceIdentity 'inputDigest')
    }
}

function Test-RcCarryProvenance {
    param($CarriedFrom)
    if ($null -eq $CarriedFrom) { return $false }
    if (-not (Test-RcRequiredText ([string](Get-RcProp $CarriedFrom 'candidateId')))) { return $false }
    if (-not (Test-RcRequiredText ([string](Get-RcProp $CarriedFrom 'runId')))) { return $false }
    if (-not (Test-RcRequiredText ([string](Get-RcProp $CarriedFrom 'candidateSha')))) { return $false }
    if (-not (Test-RcRequiredText ([string](Get-RcProp $CarriedFrom 'chunkId')))) { return $false }
    return (Test-RcDigestText ([string](Get-RcProp $CarriedFrom 'inputDigest')))
}

function Test-RcSameDigestFlake {
    param($SourceIdentity, $History, [string]$NewDigest)
    $sawPass = $false
    $sawFail = $false
    $rows = New-Object System.Collections.Generic.List[object]
    if ($null -ne $SourceIdentity) { $rows.Add($SourceIdentity) }
    foreach ($item in @(ConvertTo-RcList $History)) {
        if ($null -ne $item) { $rows.Add($item) }
    }
    foreach ($item in $rows) {
        $digest = [string](Get-RcProp $item 'inputDigest')
        if (-not (Test-RcDigestText $digest) -or $digest -cne $NewDigest) { continue }
        $outcome = [string](Get-RcProp $item 'outcome')
        if ($outcome -ceq 'passed' -or $outcome -ceq 'expected-skip') { $sawPass = $true }
        elseif ($outcome -ceq 'failed') { $sawFail = $true }
    }
    return ($sawPass -and $sawFail)
}

function Get-RcIdentityCarryDecision {
    param(
        [string]$Platform,
        [string]$Boundary,
        [string]$NewDigest,
        $SourceLedger,
        $SourceIdentity,
        $SourceChunk,
        $History,
        [bool]$FixProven = $false,
        $ExpectedSkipRoster,
        $ForcedFull
    )
    $key = ''
    if ($null -ne $SourceIdentity) { $key = Get-RcIdentityKey $SourceIdentity }
    $forced = @(ConvertTo-RcList $ForcedFull | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($forced.Count -gt 0) { return (New-RcDecision 'dirty' 'forced-full' $key 'unproven' $null) }
    if ((Test-RcUnknownBoundary $Boundary) -or (($null -ne $SourceIdentity) -and (Test-RcUnknownBoundary ([string](Get-RcProp $SourceIdentity 'boundary'))))) {
        return (New-RcDecision 'dirty' 'unknown-boundary' $key 'unproven' $null)
    }
    if ($null -eq $SourceIdentity -or $null -eq $SourceLedger) {
        return (New-RcDecision 'dirty' 'missing-row' $key 'not-run' $null)
    }
    $sourcePlatform = [string](Get-RcProp $SourceLedger 'platform')
    if ($sourcePlatform -cne $Platform) { return (New-RcDecision 'dirty' 'cross-platform' $key 'unproven' $null) }
    $compared = Compare-RcInputDigest -Left ([string](Get-RcProp $SourceIdentity 'inputDigest')) -Right $NewDigest
    if ($compared.relation -ceq 'invalid') { return (New-RcDecision 'dirty' 'missing-digest' $key 'unproven' $null) }
    if ($compared.relation -cne 'same') { return (New-RcDecision 'dirty' 'digest-mismatch' $key 'unproven' $null) }
    $outcome = Resolve-RcOutcome ([string](Get-RcProp $SourceIdentity 'outcome')) $key $ExpectedSkipRoster
    if ($outcome -ceq 'unexpected-skip' -or $outcome -ceq 'interrupted' -or $outcome -ceq 'not-run' -or $outcome -ceq 'failed' -or $outcome -ceq 'unproven') {
        return (New-RcDecision 'dirty' $outcome $key $outcome $null)
    }
    if ($outcome -cne 'passed' -and $outcome -cne 'expected-skip') {
        return (New-RcDecision 'dirty' 'unknown-outcome' $key 'unproven' $null)
    }
    if ($null -eq $SourceChunk) { return (New-RcDecision 'dirty' 'chunk-unknown' $key $outcome $null) }
    $chunkOutcome = [string](Get-RcProp $SourceChunk 'outcome')
    if ($chunkOutcome -ceq 'interrupted') { return (New-RcDecision 'dirty' 'chunk-interrupted' $key $outcome $null) }
    if ($chunkOutcome -cne 'passed' -and $chunkOutcome -cne 'expected-skip') {
        return (New-RcDecision 'dirty' 'chunk-open' $key $outcome $null)
    }
    if ((Test-RcSameDigestFlake $SourceIdentity $History $NewDigest) -and -not $FixProven) {
        return (New-RcDecision 'dirty' 'flake' $key 'unproven' $null)
    }
    $carried = New-RcCarriedFrom $SourceLedger $SourceIdentity $SourceChunk
    if (-not (Test-RcCarryProvenance $carried)) { return (New-RcDecision 'dirty' 'provenance-incomplete' $key $outcome $null) }
    return (New-RcDecision 'carry' 'carry' $key $outcome $carried)
}

function Get-RcChunkCarryDecision {
    param(
        [string]$Platform,
        [string]$NewDigest,
        $SourceLedger,
        $SourceChunk,
        $IdentityDecisions,
        $ForcedFull
    )
    $forced = @(ConvertTo-RcList $ForcedFull | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($forced.Count -gt 0) { return (New-RcDecision 'dirty' 'forced-full' '' 'unproven' $null) }
    if ($null -eq $SourceChunk -or $null -eq $SourceLedger) { return (New-RcDecision 'dirty' 'chunk-unknown' '' 'not-run' $null) }
    if (Test-RcUnknownBoundary ([string](Get-RcProp $SourceChunk 'boundary'))) {
        return (New-RcDecision 'dirty' 'unknown-boundary' '' 'unproven' $null)
    }
    $sourcePlatform = [string](Get-RcProp $SourceLedger 'platform')
    if ($sourcePlatform -cne $Platform) { return (New-RcDecision 'dirty' 'cross-platform' '' 'unproven' $null) }
    $compared = Compare-RcInputDigest -Left ([string](Get-RcProp $SourceChunk 'inputDigest')) -Right $NewDigest
    if ($compared.relation -ceq 'invalid') { return (New-RcDecision 'dirty' 'missing-digest' '' 'unproven' $null) }
    if ($compared.relation -cne 'same') { return (New-RcDecision 'dirty' 'digest-mismatch' '' 'unproven' $null) }
    $chunkOutcome = [string](Get-RcProp $SourceChunk 'outcome')
    if ($chunkOutcome -ceq 'interrupted') { return (New-RcDecision 'dirty' 'chunk-interrupted' '' $chunkOutcome $null) }
    if ($chunkOutcome -cne 'passed' -and $chunkOutcome -cne 'expected-skip') {
        return (New-RcDecision 'dirty' 'chunk-open' '' $chunkOutcome $null)
    }
    $dirty = New-Object System.Collections.Generic.List[object]
    $decisions = ConvertTo-RcList $IdentityDecisions
    if ($decisions.Count -eq 0) { return (New-RcDecision 'dirty' 'identity-missing' '' $chunkOutcome $null) }
    foreach ($decision in $decisions) {
        if ([string](Get-RcProp $decision 'decision') -ceq 'carry') { continue }
        $dirty.Add([pscustomobject]@{
            key = [string](Get-RcProp $decision 'identityKey')
            reason = [string](Get-RcProp $decision 'reason')
        })
    }
    if ($dirty.Count -gt 0) {
        $first = $dirty[0]
        foreach ($item in $dirty) {
            if ([string]::CompareOrdinal([string]$item.key, [string]$first.key) -lt 0) { $first = $item }
        }
        return (New-RcDecision 'dirty' ('identity:' + $first.key + ':' + $first.reason) '' $chunkOutcome $null)
    }
    return (New-RcDecision 'carry' 'carry' '' $chunkOutcome $null)
}

function Add-RcError {
    param($Errors, [string]$Message)
    $Errors.Add($Message)
}

function Test-RcLedgerDocument {
    param($Document)
    $errors = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Document) { Add-RcError $errors 'document-missing'; return [pscustomobject]@{ valid = $false; errors = $errors.ToArray() } }
    $format = [string](Get-RcProp $Document 'format')
    if ($format -cne $script:RcLedgerFormat -and $format -cne $script:RcLedgerFormatV2) { Add-RcError $errors 'format' }
    $platform = [string](Get-RcProp $Document 'platform')
    if ($platform -cne 'windows' -and $platform -cne 'linux') { Add-RcError $errors 'platform' }
    foreach ($name in @('repository', 'candidateId', 'candidateSha', 'runId')) {
        if (-not (Test-RcRequiredText ([string](Get-RcProp $Document $name)))) { Add-RcError $errors $name }
    }
    foreach ($name in @('baselineSha', 'baselineRef')) {
        if (-not (Test-RcHasProp $Document $name)) { Add-RcError $errors ($name + '-missing') }
        $text = Get-RcProp $Document $name
        if ($null -ne $text -and -not (Test-RcRequiredText ([string]$text))) { Add-RcError $errors $name }
    }
    if (-not (Test-RcWholeNumber (Get-RcProp $Document 'attempt') 1)) { Add-RcError $errors 'attempt' }
    $contract = Get-RcProp $Document 'contract'
    foreach ($name in @('policyHash', 'catalogHash', 'selectorHash', 'runtimeHash')) {
        if (-not (Test-RcRequiredText ([string](Get-RcProp $contract $name)))) { Add-RcError $errors ('contract-' + $name) }
    }
    $identityMap = @{}
    $identities = ConvertTo-RcList (Get-RcProp $Document 'identities')
    if ($identities.Count -eq 0) { Add-RcError $errors 'identities-empty' }
    foreach ($identity in $identities) {
        $key = Get-RcIdentityKey $identity
        if ($identityMap.ContainsKey($key)) { Add-RcError $errors ('duplicate-identity:' + $key) }
        $identityMap[$key] = $identity
        foreach ($name in @('suite', 'className', 'method', 'boundary')) {
            $text = [string](Get-RcProp $identity $name)
            if (-not (Test-RcRequiredText $text) -or $text.Contains('/')) { Add-RcError $errors ('identity-' + $name) }
        }
        if (-not (Test-RcStoredDigest ([string](Get-RcProp $identity 'inputDigest')))) { Add-RcError $errors 'identity-digest' }
        $outcome = [string](Get-RcProp $identity 'outcome')
        if ($script:RcLedgerOutcomes -cnotcontains $outcome) { Add-RcError $errors ('identity-outcome:' + $outcome) }
        if (-not (Test-RcDuration (Get-RcProp $identity 'durationSeconds'))) { Add-RcError $errors 'identity-duration' }
        if (-not (Test-RcHasProp $identity 'carriedFrom')) { Add-RcError $errors 'carriedFrom-missing' }
        $carried = Get-RcProp $identity 'carriedFrom'
        if ($null -ne $carried -and -not (Test-RcCarryProvenance $carried)) { Add-RcError $errors 'carriedFrom' }
    }
    $chunks = ConvertTo-RcList (Get-RcProp $Document 'chunks')
    if ($chunks.Count -eq 0) { Add-RcError $errors 'chunks-empty' }
    $seenKeys = @{}
    foreach ($chunk in $chunks) {
        $chunkId = [string](Get-RcProp $chunk 'chunkId')
        if (-not (Test-RcRequiredText $chunkId)) { Add-RcError $errors 'chunk-id' }
        $boundary = [string](Get-RcProp $chunk 'boundary')
        $suite = [string](Get-RcProp $chunk 'suite')
        if (-not (Test-RcRequiredText $boundary) -or -not (Test-RcRequiredText $suite)) { Add-RcError $errors 'chunk-boundary' }
        if (-not (Test-RcStoredDigest ([string](Get-RcProp $chunk 'inputDigest')))) { Add-RcError $errors 'chunk-digest' }
        if (-not (Test-RcDuration (Get-RcProp $chunk 'durationSeconds'))) { Add-RcError $errors 'chunk-duration' }
        foreach ($name in $script:RcLedgerHostFields) {
            if (-not (Test-RcHasProp $chunk $name)) { continue }
            if ($format -cne $script:RcLedgerFormatV2) { Add-RcError $errors ('unknown-property:' + $name); continue }
            $hostValue = Get-RcProp $chunk $name
            if ($null -eq $hostValue) { continue }
            if ($hostValue -isnot [string] -or -not (Test-RcRequiredText $hostValue)) { Add-RcError $errors ('chunk-' + $name) }
            elseif ($name -ceq 'os' -and $hostValue -cne 'windows' -and $hostValue -cne 'linux') { Add-RcError $errors 'chunk-os' }
        }
        $keys = ConvertTo-RcList (Get-RcProp $chunk 'identityKeys')
        if ($keys.Count -eq 0) { Add-RcError $errors 'chunk-keys-empty' }
        $memberOutcomes = New-Object System.Collections.Generic.List[string]
        foreach ($keyValue in $keys) {
            $key = [string]$keyValue
            if (-not $identityMap.ContainsKey($key)) { Add-RcError $errors ('chunk-missing-identity:' + $key); continue }
            if ($seenKeys.ContainsKey($key)) { Add-RcError $errors ('identity-two-chunks:' + $key) }
            $seenKeys[$key] = $true
            $identity = $identityMap[$key]
            if ([string](Get-RcProp $identity 'boundary') -cne $boundary) { Add-RcError $errors ('boundary-disagreement:' + $key) }
            if ([string](Get-RcProp $identity 'suite') -cne $suite) { Add-RcError $errors ('suite-disagreement:' + $key) }
            if ([string](Get-RcProp $identity 'inputDigest') -cne [string](Get-RcProp $chunk 'inputDigest')) { Add-RcError $errors ('digest-disagreement:' + $key) }
            $memberOutcomes.Add([string](Get-RcProp $identity 'outcome'))
        }
        $worst = Get-RcWorstOutcome $memberOutcomes
        if ([string](Get-RcProp $chunk 'outcome') -cne $worst) { Add-RcError $errors ('chunk-outcome:' + $chunkId) }
    }
    foreach ($key in @($identityMap.Keys)) {
        if (-not $seenKeys.ContainsKey([string]$key)) { Add-RcError $errors ('identity-without-chunk:' + $key) }
    }
    return [pscustomobject]@{ valid = ($errors.Count -eq 0); errors = $errors.ToArray() }
}

function Copy-RcIdentity {
    param($Identity)
    $carried = Get-RcProp $Identity 'carriedFrom'
    $copiedCarried = $null
    if ($null -ne $carried) { $copiedCarried = New-RcCarriedFrom $null $Identity $null }
    return [pscustomobject]@{
        suite = [string](Get-RcProp $Identity 'suite')
        className = [string](Get-RcProp $Identity 'className')
        method = [string](Get-RcProp $Identity 'method')
        boundary = [string](Get-RcProp $Identity 'boundary')
        inputDigest = [string](Get-RcProp $Identity 'inputDigest')
        outcome = [string](Get-RcProp $Identity 'outcome')
        durationSeconds = Get-RcProp $Identity 'durationSeconds'
        carriedFrom = $copiedCarried
    }
}

function New-RcLedger {
    param($Document)
    if ($null -eq $Document) { throw 'ledger-invalid: document-missing' }
    $identities = New-Object System.Collections.Generic.List[object]
    foreach ($identity in @(ConvertTo-RcList (Get-RcProp $Document 'identities'))) {
        $identities.Add((Copy-RcIdentity $identity))
    }
    $sortedIdentities = New-Object System.Collections.Generic.List[object]
    foreach ($identity in $identities) {
        $placed = $false
        for ($slot = 0; $slot -lt $sortedIdentities.Count; $slot++) {
            $suite = [string]::CompareOrdinal([string](Get-RcProp $identity 'suite'), [string](Get-RcProp $sortedIdentities[$slot] 'suite'))
            $class = [string]::CompareOrdinal([string](Get-RcProp $identity 'className'), [string](Get-RcProp $sortedIdentities[$slot] 'className'))
            $method = [string]::CompareOrdinal([string](Get-RcProp $identity 'method'), [string](Get-RcProp $sortedIdentities[$slot] 'method'))
            $cmp = $suite
            if ($cmp -eq 0) { $cmp = $class }
            if ($cmp -eq 0) { $cmp = $method }
            if ($cmp -lt 0) {
                $sortedIdentities.Insert($slot, $identity)
                $placed = $true
                break
            }
        }
        if (-not $placed) { $sortedIdentities.Add($identity) }
    }
    $identityArray = New-Object object[] $sortedIdentities.Count
    for ($i = 0; $i -lt $sortedIdentities.Count; $i++) { $identityArray[$i] = $sortedIdentities[$i] }
    $byKey = @{}
    foreach ($identity in $identityArray) { $byKey[(Get-RcIdentityKey $identity)] = $identity }
    $chunks = New-Object System.Collections.Generic.List[object]
    foreach ($chunk in @(ConvertTo-RcList (Get-RcProp $Document 'chunks'))) {
        $keys = Sort-RcText (Get-RcProp $chunk 'identityKeys')
        $keyArray = New-Object object[] $keys.Count
        $outcomes = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $keys.Count; $i++) {
            $keyArray[$i] = $keys[$i]
            if ($byKey.ContainsKey($keys[$i])) { $outcomes.Add([string](Get-RcProp $byKey[$keys[$i]] 'outcome')) }
        }
        $chunks.Add([pscustomobject]@{
            chunkId = [string](Get-RcProp $chunk 'chunkId')
            boundary = [string](Get-RcProp $chunk 'boundary')
            suite = [string](Get-RcProp $chunk 'suite')
            identityKeys = $keyArray
            inputDigest = [string](Get-RcProp $chunk 'inputDigest')
            outcome = (Get-RcWorstOutcome $outcomes)
            durationSeconds = Get-RcProp $chunk 'durationSeconds'
        })
    }
    $sortedChunks = New-Object System.Collections.Generic.List[object]
    foreach ($chunk in $chunks) {
        $placed = $false
        for ($slot = 0; $slot -lt $sortedChunks.Count; $slot++) {
            if ([string]::CompareOrdinal([string](Get-RcProp $chunk 'chunkId'), [string](Get-RcProp $sortedChunks[$slot] 'chunkId')) -lt 0) {
                $sortedChunks.Insert($slot, $chunk)
                $placed = $true
                break
            }
        }
        if (-not $placed) { $sortedChunks.Add($chunk) }
    }
    $chunkArray = New-Object object[] $sortedChunks.Count
    for ($i = 0; $i -lt $sortedChunks.Count; $i++) { $chunkArray[$i] = $sortedChunks[$i] }
    $contractIn = Get-RcProp $Document 'contract'
    $platformValue = [string](Get-RcProp $Document 'platform')
    $repositoryValue = [string](Get-RcProp $Document 'repository')
    $candidateIdValue = [string](Get-RcProp $Document 'candidateId')
    $candidateShaValue = [string](Get-RcProp $Document 'candidateSha')
    $baselineShaValue = Get-RcProp $Document 'baselineSha'
    $baselineRefValue = Get-RcProp $Document 'baselineRef'
    $runIdValue = [string](Get-RcProp $Document 'runId')
    $attemptValue = Get-RcProp $Document 'attempt'
    $policyValue = [string](Get-RcProp $contractIn 'policyHash')
    $catalogValue = [string](Get-RcProp $contractIn 'catalogHash')
    $selectorValue = [string](Get-RcProp $contractIn 'selectorHash')
    $runtimeValue = [string](Get-RcProp $contractIn 'runtimeHash')
    $forcedValue = Sort-RcText (Get-RcProp $Document 'forcedFull')
    $normalized = [pscustomobject]@{
        format = $script:RcLedgerFormat
        platform = $platformValue
        repository = $repositoryValue
        candidateId = $candidateIdValue
        candidateSha = $candidateShaValue
        baselineSha = $baselineShaValue
        baselineRef = $baselineRefValue
        runId = $runIdValue
        attempt = $attemptValue
        contract = [pscustomobject]@{
            policyHash = $policyValue
            catalogHash = $catalogValue
            selectorHash = $selectorValue
            runtimeHash = $runtimeValue
        }
        forcedFull = $forcedValue
        identities = $identityArray
        chunks = $chunkArray
    }
    $check = Test-RcLedgerDocument -Document $normalized
    if (-not $check.valid) { throw ('ledger-invalid: ' + ($check.errors -join '; ')) }
    return $normalized
}

function ConvertTo-RcJsonNullableString {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    return (ConvertTo-RcJsonString ([string]$Value))
}

function ConvertTo-RcJsonDuration {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    return (ConvertTo-RcJsonNumber $Value)
}

function ConvertTo-RcIdentityJson {
    param($Identity)
    $carried = Get-RcProp $Identity 'carriedFrom'
    $carriedJson = 'null'
    if ($null -ne $carried) {
        $carriedJson = '{' +
            '"candidateId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $carried 'candidateId'))) +
            ',"runId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $carried 'runId'))) +
            ',"candidateSha":' + (ConvertTo-RcJsonString ([string](Get-RcProp $carried 'candidateSha'))) +
            ',"chunkId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $carried 'chunkId'))) +
            ',"inputDigest":' + (ConvertTo-RcJsonString ([string](Get-RcProp $carried 'inputDigest'))) + '}'
    }
    return '{' +
        '"suite":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'suite'))) +
        ',"className":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'className'))) +
        ',"method":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'method'))) +
        ',"boundary":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'boundary'))) +
        ',"inputDigest":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'inputDigest'))) +
        ',"outcome":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Identity 'outcome'))) +
        ',"durationSeconds":' + (ConvertTo-RcJsonDuration (Get-RcProp $Identity 'durationSeconds')) +
        ',"carriedFrom":' + $carriedJson + '}'
}

function Get-RcLedgerFormatOf {
    param($Document)
    $format = [string](Get-RcProp $Document 'format')
    if ($format -ceq $script:RcLedgerFormatV2) { return $script:RcLedgerFormatV2 }
    return $script:RcLedgerFormat
}

# v1 never names a host; v2 always writes both fields, null when unset.
function ConvertTo-RcChunkHostJson {
    param($Document, $Chunk)
    if ((Get-RcLedgerFormatOf $Document) -cne $script:RcLedgerFormatV2) { return '' }
    return ',"hostId":' + (ConvertTo-RcJsonNullableString (Get-RcProp $Chunk 'hostId')) +
        ',"os":' + (ConvertTo-RcJsonNullableString (Get-RcProp $Chunk 'os'))
}

function ConvertTo-RcLedgerJson {
    param($Document)
    $contract = Get-RcProp $Document 'contract'
    $identityJson = New-Object System.Collections.Generic.List[string]
    foreach ($identity in @(ConvertTo-RcList (Get-RcProp $Document 'identities'))) {
        $identityJson.Add((ConvertTo-RcIdentityJson $identity))
    }
    $chunkJson = New-Object System.Collections.Generic.List[string]
    foreach ($chunk in @(ConvertTo-RcList (Get-RcProp $Document 'chunks'))) {
        $chunkJson.Add('{' +
            '"chunkId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $chunk 'chunkId'))) +
            ',"boundary":' + (ConvertTo-RcJsonString ([string](Get-RcProp $chunk 'boundary'))) +
            ',"suite":' + (ConvertTo-RcJsonString ([string](Get-RcProp $chunk 'suite'))) +
            ',"identityKeys":' + (ConvertTo-RcJsonStringArray (Get-RcProp $chunk 'identityKeys')) +
            ',"inputDigest":' + (ConvertTo-RcJsonString ([string](Get-RcProp $chunk 'inputDigest'))) +
            ',"outcome":' + (ConvertTo-RcJsonString ([string](Get-RcProp $chunk 'outcome'))) +
            ',"durationSeconds":' + (ConvertTo-RcJsonDuration (Get-RcProp $chunk 'durationSeconds')) + (ConvertTo-RcChunkHostJson $Document $chunk) + '}')
    }
    return '{' +
        '"format":' + (ConvertTo-RcJsonString (Get-RcLedgerFormatOf $Document)) +
        ',"platform":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Document 'platform'))) +
        ',"repository":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Document 'repository'))) +
        ',"candidateId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Document 'candidateId'))) +
        ',"candidateSha":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Document 'candidateSha'))) +
        ',"baselineSha":' + (ConvertTo-RcJsonNullableString (Get-RcProp $Document 'baselineSha')) +
        ',"baselineRef":' + (ConvertTo-RcJsonNullableString (Get-RcProp $Document 'baselineRef')) +
        ',"runId":' + (ConvertTo-RcJsonString ([string](Get-RcProp $Document 'runId'))) +
        ',"attempt":' + (ConvertTo-RcJsonNumber (Get-RcProp $Document 'attempt')) +
        ',"contract":{"policyHash":' + (ConvertTo-RcJsonString ([string](Get-RcProp $contract 'policyHash'))) +
        ',"catalogHash":' + (ConvertTo-RcJsonString ([string](Get-RcProp $contract 'catalogHash'))) +
        ',"selectorHash":' + (ConvertTo-RcJsonString ([string](Get-RcProp $contract 'selectorHash'))) +
        ',"runtimeHash":' + (ConvertTo-RcJsonString ([string](Get-RcProp $contract 'runtimeHash'))) + '}' +
        ',"forcedFull":' + (ConvertTo-RcJsonStringArray (Get-RcProp $Document 'forcedFull')) +
        ',"identities":[' + ($identityJson.ToArray() -join ',') + ']' +
        ',"chunks":[' + ($chunkJson.ToArray() -join ',') + ']}'
}

function Write-RcLedger {
    param([Parameter(Mandatory = $true)][string]$Path, $Document)
    if ([System.IO.File]::Exists($Path)) { throw 'ledger-exists' }
    $check = Test-RcLedgerDocument -Document $Document
    if (-not $check.valid) { throw ('ledger-invalid: ' + ($check.errors -join '; ')) }
    $json = (ConvertTo-RcLedgerJson -Document $Document) + "`n"
    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not [System.IO.Directory]::Exists($directory)) {
        [void][System.IO.Directory]::CreateDirectory($directory)
    }
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $json, $encoding)
}

# CARD-1842 S6. Chunk durationSeconds from the sidecar runWallSeconds whose suite and chunk
# equal the chunk's suite and chunkId; identity durationSeconds is the sum of the TRX
# UnitTestResult durations whose TestMethod className and name equal the identity's. A
# chunk or identity without a match keeps its value. Format and host are not touched.
function Get-RcTrxDurations {
    param($TrxPath)
    $totals = New-Object 'System.Collections.Generic.Dictionary[string,double]' ([System.StringComparer]::Ordinal)
    $owners = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    foreach ($pathValue in @(ConvertTo-RcList $TrxPath)) {
        $path = [string]$pathValue
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $xml = New-Object System.Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($path)
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $methods = @{}
        foreach ($test in @($xml.SelectNodes('//t:UnitTest', $ns))) {
            $method = $test.SelectSingleNode('t:TestMethod', $ns)
            if ($null -eq $method) { continue }
            $methods[[string]$test.GetAttribute('id')] = ([string]$method.GetAttribute('className')) + '/' + ([string]$method.GetAttribute('name'))
        }
        foreach ($result in @($xml.SelectNodes('//t:UnitTestResult', $ns))) {
            $key = $methods[[string]$result.GetAttribute('testId')]
            $text = [string]$result.GetAttribute('duration')
            if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($text)) { continue }
            $span = [TimeSpan]::Zero
            if (-not [TimeSpan]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$span)) { throw ('duration-invalid: ' + $key) }
            if ($owners.ContainsKey($key) -and $owners[$key] -cne $path) { throw ('duration-ambiguous: ' + $key) }
            $owners[$key] = $path
            if (-not $totals.ContainsKey($key)) { $totals[$key] = 0.0 }
            $totals[$key] = $totals[$key] + $span.TotalSeconds
        }
    }
    return ,$totals
}

function Set-RcLedgerDurations {
    param([Parameter(Mandatory = $true)]$Document, $TimingPath, $TrxPath)
    $walls = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
    foreach ($pathValue in @(ConvertTo-RcList $TimingPath)) {
        $path = [string]$pathValue
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $timing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $key = ([string](Get-RcProp $timing 'suite')) + "`n" + ([string](Get-RcProp $timing 'chunk'))
        if ($walls.ContainsKey($key)) { throw ('duration-duplicate: ' + [System.IO.Path]::GetFileName($path)) }
        $wall = Get-RcProp $timing 'runWallSeconds'
        if ((Test-RcNumber $wall) -and (Test-RcDuration $wall)) { $walls[$key] = [double]$wall } else { $walls[$key] = $null }
    }
    foreach ($chunk in @(ConvertTo-RcList (Get-RcProp $Document 'chunks'))) {
        $key = ([string](Get-RcProp $chunk 'suite')) + "`n" + ([string](Get-RcProp $chunk 'chunkId'))
        if ($walls.ContainsKey($key) -and $null -ne $walls[$key]) { $chunk.durationSeconds = $walls[$key] }
    }
    $totals = Get-RcTrxDurations $TrxPath
    foreach ($identity in @(ConvertTo-RcList (Get-RcProp $Document 'identities'))) {
        $key = Get-RcIdentityKey $identity
        if ($totals.ContainsKey($key)) { $identity.durationSeconds = $totals[$key] }
    }
    return $Document
}

# Sets chunk hostId and os from the caller's arguments; never reads the machine name.
function Set-RcLedgerHost {
    param([Parameter(Mandatory = $true)]$Document, [string]$HostId, [string]$Os)
    if ([string](Get-RcProp $Document 'format') -cne $script:RcLedgerFormatV2) { throw 'ledger-schema' }
    if (-not (Test-RcRequiredText $HostId)) { throw 'ledger-host: hostId' }
    if ($Os -cne 'windows' -and $Os -cne 'linux') { throw 'ledger-host: os' }
    foreach ($chunk in @(ConvertTo-RcList (Get-RcProp $Document 'chunks'))) {
        if ($chunk -is [System.Collections.IDictionary]) { $chunk['hostId'] = $HostId; $chunk['os'] = $Os; continue }
        $chunk | Add-Member -NotePropertyName hostId -NotePropertyValue $HostId -Force
        $chunk | Add-Member -NotePropertyName os -NotePropertyValue $Os -Force
    }
    return $Document
}

function New-RcFieldMap {
    return (New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal))
}

function Initialize-RcLedgerSchema {
    if ($script:RcSchemaReady) { return }
    $document = New-RcFieldMap
    $document.Add('format', 's')
    $document.Add('platform', 's')
    $document.Add('repository', 's')
    $document.Add('candidateId', 's')
    $document.Add('candidateSha', 's')
    $document.Add('baselineSha', 'sn')
    $document.Add('baselineRef', 'sn')
    $document.Add('runId', 's')
    $document.Add('attempt', 'n')
    $document.Add('contract', 'o:contract')
    $document.Add('forcedFull', 'as')
    $document.Add('identities', 'ai')
    $document.Add('chunks', 'ac')
    $contract = New-RcFieldMap
    $contract.Add('policyHash', 's')
    $contract.Add('catalogHash', 's')
    $contract.Add('selectorHash', 's')
    $contract.Add('runtimeHash', 's')
    $identity = New-RcFieldMap
    $identity.Add('suite', 's')
    $identity.Add('className', 's')
    $identity.Add('method', 's')
    $identity.Add('boundary', 's')
    $identity.Add('inputDigest', 's')
    $identity.Add('outcome', 's')
    $identity.Add('durationSeconds', 'nn')
    $identity.Add('carriedFrom', 'on:carried')
    $chunk = New-RcFieldMap
    $chunk.Add('chunkId', 's')
    $chunk.Add('boundary', 's')
    $chunk.Add('suite', 's')
    $chunk.Add('identityKeys', 'as')
    $chunk.Add('inputDigest', 's')
    $chunk.Add('outcome', 's')
    $chunk.Add('durationSeconds', 'nn')
    # Optional (?) here; Test-RcLedgerDocument refuses them on v1.
    $chunk.Add('hostId', 'sn?')
    $chunk.Add('os', 'sn?')
    $carried = New-RcFieldMap
    $carried.Add('candidateId', 's')
    $carried.Add('runId', 's')
    $carried.Add('candidateSha', 's')
    $carried.Add('chunkId', 's')
    $carried.Add('inputDigest', 's')
    $script:RcSchemaDocument = $document
    $script:RcSchemaContract = $contract
    $script:RcSchemaIdentity = $identity
    $script:RcSchemaChunk = $chunk
    $script:RcSchemaCarried = $carried
    $script:RcSchemaReady = $true
}

function Get-RcKindSchema {
    param([string]$Kind)
    Initialize-RcLedgerSchema
    if ($Kind -ceq 'document') { return $script:RcSchemaDocument }
    if ($Kind -ceq 'contract') { return $script:RcSchemaContract }
    if ($Kind -ceq 'identity') { return $script:RcSchemaIdentity }
    if ($Kind -ceq 'chunk') { return $script:RcSchemaChunk }
    if ($Kind -ceq 'carried') { return $script:RcSchemaCarried }
    throw 'ledger-invalid: type'
}

function Read-RcLedgerText {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $offset = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { $offset = 3 }
    $encoding = New-Object System.Text.UTF8Encoding $false, $true
    try {
        return $encoding.GetString($bytes, $offset, ($bytes.Length - $offset))
    } catch {
        throw 'ledger-invalid: json'
    }
}

function Read-RcLedger {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not [System.IO.File]::Exists($Path)) { throw 'ledger-missing' }
    $text = Read-RcLedgerText -Path $Path
    Test-RcLedgerJsonText -Text $text
    try { $parsed = $text | ConvertFrom-Json }
    catch { throw 'ledger-invalid: json' }
    $check = Test-RcLedgerDocument -Document $parsed
    if (-not $check.valid) { throw ('ledger-invalid: ' + ($check.errors -join '; ')) }
    return $parsed
}

function ConvertFrom-RcScanString {
    param([string]$Raw)
    $body = $Raw.Substring(1, ($Raw.Length - 2))
    if ($body.IndexOf([char]92) -lt 0) { return $body }
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $body.Length) {
        $ch = $body[$i]
        $i++
        if ($ch -ne '\') { [void]$sb.Append($ch); continue }
        if ($i -ge $body.Length) { throw 'ledger-invalid: json' }
        $esc = $body[$i]
        $i++
        if ($esc -eq '"') { [void]$sb.Append('"') }
        elseif ($esc -eq '\') { [void]$sb.Append('\') }
        elseif ($esc -eq '/') { [void]$sb.Append('/') }
        elseif ($esc -eq 'b') { [void]$sb.Append([char]8) }
        elseif ($esc -eq 'f') { [void]$sb.Append([char]12) }
        elseif ($esc -eq 'n') { [void]$sb.Append([char]10) }
        elseif ($esc -eq 'r') { [void]$sb.Append([char]13) }
        elseif ($esc -eq 't') { [void]$sb.Append([char]9) }
        elseif ($esc -eq 'u') {
            if (($i + 4) -gt $body.Length) { throw 'ledger-invalid: json' }
            $value = 0
            for ($hex = 0; $hex -lt 4; $hex++) {
                $digit = [int]$body[$i]
                $i++
                if ($digit -ge 48 -and $digit -le 57) { $value = ($value * 16) + ($digit - 48) }
                elseif ($digit -ge 97 -and $digit -le 102) { $value = ($value * 16) + ($digit - 87) }
                elseif ($digit -ge 65 -and $digit -le 70) { $value = ($value * 16) + ($digit - 55) }
                else { throw 'ledger-invalid: json' }
            }
            [void]$sb.Append([char]$value)
        }
        else { throw 'ledger-invalid: json' }
    }
    return $sb.ToString()
}

function Test-RcScanNumber {
    param([string]$Raw)
    $number = 0.0
    $ok = [double]::TryParse($Raw, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)
    if (-not $ok) { throw 'ledger-invalid: json' }
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { throw 'ledger-invalid: json' }
}

function New-RcScanObject {
    param([string]$Kind)
    return [pscustomobject]@{
        K = 'object'
        Step = 0
        AllowClose = $true
        Schema = (Get-RcKindSchema $Kind)
        Expect = ''
        Element = ''
        Fold = (New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase))
        Exact = (New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal))
    }
}

function New-RcScanArray {
    param([string]$ElementType)
    return [pscustomobject]@{
        K = 'array'
        Step = 0
        AllowClose = $true
        Schema = $null
        Expect = ''
        Element = $ElementType
        Fold = $null
        Exact = $null
    }
}

function Complete-RcScanValue {
    param($Frames)
    $parent = $Frames[$Frames.Count - 1]
    if ($parent.K -ceq 'root') { $parent.Step = 1; return }
    if ($parent.K -ceq 'object') { $parent.Step = 3; $parent.AllowClose = $true; return }
    $parent.Step = 1
    $parent.AllowClose = $true
}

function Open-RcScanValue {
    param($Frames, [string]$Type, [string]$Raw)
    if ($Frames.Count -gt 12) { throw 'ledger-invalid: json' }
    if ($Raw -ceq '{') {
        if ($Type -ceq 'o:document') { $Frames.Add((New-RcScanObject 'document')); return $true }
        if ($Type -ceq 'o:contract') { $Frames.Add((New-RcScanObject 'contract')); return $true }
        if ($Type -ceq 'o:identity') { $Frames.Add((New-RcScanObject 'identity')); return $true }
        if ($Type -ceq 'o:chunk') { $Frames.Add((New-RcScanObject 'chunk')); return $true }
        if ($Type -ceq 'o:carried') { $Frames.Add((New-RcScanObject 'carried')); return $true }
        if ($Type -ceq 'on:carried') { $Frames.Add((New-RcScanObject 'carried')); return $true }
        throw 'ledger-invalid: type'
    }
    if ($Raw -ceq '[') {
        if ($Type -ceq 'as') { $Frames.Add((New-RcScanArray 's')); return $true }
        if ($Type -ceq 'ai') { $Frames.Add((New-RcScanArray 'o:identity')); return $true }
        if ($Type -ceq 'ac') { $Frames.Add((New-RcScanArray 'o:chunk')); return $true }
        throw 'ledger-invalid: type'
    }
    return $false
}

function Test-RcScanScalar {
    param([string]$Type, [string]$Raw)
    $isString = $Raw.Length -ge 2 -and $Raw[0] -eq [char]34
    $isNull = $Raw -ceq 'null'
    $isBool = ($Raw -ceq 'true') -or ($Raw -ceq 'false')
    if ($Type -ceq 's') {
        if (-not $isString) { throw 'ledger-invalid: type' }
        if ($Raw.IndexOf([char]92) -ge 0) { $null = ConvertFrom-RcScanString $Raw }
        return
    }
    if ($Type -ceq 'sn' -or $Type -ceq 'sn?') {
        if ($isNull) { return }
        if (-not $isString) { throw 'ledger-invalid: type' }
        if ($Raw.IndexOf([char]92) -ge 0) { $null = ConvertFrom-RcScanString $Raw }
        return
    }
    if ($Type -ceq 'n' -or $Type -ceq 'nn') {
        if ($Type -ceq 'nn' -and $isNull) { return }
        if ($isString -or $isNull -or $isBool) { throw 'ledger-invalid: type' }
        Test-RcScanNumber $Raw
        return
    }
    if ($Type -ceq 'on:carried' -and $isNull) { return }
    throw 'ledger-invalid: type'
}

function Close-RcScanObject {
    param($Frames, $Frame)
    foreach ($required in @($Frame.Schema.Keys)) {
        if (([string]$Frame.Schema[$required]).EndsWith('?')) { continue }
        if (-not $Frame.Exact.Contains([string]$required)) { throw ('ledger-invalid: missing:' + $required) }
    }
    $Frames.RemoveAt($Frames.Count - 1)
    Complete-RcScanValue $Frames
}

function Test-RcLedgerJsonText {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { throw 'ledger-invalid: json' }
    Initialize-RcLedgerSchema
    if ($null -eq $script:RcLedgerToken) {
        $pattern = '"(?:\\.|[^"\\])*"|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+\-]?[0-9]+)?|true|false|null|[{}\[\]:,]'
        $script:RcLedgerToken = New-Object System.Text.RegularExpressions.Regex $pattern, ([System.Text.RegularExpressions.RegexOptions]::Compiled)
    }
    $scan = $Text
    if ([int]$scan[0] -eq 0xFEFF) { $scan = $scan.Substring(1) }
    $matches = $script:RcLedgerToken.Matches($scan)
    $frames = New-Object System.Collections.Generic.List[object]
    $frames.Add([pscustomobject]@{
        K = 'root'; Step = 0; AllowClose = $false; Schema = $null; Expect = 'o:document'; Element = ''; Fold = $null; Exact = $null
    })
    $cursor = 0
    foreach ($match in $matches) {
        if ($match.Index -gt $cursor) {
            $gap = $scan.Substring($cursor, ($match.Index - $cursor))
            if ($gap.Trim().Length -ne 0) { throw 'ledger-invalid: json' }
        }
        $cursor = $match.Index + $match.Length
        $raw = $match.Value
        $frame = $frames[$frames.Count - 1]
        if ($frame.K -ceq 'root') {
            if ($frame.Step -ne 0) { throw 'ledger-invalid: json' }
            $opened = Open-RcScanValue $frames $frame.Expect $raw
            if ($opened) { continue }
            Test-RcScanScalar $frame.Expect $raw
            $frame.Step = 1
            continue
        }
        if ($frame.K -ceq 'object') {
            if ($frame.Step -eq 0) {
                if ($raw -ceq '}') {
                    if (-not $frame.AllowClose) { throw 'ledger-invalid: json' }
                    Close-RcScanObject $frames $frame
                    continue
                }
                if ($raw.Length -lt 2 -or $raw[0] -ne [char]34) { throw 'ledger-invalid: json' }
                $name = ConvertFrom-RcScanString $raw
                $added = $frame.Fold.Add($name)
                if (-not $added) { throw ('ledger-invalid: duplicate-property:' + $name) }
                if (-not $frame.Schema.ContainsKey($name)) { throw ('ledger-invalid: unknown-property:' + $name) }
                [void]$frame.Exact.Add($name)
                $frame.Expect = [string]$frame.Schema[$name]
                $frame.Step = 1
                continue
            }
            if ($frame.Step -eq 1) {
                if ($raw -cne ':') { throw 'ledger-invalid: json' }
                $frame.Step = 2
                continue
            }
            if ($frame.Step -eq 2) {
                $opened = Open-RcScanValue $frames $frame.Expect $raw
                if ($opened) { continue }
                Test-RcScanScalar $frame.Expect $raw
                $frame.Step = 3
                $frame.AllowClose = $true
                continue
            }
            if ($raw -ceq '}') {
                Close-RcScanObject $frames $frame
                continue
            }
            if ($raw -cne ',') { throw 'ledger-invalid: json' }
            $frame.Step = 0
            $frame.AllowClose = $false
            continue
        }
        if ($frame.Step -eq 0) {
            if ($raw -ceq ']') {
                if (-not $frame.AllowClose) { throw 'ledger-invalid: json' }
                $frames.RemoveAt($frames.Count - 1)
                Complete-RcScanValue $frames
                continue
            }
            $opened = Open-RcScanValue $frames $frame.Element $raw
            if ($opened) { continue }
            Test-RcScanScalar $frame.Element $raw
            $frame.Step = 1
            $frame.AllowClose = $true
            continue
        }
        if ($raw -ceq ']') {
            $frames.RemoveAt($frames.Count - 1)
            Complete-RcScanValue $frames
            continue
        }
        if ($raw -cne ',') { throw 'ledger-invalid: json' }
        $frame.Step = 0
        $frame.AllowClose = $false
    }
    if ($cursor -lt $scan.Length) {
        $tail = $scan.Substring($cursor)
        if ($tail.Trim().Length -ne 0) { throw 'ledger-invalid: json' }
    }
    if ($frames.Count -ne 1 -or [int]$frames[0].Step -ne 1) { throw 'ledger-invalid: json' }
}
