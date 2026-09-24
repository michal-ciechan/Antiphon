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
#   ledger.json            execution records: prerequisites, chunks, rosters and the
#                          typed prepublication receipt (the release card's readback),
#                          which the publisher re-reads from the card itself through
#                          the Antiphon API (GET /api/cards/{id}) before it trusts it
#   publication-authority.json  digest over all of the above, written by the publisher
#
# Every git read here is a real git child with a bounded wait: the policy blob is
# never taken from a working tree and never from a seam.
#
# JSON types are validated strictly before any comparison: a one-element array is
# not the scalar it holds, a comma-joined string is not a list, and no identity is
# compared through a [string] cast. The digest chain is recomputable by whoever
# writes these files, so repository, coordinator and clock bind to things outside
# them: the checkout's origin, the publication destination, the supported
# coordinator version and the publisher's (injectable) clock.

if ($script:AntiphonReleaseAuthorityLoaded) { return }
$script:AntiphonReleaseAuthorityLoaded = $true

$script:ReleaseAuthorityPolicyPath = 'tests/test-execution-policy.json'
$script:ReleaseAuthorityRcSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
$script:ReleaseAuthorityRosterSuites = @('client', 'scripts')
$script:ReleaseAuthoritySchema = 1
$script:ReleaseAuthorityCoordinatorVersion = 'c599-b1'
$script:ReleaseAuthorityGitTimeoutMs = 30000
# Evidence may not be dated after the publisher's clock by more than this skew.
$script:ReleaseAuthorityClockSkewSeconds = 120
$script:ReleaseAuthorityReceiptKind = 'prepublication'
$script:ReleaseAuthorityRecipientKind = 'release-card'
# D-14 readback: the release card body carries exactly one line
#   release-correlation: intentId=<id> candidateId=<id> sha=<full sha> runId=<id>
# (the B5 report projection writes it). The GET has a bounded wait.
$script:ReleaseAuthorityCorrelationPrefix = 'release-correlation:'
$script:ReleaseAuthorityCardReadTimeoutSeconds = 15
$script:ReleaseAuthorityRepositoryPattern = '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'
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

# --------------------------------------------------------- strict JSON types ---

function Test-ReleaseAuthorityText {
    # A real JSON string, equal to a non-empty Expected. Never a cast of an array/number.
    param($Value, [string]$Expected, [switch]$IgnoreCase)
    if ($Value -isnot [string] -or [string]::IsNullOrEmpty($Expected)) { return $false }
    $comparison = [StringComparison]::Ordinal
    if ($IgnoreCase) { $comparison = [StringComparison]::OrdinalIgnoreCase }
    return [string]::Equals([string]$Value, $Expected, $comparison)
}

function Get-ReleaseAuthorityText {
    # The value when it is a real string, otherwise '' (which no comparison accepts).
    param($Value)
    if ($Value -is [string]) { return [string]$Value }
    return ''
}

function Test-ReleaseAuthorityInteger {
    param($Value)
    return ($Value -is [int] -or $Value -is [long])
}

function Test-ReleaseAuthorityObject {
    # A JSON object as ConvertFrom-Json produces it; never an array, string or boolean.
    param($Value)
    return ($Value -is [System.Management.Automation.PSCustomObject])
}

function Test-ReleaseAuthorityStringList {
    # A JSON array whose every element is a string. A joined string is not a list.
    param($Value)
    if ($null -eq $Value -or $Value -isnot [array]) { return $false }
    foreach ($item in $Value) { if ($item -isnot [string]) { return $false } }
    return $true
}

function Test-ReleaseAuthorityGuid {
    param($Value)
    if ($Value -isnot [string]) { return $false }
    $parsed = [guid]::Empty
    return [guid]::TryParseExact([string]$Value, 'D', [ref]$parsed)
}

function ConvertTo-ReleaseAuthorityUtc {
    # An unspecified kind is UTC (as AssumeUniversal parsing is), never local time.
    param([datetime]$Value)
    if ($Value.Kind -eq [DateTimeKind]::Unspecified) { return [datetime]::SpecifyKind($Value, [DateTimeKind]::Utc) }
    return $Value.ToUniversalTime()
}

function ConvertTo-ReleaseAuthorityRepositoryName {
    # owner/name from a GitHub remote URL (https, ssh or scp form); '' for anything else.
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { return '' }
    $m = [regex]::Match($Url.Trim(), '^(?:https://github\.com/|ssh://git@github\.com/|git@github\.com:)(?<owner>[A-Za-z0-9_.-]+)/(?<name>[A-Za-z0-9_.-]+?)(?:\.git)?/?$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $m.Success) { return '' }
    return ('{0}/{1}' -f $m.Groups['owner'].Value, $m.Groups['name'].Value)
}

function Get-ReleaseAuthorityOriginRepository {
    # The checkout's origin through real git (never a seam), as owner/name.
    param([string]$RepositoryRoot)
    $origin = Invoke-ReleaseAuthorityGit -RepositoryRoot $RepositoryRoot -Arguments @('remote', 'get-url', 'origin')
    if ([int]$origin.ExitCode -ne 0) { return '' }
    return (ConvertTo-ReleaseAuthorityRepositoryName -Url $origin.Text)
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
        [string]$ReleaseCardId,
        [datetime]$CreatedUtc = [datetime]::MinValue
    )
    if (-not (Test-ReleaseGateFullSha -Sha $Sha)) { throw 'authority-sha-not-full' }
    foreach ($pair in @(@('repository', $Repository), @('candidateId', $CandidateId), @('candidateRef', $CandidateRef), @('intentId', $IntentId), @('releaseCardId', $ReleaseCardId))) {
        if ([string]::IsNullOrWhiteSpace([string]$pair[1])) { throw ('authority-missing:' + $pair[0]) }
    }
    if ($Repository -notmatch $script:ReleaseAuthorityRepositoryPattern) { throw 'authority-repository-shape' }
    if (-not (Test-ReleaseAuthorityGuid -Value $ReleaseCardId)) { throw 'authority-release-card-shape' }
    $origin = Get-ReleaseAuthorityOriginRepository -RepositoryRoot $RepositoryRoot
    if (-not [string]::Equals($origin, $Repository, [StringComparison]::OrdinalIgnoreCase)) { throw 'checkout-origin-repository' }
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
        releaseCardId = ([guid]$ReleaseCardId).ToString('D')
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

function Read-ReleaseGateReleaseCard {
    <#
      D-14 readback (review bf928242): the release card as the Antiphon API serves it,
      read-only - one GET /api/cards/{id}, no proxy, no redirect, bounded wait. Only a
      200 answer whose body is one JSON object is a readback; a transport failure, any
      other status, an empty, non-JSON or array body is unavailable (Ok = $false), and
      the caller refuses. Nothing falls back to the ledger's own copy of the receipt.
    #>
    param([string]$BaseUrl, [string]$CardId)
    $unavailable = [pscustomobject]@{ Ok = $false; Card = $null }
    if ([string]::IsNullOrWhiteSpace($BaseUrl) -or -not (Test-ReleaseAuthorityGuid -Value $CardId)) { return $unavailable }
    $uri = $null
    if (-not [Uri]::TryCreate(($BaseUrl.TrimEnd('/') + '/api/cards/' + $CardId), [UriKind]::Absolute, [ref]$uri)) { return $unavailable }
    if ($uri.Scheme -cne 'http' -and $uri.Scheme -cne 'https') { return $unavailable }
    $handler = $null
    $client = $null
    $response = $null
    try {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.UseProxy = $false
        $handler.AllowAutoRedirect = $false
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds($script:ReleaseAuthorityCardReadTimeoutSeconds)
        $response = $client.GetAsync($uri).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) { return $unavailable }
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $card = ConvertFrom-Json -InputObject $text -NoEnumerate -ErrorAction Stop
        if (-not (Test-ReleaseAuthorityObject -Value $card)) { return $unavailable }
        return [pscustomobject]@{ Ok = $true; Card = $card }
    } catch {
        return $unavailable
    } finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
        if ($null -ne $handler) { $handler.Dispose() }
    }
}

function Test-ReleaseAuthorityCardCorrelation {
    <#
      The release card body carries exactly one correlation line naming exactly these
      identities: no key missing, repeated, renamed (keys are case-sensitive) or added,
      no second correlation line, and every value equal to a non-empty expectation.
    #>
    param($Description, [System.Collections.IDictionary]$Expected)
    if ($Description -isnot [string]) { return $false }
    $prefix = $script:ReleaseAuthorityCorrelationPrefix
    $lines = @(([string]$Description -split "`n") | ForEach-Object { $_.TrimEnd("`r") } |
        Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($lines.Count -ne 1) { return $false }
    $seen = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($token in @($lines[0].Substring($prefix.Length).Trim() -split ' +')) {
        $i = $token.IndexOf('=')
        if ($i -le 0) { return $false }
        $key = $token.Substring(0, $i)
        if ($seen.ContainsKey($key)) { return $false }
        $seen[$key] = $token.Substring($i + 1)
    }
    if ($seen.Count -ne $Expected.Count) { return $false }
    foreach ($key in @($Expected.Keys)) {
        $want = [string]$Expected[$key]
        if ([string]::IsNullOrEmpty($want) -or -not $seen.ContainsKey([string]$key)) { return $false }
        if (-not [string]::Equals($seen[[string]$key], $want, [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function Read-ReleaseAuthorityJson {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

function Test-ReleaseAuthorityInstant {
    param($Value)
    # A JSON string (or the DateTime pwsh 7 ConvertFrom-Json makes of one); an array,
    # number or boolean is never an instant.
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return (ConvertTo-ReleaseAuthorityUtc -Value ([datetime]$Value)) }
    if ($Value -isnot [string]) { return $null }
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
      Every field is type-checked before it is compared. repository must name the
      publication destination AND the checkout's origin; coordinatorVersion must be
      the coordinator this publisher supports.
    #>
    param([string]$CandidateRoot, [string]$RepositoryRoot, [string]$Sha, [string]$CandidateId, [string]$CandidateRef, [string]$Repository)
    $reasons = @()
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $CandidateRoot
    $authorityPath = Join-Path $dir 'authority.json'
    $authority = Read-ReleaseAuthorityJson -Path $authorityPath
    if ($null -eq $authority) {
        return [pscustomobject]@{ Ok = $false; Reasons = @('missing-authority'); Authority = $null; Policy = $null; Digest = '' }
    }
    if (-not (Test-ReleaseAuthorityInteger -Value $authority.schemaVersion) -or [int]$authority.schemaVersion -ne $script:ReleaseAuthoritySchema) { $reasons += 'authority-schema' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.kind -Expected 'release-authority')) { $reasons += 'authority-kind' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.profile -Expected 'rc')) { $reasons += 'authority-profile' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.coordinatorVersion -Expected $script:ReleaseAuthorityCoordinatorVersion)) { $reasons += 'authority-coordinator-version' }
    $destination = ''
    if (-not [string]::IsNullOrWhiteSpace($Repository) -and $Repository -match $script:ReleaseAuthorityRepositoryPattern) { $destination = $Repository }
    if (-not (Test-ReleaseAuthorityText -Value $authority.repository -Expected $destination -IgnoreCase)) { $reasons += 'authority-repository' }
    $origin = Get-ReleaseAuthorityOriginRepository -RepositoryRoot $RepositoryRoot
    if ([string]::IsNullOrEmpty($destination) -or -not [string]::Equals($origin, $destination, [StringComparison]::OrdinalIgnoreCase)) { $reasons += 'checkout-origin-repository' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.sha -Expected $Sha) -or -not (Test-ReleaseGateFullSha -Sha $Sha)) { $reasons += 'authority-sha' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.candidateId -Expected $CandidateId)) { $reasons += 'authority-candidate' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.candidateRef -Expected $CandidateRef)) { $reasons += 'authority-candidate-ref' }
    if ([string]::IsNullOrWhiteSpace((Get-ReleaseAuthorityText -Value $authority.intentId))) { $reasons += 'authority-intent' }
    if (-not (Test-ReleaseAuthorityGuid -Value $authority.releaseCardId)) { $reasons += 'authority-release-card' }
    if (-not (Test-ReleaseAuthorityText -Value $authority.policyPath -Expected $script:ReleaseAuthorityPolicyPath)) { $reasons += 'authority-policy-path' }
    $policy = $null
    try {
        $blob = Read-ReleaseAuthorityPinnedBlob -RepositoryRoot $RepositoryRoot -Sha $Sha
        $copyPath = Join-Path $dir 'policy.json'
        $copy = [byte[]]@()
        if (Test-Path -LiteralPath $copyPath -PathType Leaf) { $copy = [System.IO.File]::ReadAllBytes($copyPath) }
        $pinnedRaw = Get-ReleaseAuthorityBytesSha256 -Bytes $blob.Bytes
        if (-not (Test-ReleaseAuthorityText -Value $authority.policyBlobId -Expected $blob.BlobId)) { $reasons += 'policy-blob-id-mismatch' }
        if (-not (Test-ReleaseAuthorityText -Value $authority.policyRawSha256 -Expected $pinnedRaw)) { $reasons += 'policy-raw-digest-mismatch' }
        if ((Get-ReleaseAuthorityBytesSha256 -Bytes $copy) -cne $pinnedRaw) { $reasons += 'policy-copy-mismatch' }
        $policy = ConvertFrom-ReleaseAuthorityPolicyBytes -Bytes $blob.Bytes
        if (-not (Test-ReleaseAuthorityText -Value $authority.policyHash -Expected $policy.PolicyHash)) { $reasons += 'policy-hash-mismatch' }

        # requiredSuites: a JSON array of distinct known suite names, then exactly the pinned list.
        $recorded = $authority.requiredSuites
        $suitesTyped = Test-ReleaseAuthorityStringList -Value $recorded
        if ($suitesTyped) {
            $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
            foreach ($id in $recorded) {
                if ($script:ReleaseAuthorityRcSuites -cnotcontains $id -or -not $seen.Add($id)) { $suitesTyped = $false }
            }
        }
        if (-not $suitesTyped) { $reasons += 'authority-suites-type'; $reasons += 'authority-suites-mismatch' }
        elseif ((@($recorded) -join ',') -cne (@($policy.RequiredSuites) -join ',')) { $reasons += 'authority-suites-mismatch' }

        # exclusions: a JSON array of objects with string fields and a string-list methods.
        $exclusionsTyped = ($authority.exclusions -is [array])
        if ($exclusionsTyped) {
            foreach ($e in $authority.exclusions) {
                if (-not (Test-ReleaseAuthorityObject -Value $e)) { $exclusionsTyped = $false; break }
                foreach ($k in @('suite', 'class', 'reason', 'owner')) { if ($e.$k -isnot [string]) { $exclusionsTyped = $false } }
                if (-not (Test-ReleaseAuthorityStringList -Value $e.methods)) { $exclusionsTyped = $false }
            }
        }
        if (-not $exclusionsTyped) { $reasons += 'authority-exclusions-type'; $reasons += 'authority-exclusions-mismatch' }
        else {
            $pinnedEx = ConvertTo-NightlyCanonicalJson -Object @($policy.Exclusions)
            $recordedEx = ConvertTo-NightlyCanonicalJson -Object @($authority.exclusions | ForEach-Object {
                [ordered]@{ suite = $_.suite; class = $_.class; methods = @($_.methods); reason = $_.reason; owner = $_.owner } })
            if ($pinnedEx -cne $recordedEx) { $reasons += 'authority-exclusions-mismatch' }
        }
    } catch {
        $reasons += [string]$_.Exception.Message
    }
    $current = Get-ReleaseAuthorityScriptHashes
    $hashes = $authority.scriptHashes
    $hashesTyped = Test-ReleaseAuthorityObject -Value $hashes
    foreach ($k in @($current.Keys)) {
        $bound = $hashesTyped -and ($hashes.PSObject.Properties.Name -contains $k) -and (Test-ReleaseAuthorityText -Value $hashes.$k -Expected ([string]$current[$k]))
        if (-not $bound) { $reasons += ('script-digest-mismatch:' + $k) }
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
      failed, skipped, missing, unknown, duplicate or stale row. No instant may be
      later than NowUtc (the publisher's injectable clock) plus a small skew, and
      the D-14 prepublication receipt must be typed, addressed to the release card
      bound to the authority, and correlated to this run - and the card itself, read
      back through the Antiphon API at CardApiBaseUrl, must be at exactly the
      receipt's revision with this run's correlation in its body.
    #>
    param([string]$CandidateRoot, $Pinned, $Green, [datetime]$NowUtc = [datetime]::MinValue, [string]$CardApiBaseUrl = '')
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
    if ($NowUtc -eq [datetime]::MinValue) { $reasons.Add('clock-unavailable') }
    $summaryRows = @()
    if ($reasons.Count -gt 0) { return [pscustomobject]@{ Ok = $false; Reasons = @($reasons); Suites = @() } }

    # Expected identities are taken only from real strings; '' matches nothing.
    $intent = Get-ReleaseAuthorityText -Value $authority.intentId
    $candidateId = Get-ReleaseAuthorityText -Value $authority.candidateId
    $candidateRef = Get-ReleaseAuthorityText -Value $authority.candidateRef
    $sha = Get-ReleaseAuthorityText -Value $authority.sha
    $runId = Get-ReleaseAuthorityText -Value $Green.runId
    $latest = (ConvertTo-ReleaseAuthorityUtc -Value $NowUtc).AddSeconds($script:ReleaseAuthorityClockSkewSeconds)

    # Plan binding.
    if (-not (Test-ReleaseAuthorityText -Value $plan.authorityDigest -Expected ([string]$Pinned.Digest))) { $reasons.Add('plan-authority-digest') }
    if (-not (Test-ReleaseAuthorityText -Value $plan.discoveryDigest -Expected (Get-NightlyFileSha256 -Path $discoveryPath))) { $reasons.Add('plan-discovery-digest') }
    if (-not (Test-ReleaseAuthorityText -Value $plan.intentId -Expected $intent)) { $reasons.Add('plan-intent') }
    if (-not (Test-ReleaseAuthorityText -Value $plan.candidateId -Expected $candidateId)) { $reasons.Add('plan-candidate') }
    if (-not (Test-ReleaseAuthorityText -Value $plan.sha -Expected $sha)) { $reasons.Add('plan-sha') }

    # Ledger identity and whole-run markers (real booleans only).
    if (-not (Test-ReleaseAuthorityText -Value $ledger.intentId -Expected $intent)) { $reasons.Add('ledger-intent') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.candidateId -Expected $candidateId)) { $reasons.Add('ledger-candidate') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.candidateRef -Expected $candidateRef)) { $reasons.Add('ledger-candidate-ref') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.sha -Expected $sha) -or -not (Test-ReleaseGateFullSha -Sha $sha)) { $reasons.Add('ledger-sha') }
    if ([string]::IsNullOrWhiteSpace($runId) -or -not (Test-ReleaseAuthorityText -Value $ledger.runId -Expected $runId)) { $reasons.Add('ledger-run') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.authorityDigest -Expected ([string]$Pinned.Digest))) { $reasons.Add('ledger-authority-digest') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.executionPlanDigest -Expected (Get-NightlyFileSha256 -Path $planPath))) { $reasons.Add('ledger-plan-digest') }
    if (-not (Test-ReleaseGateRealBoolean -Value $ledger.teardownSucceeded)) { $reasons.Add('teardown-not-succeeded') }
    if ($ledger.seamed -isnot [bool] -or [bool]$ledger.seamed) { $reasons.Add('ledger-seamed') }
    if ($ledger.noReport -isnot [bool] -or [bool]$ledger.noReport) { $reasons.Add('ledger-no-report') }
    if ($ledger.diagnostic -isnot [bool] -or [bool]$ledger.diagnostic) { $reasons.Add('ledger-diagnostic') }
    if (-not (Test-ReleaseAuthorityText -Value $ledger.selection -Expected 'full')) { $reasons.Add('ledger-selection') }

    # Freshness and ordering: authority <= run start <= every chunk start < end <= run end,
    # and nothing dated after the publisher's clock (evidence from the future).
    $authorityAt = Test-ReleaseAuthorityInstant -Value $authority.createdAt
    $runStart = Test-ReleaseAuthorityInstant -Value $ledger.startedAt
    $runEnd = Test-ReleaseAuthorityInstant -Value $ledger.completedAt
    if ($null -eq $authorityAt -or $null -eq $runStart -or $null -eq $runEnd -or $runStart -lt $authorityAt -or $runEnd -le $runStart) {
        $reasons.Add('ledger-timestamps')
    }
    function Test-Future($t) { return ($null -ne $t -and $t -gt $latest) }
    function Test-FutureWindow($s, $e) { return ((Test-Future (Test-ReleaseAuthorityInstant -Value $s)) -or (Test-Future (Test-ReleaseAuthorityInstant -Value $e))) }
    if (Test-Future $authorityAt) { $reasons.Add('evidence-future:authority') }
    if ((Test-Future $runStart) -or (Test-Future $runEnd)) { $reasons.Add('evidence-future:ledger') }
    function Test-Window($s, $e) {
        $a = Test-ReleaseAuthorityInstant -Value $s
        $b = Test-ReleaseAuthorityInstant -Value $e
        if ($null -eq $a -or $null -eq $b -or $null -eq $runStart -or $null -eq $runEnd) { return $false }
        return ($a -ge $runStart -and $b -gt $a -and $b -le $runEnd)
    }

    # D-14 / G-306: the prepublication receipt is the release card's readback of the
    # projected test result. Only that kind, from that card, for this run, counts;
    # a final/publication receipt or any boolean acceptance flag confers nothing.
    $receipt = $ledger.prepublicationReceipt
    if ($null -eq $receipt) { $reasons.Add('receipt-missing') }
    elseif (-not (Test-ReleaseAuthorityObject -Value $receipt)) { $reasons.Add('receipt-type') }
    else {
        if (-not (Test-ReleaseAuthorityText -Value $receipt.kind -Expected $script:ReleaseAuthorityReceiptKind)) { $reasons.Add('receipt-kind') }
        $recipient = $receipt.recipient
        $recipientOk = (Test-ReleaseAuthorityObject -Value $recipient) -and
            (Test-ReleaseAuthorityText -Value $recipient.kind -Expected $script:ReleaseAuthorityRecipientKind) -and
            (Test-ReleaseAuthorityGuid -Value $recipient.cardId) -and (Test-ReleaseAuthorityGuid -Value $authority.releaseCardId) -and
            ([guid]$recipient.cardId -eq [guid]$authority.releaseCardId) -and
            (Test-ReleaseAuthorityInteger -Value $recipient.revision) -and ([long]$recipient.revision -ge 1)
        if (-not $recipientOk) { $reasons.Add('receipt-recipient') }
        $correlation = $receipt.correlation
        $correlated = (Test-ReleaseAuthorityObject -Value $correlation) -and
            (Test-ReleaseAuthorityText -Value $correlation.intentId -Expected $intent) -and
            (Test-ReleaseAuthorityText -Value $correlation.candidateId -Expected $candidateId) -and
            (Test-ReleaseAuthorityText -Value $correlation.sha -Expected $sha) -and
            (Test-ReleaseAuthorityText -Value $correlation.runId -Expected $runId)
        if (-not $correlated) { $reasons.Add('receipt-correlation') }
        $acceptedAt = Test-ReleaseAuthorityInstant -Value $receipt.acceptedAt
        if ($null -eq $acceptedAt -or $null -eq $runEnd -or $acceptedAt -lt $runEnd) { $reasons.Add('receipt-timestamps') }
        if (Test-Future $acceptedAt) { $reasons.Add('evidence-future:receipt') }

        # Review bf928242: a receipt is only as good as the card it names. Read the
        # card bound to the authority back (read-only) and require it to be at exactly
        # the receipt's revision - a card that moved on, or a receipt naming any other
        # positive revision, refuses - with this run's correlation in its body. An
        # unavailable readback refuses: publication fails closed.
        $boundCard = Get-ReleaseAuthorityText -Value $authority.releaseCardId
        $readback = Read-ReleaseGateReleaseCard -BaseUrl $CardApiBaseUrl -CardId $boundCard
        if (-not $readback.Ok) { $reasons.Add('release-card-readback-unavailable') }
        else {
            $card = $readback.Card
            if (-not ((Test-ReleaseAuthorityGuid -Value $card.id) -and ([guid]$card.id -eq [guid]$boundCard))) { $reasons.Add('release-card-readback-identity') }
            $sameRevision = (Test-ReleaseAuthorityObject -Value $recipient) -and (Test-ReleaseAuthorityInteger -Value $recipient.revision) -and
                (Test-ReleaseAuthorityInteger -Value $card.revisionCount) -and ([long]$card.revisionCount -eq [long]$recipient.revision)
            if (-not $sameRevision) { $reasons.Add('release-card-readback-revision') }
            $expected = [ordered]@{ intentId = $intent; candidateId = $candidateId; sha = $sha; runId = $runId }
            if (-not (Test-ReleaseAuthorityCardCorrelation -Description $card.description -Expected $expected)) { $reasons.Add('release-card-readback-correlation') }
        }
    }

    # Prerequisites: every declared build/prerequisite row succeeded.
    $prereqIds = @('build', 'client-bundle', 'script-census')
    foreach ($preId in $prereqIds) {
        $rows = @($ledger.prerequisites | Where-Object { Test-ReleaseAuthorityText -Value $_.id -Expected $preId })
        if ($rows.Count -ne 1) { $reasons.Add('prerequisite-missing:' + $preId); continue }
        $row = $rows[0]
        if (-not (Test-ReleaseAuthorityInteger -Value $row.exitCode)) { $reasons.Add('prerequisite-failed:' + $preId); continue }
        if ([int]$row.exitCode -ne 0 -or -not (Test-ReleaseGateRealBoolean -Value $row.succeeded)) { $reasons.Add('prerequisite-failed:' + $preId) }
    }

    # Suite set exactly the pinned eight, in plan and in ledger.
    $required = @($Pinned.Policy.RequiredSuites)
    $planSuites = @($plan.suites | ForEach-Object { Get-ReleaseAuthorityText -Value $_.id })
    foreach ($id in $required) { if ($planSuites -cnotcontains $id) { $reasons.Add('required-suite-missing:' + $id) } }
    foreach ($id in $planSuites) { if ($required -cnotcontains $id) { $reasons.Add('plan-suite-unexpected:' + $id) } }

    # Discovery-derived required UIDs, recomputed from the pinned exclusions.
    $allChunkIds = @{}
    foreach ($suite in @($plan.suites)) {
        $id = Get-ReleaseAuthorityText -Value $suite.id
        if ($required -cnotcontains $id) { continue }
        if ($script:ReleaseAuthorityRosterSuites -contains $id) {
            if (-not (Test-ReleaseAuthorityStringList -Value $suite.entries)) { $reasons.Add('roster-empty:' + $id); continue }
            $declared = @($suite.entries)
            if ($declared.Count -eq 0) { $reasons.Add('roster-empty:' + $id); continue }
            $rosterRows = @($ledger.rosters | Where-Object { Test-ReleaseAuthorityText -Value $_.suite -Expected $id })
            if ($rosterRows.Count -ne 1) { $reasons.Add('roster-missing:' + $id); continue }
            $r = $rosterRows[0]
            $rosterJoined = (Test-ReleaseAuthorityText -Value $r.intentId -Expected $intent) -and (Test-ReleaseAuthorityText -Value $r.candidateId -Expected $candidateId) -and
                (Test-ReleaseAuthorityText -Value $r.sha -Expected $sha) -and (Test-ReleaseAuthorityText -Value $r.runId -Expected $runId)
            if (-not $rosterJoined) { $reasons.Add('roster-identity:' + $id) }
            if (-not (Test-ReleaseAuthorityInteger -Value $r.exitCode)) { $reasons.Add('roster-exit:' + $id) }
            elseif ([int]$r.exitCode -ne 0) { $reasons.Add('roster-exit:' + $id) }
            if (-not (Test-Window $r.startedAt $r.completedAt)) { $reasons.Add('roster-timestamps:' + $id) }
            if (Test-FutureWindow $r.startedAt $r.completedAt) { $reasons.Add('evidence-future:roster:' + $id) }
            $seen = @{}
            foreach ($e in @($r.entries)) {
                if ($e.id -isnot [string] -or $e.outcome -isnot [string]) { $reasons.Add('roster-entry-type:' + $id); continue }
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
            if ($c.id -isnot [string] -or -not (Test-ReleaseAuthorityStringList -Value $c.uids)) { $reasons.Add('plan-chunk-type:' + $id); continue }
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
            if ($c.id -isnot [string]) { continue }
            $cid = [string]$c.id
            $rows = @($ledger.chunks | Where-Object { Test-ReleaseAuthorityText -Value $_.chunk -Expected $cid })
            if ($rows.Count -eq 0) { $reasons.Add('chunk-missing:' + $cid); continue }
            if ($rows.Count -gt 1) { $reasons.Add('chunk-duplicate:' + $cid); continue }
            $row = $rows[0]
            if (-not (Test-ReleaseAuthorityText -Value $row.suite -Expected $id)) { $reasons.Add('chunk-suite:' + $cid) }
            if (-not (Test-ReleaseAuthorityText -Value $row.intentId -Expected $intent)) { $reasons.Add('chunk-intent:' + $cid) }
            if (-not (Test-ReleaseAuthorityText -Value $row.candidateId -Expected $candidateId)) { $reasons.Add('chunk-candidate:' + $cid) }
            if (-not (Test-ReleaseAuthorityText -Value $row.sha -Expected $sha)) { $reasons.Add('chunk-sha:' + $cid) }
            if (-not (Test-ReleaseAuthorityText -Value $row.runId -Expected $runId)) { $reasons.Add('chunk-run:' + $cid) }
            if (-not (Test-Window $row.startedAt $row.completedAt)) { $reasons.Add('chunk-timestamps:' + $cid) }
            if (Test-FutureWindow $row.startedAt $row.completedAt) { $reasons.Add('evidence-future:chunk:' + $cid) }
            if (-not (Test-ReleaseAuthorityInteger -Value $row.exitCode)) { $reasons.Add('chunk-exit:' + $cid) }
            elseif ([int]$row.exitCode -ne 0) { $reasons.Add('chunk-exit:' + $cid) }
            $trx = Resolve-ReleaseAuthorityEvidencePath -CandidateRoot $CandidateRoot -Relative (Get-ReleaseAuthorityText -Value $row.trx)
            if ([string]::IsNullOrWhiteSpace($trx)) { $reasons.Add('chunk-evidence-escape:' + $cid); continue }
            if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) { $reasons.Add('chunk-evidence-missing:' + $cid); continue }
            if (-not (Test-ReleaseAuthorityText -Value $row.trxSha256 -Expected (Get-NightlyFileSha256 -Path $trx))) { $reasons.Add('chunk-evidence-digest:' + $cid) }
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
                if (-not (Test-ReleaseAuthorityInteger -Value $v) -or [int]$v -ne [int]$pair[1]) { $reasons.Add('chunk-count-' + $pair[0] + ':' + $cid) }
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
        $rowChunk = Get-ReleaseAuthorityText -Value $row.chunk
        if ([string]::IsNullOrEmpty($rowChunk) -or -not $allChunkIds.ContainsKey($rowChunk)) { $reasons.Add('chunk-unexpected:' + [string]$row.chunk) }
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
      Single entry point for the publisher: pinned policy + ledger. Repository is the
      publication destination; NowUtc is the publisher's clock (Get-ReleaseGateUtcNow);
      CardApiBaseUrl is the Antiphon API the bound release card is read back from.
      Returns Ok, Reasons, the pinned required suites and the validated suite rows.
    #>
    param([string]$CandidateRoot, [string]$RepositoryRoot, $Green, $Candidate, [string]$Repository = '', [datetime]$NowUtc = [datetime]::MinValue, [string]$CardApiBaseUrl = '')
    $pinned = Test-ReleaseGatePinnedAuthority -CandidateRoot $CandidateRoot -RepositoryRoot $RepositoryRoot `
        -Sha ([string]$Candidate.sha) -CandidateId ([string]$Candidate.candidateId) -CandidateRef ([string]$Candidate.ref) -Repository $Repository
    if ($null -eq $pinned.Authority) {
        return [pscustomobject]@{ Ok = $false; Reasons = @($pinned.Reasons); Pinned = $pinned; Suites = @() }
    }
    $reasons = @($pinned.Reasons)
    if (-not (Test-ReleaseAuthorityText -Value $Green.policyHash -Expected (Get-ReleaseAuthorityText -Value $pinned.Authority.policyHash))) { $reasons += 'green-policy-hash-mismatch' }
    if ($null -eq $pinned.Policy) {
        return [pscustomobject]@{ Ok = $false; Reasons = $reasons; Pinned = $pinned; Suites = @() }
    }
    $ledger = Test-ReleaseGateExecutionLedger -CandidateRoot $CandidateRoot -Pinned $pinned -Green $Green -NowUtc $NowUtc -CardApiBaseUrl $CardApiBaseUrl
    $reasons += @($ledger.Reasons)
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = $reasons; Pinned = $pinned; Suites = @($ledger.Suites) }
}
