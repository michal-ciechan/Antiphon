#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0599 D-3: cut one immutable release candidate from origin/master.

    Fetches origin/master in a producer-owned isolated clone, pins its full SHA
    once, and pushes a create-only branch release/rc-YYYYMMDDTHHMMSSZ. The
    candidate journal is written before the push, so an ambiguous response
    resumes the same identity instead of allocating a second candidate.

    No force push, no direct repair commits, no moving a tested branch. An
    existing candidate ref pointing at a different SHA is a hard refusal. The
    same journal/ref/SHA is an idempotent resume.

    This script does not run tests, publish a release or restart anything.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER ReleaseRoot
    Producer-owned release state. Default C:\Antiphon\releases.

.PARAMETER CheckoutRoot
    The producer's own clone. Must be an owned clone, never the canonical
    checkout and never a linked worktree.

.PARAMETER Ref
    Candidate ref to cut or resume. Generated from the current UTC time when
    omitted.

.PARAMETER SeamsPath
    Injectable Git boundary for the offline contract tests. Never used in
    production.
#>
param(
    [string]$ReleaseRoot = 'C:\Antiphon\releases',
    [string]$CheckoutRoot = '',
    [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon',
    [string]$Ref = '',
    [string]$SourceRef = 'master',
    [string]$ScheduleSlot = '',
    [string]$SeamsPath = '',
    [switch]$WhatIf,
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'release-gate.ps1')

function Write-CandidateLine { param([string]$Message) Write-Host ('[release-candidate] {0}' -f $Message) }

function Invoke-AntiphonReleaseCandidate {
    param(
        [string]$ReleaseRoot,
        [string]$CheckoutRoot,
        [string]$RemoteUrl,
        [string]$Ref,
        [string]$SourceRef,
        [string]$ScheduleSlot,
        [string]$SeamsPath,
        [switch]$WhatIf
    )
    Import-ReleaseGateSeams -SeamsPath $SeamsPath
    $result = [ordered]@{
        ExitCode = 1
        Refusal = ''
        CandidateId = ''
        Ref = ''
        Sha = ''
        JournalPath = ''
        Resumed = $false
        Pushed = $false
    }

    if ([string]::IsNullOrWhiteSpace($CheckoutRoot)) { $CheckoutRoot = Join-Path $ReleaseRoot 'checkout' }

    if ([string]::IsNullOrWhiteSpace($Ref)) { $Ref = New-ReleaseGateCandidateRef -Utc (Get-NightlyUtcNow) }
    $candidate = Test-ReleaseGateCandidateRef -Ref $Ref
    if (-not $candidate.Ok) {
        Write-CandidateLine ('REFUSED: candidate ref ({0}).' -f $candidate.Reason)
        $result.Refusal = $candidate.Reason
        $result.ExitCode = 3
        return [pscustomobject]$result
    }
    $result.CandidateId = $candidate.CandidateId
    $result.Ref = $candidate.Ref

    # Ownership: the producer clone is ours, and is not the canonical checkout or
    # a linked worktree. This mirrors the nightly bootstrap's own guard.
    try { $CheckoutRoot = ConvertTo-NightlyResolvedPath -Path $CheckoutRoot } catch {
        Write-CandidateLine ('REFUSED: reparse ({0}).' -f $_.Exception.Message)
        $result.Refusal = 'reparse'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if (Test-NightlySharedTree -Path $CheckoutRoot) {
        Write-CandidateLine 'REFUSED: -CheckoutRoot is the shared tree or a worktree.'
        $result.Refusal = 'shared-tree'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if (Test-NightlyGitWorktree -Path $CheckoutRoot) {
        Write-CandidateLine 'REFUSED: linked Git worktree is not an independent clone.'
        $result.Refusal = 'linked-worktree'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ((Test-Path -LiteralPath (Join-Path $CheckoutRoot '.git')) -and
        -not (Test-Path -LiteralPath (Join-Path $CheckoutRoot '.antiphon-release-owned'))) {
        Write-CandidateLine 'REFUSED: unmarked clone is not a proven release checkout.'
        $result.Refusal = 'unmarked-clone'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $ReleaseRoot -CandidateId $candidate.CandidateId
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $journalPath = Join-Path $root 'candidate.json'
    $result.JournalPath = $journalPath

    $existing = $null
    if (Test-Path -LiteralPath $journalPath) {
        try { $existing = Get-Content -LiteralPath $journalPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch {
            Write-CandidateLine ('REFUSED: unreadable candidate journal ({0}).' -f $_.Exception.Message)
            $result.Refusal = 'journal-unreadable'; $result.ExitCode = 3; return [pscustomobject]$result
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $CheckoutRoot '.git'))) {
        if ($WhatIf) { Write-CandidateLine ('WhatIf: would clone {0} into {1}' -f $RemoteUrl, $CheckoutRoot) }
        else {
            New-Item -ItemType Directory -Path (Split-Path -Parent $CheckoutRoot) -Force | Out-Null
            $clone = Invoke-ReleaseGateGit -WorkingDirectory (Split-Path -Parent $CheckoutRoot) -Arguments @('clone', $RemoteUrl, $CheckoutRoot)
            if ([int]$clone.ExitCode -ne 0) {
                Write-CandidateLine 'REFUSED: clone failed.'
                $result.Refusal = 'clone-failed'; $result.ExitCode = 1; return [pscustomobject]$result
            }
            Write-NightlyAtomicJson -Path (Join-Path $CheckoutRoot '.antiphon-release-owned') -Object ([ordered]@{ origin = $RemoteUrl; kind = 'release-clone' })
        }
    }

    # Pin once. A resume never re-pins: it reuses the journal's SHA and only
    # reconciles the remote.
    $sha = ''
    if ($null -ne $existing -and -not [string]::IsNullOrWhiteSpace([string]$existing.sha)) {
        $sha = [string]$existing.sha
        $result.Resumed = $true
        Write-CandidateLine ('resuming candidate {0} at {1}' -f $candidate.CandidateId, $sha)
    } else {
        $fetch = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('fetch', 'origin', $SourceRef)
        if ([int]$fetch.ExitCode -ne 0) {
            Write-CandidateLine 'REFUSED: fetch failed.'
            $result.Refusal = 'fetch-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
        $rev = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('rev-parse', 'FETCH_HEAD')
        $sha = ([string]$rev.Output).Trim()
        if (-not (Test-ReleaseGateFullSha -Sha $sha)) {
            Write-CandidateLine ('REFUSED: origin/{0} did not resolve to a full sha ({1}).' -f $SourceRef, $sha)
            $result.Refusal = 'sha-unresolved'; $result.ExitCode = 1; return [pscustomobject]$result
        }
    }
    $result.Sha = $sha

    if ($null -ne $existing -and -not [string]::IsNullOrWhiteSpace([string]$existing.sha) -and
        -not [string]::Equals([string]$existing.sha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-CandidateLine ('REFUSED: candidate {0} already pinned to {1}.' -f $candidate.CandidateId, $existing.sha)
        $result.Refusal = 'candidate-repinned'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    # D-3: an existing remote candidate ref at a different SHA is a hard refusal.
    # Resolve it BEFORE the push so an ambiguous earlier response cannot be
    # papered over with a second write.
    $remote = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('ls-remote', '--heads', 'origin', $candidate.Ref)
    $remoteSha = ''
    if ([int]$remote.ExitCode -eq 0) {
        $line = ([string]$remote.Output).Trim()
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $remoteSha = ($line -split '\s+')[0].Trim()
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($remoteSha) -and
        -not [string]::Equals($remoteSha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-CandidateLine ('REFUSED: remote {0} already points at {1}.' -f $candidate.Ref, $remoteSha)
        $result.Refusal = 'remote-candidate-conflict'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    # Journal before the remote write.
    $journal = [ordered]@{
        schemaVersion = 1
        candidateId = $candidate.CandidateId
        ref = $candidate.Ref
        sha = $sha
        sourceRef = $SourceRef
        remoteUrl = $RemoteUrl
        scheduleSlot = $ScheduleSlot
        cutUtc = $candidate.CutUtc.ToString('o')
        createdAt = (Get-NightlyUtcNow).ToString('o')
        pushed = $(if ($null -ne $existing) { [bool]$existing.pushed } else { $false })
    }
    if ($WhatIf) {
        Write-CandidateLine ('WhatIf: would cut {0} at {1}' -f $candidate.Ref, $sha)
        $result.ExitCode = 0
        return [pscustomobject]$result
    }
    Write-NightlyAtomicJson -Path $journalPath -Object $journal

    if (-not [string]::IsNullOrWhiteSpace($remoteSha)) {
        # Already published at the pinned SHA: idempotent resume, no second write.
        Write-CandidateLine ('candidate {0} already present at {1}' -f $candidate.Ref, $sha)
        $journal.pushed = $true
        Write-NightlyAtomicJson -Path $journalPath -Object $journal
        $result.Pushed = $true
        $result.ExitCode = 0
        return [pscustomobject]$result
    }

    # Create-only push. No --force, no lease, no ref move.
    $push = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('push', 'origin', ('{0}:refs/heads/{1}' -f $sha, $candidate.Ref))
    if ([int]$push.ExitCode -ne 0) {
        # An ambiguous or failed push is resolved by re-reading the remote, never
        # by pushing again under a new identity.
        $recheck = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('ls-remote', '--heads', 'origin', $candidate.Ref)
        $afterSha = ''
        if ([int]$recheck.ExitCode -eq 0) {
            $line = ([string]$recheck.Output).Trim()
            if (-not [string]::IsNullOrWhiteSpace($line)) { $afterSha = ($line -split '\s+')[0].Trim() }
        }
        if ([string]::Equals($afterSha, $sha, [StringComparison]::OrdinalIgnoreCase)) {
            Write-CandidateLine 'push response lost but the remote holds the pinned sha.'
            $journal.pushed = $true
            Write-NightlyAtomicJson -Path $journalPath -Object $journal
            $result.Pushed = $true
            $result.ExitCode = 0
            return [pscustomobject]$result
        }
        Write-CandidateLine 'REFUSED: candidate push failed.'
        $result.Refusal = 'push-failed'; $result.ExitCode = 1; return [pscustomobject]$result
    }

    $journal.pushed = $true
    Write-NightlyAtomicJson -Path $journalPath -Object $journal
    $result.Pushed = $true
    $result.ExitCode = 0
    Write-CandidateLine ('cut {0} at {1}' -f $candidate.Ref, $sha)
    return [pscustomobject]$result
}

$invokeResult = Invoke-AntiphonReleaseCandidate -ReleaseRoot $ReleaseRoot -CheckoutRoot $CheckoutRoot `
    -RemoteUrl $RemoteUrl -Ref $Ref -SourceRef $SourceRef -ScheduleSlot $ScheduleSlot `
    -SeamsPath $SeamsPath -WhatIf:$WhatIf
if ($PassThru) { return $invokeResult }
Write-Host (ConvertTo-Json -InputObject ([ordered]@{
    candidateId = [string]$invokeResult.CandidateId
    ref = [string]$invokeResult.Ref
    sha = [string]$invokeResult.Sha
    pushed = [bool]$invokeResult.Pushed
    resumed = [bool]$invokeResult.Resumed
    refusal = [string]$invokeResult.Refusal
    exitCode = [int]$invokeResult.ExitCode
}) -Compress)
exit ([int]$invokeResult.ExitCode)
