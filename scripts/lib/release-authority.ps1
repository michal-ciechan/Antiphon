#requires -Version 5.1
# CARD-0599 D-15 (B1): pinned policy and full execution-ledger publication authority.
# ASCII-only.
#
# The authority for an RC publication is the policy blob at the candidate's pinned
# commit plus the frozen execution plan and its full execution ledger. A summary or
# report is an OUTPUT of validation and never a source of required suites or success.
#
# Files under <candidate root>/authority/:
#   policy.json            raw bytes of <sha>:tests/test-execution-policy.json
#   authority.json         pinned identity: repository/ref/sha, blob id, digests, suites
#   discovery.json         full discovery roster per native suite (incl. excluded rows)
#   execution-plan.json    frozen chunk manifest (native) and declared entry rosters
#   ledger.json            execution records: prerequisites, chunks, rosters
#   publication-authority.json  digest over all of the above, written by the publisher
#
# Every git read here is a real git child with a bounded wait: the policy blob is
# never taken from a working tree and never from a seam.

if ($script:AntiphonReleaseAuthorityLoaded) { return }
$script:AntiphonReleaseAuthorityLoaded = $true

$script:ReleaseAuthorityPolicyPath = 'tests/test-execution-policy.json'
$script:ReleaseAuthorityRcSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
$script:ReleaseAuthorityRosterSuites = @('client', 'scripts')
$script:ReleaseAuthoritySchema = 1
$script:ReleaseAuthorityCoordinatorVersion = 'c599-b1'
$script:ReleaseAuthorityGitTimeoutMs = 30000
$script:ReleaseAuthorityScripts = @(
    'publish-release.ps1',
    'lib/release-gate.ps1',
    'lib/release-authority.ps1',
    'lib/nightly-policy.ps1',
    'lib/nightly-coverage.ps1',
    'lib/nightly-common.ps1'
)

# ------------------------------------------------------------------ helpers ---

function Get-ReleaseAuthorityDir {
    param([string]$CandidateRoot)
    return (Join-Path $CandidateRoot 'authority')
}

function Get-ReleaseAuthorityBytesSha256 {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($Bytes) } finally { $sha.Dispose() }
    return (-join ($hash | ForEach-Object { $_.ToString('x2') }))
}

function Get-ReleaseAuthorityGitBlobId {
    # git's object id for a blob: sha1("blob <len>\0" + bytes). Computed locally so
    # the pinned blob identity is checked independently of any single git answer.
    param([byte[]]$Bytes)
    $header = [System.Text.Encoding]::ASCII.GetBytes(('blob {0}' -f $Bytes.Length))
    $all = New-Object byte[] ($header.Length + 1 + $Bytes.Length)
    [Array]::Copy($header, 0, $all, 0, $header.Length)
    $all[$header.Length] = 0
    [Array]::Copy($Bytes, 0, $all, $header.Length + 1, $Bytes.Length)
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try { $hash = $sha1.ComputeHash($all) } finally { $sha1.Dispose() }
    return (-join ($hash | ForEach-Object { $_.ToString('x2') }))
}

function Invoke-ReleaseAuthorityGit {
    <#
      A real git child with captured raw stdout bytes and a bounded wait. On timeout
      the child is killed and joined before returning; the result is a failure.
    #>
    param([string]$RepositoryRoot, [string[]]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($a in @('-C', $RepositoryRoot) + @($Arguments)) { [void]$psi.ArgumentList.Add([string]$a) }
    $proc = $null
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $ms = New-Object System.IO.MemoryStream
        $copy = $proc.StandardOutput.BaseStream.CopyToAsync($ms)
        $err = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($script:ReleaseAuthorityGitTimeoutMs)) {
            try { $proc.Kill($true) } catch { }
            $proc.WaitForExit()
            return [pscustomobject]@{ ExitCode = -1; Bytes = [byte[]]@(); Text = ''; Error = 'git timed out' }
        }
        $proc.WaitForExit()
        [void]$copy.Wait($script:ReleaseAuthorityGitTimeoutMs)
        $bytes = $ms.ToArray()
        return [pscustomobject]@{
            ExitCode = [int]$proc.ExitCode
            Bytes = $bytes
            Text = [System.Text.Encoding]::UTF8.GetString($bytes)
            Error = [string]$err.Result
        }
    } catch {
        return [pscustomobject]@{ ExitCode = -1; Bytes = [byte[]]@(); Text = ''; Error = $_.Exception.Message }
    } finally {
        if ($null -ne $proc) { $proc.Dispose() }
    }
}

function ConvertFrom-ReleaseAuthorityPolicyBytes {
    <#
      Parses the pinned blob and validates schema, stated canonical hash and the rc
      profile. Throws with a stable reason token on any refusal.
    #>
    param([byte[]]$Bytes)
    $text = [System.Text.Encoding]::UTF8.GetString($Bytes)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    try { $obj = $text | ConvertFrom-Json } catch { throw 'policy-unparseable' }
    if ($null -eq $obj) { throw 'policy-unparseable' }
    $version = $obj.schemaVersion
    if ($null -eq $version -or ($version -isnot [int] -and $version -isnot [long]) -or [int]$version -ne 2) { throw 'policy-schema-unsupported' }
    $computed = Get-NightlyPolicyHash -Object $obj
    $stated = [string]$obj.policyHash
    if ([string]::IsNullOrWhiteSpace($stated) -or -not [string]::Equals($stated, $computed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'policy-hash-stale'
    }
    try { $profile = Get-NightlyPolicyProfile -PolicyObject $obj -Profile 'rc' } catch { throw 'policy-rc-profile-missing' }
    $suites = @($profile.RequiredSuites | ForEach-Object { [string]$_ })
    $missing = @($script:ReleaseAuthorityRcSuites | Where-Object { $suites -notcontains $_ })
    $unknown = @($suites | Where-Object { $script:ReleaseAuthorityRcSuites -notcontains $_ })
    if ($missing.Count -gt 0) { throw ('policy-rc-suite-missing:' + ($missing -join '+')) }
    if ($unknown.Count -gt 0) { throw ('policy-rc-suite-unknown:' + ($unknown -join '+')) }
    $map = Get-NightlySuiteMap -PolicyObject $obj
    foreach ($id in $suites) { if (-not $map.ContainsKey($id)) { throw ('policy-rc-suite-undefined:' + $id) } }
    $exclusions = @()
    foreach ($id in $suites) {
        foreach ($row in @(Get-NightlyProfileExclusions -PolicyObject $obj -Profile 'rc' -SuiteId $id)) {
            $exclusions += [ordered]@{ suite = $row.Suite; class = $row.Class; methods = @($row.Methods); reason = $row.Reason; owner = $row.Owner }
        }
    }
    return [pscustomobject]@{
        Object = $obj
        PolicyHash = $computed
        RequiredSuites = $suites
        Exclusions = $exclusions
    }
}

function Read-ReleaseAuthorityPinnedBlob {
    # Reads the policy blob at the pinned commit through git, never the working tree.
    param([string]$RepositoryRoot, [string]$Sha)
    $spec = ('{0}:{1}' -f $Sha, $script:ReleaseAuthorityPolicyPath)
    $id = Invoke-ReleaseAuthorityGit -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse', '--verify', '--quiet', $spec)
    if ([int]$id.ExitCode -ne 0) { throw 'policy-blob-unresolved' }
    $blobId = $id.Text.Trim()
    $blob = Invoke-ReleaseAuthorityGit -RepositoryRoot $RepositoryRoot -Arguments @('cat-file', 'blob', $blobId)
    if ([int]$blob.ExitCode -ne 0) { throw 'policy-blob-unreadable' }
    $bytes = [byte[]]$blob.Bytes
    if (-not [string]::Equals((Get-ReleaseAuthorityGitBlobId -Bytes $bytes), $blobId, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'policy-blob-identity'
    }
    return [pscustomobject]@{ BlobId = $blobId; Bytes = $bytes }
}

function Get-ReleaseAuthorityScriptHashes {
    param([string]$ScriptsRoot = '')
    if ([string]::IsNullOrWhiteSpace($ScriptsRoot)) { $ScriptsRoot = Split-Path -Parent $PSScriptRoot }
    $out = [ordered]@{}
    foreach ($rel in $script:ReleaseAuthorityScripts) {
        $p = Join-Path $ScriptsRoot $rel
        $out[$rel] = (Get-NightlyFileSha256 -Path $p)
    }
    return $out
}

function Get-ReleaseAuthorityFileDigest {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    return (Get-NightlyFileSha256 -Path $Path)
}

# ------------------------------------------------------------------- freeze ---

function New-ReleaseGateAuthority {
    <#
      Cut preparation: freeze the policy blob at the pinned SHA plus the candidate
      identity. Refuses (throws) on a missing/unknown schema, stale hash, missing
      rc profile or any missing member of the eight-suite set.
    #>
    param(
        [string]$RepositoryRoot,
        [string]$CandidateRoot,
        [string]$Sha,
        [string]$Repository,
        [string]$CandidateId,
        [string]$CandidateRef,
        [string]$IntentId,
        [string]$ReleaseCardId = '',
        [datetime]$CreatedUtc = [datetime]::MinValue
    )
    if (-not (Test-ReleaseGateFullSha -Sha $Sha)) { throw 'authority-sha-not-full' }
    foreach ($pair in @(@('repository', $Repository), @('candidateId', $CandidateId), @('candidateRef', $CandidateRef), @('intentId', $IntentId))) {
        if ([string]::IsNullOrWhiteSpace([string]$pair[1])) { throw ('authority-missing:' + $pair[0]) }
    }
    $blob = Read-ReleaseAuthorityPinnedBlob -RepositoryRoot $RepositoryRoot -Sha $Sha
    $policy = ConvertFrom-ReleaseAuthorityPolicyBytes -Bytes $blob.Bytes
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $dir 'policy.json'), $blob.Bytes)
    if ($CreatedUtc -eq [datetime]::MinValue) { $CreatedUtc = Get-NightlyUtcNow }
    $authority = [ordered]@{
        schemaVersion = $script:ReleaseAuthoritySchema
        kind = 'release-authority'
        repository = $Repository
        candidateId = $CandidateId
        candidateRef = $CandidateRef
        intentId = $IntentId
        releaseCardId = $ReleaseCardId
        sha = $Sha
        profile = 'rc'
        coordinatorVersion = $script:ReleaseAuthorityCoordinatorVersion
        policyPath = $script:ReleaseAuthorityPolicyPath
        policyBlobId = $blob.BlobId
        policyRawSha256 = (Get-ReleaseAuthorityBytesSha256 -Bytes $blob.Bytes)
        policyHash = $policy.PolicyHash
        requiredSuites = @($policy.RequiredSuites)
        exclusions = @($policy.Exclusions)
        scriptHashes = (Get-ReleaseAuthorityScriptHashes)
        createdAt = $CreatedUtc.ToUniversalTime().ToString('o')
    }
    Write-NightlyAtomicJson -Path (Join-Path $dir 'authority.json') -Object $authority
    return [pscustomobject]$authority
}

function Get-ReleaseAuthorityRequiredUids {
    # Required = full discovery minus the pinned profile exclusions (class or class+method).
    param([object[]]$Nodes, $Exclusions, [string]$SuiteId)
    $rows = @()
    foreach ($e in @($Exclusions)) {
        if ([string]$e.suite -ne $SuiteId) { continue }
        $rows += [pscustomobject]@{ Class = [string]$e.class; Methods = @($e.methods) }
    }
    $required = @()
    foreach ($n in @($Nodes)) {
        $node = [pscustomobject]@{ ClassName = [string]$n.class; Type = [string]$n.class; Method = [string]$n.method }
        if (Test-NightlyProfileNodeExcluded -Node $node -Exclusions $rows) { continue }
        $required += [string]$n.uid
    }
    return $required
}

function New-ReleaseGateExecutionPlan {
    <#
      Freeze the discovery roster and the chunk manifest before execution: one native
      class per chunk by default, and each roster suite's own declared entries.
      Discovery: @{ suiteId = @(@{ uid; class; method }) }. Rosters: @{ suiteId = @('entry') }.
    #>
    param([string]$CandidateRoot, [hashtable]$Discovery, [hashtable]$Rosters)
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    $authorityPath = Join-Path $dir 'authority.json'
    $authority = Get-Content -LiteralPath $authorityPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $discoveryDoc = [ordered]@{ schemaVersion = 1; suites = [ordered]@{} }
    $plan = [ordered]@{
        schemaVersion = 1
        candidateId = [string]$authority.candidateId
        intentId = [string]$authority.intentId
        sha = [string]$authority.sha
        authorityDigest = (Get-NightlyFileSha256 -Path $authorityPath)
        discoveryDigest = ''
        suites = @()
    }
    foreach ($suite in @($authority.requiredSuites)) {
        $id = [string]$suite
        if ($script:ReleaseAuthorityRosterSuites -contains $id) {
            $plan.suites += [ordered]@{ id = $id; kind = 'roster'; entries = @($Rosters[$id]) }
            continue
        }
        $nodes = @($Discovery[$id])
        $discoveryDoc.suites[$id] = $nodes
        $required = @(Get-ReleaseAuthorityRequiredUids -Nodes $nodes -Exclusions $authority.exclusions -SuiteId $id)
        $byClass = [ordered]@{}
        foreach ($n in $nodes) {
            if ($required -notcontains [string]$n.uid) { continue }
            $cls = [string]$n.class
            if (-not $byClass.Contains($cls)) { $byClass[$cls] = @() }
            $byClass[$cls] = @($byClass[$cls]) + [string]$n.uid
        }
        $chunks = @()
        $i = 0
        foreach ($cls in @($byClass.Keys)) {
            $i++
            $chunks += [ordered]@{ id = ('{0}-{1:d3}' -f $id, $i); classes = @($cls); uids = @($byClass[$cls]) }
        }
        $plan.suites += [ordered]@{ id = $id; kind = 'native'; chunks = $chunks }
    }
    $discoveryPath = Join-Path $dir 'discovery.json'
    Write-NightlyAtomicJson -Path $discoveryPath -Object $discoveryDoc
    $plan.discoveryDigest = (Get-NightlyFileSha256 -Path $discoveryPath)
    Write-NightlyAtomicJson -Path (Join-Path $dir 'execution-plan.json') -Object $plan
    return [pscustomobject]$plan
}

# --------------------------------------------------------------- validation ---

function Resolve-ReleaseAuthorityEvidencePath {
    <#
      G-287: evidence must be a relative path that resolves under the candidate
      root with no link/reparse component. Returns '' when it escapes.
    #>
    param([string]$CandidateRoot, [string]$Relative)
    if ([string]::IsNullOrWhiteSpace($Relative)) { return '' }
    if ([System.IO.Path]::IsPathRooted($Relative)) { return '' }
    $root = [System.IO.Path]::GetFullPath($CandidateRoot).TrimEnd([char[]]@('\', '/'))
    $full = [System.IO.Path]::GetFullPath((Join-Path $root $Relative))
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root + $sep, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    $cursor = $full
    while ($cursor.Length -gt $root.Length) {
        $info = New-Object System.IO.FileInfo($cursor)
        if ($info.Exists -or (Test-Path -LiteralPath $cursor)) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($null -ne $item.LinkTarget -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { return '' }
        }
        $cursor = [System.IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrEmpty($cursor)) { break }
    }
    return $full
}

function Read-ReleaseAuthorityJson {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

function Test-ReleaseAuthorityInstant {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return ([datetime]$Value).ToUniversalTime() }
    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal
    if ([datetime]::TryParse([string]$Value, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) { return $parsed }
    return $null
}

function Test-ReleaseGatePinnedAuthority {
    <#
      Publisher-side reload of the frozen authority. The policy blob is re-read at
      the pinned SHA through git and must match the private copy, the recorded blob
      id, raw digest and canonical hash; the required suite set is exactly the eight.
    #>
    param([string]$CandidateRoot, [string]$RepositoryRoot, [string]$Sha, [string]$CandidateId, [string]$CandidateRef)
    $reasons = @()
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    $authorityPath = Join-Path $dir 'authority.json'
    $authority = Read-ReleaseAuthorityJson -Path $authorityPath
    if ($null -eq $authority) {
        return [pscustomobject]@{ Ok = $false; Reasons = @('missing-authority'); Authority = $null; Policy = $null; Digest = '' }
    }
    if ($authority.schemaVersion -isnot [long] -and $authority.schemaVersion -isnot [int]) { $reasons += 'authority-schema' }
    elseif ([int]$authority.schemaVersion -ne $script:ReleaseAuthoritySchema) { $reasons += 'authority-schema' }
    if ([string]$authority.kind -cne 'release-authority') { $reasons += 'authority-kind' }
    if ([string]$authority.profile -cne 'rc') { $reasons += 'authority-profile' }
    if (-not [string]::Equals([string]$authority.sha, $Sha, [StringComparison]::Ordinal) -or -not (Test-ReleaseGateFullSha -Sha ([string]$authority.sha))) { $reasons += 'authority-sha' }
    if ([string]$authority.candidateId -cne $CandidateId) { $reasons += 'authority-candidate' }
    if ([string]$authority.candidateRef -cne $CandidateRef) { $reasons += 'authority-candidate-ref' }
    if ([string]::IsNullOrWhiteSpace([string]$authority.intentId)) { $reasons += 'authority-intent' }
    if ([string]$authority.policyPath -cne $script:ReleaseAuthorityPolicyPath) { $reasons += 'authority-policy-path' }
    $policy = $null
    try {
        $blob = Read-ReleaseAuthorityPinnedBlob -RepositoryRoot $RepositoryRoot -Sha $Sha
        $copyPath = Join-Path $dir 'policy.json'
        $copy = [byte[]]@()
        if (Test-Path -LiteralPath $copyPath -PathType Leaf) { $copy = [System.IO.File]::ReadAllBytes($copyPath) }
        $pinnedRaw = Get-ReleaseAuthorityBytesSha256 -Bytes $blob.Bytes
        if ([string]$authority.policyBlobId -cne $blob.BlobId) { $reasons += 'policy-blob-id-mismatch' }
        if ([string]$authority.policyRawSha256 -cne $pinnedRaw) { $reasons += 'policy-raw-digest-mismatch' }
        if ((Get-ReleaseAuthorityBytesSha256 -Bytes $copy) -cne $pinnedRaw) { $reasons += 'policy-copy-mismatch' }
        $policy = ConvertFrom-ReleaseAuthorityPolicyBytes -Bytes $blob.Bytes
        if ([string]$authority.policyHash -cne $policy.PolicyHash) { $reasons += 'policy-hash-mismatch' }
        $recorded = @($authority.requiredSuites | ForEach-Object { [string]$_ })
        if (($recorded -join ',') -cne (@($policy.RequiredSuites) -join ',')) { $reasons += 'authority-suites-mismatch' }
        $pinnedEx = ConvertTo-NightlyCanonicalJson -Object @($policy.Exclusions)
        $recordedEx = ConvertTo-NightlyCanonicalJson -Object @($authority.exclusions | ForEach-Object {
            [ordered]@{ suite = [string]$_.suite; class = [string]$_.class; methods = @($_.methods | ForEach-Object { [string]$_ }); reason = [string]$_.reason; owner = [string]$_.owner } })
        if ($pinnedEx -cne $recordedEx) { $reasons += 'authority-exclusions-mismatch' }
    } catch {
        $reasons += [string]$_.Exception.Message
    }
    $current = Get-ReleaseAuthorityScriptHashes
    foreach ($k in @($current.Keys)) {
        $have = ''
        if ($authority.scriptHashes -and ($authority.scriptHashes.PSObject.Properties.Name -contains $k)) { $have = [string]$authority.scriptHashes.$k }
        if ($have -cne [string]$current[$k]) { $reasons += ('script-digest-mismatch:' + $k) }
    }
    return [pscustomobject]@{
        Ok = ($reasons.Count -eq 0)
        Reasons = $reasons
        Authority = $authority
        Policy = $policy
        Digest = (Get-NightlyFileSha256 -Path $authorityPath)
    }
}

function Test-ReleaseGateExecutionLedger {
    <#
      D-15: the frozen plan and full ledger. Every chunk, every required expanded
      UID and every declared roster entry, each joined to the same intent,
      candidate, SHA, native RunId and authority. A passing sibling never hides a
      failed, skipped, missing, unknown, duplicate or stale row.
    #>
    param([string]$CandidateRoot, $Pinned, $Green)
    $reasons = New-Object System.Collections.Generic.List[string]
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    $authority = $Pinned.Authority
    $planPath = Join-Path $dir 'execution-plan.json'
    $ledgerPath = Join-Path $dir 'ledger.json'
    $discoveryPath = Join-Path $dir 'discovery.json'
    $plan = Read-ReleaseAuthorityJson -Path $planPath
    $ledger = Read-ReleaseAuthorityJson -Path $ledgerPath
    $discovery = Read-ReleaseAuthorityJson -Path $discoveryPath
    if ($null -eq $plan) { $reasons.Add('missing-execution-plan') }
    if ($null -eq $ledger) { $reasons.Add('missing-ledger') }
    if ($null -eq $discovery) { $reasons.Add('missing-discovery') }
    $summaryRows = @()
    if ($reasons.Count -gt 0) { return [pscustomobject]@{ Ok = $false; Reasons = @($reasons); Suites = @() } }

    $intent = [string]$authority.intentId
    $candidateId = [string]$authority.candidateId
    $sha = [string]$authority.sha
    $runId = [string]$Green.runId

    # Plan binding.
    if ([string]$plan.authorityDigest -cne [string]$Pinned.Digest) { $reasons.Add('plan-authority-digest') }
    if ([string]$plan.discoveryDigest -cne (Get-NightlyFileSha256 -Path $discoveryPath)) { $reasons.Add('plan-discovery-digest') }
    if ([string]$plan.intentId -cne $intent) { $reasons.Add('plan-intent') }
    if ([string]$plan.candidateId -cne $candidateId) { $reasons.Add('plan-candidate') }
    if ([string]$plan.sha -cne $sha) { $reasons.Add('plan-sha') }

    # Ledger identity and whole-run markers (real booleans only).
    if ([string]$ledger.intentId -cne $intent) { $reasons.Add('ledger-intent') }
    if ([string]$ledger.candidateId -cne $candidateId) { $reasons.Add('ledger-candidate') }
    if ([string]$ledger.candidateRef -cne [string]$authority.candidateRef) { $reasons.Add('ledger-candidate-ref') }
    if ([string]$ledger.sha -cne $sha -or -not (Test-ReleaseGateFullSha -Sha ([string]$ledger.sha))) { $reasons.Add('ledger-sha') }
    if ([string]::IsNullOrWhiteSpace($runId) -or [string]$ledger.runId -cne $runId) { $reasons.Add('ledger-run') }
    if ([string]$ledger.authorityDigest -cne [string]$Pinned.Digest) { $reasons.Add('ledger-authority-digest') }
    if ([string]$ledger.executionPlanDigest -cne (Get-NightlyFileSha256 -Path $planPath)) { $reasons.Add('ledger-plan-digest') }
    if (-not (Test-ReleaseGateRealBoolean -Value $ledger.teardownSucceeded)) { $reasons.Add('teardown-not-succeeded') }
    if ($ledger.seamed -isnot [bool] -or [bool]$ledger.seamed) { $reasons.Add('ledger-seamed') }
    if ($ledger.noReport -isnot [bool] -or [bool]$ledger.noReport) { $reasons.Add('ledger-no-report') }
    if ($ledger.diagnostic -isnot [bool] -or [bool]$ledger.diagnostic) { $reasons.Add('ledger-diagnostic') }
    if ([string]$ledger.selection -cne 'full') { $reasons.Add('ledger-selection') }
    if (-not (Test-ReleaseGateRealBoolean -Value $ledger.reportAccepted)) { $reasons.Add('ledger-report-not-accepted') }

    # Freshness and ordering: authority <= run start <= every chunk start < end <= run end.
    $authorityAt = Test-ReleaseAuthorityInstant -Value $authority.createdAt
    $runStart = Test-ReleaseAuthorityInstant -Value $ledger.startedAt
    $runEnd = Test-ReleaseAuthorityInstant -Value $ledger.completedAt
    if ($null -eq $authorityAt -or $null -eq $runStart -or $null -eq $runEnd -or $runStart -lt $authorityAt -or $runEnd -le $runStart) {
        $reasons.Add('ledger-timestamps')
    }
    function Test-Window($s, $e) {
        $a = Test-ReleaseAuthorityInstant -Value $s
        $b = Test-ReleaseAuthorityInstant -Value $e
        if ($null -eq $a -or $null -eq $b -or $null -eq $runStart -or $null -eq $runEnd) { return $false }
        return ($a -ge $runStart -and $b -gt $a -and $b -le $runEnd)
    }

    # Prerequisites: every declared build/prerequisite row succeeded.
    $prereqIds = @('build', 'client-bundle', 'script-census')
    foreach ($preId in $prereqIds) {
        $rows = @($ledger.prerequisites | Where-Object { [string]$_.id -ceq $preId })
        if ($rows.Count -ne 1) { $reasons.Add('prerequisite-missing:' + $preId); continue }
        $row = $rows[0]
        if ($row.exitCode -isnot [long] -and $row.exitCode -isnot [int]) { $reasons.Add('prerequisite-failed:' + $preId); continue }
        if ([int]$row.exitCode -ne 0 -or -not (Test-ReleaseGateRealBoolean -Value $row.succeeded)) { $reasons.Add('prerequisite-failed:' + $preId) }
    }

    # Suite set exactly the pinned eight, in plan and in ledger.
    $required = @($Pinned.Policy.RequiredSuites)
    $planSuites = @($plan.suites | ForEach-Object { [string]$_.id })
    foreach ($id in $required) { if ($planSuites -notcontains $id) { $reasons.Add('required-suite-missing:' + $id) } }
    foreach ($id in $planSuites) { if ($required -notcontains $id) { $reasons.Add('plan-suite-unexpected:' + $id) } }

    # Discovery-derived required UIDs, recomputed from the pinned exclusions.
    $allChunkIds = @{}
    foreach ($suite in @($plan.suites)) {
        $id = [string]$suite.id
        if ($required -notcontains $id) { continue }
        if ($script:ReleaseAuthorityRosterSuites -contains $id) {
            $declared = @($suite.entries | ForEach-Object { [string]$_ })
            if ($declared.Count -eq 0) { $reasons.Add('roster-empty:' + $id); continue }
            $rosterRows = @($ledger.rosters | Where-Object { [string]$_.suite -ceq $id })
            if ($rosterRows.Count -ne 1) { $reasons.Add('roster-missing:' + $id); continue }
            $r = $rosterRows[0]
            if ([string]$r.intentId -cne $intent -or [string]$r.candidateId -cne $candidateId -or [string]$r.sha -cne $sha -or [string]$r.runId -cne $runId) { $reasons.Add('roster-identity:' + $id) }
            if ($r.exitCode -isnot [long] -and $r.exitCode -isnot [int]) { $reasons.Add('roster-exit:' + $id) }
            elseif ([int]$r.exitCode -ne 0) { $reasons.Add('roster-exit:' + $id) }
            if (-not (Test-Window $r.startedAt $r.completedAt)) { $reasons.Add('roster-timestamps:' + $id) }
            $seen = @{}
            foreach ($e in @($r.entries)) {
                $eid = [string]$e.id
                if ($seen.ContainsKey($eid)) { $reasons.Add('roster-duplicate:' + $id + ':' + $eid); continue }
                $seen[$eid] = [string]$e.outcome
            }
            foreach ($eid in $declared) {
                if (-not $seen.ContainsKey($eid)) { $reasons.Add('roster-entry-missing:' + $id + ':' + $eid) }
                elseif ($seen[$eid] -cne 'passed') { $reasons.Add('roster-entry-not-passed:' + $id + ':' + $eid) }
            }
            foreach ($eid in @($seen.Keys)) { if ($declared -notcontains $eid) { $reasons.Add('roster-entry-unknown:' + $id + ':' + $eid) } }
            $summaryRows += [ordered]@{ id = $id; kind = 'roster'; required = $declared.Count; executed = $seen.Count; passed = @($seen.Values | Where-Object { $_ -ceq 'passed' }).Count }
            continue
        }

        $nodes = @()
        if ($discovery.suites -and ($discovery.suites.PSObject.Properties.Name -contains $id)) { $nodes = @($discovery.suites.$id) }
        $requiredUids = @(Get-ReleaseAuthorityRequiredUids -Nodes $nodes -Exclusions $authority.exclusions -SuiteId $id)
        if ($requiredUids.Count -eq 0) { $reasons.Add('required-uids-empty:' + $id); continue }
        $member = @{}
        $planChunks = @($suite.chunks)
        foreach ($c in $planChunks) {
            $allChunkIds[[string]$c.id] = $id
            foreach ($u in @($c.uids)) {
                $key = [string]$u
                if ($member.ContainsKey($key)) { $reasons.Add('plan-uid-overlap:' + $key) } else { $member[$key] = [string]$c.id }
            }
        }
        foreach ($u in $requiredUids) { if (-not $member.ContainsKey($u)) { $reasons.Add('plan-uid-missing:' + $u) } }
        foreach ($u in @($member.Keys)) { if ($requiredUids -notcontains $u) { $reasons.Add('plan-uid-unexpected:' + $u) } }

        $terminal = @{}
        $executed = 0; $passed = 0
        foreach ($c in $planChunks) {
            $cid = [string]$c.id
            $rows = @($ledger.chunks | Where-Object { [string]$_.chunk -ceq $cid })
            if ($rows.Count -eq 0) { $reasons.Add('chunk-missing:' + $cid); continue }
            if ($rows.Count -gt 1) { $reasons.Add('chunk-duplicate:' + $cid); continue }
            $row = $rows[0]
            if ([string]$row.suite -cne $id) { $reasons.Add('chunk-suite:' + $cid) }
            if ([string]$row.intentId -cne $intent) { $reasons.Add('chunk-intent:' + $cid) }
            if ([string]$row.candidateId -cne $candidateId) { $reasons.Add('chunk-candidate:' + $cid) }
            if ([string]$row.sha -cne $sha) { $reasons.Add('chunk-sha:' + $cid) }
            if ([string]$row.runId -cne $runId) { $reasons.Add('chunk-run:' + $cid) }
            if (-not (Test-Window $row.startedAt $row.completedAt)) { $reasons.Add('chunk-timestamps:' + $cid) }
            if ($row.exitCode -isnot [long] -and $row.exitCode -isnot [int]) { $reasons.Add('chunk-exit:' + $cid) }
            elseif ([int]$row.exitCode -ne 0) { $reasons.Add('chunk-exit:' + $cid) }
            $trx = Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $CandidateRoot -Relative ([string]$row.trx)
            if ([string]::IsNullOrWhiteSpace($trx)) { $reasons.Add('chunk-evidence-escape:' + $cid); continue }
            if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) { $reasons.Add('chunk-evidence-missing:' + $cid); continue }
            if ([string]$row.trxSha256 -cne (Get-NightlyFileSha256 -Path $trx)) { $reasons.Add('chunk-evidence-digest:' + $cid) }
            try { $results = @(Read-NightlyTrxIdentities -TrxPath $trx) } catch { $reasons.Add('chunk-trx-unreadable:' + $cid); continue }
            $cx = 0; $cp = 0; $cf = 0; $cs = 0
            foreach ($t in $results) {
                $uid = [string]$t.TestId
                $cx++
                switch -CaseSensitive ([string]$t.Outcome) { 'Passed' { $cp++ } 'Failed' { $cf++ } default { $cs++ } }
                if (-not $member.ContainsKey($uid)) { $reasons.Add('uid-unknown:' + $uid); continue }
                if ($member[$uid] -cne $cid) { $reasons.Add('uid-wrong-chunk:' + $uid); continue }
                if ($terminal.ContainsKey($uid)) { $reasons.Add('uid-duplicate:' + $uid); continue }
                $terminal[$uid] = [string]$t.Outcome
            }
            foreach ($pair in @(@('executed', $cx), @('passed', $cp), @('failed', $cf), @('skipped', $cs))) {
                $v = $row.($pair[0])
                if (($v -isnot [long] -and $v -isnot [int]) -or [int]$v -ne [int]$pair[1]) { $reasons.Add('chunk-count-' + $pair[0] + ':' + $cid) }
            }
            if ($cx -eq 0) { $reasons.Add('chunk-zero-executed:' + $cid) }
            $executed += $cx; $passed += $cp
        }
        foreach ($u in $requiredUids) {
            if (-not $terminal.ContainsKey($u)) { $reasons.Add('uid-missing:' + $u) }
            elseif ($terminal[$u] -cne 'Passed') { $reasons.Add('uid-not-passed:' + $u) }
        }
        $summaryRows += [ordered]@{ id = $id; kind = 'native'; chunks = $planChunks.Count; required = $requiredUids.Count; executed = $executed; passed = $passed }
    }
    foreach ($row in @($ledger.chunks)) {
        if (-not $allChunkIds.ContainsKey([string]$row.chunk)) { $reasons.Add('chunk-unexpected:' + [string]$row.chunk) }
    }
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = @($reasons); Suites = $summaryRows }
}

function New-ReleaseGatePublicationAuthority {
    <#
      Freezes the digest over the pinned authority, plan, discovery, ledger and every
      chunk's evidence, plus the sanitized summary digest. The summary is an output.
      -NoWrite (publisher -WhatIf) computes and compares the digest but writes nothing.
    #>
    param([string]$CandidateRoot, $Pinned, [string]$SummaryDigest, [switch]$NoWrite)
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    $ledger = Read-ReleaseAuthorityJson -Path (Join-Path $dir 'ledger.json')
    $evidence = [ordered]@{}
    foreach ($row in @($ledger.chunks)) { $evidence[[string]$row.chunk] = [string]$row.trxSha256 }
    $doc = [ordered]@{
        schemaVersion = 1
        candidateId = [string]$Pinned.Authority.candidateId
        intentId = [string]$Pinned.Authority.intentId
        sha = [string]$Pinned.Authority.sha
        runId = [string]$ledger.runId
        policyBlobId = [string]$Pinned.Authority.policyBlobId
        policyHash = [string]$Pinned.Authority.policyHash
        authorityDigest = [string]$Pinned.Digest
        executionPlanDigest = (Get-ReleaseAuthorityFileDigest -Path (Join-Path $dir 'execution-plan.json'))
        discoveryDigest = (Get-ReleaseAuthorityFileDigest -Path (Join-Path $dir 'discovery.json'))
        ledgerDigest = (Get-ReleaseAuthorityFileDigest -Path (Join-Path $dir 'ledger.json'))
        evidence = $evidence
        summaryDigest = $SummaryDigest
    }
    $digest = Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object $doc)
    $path = Join-Path $dir 'publication-authority.json'
    $existing = Read-ReleaseAuthorityJson -Path $path
    if ($null -ne $existing -and [string]$existing.digest -cne $digest) {
        return [pscustomobject]@{ Ok = $false; Reason = 'publication-authority-changed'; Digest = $digest; Path = $path }
    }
    if ($null -eq $existing -and -not $NoWrite) {
        Write-NightlyAtomicJson -Path $path -Object ([ordered]@{ digest = $digest; inputs = $doc })
    }
    return [pscustomobject]@{ Ok = $true; Reason = ''; Digest = $digest; Path = $path }
}

function Test-ReleaseGateAuthority {
    <#
      Single entry point for the publisher: pinned policy + ledger. Returns Ok,
      Reasons, the pinned required suites and the validated suite rows.
    #>
    param([string]$CandidateRoot, [string]$RepositoryRoot, $Green, $Candidate, [string]$Repository = '', [datetime]$NowUtc = [datetime]::MinValue)
    $pinned = Test-ReleaseGatePinnedAuthority -CandidateRoot $CandidateRoot -RepositoryRoot $RepositoryRoot `
        -Sha ([string]$Candidate.sha) -CandidateId ([string]$Candidate.candidateId) -CandidateRef ([string]$Candidate.ref)
    if ($null -eq $pinned.Authority) {
        return [pscustomobject]@{ Ok = $false; Reasons = @($pinned.Reasons); Pinned = $pinned; Suites = @() }
    }
    $reasons = @($pinned.Reasons)
    if ([string]$Green.policyHash -cne [string]$pinned.Authority.policyHash) { $reasons += 'green-policy-hash-mismatch' }
    if ($null -eq $pinned.Policy) {
        return [pscustomobject]@{ Ok = $false; Reasons = $reasons; Pinned = $pinned; Suites = @() }
    }
    $ledger = Test-ReleaseGateExecutionLedger -CandidateRoot $CandidateRoot -Pinned $pinned -Green $Green
    $reasons += @($ledger.Reasons)
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = $reasons; Pinned = $pinned; Suites = @($ledger.Suites) }
}
