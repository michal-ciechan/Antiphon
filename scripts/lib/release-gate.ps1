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

$script:ReleaseGateProfiles = @('nightly', 'rc')
$script:ReleaseGateCreditKinds = @('master-scheduled', 'rc-release')
$script:ReleaseGateDefaultReleaseRoot = 'C:\Antiphon\releases'
$script:ReleaseGateDefaultCoordinationRoot = 'C:\Antiphon\verification'
$script:ReleaseGateCandidateRefPattern = '^release/rc-(?<stamp>\d{8}T\d{6}Z)$'
$script:ReleaseGateLongLivedRefPattern = '^rc/(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$'
$script:ReleaseGateTagPattern = '^v(?<y>\d{4})\.(?<m>\d{2})\.(?<d>\d{2})\.(?<n>[1-9]\d*)(?:-rc\.(?<k>[1-9]\d*))?$'
$script:ReleaseGateLongLivedCandidateIdPattern = '^rc-\d{4}-\d{2}-\d{2}-[0-9a-f]{12}$'
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

function ConvertFrom-ReleaseGateRemoteRef {
    # ls-remote patterns can match suffixes. Only the complete second field is
    # identity; failed queries and unusable matching records are never absence.
    param($Result, [string]$Ref)
    $failed = [pscustomobject]@{ Status = 'Failed'; Sha = '' }
    if ($null -eq $Result -or [int]$Result.ExitCode -ne 0) { return $failed }
    $matches = @()
    foreach ($line in ([string]$Result.Output -split "`n")) {
        $fields = @($line.Trim() -split '\s+')
        if ($fields.Count -lt 2) {
            if ([string]::Equals($line.Trim(), $Ref, [StringComparison]::Ordinal)) { return $failed }
            continue
        }
        if (-not [string]::Equals($fields[1], $Ref, [StringComparison]::Ordinal)) { continue }
        if ($fields.Count -ne 2 -or -not (Test-ReleaseGateFullSha -Sha $fields[0])) { return $failed }
        $matches += $fields[0]
    }
    if ($matches.Count -gt 1) { return $failed }
    if ($matches.Count -eq 0) { return [pscustomobject]@{ Status = 'Absent'; Sha = '' } }
    return [pscustomobject]@{ Status = 'Found'; Sha = [string]$matches[0] }
}

function Get-ReleaseGateRemoteRef {
    param([string]$WorkingDirectory, [string]$Ref)
    if ($Ref.StartsWith('refs/heads/', [StringComparison]::Ordinal)) { $kind = '--heads' }
    elseif ($Ref.StartsWith('refs/tags/', [StringComparison]::Ordinal)) { $kind = '--tags' }
    else { throw 'remote ref must be fully qualified' }
    $read = Invoke-ReleaseGateGit -WorkingDirectory $WorkingDirectory -Arguments @('ls-remote', $kind, 'origin', $Ref)
    return (ConvertFrom-ReleaseGateRemoteRef -Result $read -Ref $Ref)
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

function Test-ReleaseGateCandidateRef {
    # D-3: a legacy stamp, or a long-lived rc/YYYY-MM-DD. Anything else is not a candidate.
    # The long-lived candidate id is rc-YYYY-MM-DD- plus the first 12 hex digits of -Sha.
    param([string]$Ref, [string]$Sha = '')
    if ([string]::IsNullOrWhiteSpace($Ref)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'empty-ref'; Stamp = ''; CandidateId = ''; Ref = ''; Kind = '' }
    }
    $normalised = $Ref
    if ($normalised.StartsWith('refs/heads/')) { $normalised = $normalised.Substring('refs/heads/'.Length) }
    if ($normalised -match $script:ReleaseGateCandidateRefPattern) {
        $stamp = $Matches['stamp']
        $parsed = [datetime]::MinValue
        $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
        if (-not [datetime]::TryParseExact($stamp, 'yyyyMMddTHHmmssZ', [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'ref-timestamp'; Stamp = $stamp; CandidateId = ''; Ref = ''; Kind = 'stamp' }
        }
        return [pscustomobject]@{
            Ok = $true
            Reason = ''
            Stamp = $stamp
            Kind = 'stamp'
            CandidateId = ('rc-{0}' -f $stamp)
            Ref = ('release/rc-{0}' -f $stamp)
            CutUtc = $parsed.ToUniversalTime()
        }
    }
    if ($normalised -match $script:ReleaseGateLongLivedRefPattern) {
        $dateText = '{0}-{1}-{2}' -f $Matches['y'], $Matches['m'], $Matches['d']
        $parsed = [datetime]::MinValue
        $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
        if (-not [datetime]::TryParseExact($dateText, 'yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'ref-date'; Stamp = ''; CandidateId = ''; Ref = ''; Kind = 'long-lived' }
        }
        $candidateId = ''
        if (-not [string]::IsNullOrWhiteSpace($Sha) -and (Test-ReleaseGateFullSha -Sha $Sha)) {
            $candidateId = 'rc-{0}-{1}' -f $dateText, $Sha.Substring(0, 12)
        }
        return [pscustomobject]@{
            Ok = $true
            Reason = ''
            Stamp = ''
            Kind = 'long-lived'
            CandidateId = $candidateId
            Ref = ('rc/{0}' -f $dateText)
            CutUtc = $parsed.ToUniversalTime()
        }
    }
    return [pscustomobject]@{ Ok = $false; Reason = 'ref-shape'; Stamp = ''; CandidateId = ''; Ref = ''; Kind = '' }
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
    # Absent scope is the historical v1 Full record. Explicit scope must be Full.
    $scopePresent = if ($State -is [Collections.IDictionary]) { $State.Contains('coverageScope') } else { $null -ne $State.PSObject.Properties['coverageScope'] }
    if ($scopePresent -and ($State.coverageScope -isnot [string] -or $State.coverageScope -cne 'Full')) {
        $reasons += 'full-credit-scope-required'
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
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'empty-tag'; Prerelease = $false; Rc = 0 }
    }
    if ($Tag -notmatch $script:ReleaseGateTagPattern) {
        return [pscustomobject]@{ Ok = $false; Reason = 'tag-shape'; Prerelease = $false; Rc = 0 }
    }
    $y = [int]$Matches['y']; $m = [int]$Matches['m']; $d = [int]$Matches['d']; $n = [int]$Matches['n']
    $k = 0
    $prerelease = $false
    if ($Matches.ContainsKey('k') -and -not [string]::IsNullOrWhiteSpace([string]$Matches['k'])) {
        $k = [int]$Matches['k']
        $prerelease = $true
    }
    if ($m -lt 1 -or $m -gt 12 -or $d -lt 1 -or $d -gt 31) {
        return [pscustomobject]@{ Ok = $false; Reason = 'tag-date'; Prerelease = $prerelease; Rc = $k }
    }
    try { $date = [datetime]::new($y, $m, $d, 0, 0, 0, [System.DateTimeKind]::Utc) } catch {
        return [pscustomobject]@{ Ok = $false; Reason = 'tag-date'; Prerelease = $prerelease; Rc = $k }
    }
    return [pscustomobject]@{ Ok = $true; Reason = ''; Date = $date; Sequence = $n; Tag = $Tag; Prerelease = $prerelease; Rc = $k }
}

function New-ReleaseGateTag {
    # D-8: vYYYY.MM.DD.N from the UTC cut date and a positive daily sequence.
    param([datetime]$CutUtc, [int]$Sequence)
    if ($Sequence -lt 1) { throw ('release sequence must be positive, got {0}' -f $Sequence) }
    $u = $CutUtc.ToUniversalTime()
    return ('v{0:0000}.{1:00}.{2:00}.{3}' -f $u.Year, $u.Month, $u.Day, $Sequence)
}

function New-ReleaseGatePreReleaseTag {
    # D-4: vYYYY.MM.DD.N-rc.K. K is a positive integer with no leading zero.
    param([datetime]$CutUtc, [int]$Sequence, [int]$Rc)
    if ($Rc -lt 1) { throw ('release rc number must be positive, got {0}' -f $Rc) }
    return ((New-ReleaseGateTag -CutUtc $CutUtc -Sequence $Sequence) + '-rc.' + [string]$Rc)
}

function Test-ReleaseGateLongLivedCandidateId {
    param([string]$CandidateId)
    if ([string]::IsNullOrWhiteSpace($CandidateId)) { return $false }
    return ($CandidateId -cmatch $script:ReleaseGateLongLivedCandidateIdPattern)
}

function Get-ReleaseGateOptionalInt {
    param($Row, [string]$Name)
    if ($null -eq $Row) { return 0 }
    if ($Row.PSObject.Properties.Name -notcontains $Name) { return 0 }
    $text = [string]$Row.$Name
    if ([string]::IsNullOrWhiteSpace($text)) { return 0 }
    return [int]$text
}

function Get-ReleaseGateOptionalText {
    param($Row, [string]$Name)
    if ($null -eq $Row) { return '' }
    if ($Row.PSObject.Properties.Name -notcontains $Name) { return '' }
    return [string]$Row.$Name
}

function New-ReleaseGateReservationEntry {
    # Stamp rows keep the historical keys. rc and finalTag are written only when set,
    # so a later publish of the same row does not drop them and does not invent them.
    param(
        [string]$CandidateId,
        [string]$Sha,
        [string]$Tag,
        [int]$Sequence,
        [string]$ReservedAt,
        [bool]$Published,
        [string]$ReleaseId,
        [string]$ReleaseUrl,
        [string]$PublishedAt = '',
        [int]$Rc = 0,
        [string]$FinalTag = ''
    )
    $entry = [ordered]@{
        candidateId = $CandidateId
        sha = $Sha
        tag = $Tag
        sequence = $Sequence
        reservedAt = $ReservedAt
        published = $Published
        releaseId = $ReleaseId
        releaseUrl = $ReleaseUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($PublishedAt)) { $entry.publishedAt = $PublishedAt }
    if ($Rc -ge 1) { $entry.rc = $Rc }
    if (-not [string]::IsNullOrWhiteSpace($FinalTag)) { $entry.finalTag = $FinalTag }
    return $entry
}

function Get-ReleaseGatePreReleaseAllocation {
    # D-4: one open N per UTC day. The next new SHA on that day takes the next K.
    # A final tag, or a reservation whose finalTag names that N, closes N.
    # The next pre-release then takes the next free N at K=1. A new UTC day starts at N=1.
    param($Journal, [datetime]$CutUtc)
    $day = $CutUtc.ToUniversalTime().ToString('yyyy.MM.dd')
    $closed = @()
    $used = @()
    $openN = @()
    $openK = @()
    foreach ($row in @($Journal.reservations)) {
        if ($null -eq $row) { continue }
        $parsed = Test-ReleaseGateTag -Tag ([string]$row.tag)
        if ($parsed.Ok -and $parsed.Date.ToUniversalTime().ToString('yyyy.MM.dd') -eq $day) {
            $n = [int]$parsed.Sequence
            if ($used -notcontains $n) { $used += $n }
            if ($parsed.Prerelease) {
                $openN += $n
                $openK += [int]$parsed.Rc
            } elseif ($closed -notcontains $n) {
                $closed += $n
            }
        }
        $finalText = Get-ReleaseGateOptionalText -Row $row -Name 'finalTag'
        if (-not [string]::IsNullOrWhiteSpace($finalText)) {
            $finalParsed = Test-ReleaseGateTag -Tag $finalText
            if ($finalParsed.Ok -and -not $finalParsed.Prerelease -and $finalParsed.Date.ToUniversalTime().ToString('yyyy.MM.dd') -eq $day) {
                $fn = [int]$finalParsed.Sequence
                if ($closed -notcontains $fn) { $closed += $fn }
                if ($used -notcontains $fn) { $used += $fn }
            }
        }
    }
    $bestN = 0
    $bestK = 0
    for ($i = 0; $i -lt $openN.Count; $i++) {
        if ($closed -contains $openN[$i]) { continue }
        if ($openN[$i] -gt $bestN -or ($openN[$i] -eq $bestN -and $openK[$i] -gt $bestK)) {
            $bestN = [int]$openN[$i]
            $bestK = [int]$openK[$i]
        }
    }
    if ($bestN -ge 1) { return [pscustomobject]@{ Sequence = $bestN; Rc = ($bestK + 1) } }
    $next = 1
    while (($used -contains $next) -or ($closed -contains $next)) { $next++ }
    return [pscustomobject]@{ Sequence = $next; Rc = 1 }
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
        return [pscustomobject]@{
            Tag = [string]$existing.tag
            Sequence = [int]$existing.sequence
            Rc = (Get-ReleaseGateOptionalInt -Row $existing -Name 'rc')
            Reused = $true
            Row = $existing
            Journal = $journal
        }
    }
    if (Test-ReleaseGateLongLivedCandidateId -CandidateId $CandidateId) {
        $alloc = Get-ReleaseGatePreReleaseAllocation -Journal $journal -CutUtc $CutUtc
        $pre = New-ReleaseGatePreReleaseTag -CutUtc $CutUtc -Sequence $alloc.Sequence -Rc $alloc.Rc
        return [pscustomobject]@{ Tag = $pre; Sequence = $alloc.Sequence; Rc = $alloc.Rc; Reused = $false; Row = $null; Journal = $journal }
    }
    $day = $CutUtc.ToUniversalTime().ToString('yyyy.MM.dd')
    $used = @()
    foreach ($row in @($journal.reservations)) {
        if ($null -eq $row) { continue }
        $parsed = Test-ReleaseGateTag -Tag ([string]$row.tag)
        if ($parsed.Ok -and $parsed.Date.ToString('yyyy.MM.dd') -eq $day) { $used += [int]$parsed.Sequence }
    }
    $next = 1
    while ($used -contains $next) { $next++ }
    return [pscustomobject]@{ Tag = (New-ReleaseGateTag -CutUtc $CutUtc -Sequence $next); Sequence = $next; Rc = 0; Reused = $false; Row = $null; Journal = $journal }
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
    $rows += (New-ReleaseGateReservationEntry -CandidateId $CandidateId -Sha $Sha -Tag $tag -Sequence $next `
        -ReservedAt (Get-NightlyUtcNow).ToString('o') -Published $false -ReleaseId '' -ReleaseUrl '' -Rc ([int]$proposal.Rc))
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
            $rows += (New-ReleaseGateReservationEntry -CandidateId ([string]$row.candidateId) -Sha ([string]$row.sha) `
                -Tag ([string]$row.tag) -Sequence ([int]$row.sequence) -ReservedAt ([string]$row.reservedAt) `
                -Published $true -ReleaseId $ReleaseId -ReleaseUrl $ReleaseUrl `
                -PublishedAt (Get-NightlyUtcNow).ToString('o') `
                -Rc (Get-ReleaseGateOptionalInt -Row $row -Name 'rc') `
                -FinalTag (Get-ReleaseGateOptionalText -Row $row -Name 'finalTag'))
        } else { $rows += $row }
    }
    if (-not $found) { throw ('no reservation for candidate {0}' -f $CandidateId) }
    Write-NightlyAtomicJson -Path $JournalPath -Object ([ordered]@{
        schemaVersion = $script:ReleaseGateManifestSchema
        reservations = $rows
    })
    return $true
}

function Set-ReleaseGateReservationFinalTag {
    # Record the final CalVer beside the pre-release reservation. The reserved tag
    # stays the pre-release name; a second row for the same candidate would make
    # reuse return the wrong tag. Returns false when the row or the final name is absent.
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][string]$CandidateId,
        [Parameter(Mandatory = $true)][string]$FinalTag
    )
    $parsed = Test-ReleaseGateTag -Tag $FinalTag
    if (-not $parsed.Ok -or $parsed.Prerelease) { return $false }
    $journal = Get-ReleaseGatePublicationJournal -Path $JournalPath
    $rows = @()
    $found = $false
    foreach ($row in @($journal.reservations)) {
        if ($null -eq $row) { continue }
        if ([string]::Equals([string]$row.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            $found = $true
            $rows += (New-ReleaseGateReservationEntry -CandidateId ([string]$row.candidateId) -Sha ([string]$row.sha) `
                -Tag ([string]$row.tag) -Sequence ([int]$row.sequence) -ReservedAt ([string]$row.reservedAt) `
                -Published ([bool]$row.published) -ReleaseId ([string]$row.releaseId) -ReleaseUrl ([string]$row.releaseUrl) `
                -PublishedAt (Get-ReleaseGateOptionalText -Row $row -Name 'publishedAt') `
                -Rc (Get-ReleaseGateOptionalInt -Row $row -Name 'rc') -FinalTag $FinalTag)
        } else { $rows += $row }
    }
    if (-not $found) { return $false }
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
