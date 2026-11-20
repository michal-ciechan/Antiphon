#requires -Version 5.1
# CARD-0599: release-gate primitives shared by the nightly master lane and the RC lane.
#
# D-2  credit kinds: master green earns master readiness credit; RC green earns release
#       credit only. Unknown combinations fail closed.
# D-3  candidate identity: legacy release/rc-YYYYMMDDTHHMMSSZ pinned to one full SHA,
#      or long-lived rc/YYYY-MM-DD whose candidate id is that date plus the tested SHA.
# D-4  one shared native-run lock serialises the master and RC lanes.
# D-8  CalVer vYYYY.MM.DD.N, optional pre-release vYYYY.MM.DD.N-rc.K, and the SHA-bound manifest.
#
# ASCII-only on purpose - parseable under Windows PowerShell 5.1.

if ($script:AntiphonReleaseGateLoaded) { return }
$script:AntiphonReleaseGateLoaded = $true
# The policy and canonical-JSON helpers are compiled (Antiphon.Nightly): load the core.
. (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable

$script:ReleaseGateProfiles = @('nightly', 'rc')
$script:ReleaseGateCreditKinds = @('master-scheduled', 'rc-release')

# ---------------------------------------------------------------- identity ---

function Get-ReleaseGateRemoteRef {
    param([string]$WorkingDirectory, [string]$Ref)
    if ($Ref.StartsWith('refs/heads/', [StringComparison]::Ordinal)) { $kind = '--heads' }
    elseif ($Ref.StartsWith('refs/tags/', [StringComparison]::Ordinal)) { $kind = '--tags' }
    else { throw 'remote ref must be fully qualified' }
    $read = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('ls-remote', $kind, 'origin', $Ref)
    return ([Antiphon.Nightly.ReleaseGateTags]::RemoteRef($read, $Ref))
}

function Get-ReleaseGateRemoteHead {
    param([string]$WorkingDirectory, [string]$Ref)
    if (-not $Ref.StartsWith('refs/heads/', [StringComparison]::Ordinal)) { $Ref = 'refs/heads/' + $Ref }
    return (Get-ReleaseGateRemoteRef -WorkingDirectory $WorkingDirectory -Ref $Ref)
}

function Get-ReleaseGateRemoteTag {
    param([string]$WorkingDirectory, [string]$Tag)
    $ref = 'refs/tags/' + $Tag
    $peel = Get-ReleaseGateRemoteRef -WorkingDirectory $WorkingDirectory -Ref ($ref + '^{}')
    $direct = Get-ReleaseGateRemoteRef -WorkingDirectory $WorkingDirectory -Ref $ref
    if ($peel.Status -eq 'Absent' -and $direct.Status -eq 'Absent') {
        return [pscustomobject]@{ Status = 'Absent'; Sha = ''; ObjectSha = '' }
    }
    if ($peel.Status -ne 'Found' -or $direct.Status -ne 'Found') {
        return [pscustomobject]@{ Status = 'Failed'; Sha = ''; ObjectSha = $direct.Sha }
    }
    return [pscustomobject]@{ Status = 'Found'; Sha = $peel.Sha; ObjectSha = $direct.Sha }
}

function Push-ReleaseGateExactTag {
    # Git applies DWIM even to explicit push destinations. Pattern refspecs map
    # complete refs literally. Give one such mapping its own private namespace,
    # containing only our annotated tag object, and remove that ref with a CAS.
    param([string]$WorkingDirectory, [string]$Tag)
    $failed = @{ ExitCode = 1; Output = '' }
    $object = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('show-ref', '--verify', '--hash', ('refs/tags/' + $Tag))
    $sha = ([string]$object.Output).Trim()
    if ($null -eq $object -or [int]$object.ExitCode -ne 0 -or -not ([Antiphon.Nightly.ReleaseGateCredit]::FullSha($sha))) { return $failed }
    $prefix = 'refs/antiphon-release-push/' + [guid]::NewGuid().ToString('N') + '/'
    $privateRef = $prefix + $Tag
    $create = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('update-ref', $privateRef, $sha, ('0' * 40))
    if ($null -eq $create -or [int]$create.ExitCode -ne 0) { return $failed }
    $push = $failed
    $cleanup = $null
    try {
        $push = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('push', 'origin', ($prefix + '*:refs/tags/*'))
    } finally {
        $cleanup = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('update-ref', '-d', $privateRef, $sha)
    }
    if ($null -eq $cleanup -or [int]$cleanup.ExitCode -ne 0) { return $failed }
    return $push
}

function New-ReleaseGateCandidateRef {
    param([datetime]$Utc)
    if ($null -eq $Utc -or $Utc -eq [datetime]::MinValue) { $Utc = (Get-Date).ToUniversalTime() }
    return ('release/rc-{0}' -f $Utc.ToUniversalTime().ToString('yyyyMMddTHHmmss') + 'Z')
}

# ------------------------------------------------------------------- state ---

# -------------------------------------------------------------- shared lock ---

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
    $lockPath = [Antiphon.Nightly.ReleaseGatePublication]::NativeLockPath($CoordinationRoot)
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
    $lockPath = [Antiphon.Nightly.ReleaseGatePublication]::NativeLockPath($CoordinationRoot)
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

function Get-ReleaseGateRemoteTagInventory {
    param([string]$WorkingDirectory)
    $read = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('ls-remote', '--tags', '--refs', 'origin')
    $failed = [pscustomobject]@{ Ok = $false; Tags = @() }
    if ($null -eq $read -or [int]$read.ExitCode -ne 0) { return $failed }
    $names = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($line in ([string]$read.Output -split "`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $fields = @($line.Trim() -split '\s+')
        if ($fields.Count -ne 2) { return $failed }
        $ref = $fields[1]
        if (-not $ref.StartsWith('refs/tags/', [StringComparison]::Ordinal)) { continue }
        $tag = $ref.Substring('refs/tags/'.Length)
        $parsed = [Antiphon.Nightly.ReleaseGateTags]::Tag($tag)
        if (-not $parsed.Ok) { continue }
        $canonical = [Antiphon.Nightly.ReleaseGateTags]::NewTag($parsed.Date, $parsed.Sequence)
        if ($parsed.Prerelease) { $canonical += '-rc.' + [string]$parsed.Rc }
        if ($tag -cne $canonical) { continue }
        if (-not ([Antiphon.Nightly.ReleaseGateCredit]::FullSha($fields[0]))) { return $failed }
        if ($names.ContainsKey($tag) -and $names[$tag] -cne $fields[0]) { return $failed }
        $names[$tag] = $fields[0]
    }
    return [pscustomobject]@{ Ok = $true; Tags = @($names.Keys) }
}

function Get-ReleaseGatePublicationJournal {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ schemaVersion = [Antiphon.Nightly.ReleaseGatePublication]::ManifestSchema; reservations = @() }
    }
    try { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) }
    catch { throw ('unreadable publication journal {0}: {1}' -f $Path, $_.Exception.Message) }
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
        [datetime]$CutUtc,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RemoteTags
    )
    if (-not ([Antiphon.Nightly.ReleaseGateCredit]::FullSha($Sha))) { throw ('reservation requires a full sha, got {0}' -f $Sha) }
    if ($null -eq $CutUtc -or $CutUtc -eq [datetime]::MinValue) { $CutUtc = Get-NightlyUtcNow }
    $journal = Get-ReleaseGatePublicationJournal -Path $JournalPath
    $existing = [Antiphon.Nightly.ReleaseGateTags]::ReservedTag($journal, $CandidateId, $Sha)
    if ($null -ne $existing) {
        if (-not [string]::Equals([string]$existing.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('sha {0} already reserved under candidate {1}' -f $Sha, $existing.candidateId)
        }
        if (-not [string]::Equals([string]$existing.sha, $Sha, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('candidate {0} already reserved for sha {1}' -f $CandidateId, $existing.sha)
        }
        return [pscustomobject]@{
            Tag = [string]$existing.tag
            Sequence = [int]$existing.sequence
            Rc = ([Antiphon.Nightly.ReleaseGateTags]::OptionalInt($existing, 'rc'))
            Reused = $true
            Row = $existing
            Journal = $journal
        }
    }
    if ([Antiphon.Nightly.ReleaseGateTags]::LongLivedCandidateId($CandidateId)) {
        $alloc = [Antiphon.Nightly.ReleaseGateTags]::Allocation($journal, $CutUtc, $RemoteTags)
        $pre = [Antiphon.Nightly.ReleaseGateTags]::NewPreReleaseTag($CutUtc, $alloc.Sequence, $alloc.Rc)
        return [pscustomobject]@{ Tag = $pre; Sequence = $alloc.Sequence; Rc = $alloc.Rc; Reused = $false; Row = $null; Journal = $journal }
    }
    $day = $CutUtc.ToUniversalTime().ToString('yyyy.MM.dd')
    $used = @()
    $occupancy = @($journal.reservations) + @($RemoteTags | ForEach-Object { [pscustomobject]@{ tag = $_ } })
    foreach ($row in $occupancy) {
        if ($null -eq $row) { continue }
        $parsed = [Antiphon.Nightly.ReleaseGateTags]::Tag(([string]$row.tag))
        if ($parsed.Ok -and $parsed.Date.ToString('yyyy.MM.dd') -eq $day) { $used += [int]$parsed.Sequence }
    }
    $next = 1
    while ($used -contains $next) { $next++ }
    return [pscustomobject]@{ Tag = ([Antiphon.Nightly.ReleaseGateTags]::NewTag($CutUtc, $next)); Sequence = $next; Rc = 0; Reused = $false; Row = $null; Journal = $journal }
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
        [datetime]$CutUtc,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RemoteTags
    )
    $proposal = Get-ReleaseGateTagProposal -JournalPath $JournalPath -CandidateId $CandidateId -Sha $Sha -CutUtc $CutUtc -RemoteTags $RemoteTags
    if ($proposal.Reused) {
        return [pscustomobject]@{ Tag = $proposal.Tag; Sequence = $proposal.Sequence; Reused = $true; Row = $proposal.Row }
    }
    $journal = $proposal.Journal
    $tag = $proposal.Tag
    $next = $proposal.Sequence
    $rows = @()
    foreach ($row in @($journal.reservations)) { $rows += $row }
    $rows += ([Antiphon.Nightly.ReleaseGateTags]::ReservationEntry($CandidateId, $Sha, $tag, $next, (Get-NightlyUtcNow).ToString('o'), $false, '', '', '', ([int]$proposal.Rc), ''))
    Write-NightlyAtomicJson -Path $JournalPath -Object ([ordered]@{
        schemaVersion = [Antiphon.Nightly.ReleaseGatePublication]::ManifestSchema
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
            $rows += ([Antiphon.Nightly.ReleaseGateTags]::ReservationEntry(([string]$row.candidateId), ([string]$row.sha), ([string]$row.tag), ([int]$row.sequence), ([string]$row.reservedAt), $true, $ReleaseId, $ReleaseUrl, (Get-NightlyUtcNow).ToString('o'), ([Antiphon.Nightly.ReleaseGateTags]::OptionalInt($row, 'rc')), ([Antiphon.Nightly.ReleaseGateTags]::OptionalText($row, 'finalTag'))))
        } else { $rows += $row }
    }
    if (-not $found) { throw ('no reservation for candidate {0}' -f $CandidateId) }
    Write-NightlyAtomicJson -Path $JournalPath -Object ([ordered]@{
        schemaVersion = [Antiphon.Nightly.ReleaseGatePublication]::ManifestSchema
        reservations = $rows
    })
    return $true
}

function Test-ReleaseGatePromotionReservation {
    # Promotion consumes an existing published reservation; it never creates one.
    param([string]$JournalPath, [string]$CandidateId, [string]$Sha, [string]$PreReleaseTag, [string]$FinalTag)
    $fail = { param($reason) [pscustomobject]@{ Ok = $false; Reason = ('promotion-reservation-' + $reason) } }
    if (-not (Test-Path -LiteralPath $JournalPath)) { return (& $fail 'missing') }
    try {
        $raw = Get-Content -LiteralPath $JournalPath -Raw -Encoding UTF8 -ErrorAction Stop
        # ConvertFrom-Json enumerates root arrays, including a one-object array.
        # Require an object in the source bytes before that boundary is lost.
        if (-not $raw.TrimStart().StartsWith('{', [StringComparison]::Ordinal)) { return (& $fail 'invalid') }
        $journal = $raw | ConvertFrom-Json -ErrorAction Stop
    } catch { return (& $fail 'invalid') }
    if ($journal -isnot [pscustomobject] -or
        ($journal.schemaVersion -isnot [int] -and $journal.schemaVersion -isnot [long]) -or
        $journal.schemaVersion -ne 1 -or $journal.reservations -isnot [array]) { return (& $fail 'invalid') }
    $matching = @()
    foreach ($row in @($journal.reservations)) {
        if ($row -isnot [pscustomobject]) { return (& $fail 'invalid') }
        foreach ($field in @('candidateId', 'sha', 'tag', 'releaseId', 'releaseUrl')) {
            if ($row.$field -isnot [string]) { return (& $fail 'invalid') }
        }
        if ($row.published -isnot [bool] -or
            ($row.PSObject.Properties.Name -contains 'finalTag' -and $row.finalTag -isnot [string])) { return (& $fail 'invalid') }
        if ($row.candidateId -ceq $CandidateId -or $row.sha -ieq $Sha -or $row.tag -ceq $PreReleaseTag) { $matching += $row }
    }
    if ($matching.Count -eq 0) { return (& $fail 'missing') }
    if ($matching.Count -ne 1) { return (& $fail 'ambiguous') }
    $row = $matching[0]
    if ($row.candidateId -cne $CandidateId) { return (& $fail 'candidate-mismatch') }
    if (-not ([Antiphon.Nightly.ReleaseGateCredit]::FullSha($row.sha)) -or $row.sha -ine $Sha) { return (& $fail 'sha-mismatch') }
    if ($row.tag -cne $PreReleaseTag) { return (& $fail 'tag-mismatch') }
    if (-not $row.published) { return (& $fail 'unpublished') }
    if ([string]::IsNullOrWhiteSpace($row.releaseId) -or [string]::IsNullOrWhiteSpace($row.releaseUrl)) { return (& $fail 'release-identity') }
    $parsed = [Antiphon.Nightly.ReleaseGateTags]::Tag($PreReleaseTag)
    if (-not $parsed.Ok -or -not $parsed.Prerelease -or
        $FinalTag -cne ([Antiphon.Nightly.ReleaseGateTags]::NewTag($parsed.Date, $parsed.Sequence)) -or
        (-not [string]::IsNullOrEmpty([string]$row.finalTag) -and $row.finalTag -cne $FinalTag)) { return (& $fail 'final-conflict') }
    return [pscustomobject]@{ Ok = $true; Reason = ''; Journal = $journal; Row = $row }
}

function Set-ReleaseGateReservationFinalTag {
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [Parameter(Mandatory = $true)][string]$Sha,
        [Parameter(Mandatory = $true)][string]$PreReleaseTag,
        [Parameter(Mandatory = $true)][string]$FinalTag
    )
    # Re-read and validate the admitted tuple immediately before atomic replacement.
    $check = Test-ReleaseGatePromotionReservation @PSBoundParameters
    if (-not $check.Ok) { return $false }
    $check.Row | Add-Member -NotePropertyName finalTag -NotePropertyValue $FinalTag -Force
    Write-NightlyAtomicJson -Path $JournalPath -Object $check.Journal
    $recorded = Test-ReleaseGatePromotionReservation @PSBoundParameters
    return ($recorded.Ok -and $recorded.Row.finalTag -ceq $FinalTag)
}

# ---------------------------------------------------------------- manifest ---

# -------------------------------------------------------------- publication ---

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
            if ($Arguments -and $Arguments.ContainsKey('prerelease') -and [bool]$Arguments['prerelease']) {
                $ghArgs += '--prerelease'
            }
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
