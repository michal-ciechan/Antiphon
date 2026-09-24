#requires -Version 5.1
# CARD-0599: release-gate primitives shared by the nightly master lane and the RC lane.
#
# D-2  credit kinds: master green earns master readiness credit; RC green earns release
#       credit only. Unknown combinations fail closed.
# D-3  immutable candidate identity: release/rc-YYYYMMDDTHHMMSSZ pinned to one full SHA.
# D-4  one shared native-run lock serialises the master and RC lanes.
# D-8  CalVer vYYYY.MM.DD.N tags and the SHA-bound release manifest.
#
# ASCII-only on purpose - parseable under Windows PowerShell 5.1.

if ($script:AntiphonReleaseGateLoaded) { return }
$script:AntiphonReleaseGateLoaded = $true

$script:ReleaseGateProfiles = @('nightly', 'rc')
$script:ReleaseGateCreditKinds = @('master-scheduled', 'rc-release')
$script:ReleaseGateDefaultReleaseRoot = 'C:\Antiphon\releases'
$script:ReleaseGateDefaultCoordinationRoot = 'C:\Antiphon\verification'
$script:ReleaseGateCandidateRefPattern = '^release/rc-(?<stamp>\d{8}T\d{6}Z)$'
$script:ReleaseGateTagPattern = '^v(?<y>\d{4})\.(?<m>\d{2})\.(?<d>\d{2})\.(?<n>[1-9]\d*)$'
$script:ReleaseGateManifestSchema = 1

# ---------------------------------------------------------------- identity ---

function Test-ReleaseGateRealBoolean {
    # D-2: a JSON string "true" is not a boolean. Only a real boolean counts.
    param($Value)
    if ($null -eq $Value) { return $false }
    if ($Value -is [bool]) { return [bool]$Value }
    return $false
}

function Test-ReleaseGateFullSha {
    param([string]$Sha)
    if ([string]::IsNullOrWhiteSpace($Sha)) { return $false }
    return ($Sha -cmatch '^[0-9a-f]{40}$')
}

function Test-ReleaseGateCandidateRef {
    # D-3: exactly one shape. Anything else is not a candidate.
    param([string]$Ref)
    if ([string]::IsNullOrWhiteSpace($Ref)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'empty-ref'; Stamp = ''; CandidateId = '' }
    }
    $normalised = $Ref
    if ($normalised.StartsWith('refs/heads/')) { $normalised = $normalised.Substring('refs/heads/'.Length) }
    if ($normalised -notmatch $script:ReleaseGateCandidateRefPattern) {
        return [pscustomobject]@{ Ok = $false; Reason = 'ref-shape'; Stamp = ''; CandidateId = '' }
    }
    $stamp = $Matches['stamp']
    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    if (-not [datetime]::TryParseExact($stamp, 'yyyyMMddTHHmmssZ', [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'ref-timestamp'; Stamp = $stamp; CandidateId = '' }
    }
    return [pscustomobject]@{
        Ok = $true
        Reason = ''
        Stamp = $stamp
        CandidateId = ('rc-{0}' -f $stamp)
        Ref = ('release/rc-{0}' -f $stamp)
        CutUtc = $parsed.ToUniversalTime()
    }
}

function New-ReleaseGateCandidateRef {
    param([datetime]$Utc)
    if ($null -eq $Utc -or $Utc -eq [datetime]::MinValue) { $Utc = (Get-Date).ToUniversalTime() }
    return ('release/rc-{0}' -f $Utc.ToUniversalTime().ToString('yyyyMMddTHHmmss') + 'Z')
}

function Get-ReleaseGateCreditKind {
    <#
      D-2: the credit kind a run may earn from its own identity alone, before any
      green flags are consulted. Returns '' when the combination is unknown; the
      caller must treat that as no credit, never as the master default.
    #>
    param(
        [string]$Profile = '',
        [string]$Trigger = '',
        [string]$Ref = ''
    )
    $p = ([string]$Profile).Trim().ToLowerInvariant()
    $t = ([string]$Trigger).Trim().ToLowerInvariant()
    $r = ([string]$Ref).Trim()
    # An absent profile is the pre-CARD-0599 master lane.
    if ([string]::IsNullOrWhiteSpace($p)) { $p = 'nightly' }
    if ($script:ReleaseGateProfiles -notcontains $p) { return '' }
    if ($p -eq 'nightly') {
        if ($t -ne 'scheduled') { return '' }
        if ($r -ne 'master' -and $r -ne 'origin/master') { return '' }
        return 'master-scheduled'
    }
    if ($t -ne 'rc') { return '' }
    $candidate = Test-ReleaseGateCandidateRef -Ref $r
    if (-not $candidate.Ok) { return '' }
    return 'rc-release'
}

function Test-ReleaseGateGreenFlags {
    # D-2: the flags both credit kinds share. Real booleans and exit 0 only.
    param($State)
    $reasons = @()
    if ($null -eq $State) { return [pscustomobject]@{ Ok = $false; Reasons = @('missing-state') } }
    foreach ($field in @('coverageComplete', 'testsPassed', 'reportDelivered')) {
        $value = $State.$field
        if (-not (Test-ReleaseGateRealBoolean -Value $value)) { $reasons += ('not-true-boolean:' + $field) }
    }
    $exit = $State.exitCode
    if ($null -eq $exit) { $reasons += 'missing-exitCode' }
    elseif ($exit -isnot [int] -and $exit -isnot [long] -and $exit -isnot [double]) { $reasons += 'non-numeric-exitCode' }
    elseif ([int]$exit -ne 0) { $reasons += ('exitCode:' + [string]$exit) }
    if ([string]::IsNullOrWhiteSpace([string]$State.runId)) { $reasons += 'missing-runId' }
    if ([string]::IsNullOrWhiteSpace([string]$State.policyHash)) { $reasons += 'missing-policyHash' }
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = $reasons }
}

function Get-ReleaseGateCreditVerdict {
    <#
      D-2: the single decision point. Returns the credit kind this state actually
      earns ('' when none) plus the reasons it did not. Never guesses a kind.
    #>
    param($State)
    $reasons = @()
    if ($null -eq $State) {
        return [pscustomobject]@{ CreditKind = ''; Ok = $false; Reasons = @('missing-state') }
    }
    $kind = Get-ReleaseGateCreditKind -Profile ([string]$State.profile) -Trigger ([string]$State.trigger) -Ref ([string]$State.ref)
    if ([string]::IsNullOrWhiteSpace($kind)) {
        return [pscustomobject]@{ CreditKind = ''; Ok = $false; Reasons = @('unknown-credit-identity') }
    }
    $flags = Test-ReleaseGateGreenFlags -State $State
    if (-not $flags.Ok) { $reasons += @($flags.Reasons) }
    if ($kind -eq 'rc-release') {
        # D-3: an RC only earns credit against the SHA its candidate was pinned to.
        $sha = [string]$State.sha
        $expected = [string]$State.expectedSha
        if (-not (Test-ReleaseGateFullSha -Sha $sha)) { $reasons += 'rc-sha-not-full' }
        if (-not (Test-ReleaseGateFullSha -Sha $expected)) { $reasons += 'rc-expected-sha-missing' }
        elseif (-not [string]::Equals($sha, $expected, [StringComparison]::OrdinalIgnoreCase)) { $reasons += 'rc-sha-mismatch' }
        if ([string]::IsNullOrWhiteSpace([string]$State.candidateId)) { $reasons += 'rc-candidate-missing' }
    }
    if ($reasons.Count -gt 0) {
        return [pscustomobject]@{ CreditKind = ''; Ok = $false; Reasons = $reasons; RequestedKind = $kind }
    }
    return [pscustomobject]@{ CreditKind = $kind; Ok = $true; Reasons = @(); RequestedKind = $kind }
}

# ------------------------------------------------------------------- state ---

function Get-ReleaseGateCandidateRoot {
    param([string]$ReleaseRoot = '', [string]$CandidateId)
    if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) { $ReleaseRoot = $script:ReleaseGateDefaultReleaseRoot }
    return (Join-Path (Join-Path $ReleaseRoot 'candidates') $CandidateId)
}

function Get-ReleaseGateStatePaths {
    <#
      D-2: the credit store. Master green is the only thing that ever reaches
      <StateRoot>\last-complete-green.json; RC green lands in the candidate root.
    #>
    param(
        [string]$CreditKind,
        [string]$StateRoot,
        [string]$ReleaseRoot = '',
        [string]$CandidateId = ''
    )
    if ($CreditKind -eq 'rc-release') {
        if ([string]::IsNullOrWhiteSpace($CandidateId)) { throw 'rc credit requires a candidate id' }
        $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
        return [pscustomobject]@{
            CreditKind = $CreditKind
            Root = $root
            GreenPath = (Join-Path $root 'complete-green.json')
            JournalPath = (Join-Path $root 'candidate.json')
            ManifestPath = (Join-Path $root 'release-manifest.json')
            PublishJournalPath = (Join-Path $root 'publish.json')
        }
    }
    if ($CreditKind -ne 'master-scheduled') { throw ('unknown credit kind {0}' -f $CreditKind) }
    return [pscustomobject]@{
        CreditKind = $CreditKind
        Root = $StateRoot
        GreenPath = (Join-Path $StateRoot 'last-complete-green.json')
        JournalPath = ''
        ManifestPath = ''
        PublishJournalPath = ''
    }
}

function Get-ReleaseGateMasterStateFiles {
    # The four files an RC lane must leave byte-identical (V-2).
    param([string]$StateRoot)
    return @(
        (Join-Path $StateRoot 'last-run.json'),
        (Join-Path $StateRoot 'last-complete-green.json'),
        (Join-Path $StateRoot 'last-monitor.json'),
        (Join-Path $StateRoot 'interim-qualification-receipt.json')
    )
}

# -------------------------------------------------------------- shared lock ---

function Get-ReleaseGateNativeLockPath {
    param([string]$CoordinationRoot = '')
    if ([string]::IsNullOrWhiteSpace($CoordinationRoot)) { $CoordinationRoot = $script:ReleaseGateDefaultCoordinationRoot }
    return (Join-Path $CoordinationRoot 'native-run.lock')
}

function Test-ReleaseGateSameInstant {
    <#
      ConvertFrom-Json coerces an ISO-8601 string into a DateTime, so a naive
      string compare between what was written and what was read back fails under
      a non-invariant culture. Compare instants when both sides parse, and fall
      back to an ordinal compare only when they do not.
    #>
    param($Left, $Right)
    $leftEmpty = ($null -eq $Left) -or [string]::IsNullOrWhiteSpace([string]$Left)
    $rightEmpty = ($null -eq $Right) -or [string]::IsNullOrWhiteSpace([string]$Right)
    if ($leftEmpty -and $rightEmpty) { return $true }
    if ($leftEmpty -or $rightEmpty) { return $false }
    $l = [datetime]::MinValue
    $r = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    $lOk = $false
    $rOk = $false
    if ($Left -is [datetime]) { $l = ([datetime]$Left).ToUniversalTime(); $lOk = $true }
    else { $lOk = [datetime]::TryParse([string]$Left, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$l) }
    if ($Right -is [datetime]) { $r = ([datetime]$Right).ToUniversalTime(); $rOk = $true }
    else { $rOk = [datetime]::TryParse([string]$Right, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$r) }
    if ($lOk -and $rOk) { return ($l.ToUniversalTime() -eq $r.ToUniversalTime()) }
    return [string]::Equals([string]$Left, [string]$Right, [StringComparison]::OrdinalIgnoreCase)
}

function ConvertTo-ReleaseGateUtc {
    <#
      A timestamp read back from JSON may already be a DateTime, and casting one
      to string renders it in the CURRENT culture - which the invariant parser
      then rejects. Take the DateTime directly when that is what we were handed,
      and only parse when it really is text.
    #>
    param($Value, [switch]$AllowEmpty)
    if ($Value -is [datetime]) { return ([datetime]$Value).ToUniversalTime() }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        if ($AllowEmpty) { return (Get-NightlyUtcNow) }
        throw 'empty timestamp'
    }
    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    if ([datetime]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
        return $parsed.ToUniversalTime()
    }
    if ([datetime]::TryParse($text, [System.Globalization.CultureInfo]::CurrentCulture, $styles, [ref]$parsed)) {
        return $parsed.ToUniversalTime()
    }
    throw ('unparseable timestamp {0}' -f $text)
}

function New-ReleaseGateLockOwner {
    <#
      D-4: owner identity is PID + process start + run id + continuation id, so a
      self-reexec hop can prove it inherited the lock instead of stealing it.
    #>
    param([string]$RunId, [string]$Lane, [string]$ContinuationId = '')
    $start = Get-NightlyProcessStartUtc -ProcessId $PID
    $startText = ''
    if ($null -ne $start) { $startText = ([datetime]$start).ToUniversalTime().ToString('o') }
    return [ordered]@{
        pid = $PID
        processStartedAt = $startText
        runId = $RunId
        lane = $Lane
        continuationId = $ContinuationId
        acquiredAt = (Get-NightlyUtcNow).ToString('o')
    }
}

function Enter-ReleaseGateNativeLock {
    <#
      D-4: one atomic create-new lock across both lanes. A live owner is never
      stolen on age; the caller writes deferred-busy and waits for its next slot.
    #>
    param(
        [string]$CoordinationRoot = '',
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][string]$Lane,
        [string]$ContinuationId = '',
        [int]$ContinueOwnerPid = 0,
        [string]$ContinueOwnerStartedAt = ''
    )
    $lockPath = Get-ReleaseGateNativeLockPath -CoordinationRoot $CoordinationRoot
    $dir = Split-Path -Parent $lockPath
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    if ($ContinueOwnerPid -gt 0) {
        $existing = Read-NightlyLockRecord -LockPath $lockPath
        if ($null -eq $existing) {
            return [pscustomobject]@{ Ok = $false; Reason = 'missing-shared-lock'; OwnsLock = $false }
        }
        $alive = Test-NightlyProcessAlive -ProcessId $ContinueOwnerPid
        $runOk = [string]::Equals([string]$existing.runId, $RunId, [StringComparison]::OrdinalIgnoreCase)
        $pidOk = ([int]$existing.pid -eq $ContinueOwnerPid)
        $startOk = $true
        if (-not [string]::IsNullOrWhiteSpace($ContinueOwnerStartedAt)) {
            $startOk = Test-ReleaseGateSameInstant -Left $existing.processStartedAt -Right $ContinueOwnerStartedAt
        }
        $laneOk = [string]::Equals([string]$existing.lane, $Lane, [StringComparison]::OrdinalIgnoreCase)
        if (-not ($alive -and $runOk -and $pidOk -and $startOk -and $laneOk)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'shared-hop-identity'; OwnsLock = $false; Record = $existing }
        }
        return [pscustomobject]@{ Ok = $true; Reason = 'hop'; OwnsLock = $false; Record = $existing }
    }

    $record = New-ReleaseGateLockOwner -RunId $RunId -Lane $Lane -ContinuationId $ContinuationId
    $payload = ($record | ConvertTo-Json -Compress)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)
    try {
        # CreateNew is the exclusion; the handle is closed immediately afterwards so the
        # owner record stays READABLE. A contender must be able to say who holds the lock
        # and prove the owner is alive - it cannot do that against a file it cannot open.
        $stream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
        } finally {
            $stream.Dispose()
        }
        $script:ReleaseGateNativeLockStream = $null
        $script:ReleaseGateNativeLockPath = $lockPath
        $script:ReleaseGateOwnsNativeLock = $true
        return [pscustomobject]@{ Ok = $true; Reason = 'acquired'; OwnsLock = $true; Record = [pscustomobject]$record; Path = $lockPath }
    } catch [System.IO.IOException] {
        $existing = Read-NightlyLockRecord -LockPath $lockPath
        $alive = $false
        if ($existing -and [int]$existing.pid -gt 0) { $alive = Test-NightlyProcessAlive -ProcessId ([int]$existing.pid) }
        $reason = 'deferred-busy'
        if (-not $alive) { $reason = 'stale-shared-lock' }
        return [pscustomobject]@{ Ok = $false; Reason = $reason; OwnsLock = $false; Record = $existing; Path = $lockPath }
    }
}

function Exit-ReleaseGateNativeLock {
    param([string]$CoordinationRoot = '', [string]$RunId = '')
    if (-not $script:ReleaseGateOwnsNativeLock) { return }
    $lockPath = Get-ReleaseGateNativeLockPath -CoordinationRoot $CoordinationRoot
    $existing = Read-NightlyLockRecord -LockPath $lockPath
    $same = $false
    if ($existing) {
        $same = ([int]$existing.pid -eq $PID) -and
                ([string]::IsNullOrWhiteSpace($RunId) -or [string]::Equals([string]$existing.runId, $RunId, [StringComparison]::OrdinalIgnoreCase))
    }
    if ($script:ReleaseGateNativeLockStream) {
        try { $script:ReleaseGateNativeLockStream.Dispose() } catch { }
        $script:ReleaseGateNativeLockStream = $null
    }
    if ($same -and (Test-Path -LiteralPath $lockPath)) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }
    $script:ReleaseGateOwnsNativeLock = $false
}

# ------------------------------------------------------------------ calver ---

function Test-ReleaseGateTag {
    param([string]$Tag)
    if ([string]::IsNullOrWhiteSpace($Tag)) { return [pscustomobject]@{ Ok = $false; Reason = 'empty-tag' } }
    if ($Tag -notmatch $script:ReleaseGateTagPattern) { return [pscustomobject]@{ Ok = $false; Reason = 'tag-shape' } }
    $y = [int]$Matches['y']; $m = [int]$Matches['m']; $d = [int]$Matches['d']; $n = [int]$Matches['n']
    if ($m -lt 1 -or $m -gt 12 -or $d -lt 1 -or $d -gt 31) { return [pscustomobject]@{ Ok = $false; Reason = 'tag-date' } }
    try { $date = [datetime]::new($y, $m, $d, 0, 0, 0, [System.DateTimeKind]::Utc) } catch { return [pscustomobject]@{ Ok = $false; Reason = 'tag-date' } }
    return [pscustomobject]@{ Ok = $true; Reason = ''; Date = $date; Sequence = $n; Tag = $Tag }
}

function New-ReleaseGateTag {
    # D-8: vYYYY.MM.DD.N from the UTC cut date and a positive daily sequence.
    param([datetime]$CutUtc, [int]$Sequence)
    if ($Sequence -lt 1) { throw ('release sequence must be positive, got {0}' -f $Sequence) }
    $u = $CutUtc.ToUniversalTime()
    return ('v{0:0000}.{1:00}.{2:00}.{3}' -f $u.Year, $u.Month, $u.Day, $Sequence)
}

function Get-ReleaseGatePublicationJournal {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ schemaVersion = $script:ReleaseGateManifestSchema; reservations = @() }
    }
    try { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) }
    catch { throw ('unreadable publication journal {0}: {1}' -f $Path, $_.Exception.Message) }
}

function Get-ReleaseGateReservedTag {
    <#
      D-8: a crash after publication must recover the existing release, not allocate
      another N. An existing reservation for this candidate/SHA is returned as-is.
    #>
    param($Journal, [string]$CandidateId, [string]$Sha)
    foreach ($row in @($Journal.reservations)) {
        if ([string]::Equals([string]$row.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            return $row
        }
    }
    foreach ($row in @($Journal.reservations)) {
        if ([string]::Equals([string]$row.sha, $Sha, [StringComparison]::OrdinalIgnoreCase)) { return $row }
    }
    return $null
}

function Get-ReleaseGateTagProposal {
    <#
      The reservation New-ReleaseGateTagReservation would make, computed read-only:
      the existing row for this candidate, or the next free daily sequence. It never
      writes, so publish -WhatIf can report the proposed tag without reserving it.
      A different SHA under the same candidate id is a hard refusal (D-3).
    #>
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [Parameter(Mandatory = $true)][string]$Sha,
        [datetime]$CutUtc
    )
    if (-not (Test-ReleaseGateFullSha -Sha $Sha)) { throw ('reservation requires a full sha, got {0}' -f $Sha) }
    if ($null -eq $CutUtc -or $CutUtc -eq [datetime]::MinValue) { $CutUtc = Get-NightlyUtcNow }
    $journal = Get-ReleaseGatePublicationJournal -Path $JournalPath
    $existing = Get-ReleaseGateReservedTag -Journal $journal -CandidateId $CandidateId -Sha $Sha
    if ($null -ne $existing) {
        if (-not [string]::Equals([string]$existing.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('sha {0} already reserved under candidate {1}' -f $Sha, $existing.candidateId)
        }
        if (-not [string]::Equals([string]$existing.sha, $Sha, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('candidate {0} already reserved for sha {1}' -f $CandidateId, $existing.sha)
        }
        return [pscustomobject]@{ Tag = [string]$existing.tag; Sequence = [int]$existing.sequence; Reused = $true; Row = $existing; Journal = $journal }
    }
    $day = $CutUtc.ToUniversalTime().ToString('yyyy.MM.dd')
    $used = @()
    foreach ($row in @($journal.reservations)) {
        $parsed = Test-ReleaseGateTag -Tag ([string]$row.tag)
        if ($parsed.Ok -and $parsed.Date.ToString('yyyy.MM.dd') -eq $day) { $used += [int]$parsed.Sequence }
    }
    $next = 1
    while ($used -contains $next) { $next++ }
    return [pscustomobject]@{ Tag = (New-ReleaseGateTag -CutUtc $CutUtc -Sequence $next); Sequence = $next; Reused = $false; Row = $null; Journal = $journal }
}

function New-ReleaseGateTagReservation {
    <#
      Reserve the next daily sequence, or return the existing reservation for this
      candidate. Reservation is idempotent by candidate id; a different SHA under
      the same candidate id is a hard refusal (D-3 immutability).
    #>
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [Parameter(Mandatory = $true)][string]$Sha,
        [datetime]$CutUtc
    )
    $proposal = Get-ReleaseGateTagProposal -JournalPath $JournalPath -CandidateId $CandidateId -Sha $Sha -CutUtc $CutUtc
    if ($proposal.Reused) {
        return [pscustomobject]@{ Tag = $proposal.Tag; Sequence = $proposal.Sequence; Reused = $true; Row = $proposal.Row }
    }
    $journal = $proposal.Journal
    $tag = $proposal.Tag
    $next = $proposal.Sequence
    $rows = @()
    foreach ($row in @($journal.reservations)) { $rows += $row }
    $rows += [ordered]@{
        candidateId = $CandidateId
        sha = $Sha
        tag = $tag
        sequence = $next
        reservedAt = (Get-NightlyUtcNow).ToString('o')
        published = $false
        releaseId = ''
        releaseUrl = ''
    }
    Write-NightlyAtomicJson -Path $JournalPath -Object ([ordered]@{
        schemaVersion = $script:ReleaseGateManifestSchema
        reservations = $rows
    })
    return [pscustomobject]@{ Tag = $tag; Sequence = $next; Reused = $false }
}

function Set-ReleaseGateReservationPublished {
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [string]$ReleaseId = '',
        [string]$ReleaseUrl = ''
    )
    $journal = Get-ReleaseGatePublicationJournal -Path $JournalPath
    $rows = @()
    $found = $false
    foreach ($row in @($journal.reservations)) {
        if ([string]::Equals([string]$row.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            $found = $true
            $rows += [ordered]@{
                candidateId = [string]$row.candidateId
                sha = [string]$row.sha
                tag = [string]$row.tag
                sequence = [int]$row.sequence
                reservedAt = [string]$row.reservedAt
                published = $true
                releaseId = $ReleaseId
                releaseUrl = $ReleaseUrl
                publishedAt = (Get-NightlyUtcNow).ToString('o')
            }
        } else { $rows += $row }
    }
    if (-not $found) { throw ('no reservation for candidate {0}' -f $CandidateId) }
    Write-NightlyAtomicJson -Path $JournalPath -Object ([ordered]@{
        schemaVersion = $script:ReleaseGateManifestSchema
        reservations = $rows
    })
    return $true
}

# ---------------------------------------------------------------- manifest ---

$script:ReleaseGateManifestAllowlist = @(
    'schemaVersion', 'repository', 'tag', 'candidateRef', 'candidateId', 'sha',
    'nativeRunId', 'windmillJobId', 'scheduleSlot', 'startedAt', 'completedAt',
    'policyHash', 'profile', 'scriptHashes', 'buildHash', 'bundleHash',
    'suites', 'exclusions', 'summaryDigest', 'capabilities', 'publicationAuthorityDigest'
)

function New-ReleaseGateManifest {
    <#
      D-8: the SHA-bound manifest. Only allowlisted keys reach the public asset;
      raw logs, credentials, hostnames and transcripts never do.
    #>
    param([hashtable]$Fields)
    $manifest = [ordered]@{}
    $manifest['schemaVersion'] = $script:ReleaseGateManifestSchema
    foreach ($key in $script:ReleaseGateManifestAllowlist) {
        if ($key -eq 'schemaVersion') { continue }
        if ($Fields -and $Fields.ContainsKey($key)) { $manifest[$key] = $Fields[$key] }
        else { $manifest[$key] = $null }
    }
    if ($Fields) {
        foreach ($key in $Fields.Keys) {
            if ($script:ReleaseGateManifestAllowlist -notcontains $key) {
                throw ('manifest field {0} is not allowlisted' -f $key)
            }
        }
    }
    return $manifest
}

function Test-ReleaseGateManifestAllowlist {
    param($Manifest)
    $extra = @()
    $map = ConvertTo-NightlyPolicyMap -Object $Manifest
    foreach ($key in @($map.Keys)) {
        if ($script:ReleaseGateManifestAllowlist -notcontains [string]$key) { $extra += [string]$key }
    }
    return [pscustomobject]@{ Ok = ($extra.Count -eq 0); Extra = $extra }
}

function Get-ReleaseGateManifestDigest {
    param($Manifest)
    return (Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object $Manifest))
}

# -------------------------------------------------------------- publication ---

function Test-ReleaseGatePublicationGate {
    <#
      D-8/D-15: publication preconditions. Every one of these is a hard refusal; none
      of them can be waived by a later readback succeeding. The required suite set
      and the pass/fail verdict come ONLY from the pinned authority and its full
      execution ledger (Test-ReleaseGateAuthority); a report or summary confers
      none. ExpectedPolicyHash / RequiredSuites are legacy extra equality
      assertions against that authority, never a replacement for it, and an
      absent value never waives the pinned requirement.
    #>
    param(
        $Green,
        $Candidate,
        [string]$RemoteSha = '',
        $Authority = $null,
        [string]$ExpectedPolicyHash = '',
        [string[]]$RequiredSuites = @()
    )
    $reasons = @()
    $verdict = Get-ReleaseGateCreditVerdict -State $Green
    if ($verdict.CreditKind -ne 'rc-release') { $reasons += 'not-rc-complete-green' }
    if ($null -eq $Candidate) { $reasons += 'missing-candidate-journal' }
    else {
        $sha = [string]$Green.sha
        if (-not [string]::Equals([string]$Candidate.sha, $sha, [StringComparison]::OrdinalIgnoreCase)) { $reasons += 'candidate-sha-mismatch' }
        if (-not [string]::Equals([string]$Candidate.candidateId, [string]$Green.candidateId, [StringComparison]::OrdinalIgnoreCase)) { $reasons += 'candidate-id-mismatch' }
        if (-not [string]::IsNullOrWhiteSpace($RemoteSha) -and -not [string]::Equals($RemoteSha, $sha, [StringComparison]::OrdinalIgnoreCase)) { $reasons += 'remote-candidate-moved' }
        if ([string]::IsNullOrWhiteSpace($RemoteSha)) { $reasons += 'remote-candidate-unresolved' }
    }
    if ([bool]$Green.noReport) { $reasons += 'no-report-run' }
    if ([bool]$Green.diagnostic) { $reasons += 'diagnostic-run' }
    if ([bool]$Green.seamed) { $reasons += 'seam-driven-run' }
    if ($null -eq $Authority) {
        $reasons += 'missing-authority'
    } else {
        foreach ($r in @($Authority.Reasons)) { $reasons += [string]$r }
        $pinned = $null
        if ($Authority.Pinned) { $pinned = $Authority.Pinned.Authority }
        $pinnedHash = ''
        $pinnedSuites = @()
        if ($pinned) {
            $pinnedHash = [string]$pinned.policyHash
            $pinnedSuites = @($pinned.requiredSuites | ForEach-Object { [string]$_ })
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedPolicyHash) -and
            -not [string]::Equals($pinnedHash, $ExpectedPolicyHash, [StringComparison]::OrdinalIgnoreCase)) {
            $reasons += 'policy-hash-mismatch'
        }
        if (@($RequiredSuites).Count -gt 0) {
            $want = @($RequiredSuites | ForEach-Object { [string]$_ } | Sort-Object -Unique)
            $have = @($pinnedSuites | Sort-Object -Unique)
            if (($want -join ',') -cne ($have -join ',')) { $reasons += 'required-suites-mismatch' }
        }
    }
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = @($reasons | Select-Object -Unique) }
}

# ------------------------------------------------------------------- seams ---
# Git, GitHub, Windmill and the publisher's clock are injected at the I/O boundary
# so the contract tests drive the real production code paths. Nothing below
# replaces a predicate or a gate: a seam may only answer for the remote or the
# time, never decide whether to publish.
#
# Review bf928242: the publisher admits seams only in explicit test mode
# (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1'), and refuses test mode whenever a
# real remote would be reached. A real publication therefore never runs on an
# injected clock, and the clock seam itself refuses outside test mode.

$script:ReleaseGateTestModeVariable = 'ANTIPHON_RELEASE_GATE_TEST_MODE'

function Test-ReleaseGateTestMode {
    # Explicit opt-in only: the exact value '1'. Unset or anything else is production.
    return ([string][Environment]::GetEnvironmentVariable($script:ReleaseGateTestModeVariable) -ceq '1')
}

function Import-ReleaseGatePublisherSeams {
    <#
      The publisher's seam admission. Outside test mode a seams file is refused
      before it is loaded ('seams-outside-test-mode'). In test mode both the Git and
      the GitHub adapters must be fakes; no seams file, or one without either adapter,
      would reach a real remote and is refused ('test-mode-real-remote'). Returns ''
      when admitted; a refusal leaves no seam loaded.
    #>
    param([string]$SeamsPath)
    $script:ReleaseGateSeams = $null
    $testMode = Test-ReleaseGateTestMode
    $hasSeams = -not [string]::IsNullOrWhiteSpace($SeamsPath)
    if (-not $testMode) {
        if ($hasSeams) { return 'seams-outside-test-mode' }
        return ''
    }
    if ($hasSeams) { Import-ReleaseGateSeams -SeamsPath $SeamsPath }
    if (-not ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.Git -and $script:ReleaseGateSeams.GitHub)) {
        $script:ReleaseGateSeams = $null
        return 'test-mode-real-remote'
    }
    return ''
}

function Import-ReleaseGateSeams {
    param([string]$SeamsPath)
    if ([string]::IsNullOrWhiteSpace($SeamsPath)) { return }
    if (-not (Test-Path -LiteralPath $SeamsPath)) { throw ('missing seams {0}' -f $SeamsPath) }
    $script:ReleaseGateSeams = $null
    . $SeamsPath
    if ($ReleaseGateSeams) { $script:ReleaseGateSeams = $ReleaseGateSeams }
}

function Get-ReleaseGateUtcNow {
    # D-15: the clock evidence timestamps are bounded by. Tests inject it, in test
    # mode only; production reads the nightly clock (wall clock unless a nightly seam
    # answers). A clock seam outside test mode is refused, never silently used.
    if ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.UtcNow) {
        if (-not (Test-ReleaseGateTestMode)) { throw 'release-gate clock seam refused outside test mode' }
        $value = @($script:ReleaseGateSeams.UtcNow.Invoke())
        if ($value.Count -gt 0 -and $value[0] -is [datetime]) { return ([datetime]$value[0]).ToUniversalTime() }
        throw 'release-gate clock seam returned no instant'
    }
    return (Get-NightlyUtcNow).ToUniversalTime()
}

function Invoke-ReleaseGateGit {
    param([string]$WorkingDirectory, [string[]]$Arguments)
    if ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.Git) {
        return $script:ReleaseGateSeams.Git.Invoke($WorkingDirectory, $Arguments)
    }
    return (Invoke-NightlyGit -WorkingDirectory $WorkingDirectory -Arguments $Arguments)
}

function Invoke-ReleaseGateGitHub {
    <#
      One call shape for every GitHub operation: Verb plus an argument bag. The
      default implementation shells out to the already-authorized gh CLI; this
      function never mints, reads or prints a credential.
    #>
    param([string]$Verb, [hashtable]$Arguments = @{}, [string]$WorkingDirectory = '')
    if ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.GitHub) {
        return $script:ReleaseGateSeams.GitHub.Invoke($Verb, $Arguments, $WorkingDirectory)
    }
    $ghArgs = @()
    switch ($Verb) {
        'release-view' { $ghArgs = @('release', 'view', [string]$Arguments.tag, '--json', 'id,name,url,isDraft,tagName') }
        'release-create' {
            $ghArgs = @('release', 'create', [string]$Arguments.tag, '--verify-tag', '--draft',
                        '--title', [string]$Arguments.tag, '--notes-file', [string]$Arguments.notesFile)
        }
        'release-upload' { $ghArgs = @('release', 'upload', [string]$Arguments.tag, [string]$Arguments.asset, '--clobber') }
        'release-publish' { $ghArgs = @('release', 'edit', [string]$Arguments.tag, '--draft=false') }
        default { throw ('unknown github verb {0}' -f $Verb) }
    }
    $start = @{ FilePath = 'gh'; ArgumentList = $ghArgs; WorkingDirectory = $WorkingDirectory; PassThru = $true; NoNewWindow = $true; Wait = $true }
    $proc = Start-NightlyProcess -StartParams $start
    $code = 1
    if ($null -ne $proc) { $code = [int]$proc.ExitCode }
    return [pscustomobject]@{ ExitCode = $code; Output = ''; Verb = $Verb }
}

function Invoke-ReleaseGateWindmill {
    <#
      D-7: registration uses the installed Windmill HTTP API, never a database
      write. The token is read from an operator-placed file by the caller and is
      passed straight into the Authorization header; it is never logged.
    #>
    param([string]$Method, [string]$Path, $Body = $null, [hashtable]$Context = @{})
    if ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.Windmill) {
        return $script:ReleaseGateSeams.Windmill.Invoke($Method, $Path, $Body, $Context)
    }
    $baseUrl = [string]$Context.baseUrl
    if ([string]::IsNullOrWhiteSpace($baseUrl)) { throw 'windmill base url required' }
    $uri = ($baseUrl.TrimEnd('/') + $Path)
    $headers = @{ Authorization = ('Bearer ' + [string]$Context.token) }
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers; ErrorAction = 'Stop' }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 12)
        $params.ContentType = 'application/json'
    }
    try {
        $response = Invoke-RestMethod @params
        return [pscustomobject]@{ Ok = $true; Status = 200; Body = $response }
    } catch {
        $status = 0
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $status = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Ok = $false; Status = $status; Body = $null; Error = $_.Exception.Message }
    }
}

function Get-ReleaseGateTokenFromFile {
    # The token never reaches a log line, a script payload or a preview diff.
    param([string]$TokenFile)
    if ([string]::IsNullOrWhiteSpace($TokenFile)) { throw 'tokenFile is required' }
    if (-not (Test-Path -LiteralPath $TokenFile)) { throw ('token file missing: {0}' -f $TokenFile) }
    $text = [System.IO.File]::ReadAllText($TokenFile).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { throw ('token file empty: {0}' -f $TokenFile) }
    return $text
}

function Write-ReleaseGateJournalStep {
    <#
      Durable intent before every remote write. Each completed transition is
      persisted before the next begins, so a crash or a lost response resumes the
      same journal instead of allocating a new identity.
    #>
    param([string]$Path, [string]$Step, [hashtable]$Fields = @{})
    $journal = [ordered]@{}
    if (Test-Path -LiteralPath $Path) {
        try {
            $existing = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($p in @($existing.PSObject.Properties)) { $journal[$p.Name] = $p.Value }
        } catch { throw ('unreadable journal {0}: {1}' -f $Path, $_.Exception.Message) }
    }
    $steps = @()
    if ($journal.Contains('steps')) { foreach ($x in @($journal['steps'])) { $steps += [string]$x } }
    if ($steps -notcontains $Step) { $steps += $Step }
    $journal['steps'] = $steps
    foreach ($k in $Fields.Keys) { $journal[$k] = $Fields[$k] }
    $journal['updatedAt'] = (Get-NightlyUtcNow).ToString('o')
    Write-NightlyAtomicJson -Path $Path -Object $journal
    return $journal
}

function Test-ReleaseGateJournalStep {
    param([string]$Path, [string]$Step)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try { $journal = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $false }
    foreach ($x in @($journal.steps)) { if ([string]$x -eq $Step) { return $true } }
    return $false
}
