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
      1. reserve tag vYYYY.MM.DD.N in the publication journal
      2. create the local annotated tag at that SHA
      3. push only that tag, never with --force
      4. verify the peeled remote target
      5. gh release create --verify-tag --draft --notes-file <file>
      6. upload release-manifest.json and the sanitized verification summary
      7. read back tag and asset digests
      8. publish the draft and persist the release id/url

    A retry resumes the same journal. Same tag/SHA/manifest is success or
    recovery; any mismatch is a hard refusal. A tag with no published release
    stays pending, not released. Tags are never deleted or recreated.

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
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'release-authority.ps1')

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
        [switch]$WhatIf
    )
    Import-ReleaseGateSeams -SeamsPath $SeamsPath
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
    if ([string]::IsNullOrWhiteSpace($CandidateId)) {
        Write-PublishLine 'REFUSED: -CandidateId is required.'
        $result.Refusal = 'missing-candidate'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ([string]::IsNullOrWhiteSpace($CheckoutRoot)) { $CheckoutRoot = Join-Path $ReleaseRoot 'checkout' }

    $paths = Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot '' -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
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
    $remoteSha = ''
    $remote = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('ls-remote', '--heads', 'origin', [string]$candidate.ref)
    if ([int]$remote.ExitCode -eq 0) {
        $line = ([string]$remote.Output).Trim()
        if (-not [string]::IsNullOrWhiteSpace($line)) { $remoteSha = ($line -split '\s+')[0].Trim() }
    }

    # D-15: the authority is re-verified on every attempt, recovery included. It
    # never reads summary.json; the pinned blob and the ledger are the only inputs.
    $authority = Test-ReleaseGateAuthority -CandidateRoot $paths.Root -RepositoryRoot $CheckoutRoot -Green $green -Candidate $candidate
    $gate = Test-ReleaseGatePublicationGate -Green $green -Candidate $candidate -RemoteSha $remoteSha `
        -Authority $authority -ExpectedPolicyHash $ExpectedPolicyHash -RequiredSuites $RequiredSuites
    if (-not $gate.Ok) {
        Write-PublishLine ('REFUSED: publication gate ({0}).' -f ($gate.Reasons -join ','))
        $result.Refusal = ($gate.Reasons -join ','); $result.ExitCode = 3; return [pscustomobject]$result
    }
    $pinned = $authority.Pinned.Authority
    $ledgerDoc = Read-ReleaseAuthorityJson -Path (Join-Path (Get-ReleaseAuthorityDir -CandidateRoot $paths.Root) 'ledger.json')
    $sanitizedSuites = @($authority.Suites)
    $sanitizedExclusions = @($pinned.exclusions | ForEach-Object {
        [ordered]@{ suite = [string]$_.suite; class = [string]$_.class; reason = [string]$_.reason; owner = [string]$_.owner } })
    $summaryDigest = Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object ([ordered]@{
        profile = 'rc'; policyHash = [string]$pinned.policyHash; suites = $sanitizedSuites; exclusions = $sanitizedExclusions }))
    $pubAuthority = New-ReleaseGatePublicationAuthority -CandidateRoot $paths.Root -Pinned $authority.Pinned -SummaryDigest $summaryDigest -NoWrite:$WhatIf
    if (-not $pubAuthority.Ok) {
        Write-PublishLine ('REFUSED: {0}.' -f $pubAuthority.Reason)
        $result.Refusal = $pubAuthority.Reason; $result.ExitCode = 3; return [pscustomobject]$result
    }

    $publicationJournal = Join-Path $ReleaseRoot 'publications.json'
    $reservation = New-ReleaseGateTagReservation -JournalPath $publicationJournal -CandidateId $CandidateId -Sha $sha `
        -CutUtc (ConvertTo-ReleaseGateUtc -Value $candidate.cutUtc -AllowEmpty)
    $tag = $reservation.Tag
    $result.Tag = $tag
    $result.Resumed = [bool]$reservation.Reused

    $manifest = New-ReleaseGateManifest -Fields @{
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
    }
    $allow = Test-ReleaseGateManifestAllowlist -Manifest $manifest
    if (-not $allow.Ok) {
        Write-PublishLine ('REFUSED: manifest carries non-allowlisted fields ({0}).' -f ($allow.Extra -join ','))
        $result.Refusal = 'manifest-allowlist'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $manifestDigest = Get-ReleaseGateManifestDigest -Manifest $manifest
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
    [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step 'intent' -Fields @{
        candidateId = $CandidateId; tag = $tag; sha = $sha; manifestDigest = $manifestDigest; repository = $Repository
        publicationAuthorityDigest = $pubAuthority.Digest
    })

    $manifestPath = $paths.ManifestPath
    Write-NightlyAtomicJson -Path $manifestPath -Object $manifest

    # 2/3. local annotated tag, then a create-only push of that tag alone.
    if (-not (Test-ReleaseGateJournalStep -Path $publishJournal -Step 'tag-local')) {
        $mk = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('tag', '-a', $tag, $sha, '-m', ('Antiphon {0}' -f $tag))
        if ([int]$mk.ExitCode -ne 0) {
            $show = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('rev-list', '-n', '1', $tag)
            if ([int]$show.ExitCode -ne 0 -or -not [string]::Equals(([string]$show.Output).Trim(), $sha, [StringComparison]::OrdinalIgnoreCase)) {
                Write-PublishLine 'REFUSED: local tag creation failed and no matching tag exists.'
                $result.Refusal = 'tag-local-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step 'tag-local')
    }
    if (-not (Test-ReleaseGateJournalStep -Path $publishJournal -Step 'tag-pushed')) {
        $push = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('push', 'origin', ('refs/tags/{0}' -f $tag))
        if ([int]$push.ExitCode -ne 0) {
            # Re-read remote state after an ambiguous response before any further write.
            $peek = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('ls-remote', '--tags', 'origin', ('refs/tags/{0}^{{}}' -f $tag))
            $peeled = ''
            if ([int]$peek.ExitCode -eq 0) {
                $line = ([string]$peek.Output).Trim()
                if (-not [string]::IsNullOrWhiteSpace($line)) { $peeled = ($line -split '\s+')[0].Trim() }
            }
            if (-not [string]::Equals($peeled, $sha, [StringComparison]::OrdinalIgnoreCase)) {
                Write-PublishLine 'REFUSED: tag push failed and the remote tag does not hold the tested sha.'
                $result.Refusal = 'tag-push-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step 'tag-pushed')
    }

    # 4. verify the peeled remote target agrees with the tested sha.
    $verify = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('ls-remote', '--tags', 'origin', ('refs/tags/{0}^{{}}' -f $tag))
    $peeledSha = ''
    if ([int]$verify.ExitCode -eq 0) {
        $line = ([string]$verify.Output).Trim()
        if (-not [string]::IsNullOrWhiteSpace($line)) { $peeledSha = ($line -split '\s+')[0].Trim() }
    }
    if (-not [string]::Equals($peeledSha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PublishLine ('REFUSED: remote tag peels to {0}, expected {1}.' -f $peeledSha, $sha)
        $result.Refusal = 'tag-peel-mismatch'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    # 5. draft release.
    $notesPath = Join-Path $paths.Root 'release-notes.md'
    $previousTag = ''
    $journalAll = Get-ReleaseGatePublicationJournal -Path $publicationJournal
    foreach ($row in @($journalAll.reservations)) {
        if ([bool]$row.published -and -not [string]::Equals([string]$row.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
            $previousTag = [string]$row.tag
        }
    }
    $commits = @()
    $range = $sha
    if (-not [string]::IsNullOrWhiteSpace($previousTag)) { $range = ('{0}..{1}' -f $previousTag, $sha) }
    $log = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('log', '--no-merges', '--format=%h %s', $range)
    if ([int]$log.ExitCode -eq 0) {
        foreach ($l in (([string]$log.Output) -split "`n")) { if (-not [string]::IsNullOrWhiteSpace($l)) { $commits += $l.Trim() } }
    }
    Set-Content -LiteralPath $notesPath -Value (Get-ReleaseGateNotesBody -Manifest $manifest -PreviousTag $previousTag -Commits $commits) -Encoding UTF8

    if (-not (Test-ReleaseGateJournalStep -Path $publishJournal -Step 'draft-created')) {
        $create = Invoke-ReleaseGateGitHub -Verb 'release-create' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag; notesFile = $notesPath }
        if ([int]$create.ExitCode -ne 0) {
            $view = Invoke-ReleaseGateGitHub -Verb 'release-view' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag }
            if ([int]$view.ExitCode -ne 0) {
                Write-PublishLine 'REFUSED: draft creation failed and no release exists for the tag.'
                $result.Refusal = 'draft-create-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
        }
        [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step 'draft-created')
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
        if (Test-ReleaseGateJournalStep -Path $publishJournal -Step $step) { continue }
        $up = Invoke-ReleaseGateGitHub -Verb 'release-upload' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag; asset = $asset }
        if ([int]$up.ExitCode -ne 0) {
            Write-PublishLine ('REFUSED: asset upload failed ({0}); release stays a draft.' -f $name)
            $result.Refusal = ('asset-upload-failed:' + $name); $result.ExitCode = 1; return [pscustomobject]$result
        }
        [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step $step -Fields @{
            ($step + ':digest') = (Get-NightlyFileSha256 -Path $asset)
        })
    }

    # 8. publish the draft, then persist the final identity.
    $publish = Invoke-ReleaseGateGitHub -Verb 'release-publish' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag }
    if ([int]$publish.ExitCode -ne 0) {
        $view = Invoke-ReleaseGateGitHub -Verb 'release-view' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag }
        $isDraft = $true
        if ([int]$view.ExitCode -eq 0 -and $view.Body) { $isDraft = [bool]$view.Body.isDraft }
        if ($isDraft) {
            Write-PublishLine 'REFUSED: publish failed; the draft remains pending, not released.'
            $result.Refusal = 'publish-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
    }
    $final = Invoke-ReleaseGateGitHub -Verb 'release-view' -WorkingDirectory $CheckoutRoot -Arguments @{ tag = $tag }
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
    [void](Write-ReleaseGateJournalStep -Path $publishJournal -Step 'published' -Fields @{
        releaseId = $releaseId; releaseUrl = $releaseUrl; publishedAt = (Get-NightlyUtcNow).ToString('o')
    })
    [void](Set-ReleaseGateReservationPublished -JournalPath $publicationJournal -CandidateId $CandidateId -ReleaseId $releaseId -ReleaseUrl $releaseUrl)

    $result.Published = $true
    $result.ReleaseId = $releaseId
    $result.ReleaseUrl = $releaseUrl
    $result.ExitCode = 0
    Write-PublishLine ('published {0} for {1}' -f $tag, $sha)
    return [pscustomobject]$result
}

$invokeResult = Invoke-AntiphonPublishRelease -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId `
    -CheckoutRoot $CheckoutRoot -Repository $Repository -ExpectedPolicyHash $ExpectedPolicyHash `
    -RequiredSuites $RequiredSuites -SeamsPath $SeamsPath -WhatIf:$WhatIf
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
