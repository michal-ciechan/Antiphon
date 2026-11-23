#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0599 D-8: publish one CalVer tag and GitHub Release for the exact tested SHA.

    Runs only on an RC complete-green. It validates the immutable candidate
    journal, the remote candidate SHA and - CARD-0599 D-15 - the pinned authority
    before it writes anything: the policy blob re-read through git at the pinned
    SHA, the exact eight-suite rc set, the frozen chunk plan and the full
    execution ledger (every chunk, every required expanded UID, every declared
    client/script roster entry, one intent/candidate/SHA/RunId). Diagnostic /
    NoReport / seam-driven runs refuse outright. A summary or report is an output
    of that validation, never a source of required suites or success; a legacy
    candidate with no authority cannot publish and must be recut.

    Publication sequence, each transition journalled before the next begins:
      1. reserve the tag (long-lived vYYYY.MM.DD.N-rc.K, stamp vYYYY.MM.DD.N)
      2. create the local annotated tag at that SHA
      3. push only that tag, never with --force
      4. verify the peeled remote target
      5. gh release create --verify-tag --draft --notes-file <file>
         A long-lived pre-release tag also passes --prerelease. A stamp tag does not.
      6. upload release-manifest.json and the sanitized verification summary
      7. read back tag and asset digests
      8. publish the draft and persist the release id/url

    A retry resumes the same journal. Same tag/SHA/manifest is success or
    recovery; any mismatch is a hard refusal. A tag with no published release
    stays pending, not released. Tags are never deleted or recreated.

    Before it trusts the ledger's prepublication receipt it reads the bound
    release card back through the Antiphon API (-AntiphonApiUrl, default
    ANTIPHON_API or http://localhost:17202; GET /api/cards/{id} only): the card
    must be at exactly the receipt's revision with this run's correlation line
    in its body. An unavailable readback refuses.

    -SeamsPath is test-only: it is refused unless ANTIPHON_RELEASE_GATE_TEST_MODE
    is exactly 1, and test mode is refused unless both the Git and GitHub
    adapters are fakes, so a real publication never runs on an injected clock.
    The remote reads, the tag push, GitHub, the clock and the publication and
    publish journals are compiled (Antiphon.Nightly ReleaseGateRemote and
    ReleaseGateJournal, which replaced scripts/lib/release-gate.ps1); a seams
    file's $ReleaseGateSeams.Git, .GitHub, .UtcNow and .JournalWrite are handed
    to them.

    Publishing never restarts AppHost or the runner and never checks out a tag
    in the main worktree.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.
#>
param(
    [string]$ReleaseRoot = 'C:\Antiphon\releases',
    [string]$CandidateId = '',
    [string]$CheckoutRoot = '',
    [string]$Repository = 'michal-ciechan/Antiphon',
    [string]$ExpectedPolicyHash = '',
    [string[]]$RequiredSuites = @(),
    [string]$SeamsPath = '',
    [string]$AntiphonApiUrl = '',
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-core.ps1'); Assert-NightlyCoreAvailable 'publish-release'

function Write-PublishLine { param([string]$Message) Write-Host ('[publish-release] {0}' -f $Message) }

function Get-ReleaseGateNotesBody {
    <#
      Notes name the commits since the prior published tag, card references
      where present, test counts, profile and exclusions, evidence identities and
      the previous release. The first release says there is no earlier release.
      Raw logs, credentials, hostnames and transcripts never appear here.
    #>
    param($Manifest, [string]$PreviousTag, [string[]]$Commits)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine(('Release {0}' -f [string]$Manifest.tag))
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine(('Tested SHA: {0}' -f [string]$Manifest.sha))
    [void]$sb.AppendLine(('Candidate: {0} ({1})' -f [string]$Manifest.candidateId, [string]$Manifest.candidateRef))
    [void]$sb.AppendLine(('Profile: {0}   policy hash: {1}' -f [string]$Manifest.profile, [string]$Manifest.policyHash))
    [void]$sb.AppendLine(('Native run: {0}' -f [string]$Manifest.nativeRunId))
    [void]$sb.AppendLine('')
    if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
        [void]$sb.AppendLine('Previous release: none - there is no earlier release.')
    } else {
        [void]$sb.AppendLine(('Previous release: {0}' -f $PreviousTag))
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Suites')
    foreach ($row in @($Manifest.suites)) {
        [void]$sb.AppendLine(('- {0}: discovered {1}, required {2}, executed {3}, passed {4}, failed {5}, skipped {6}, excluded {7}' -f `
            [string]$row.id, [string]$row.discovered, [string]$row.required, [string]$row.executed,
            [string]$row.passed, [string]$row.failed, [string]$row.skipped, [string]$row.excluded))
    }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Exclusions')
    $anyExclusion = $false
    foreach ($row in @($Manifest.exclusions)) {
        $anyExclusion = $true
        [void]$sb.AppendLine(('- {0}: {1} (owner {2})' -f [string]$row.class, [string]$row.reason, [string]$row.owner))
    }
    if (-not $anyExclusion) { [void]$sb.AppendLine('- none') }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Commits')
    $anyCommit = $false
    foreach ($c in @($Commits)) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        $anyCommit = $true
        [void]$sb.AppendLine(('- {0}' -f $c))
    }
    if (-not $anyCommit) { [void]$sb.AppendLine('- none recorded') }
    return $sb.ToString()
}

function Invoke-AntiphonPublishRelease {
    param(
        [string]$ReleaseRoot,
        [string]$CandidateId,
        [string]$CheckoutRoot,
        [string]$Repository,
        [string]$ExpectedPolicyHash,
        [string[]]$RequiredSuites,
        [string]$SeamsPath,
        [string]$AntiphonApiUrl,
        [switch]$WhatIf
    )
    $result = [ordered]@{
        ExitCode = 1
        Refusal = ''
        CandidateId = $CandidateId
        Tag = ''
        Sha = ''
        Published = $false
        ReleaseId = ''
        ReleaseUrl = ''
        Resumed = $false
        ManifestDigest = ''
    }
    # Review bf928242: seams (fake remote, injected clock) only in explicit test
    # mode, and never alongside a real remote. Refused before anything is read;
    # outside test mode a seams file is never loaded.
    $testMode = [Antiphon.Nightly.ReleaseGateRemote]::TestMode([string]$env:ANTIPHON_RELEASE_GATE_TEST_MODE)
    $ReleaseGateSeams = $null
    if ($testMode -and -not [string]::IsNullOrWhiteSpace($SeamsPath) -and (Test-Path -LiteralPath $SeamsPath)) { . $SeamsPath }
    $admission = [Antiphon.Nightly.ReleaseGateRemote]::PublisherAdmission($testMode, $SeamsPath, $ReleaseGateSeams.Git, $ReleaseGateSeams.GitHub)
    if (-not [string]::IsNullOrEmpty($admission)) {
        Write-PublishLine ('REFUSED: seam admission ({0}).' -f $admission)
        $result.Refusal = $admission; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $remoteGate = [Antiphon.Nightly.ReleaseGateRemote]::Seamed($ReleaseGateSeams.Git, $ReleaseGateSeams.GitHub, $ReleaseGateSeams.UtcNow, $testMode)
    $journals = [Antiphon.Nightly.ReleaseGateJournal]::new([TimeProvider]::System, $ReleaseGateSeams.JournalWrite)
    if ([string]::IsNullOrWhiteSpace($AntiphonApiUrl)) { $AntiphonApiUrl = [string]$env:ANTIPHON_API }
    if ([string]::IsNullOrWhiteSpace($AntiphonApiUrl)) { $AntiphonApiUrl = 'http://localhost:17202' }
    if ([string]::IsNullOrWhiteSpace($CandidateId)) {
        Write-PublishLine 'REFUSED: -CandidateId is required.'
        $result.Refusal = 'missing-candidate'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ([string]::IsNullOrWhiteSpace($CheckoutRoot)) { $CheckoutRoot = Join-Path $ReleaseRoot 'checkout' }

    $paths = [Antiphon.Nightly.ReleaseGateCredit]::StatePaths('rc-release', '', $ReleaseRoot, $CandidateId)
    foreach ($needed in @($paths.GreenPath, $paths.JournalPath)) {
        if (-not (Test-Path -LiteralPath $needed)) {
            Write-PublishLine ('REFUSED: missing {0}.' -f $needed)
            $result.Refusal = 'missing-evidence'; $result.ExitCode = 3; return [pscustomobject]$result
        }
    }
    $green = Get-Content -LiteralPath $paths.GreenPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $paths.JournalPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sha = [string]$green.sha
    $result.Sha = $sha

    # Resolve the remote candidate before any gate decision: a moved ref must
    # fail, and an unresolved remote is not treated as agreement.
    $remote = $remoteGate.Head($CheckoutRoot, [string]$candidate.ref)
    $remoteSha = $remote.Sha

    # D-15: the authority is re-verified on every attempt, recovery included. It
    # never reads summary.json; the pinned blob and the ledger are the only inputs.
    # Repository is the publication destination (it must equal the frozen value and
    # the checkout's origin); evidence may not post-date the publisher's clock; the
    # receipt must match the release card as the Antiphon API serves it now.
    $attemptUtc = $remoteGate.UtcNow()
    $authority = [Antiphon.Nightly.ReleaseAuthority]::Check($paths.Root, $CheckoutRoot, $green, $candidate, $Repository, $attemptUtc, $AntiphonApiUrl, $PSScriptRoot)
    $gate = [Antiphon.Nightly.ReleaseGatePublication]::PublicationGate($green, $candidate, $remoteSha, $authority, $ExpectedPolicyHash, $RequiredSuites)
    if (-not $gate.Ok) {
        Write-PublishLine ('REFUSED: publication gate ({0}).' -f ($gate.Reasons -join ','))
        $result.Refusal = ($gate.Reasons -join ','); $result.ExitCode = 3; return [pscustomobject]$result
    }
    $inventory = $remoteGate.TagInventory($CheckoutRoot)
    if (-not $inventory.Ok) {
        Write-PublishLine 'REFUSED: remote tag inventory could not be read.'
        $result.Refusal = 'remote-tag-inventory-failed'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $pinned = $authority.Pinned.Authority
    $ledgerDoc = $null
    $ledgerPath = Join-Path ([Antiphon.Nightly.ReleaseAuthority]::Dir($paths.Root)) 'ledger.json'
    if (Test-Path -LiteralPath $ledgerPath -PathType Leaf) { try { $ledgerDoc = Get-Content -LiteralPath $ledgerPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } }
    $sanitizedSuites = @($authority.Suites)
    $sanitizedExclusions = @($pinned.exclusions | ForEach-Object {
        [ordered]@{ suite = [string]$_.suite; class = [string]$_.class; reason = [string]$_.reason; owner = [string]$_.owner } })
    $summaryDigest = Get-NightlySha256Text -Text ([Antiphon.Nightly.NightlyDocuments]::CanonicalJson(([ordered]@{
        profile = 'rc'; policyHash = [string]$pinned.policyHash; suites = $sanitizedSuites; exclusions = $sanitizedExclusions })))
    $pubAuthority = [Antiphon.Nightly.ReleaseAuthority]::Publication($paths.Root, $authority.Pinned, $summaryDigest, [bool]$WhatIf)
    if (-not $pubAuthority.Ok) {
        Write-PublishLine ('REFUSED: {0}.' -f $pubAuthority.Reason)
        $result.Refusal = $pubAuthority.Reason; $result.ExitCode = 3; return [pscustomobject]$result
    }

    $publicationJournal = Join-Path $ReleaseRoot 'publications.json'
    # A candidate without a cut time is cut now (the release-gate clock seam is read only then).
    $cutUtc = [Antiphon.Nightly.ReleaseGateCredit]::Utc($candidate.cutUtc)
    if ($null -eq $cutUtc) { $cutUtc = Get-NightlyUtcNow }
    if ([Antiphon.Nightly.ReleaseGateTags]::LongLivedCandidateId($CandidateId)) { $cutUtc = $attemptUtc }
    if ($WhatIf) {
        # Read-only: -WhatIf reports the proposed tag and never reserves it, so
        # publications.json is untouched (not created, not rewritten).
        $reservation = $journals.Proposal($publicationJournal, $CandidateId, $sha, $cutUtc, [string[]]$inventory.Tags)
    } else {
        $reservation = $journals.Reserve($publicationJournal, $CandidateId, $sha, $cutUtc, [string[]]$inventory.Tags)
    }
    $tag = $reservation.Tag
    $result.Tag = $tag
    $result.Resumed = [bool]$reservation.Reused

    if (-not $WhatIf) {
        # A reservation's identity never changes after an external race. Refuse
        # before creating even a local tag, and leave the reservation for recovery.
        $selected = $remoteGate.Tag($CheckoutRoot, $tag)
        if ($selected.Status -ne 'Absent' -and ($selected.Status -ne 'Found' -or $selected.Sha -cne $sha)) {
            $result.Refusal = 'tag-conflict'; $result.ExitCode = 3; return [pscustomobject]$result
        }
        $parsedTag = [Antiphon.Nightly.ReleaseGateTags]::Tag($tag)
        if ($parsedTag.Prerelease) {
            $finalTag = [Antiphon.Nightly.ReleaseGateTags]::NewTag($parsedTag.Date, $parsedTag.Sequence)
            $final = $remoteGate.Tag($CheckoutRoot, $finalTag)
            if ($final.Status -ne 'Absent' -and ($final.Status -ne 'Found' -or $final.Sha -cne $sha)) {
                $result.Refusal = 'final-tag-conflict'; $result.ExitCode = 3; return [pscustomobject]$result
            }
        }
    }

    $manifest = [Antiphon.Nightly.ReleaseGatePublication]::NewManifest(@{
        repository = $Repository
        tag = $tag
        candidateRef = [string]$candidate.ref
        candidateId = $CandidateId
        sha = $sha
        nativeRunId = [string]$green.runId
        windmillJobId = [string]$green.windmillJobId
        scheduleSlot = [string]$candidate.scheduleSlot
        startedAt = [string]$green.startedAt
        completedAt = [string]$green.completedAt
        policyHash = [string]$pinned.policyHash
        profile = 'rc'
        scriptHashes = $pinned.scriptHashes
        buildHash = [string]$ledgerDoc.buildHash
        bundleHash = [string]$ledgerDoc.bundleHash
        suites = $sanitizedSuites
        exclusions = $sanitizedExclusions
        summaryDigest = $summaryDigest
        capabilities = $(if ($green.capabilities) { @($green.capabilities) } else { @() })
        publicationAuthorityDigest = $pubAuthority.Digest
    })
    $allow = [Antiphon.Nightly.ReleaseGatePublication]::ManifestAllowlistCheck($manifest)
    if (-not $allow.Ok) {
        Write-PublishLine ('REFUSED: manifest carries non-allowlisted fields ({0}).' -f ($allow.Extra -join ','))
        $result.Refusal = 'manifest-allowlist'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $manifestDigest = [Antiphon.Nightly.ReleaseGatePublication]::ManifestDigest($manifest)
    $result.ManifestDigest = $manifestDigest

    $publishJournal = $paths.PublishJournalPath
    if (Test-Path -LiteralPath $publishJournal) {
        $prior = Get-Content -LiteralPath $publishJournal -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($pair in @(@('tag', $tag), @('sha', $sha), @('manifestDigest', $manifestDigest), @('publicationAuthorityDigest', $pubAuthority.Digest))) {
            $have = [string]$prior.($pair[0])
            if (-not [string]::IsNullOrWhiteSpace($have) -and -not [string]::Equals($have, [string]$pair[1], [StringComparison]::OrdinalIgnoreCase)) {
                Write-PublishLine ('REFUSED: {0} changed since the journal ({1} -> {2}).' -f $pair[0], $have, $pair[1])
                $result.Refusal = ('journal-mismatch:' + $pair[0]); $result.ExitCode = 3; return [pscustomobject]$result
            }
        }
    }

    if ($WhatIf) {
        Write-PublishLine ('WhatIf: would publish {0} at {1}' -f $tag, $sha)
        $result.ExitCode = 0
        return [pscustomobject]$result
    }

    New-Item -ItemType Directory -Path $paths.Root -Force | Out-Null
    [void]$journals.WriteStep($publishJournal, 'intent', @{
        candidateId = $CandidateId; tag = $tag; sha = $sha; manifestDigest = $manifestDigest; repository = $Repository
        publicationAuthorityDigest = $pubAuthority.Digest
    })

    $manifestPath = $paths.ManifestPath
    Write-NightlyAtomicJson -Path $manifestPath -Object $manifest

    # 2/3. local annotated tag, then a create-only push of that tag alone.
    if (-not [Antiphon.Nightly.ReleaseGateJournal]::HasStep($publishJournal, 'tag-local')) {
        $mk = $remoteGate.Git($CheckoutRoot, [string[]]@('tag', '-a', $tag, $sha, '-m', ('Antiphon {0}' -f $tag)))
        if ([int]$mk.ExitCode -ne 0) {
            $show = $remoteGate.Git($CheckoutRoot, [string[]]@('rev-list', '-n', '1', $tag))
            if ([int]$show.ExitCode -ne 0 -or -not [string]::Equals(([string]$show.Output).Trim(), $sha, [StringComparison]::OrdinalIgnoreCase)) {
                Write-PublishLine 'REFUSED: local tag creation failed and no matching tag exists.'
                $result.Refusal = 'tag-local-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void]$journals.WriteStep($publishJournal, 'tag-local', $null)
    }
    if (-not [Antiphon.Nightly.ReleaseGateJournal]::HasStep($publishJournal, 'tag-pushed')) {
        $push = $remoteGate.PushExactTag($CheckoutRoot, $tag)
        if ([int]$push.ExitCode -ne 0) {
            # Re-read remote state after an ambiguous response before any further write.
            $peek = $remoteGate.Tag($CheckoutRoot, $tag)
            if ($peek.Status -ne 'Found' -or -not [string]::Equals($peek.Sha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
                Write-PublishLine 'REFUSED: tag push failed and the remote tag does not hold the tested sha.'
                $result.Refusal = 'tag-push-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void]$journals.WriteStep($publishJournal, 'tag-pushed', $null)
    }

    # 4. verify the peeled remote target agrees with the tested sha.
    $verify = $remoteGate.Tag($CheckoutRoot, $tag)
    $peeledSha = $verify.Sha
    if ($verify.Status -ne 'Found' -or -not [string]::Equals($peeledSha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PublishLine ('REFUSED: remote tag peels to {0}, expected {1}.' -f $peeledSha, $sha)
        $result.Refusal = 'tag-peel-mismatch'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    # 5. draft release.
    $notesPath = Join-Path $paths.Root 'release-notes.md'
    $previousTag = [Antiphon.Nightly.ReleaseGateJournal]::PreviousPublishedTag($publicationJournal, $CandidateId)
    $commits = @()
    $range = $sha
    if (-not [string]::IsNullOrWhiteSpace($previousTag)) { $range = ('{0}..{1}' -f $previousTag, $sha) }
    $log = $remoteGate.Git($CheckoutRoot, [string[]]@('log', '--no-merges', '--format=%h %s', $range))
    if ([int]$log.ExitCode -eq 0) {
        foreach ($l in (([string]$log.Output) -split "`n")) { if (-not [string]::IsNullOrWhiteSpace($l)) { $commits += $l.Trim() } }
    }
    Set-Content -LiteralPath $notesPath -Value (Get-ReleaseGateNotesBody -Manifest $manifest -PreviousTag $previousTag -Commits $commits) -Encoding UTF8

    if (-not [Antiphon.Nightly.ReleaseGateJournal]::HasStep($publishJournal, 'draft-created')) {
        $createArgs = @{ tag = $tag; notesFile = $notesPath }
        $parsedTag = [Antiphon.Nightly.ReleaseGateTags]::Tag($tag)
        if ($parsedTag.Ok -and $parsedTag.Prerelease) { $createArgs['prerelease'] = $true }
        $create = $remoteGate.GitHub('release-create', $createArgs, $CheckoutRoot)
        if ([int]$create.ExitCode -ne 0) {
            $view = $remoteGate.GitHub('release-view', @{ tag = $tag }, $CheckoutRoot)
            if ([int]$view.ExitCode -ne 0) {
                Write-PublishLine 'REFUSED: draft creation failed and no release exists for the tag.'
                $result.Refusal = 'draft-create-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void]$journals.WriteStep($publishJournal, 'draft-created', $null)
    }

    # 6/7. assets, each journalled and read back by digest.
    $summaryAssetPath = Join-Path $paths.Root 'verification-summary.json'
    Write-NightlyAtomicJson -Path $summaryAssetPath -Object ([ordered]@{
        schemaVersion = 1
        tag = $tag
        sha = $sha
        profile = 'rc'
        policyHash = [string]$pinned.policyHash
        suites = $manifest.suites
        exclusions = $manifest.exclusions
    })
    foreach ($asset in @($manifestPath, $summaryAssetPath)) {
        $name = [System.IO.Path]::GetFileName($asset)
        $step = ('asset:' + $name)
        if ([Antiphon.Nightly.ReleaseGateJournal]::HasStep($publishJournal, $step)) { continue }
        $up = $remoteGate.GitHub('release-upload', @{ tag = $tag; asset = $asset }, $CheckoutRoot)
        if ([int]$up.ExitCode -ne 0) {
            Write-PublishLine ('REFUSED: asset upload failed ({0}); release stays a draft.' -f $name)
            $result.Refusal = ('asset-upload-failed:' + $name); $result.ExitCode = 1; return [pscustomobject]$result
        }
        [void]$journals.WriteStep($publishJournal, $step, @{
            ($step + ':digest') = (Get-NightlyFileSha256 -Path $asset)
        })
    }

    # 8. publish the draft, then persist the final identity.
    $publish = $remoteGate.GitHub('release-publish', @{ tag = $tag }, $CheckoutRoot)
    if ([int]$publish.ExitCode -ne 0) {
        $view = $remoteGate.GitHub('release-view', @{ tag = $tag }, $CheckoutRoot)
        $isDraft = $true
        if ([int]$view.ExitCode -eq 0 -and $view.Body) { $isDraft = [bool]$view.Body.isDraft }
        if ($isDraft) {
            Write-PublishLine 'REFUSED: publish failed; the draft remains pending, not released.'
            $result.Refusal = 'publish-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
    }
    $final = $remoteGate.GitHub('release-view', @{ tag = $tag }, $CheckoutRoot)
    $releaseId = ''
    $releaseUrl = ''
    $stillDraft = $false
    if ([int]$final.ExitCode -eq 0 -and $final.Body) {
        $releaseId = [string]$final.Body.id
        $releaseUrl = [string]$final.Body.url
        $stillDraft = [bool]$final.Body.isDraft
    }
    if ($stillDraft) {
        Write-PublishLine 'REFUSED: readback still reports a draft.'
        $result.Refusal = 'still-draft'; $result.ExitCode = 1; return [pscustomobject]$result
    }
    [void]$journals.WriteStep($publishJournal, 'published', @{
        releaseId = $releaseId; releaseUrl = $releaseUrl; publishedAt = (Get-NightlyUtcNow).ToString('o')
    })
    $journals.MarkPublished($publicationJournal, $CandidateId, $releaseId, $releaseUrl)

    $result.Published = $true
    $result.ReleaseId = $releaseId
    $result.ReleaseUrl = $releaseUrl
    $result.ExitCode = 0
    Write-PublishLine ('published {0} for {1}' -f $tag, $sha)
    return [pscustomobject]$result
}

$invokeResult = Invoke-AntiphonPublishRelease -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId `
    -CheckoutRoot $CheckoutRoot -Repository $Repository -ExpectedPolicyHash $ExpectedPolicyHash `
    -RequiredSuites $RequiredSuites -SeamsPath $SeamsPath -AntiphonApiUrl $AntiphonApiUrl -WhatIf:$WhatIf
if ($PassThru) { return $invokeResult }
Write-Host (ConvertTo-Json -InputObject ([ordered]@{
    candidateId = [string]$invokeResult.CandidateId
    tag = [string]$invokeResult.Tag
    sha = [string]$invokeResult.Sha
    published = [bool]$invokeResult.Published
    releaseId = [string]$invokeResult.ReleaseId
    releaseUrl = [string]$invokeResult.ReleaseUrl
    refusal = [string]$invokeResult.Refusal
    exitCode = [int]$invokeResult.ExitCode
}) -Compress)
exit ([int]$invokeResult.ExitCode)
