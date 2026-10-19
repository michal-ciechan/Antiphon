#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1311: promote one pre-release tag vYYYY.MM.DD.N-rc.K to the final
    CalVer tag vYYYY.MM.DD.N at the same peeled SHA.

    The pre-release tag must already exist and must peel to a full SHA that the
    candidate's RC complete-green credits. The live candidate ref tip must be
    that same SHA. A different tip refuses remote-candidate-moved, and an
    unresolved tip refuses remote-candidate-unresolved, before any tag or
    GitHub write. The final tag is created at the peeled SHA.
    An existing final tag at the same SHA is success and creates nothing. An
    existing final tag at another SHA is a refusal. The pre-release tag and its
    GitHub pre-release are left in place. An older tested SHA stays promotable
    only while the live tip still equals its peeled SHA.

    -SeamsPath is test-only and uses the same admission as publish-release.ps1.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.
#>
param(
    [string]$ReleaseRoot = 'C:\Antiphon\releases',
    [string]$Tag = '',
    [string]$CandidateId = '',
    [string]$CheckoutRoot = '',
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

function Write-PromoteLine { param([string]$Message) Write-Host ('[promote-release] {0}' -f $Message) }

function Update-PromoteFinalTag {
    param([string]$Root, [string]$Id, [string]$FinalName)
    $journalPath = Join-Path $Root 'publications.json'
    if (-not (Test-Path -LiteralPath $journalPath)) { return }
    [void](Set-ReleaseGateReservationFinalTag -JournalPath $journalPath -CandidateId $Id -FinalTag $FinalName)
}

function Invoke-AntiphonPromoteRelease {
    param(
        [string]$ReleaseRoot,
        [string]$Tag,
        [string]$CandidateId,
        [string]$CheckoutRoot,
        [string]$SeamsPath,
        [switch]$WhatIf
    )
    $result = [ordered]@{
        ExitCode = 1
        Refusal = ''
        CandidateId = $CandidateId
        Tag = ''
        PreReleaseTag = $Tag
        Sha = ''
        Promoted = $false
        Resumed = $false
    }
    $admission = Import-ReleaseGatePublisherSeams -SeamsPath $SeamsPath
    if (-not [string]::IsNullOrEmpty($admission)) {
        Write-PromoteLine ('REFUSED: seam admission ({0}).' -f $admission)
        $result.Refusal = $admission; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ([string]::IsNullOrWhiteSpace($CandidateId)) {
        Write-PromoteLine 'REFUSED: -CandidateId is required.'
        $result.Refusal = 'missing-candidate'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ([string]::IsNullOrWhiteSpace($CheckoutRoot)) { $CheckoutRoot = Join-Path $ReleaseRoot 'checkout' }
    $parsed = Test-ReleaseGateTag -Tag $Tag
    if (-not $parsed.Ok -or -not [bool]$parsed.Prerelease) {
        $reason = 'tag-shape'
        if (-not $parsed.Ok -and -not [string]::IsNullOrWhiteSpace([string]$parsed.Reason) -and [string]$parsed.Reason -ne 'tag-shape') {
            $reason = [string]$parsed.Reason
        }
        Write-PromoteLine ('REFUSED: {0} is not a pre-release tag ({1}).' -f $Tag, $reason)
        $result.Refusal = $reason; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $finalName = New-ReleaseGateTag -CutUtc $parsed.Date -Sequence ([int]$parsed.Sequence)
    $result.Tag = $finalName

    $pre = Get-ReleaseGateRemoteTag -WorkingDirectory $CheckoutRoot -Tag $Tag
    if ($pre.Status -eq 'Absent') {
        Write-PromoteLine ('REFUSED: pre-release tag {0} is not on the remote.' -f $Tag)
        $result.Refusal = 'prerelease-missing'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $peeled = $pre.Sha
    if ($pre.Status -ne 'Found') {
        Write-PromoteLine ('REFUSED: pre-release tag {0} peeled to {1}.' -f $Tag, $peeled)
        $result.Refusal = 'tag-peel'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    $result.Sha = $peeled

    $paths = Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot '' -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
    foreach ($needed in @($paths.GreenPath, $paths.JournalPath)) {
        if (-not (Test-Path -LiteralPath $needed)) {
            Write-PromoteLine ('REFUSED: missing {0}.' -f $needed)
            $result.Refusal = 'missing-evidence'; $result.ExitCode = 3; return [pscustomobject]$result
        }
    }
    $green = Get-Content -LiteralPath $paths.GreenPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $paths.JournalPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $verdict = Get-ReleaseGateCreditVerdict -State $green
    if ($verdict.CreditKind -ne 'rc-release' -or -not $verdict.Ok) {
        Write-PromoteLine ('REFUSED: credit is not rc-release ({0}).' -f ($verdict.Reasons -join ','))
        $result.Refusal = 'not-rc-complete-green'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if (-not [string]::Equals([string]$green.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$candidate.candidateId, $CandidateId, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PromoteLine 'REFUSED: credited candidate id does not match -CandidateId.'
        $result.Refusal = 'not-rc-complete-green'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if (-not [string]::Equals([string]$green.sha, $peeled, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$candidate.sha, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PromoteLine ('REFUSED: credited sha {0} does not match peeled sha {1}.' -f [string]$green.sha, $peeled)
        $result.Refusal = 'credited-sha-mismatch'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    # The tested SHA is what is promoted. A live tip that has moved is a
    # different candidate. Refuse before any tag or GitHub write.
    $live = Get-ReleaseGateRemoteHead -WorkingDirectory $CheckoutRoot -Ref ([string]$candidate.ref)
    $liveSha = $live.Sha
    if ($live.Status -ne 'Found') {
        Write-PromoteLine ('REFUSED: live ref {0} did not resolve.' -f [string]$candidate.ref)
        $result.Refusal = 'remote-candidate-unresolved'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if (-not [string]::Equals($liveSha, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PromoteLine ('REFUSED: live {0} is {1}; pre-release peels to {2}.' -f [string]$candidate.ref, $liveSha, $peeled)
        $result.Refusal = 'remote-candidate-moved'; $result.ExitCode = 3; return [pscustomobject]$result
    }

    $final = Get-ReleaseGateRemoteTag -WorkingDirectory $CheckoutRoot -Tag $finalName
    $finalPeeled = $final.Sha
    if ($final.Status -eq 'Failed') {
        Write-PromoteLine ('REFUSED: final tag {0} could not be verified.' -f $finalName)
        $result.Refusal = 'tag-peel'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    if ($final.Status -eq 'Found') {
        if (-not [string]::Equals($finalPeeled, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
            Write-PromoteLine ('REFUSED: final tag {0} already points at {1}.' -f $finalName, $finalPeeled)
            $result.Refusal = 'final-tag-conflict'; $result.ExitCode = 3; return [pscustomobject]$result
        }
        if (-not $WhatIf) { Update-PromoteFinalTag -Root $ReleaseRoot -Id $CandidateId -FinalName $finalName }
        $result.Resumed = $true
        $result.Promoted = $false
        $result.ExitCode = 0
        Write-PromoteLine ('final tag {0} already points at {1}' -f $finalName, $peeled)
        return [pscustomobject]$result
    }

    if ($WhatIf) {
        Write-PromoteLine ('WhatIf: would promote {0} to {1} at {2}' -f $Tag, $finalName, $peeled)
        $result.ExitCode = 0
        return [pscustomobject]$result
    }

    $mk = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('tag', '-a', $finalName, $peeled, '-m', ('Antiphon {0}' -f $finalName))
    if ($null -eq $mk -or [int]$mk.ExitCode -ne 0) {
        $show = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('rev-list', '-n', '1', $finalName)
        $shown = ''
        if ($null -ne $show -and [int]$show.ExitCode -eq 0) { $shown = ([string]$show.Output).Trim() }
        if (-not [string]::Equals($shown, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
            Write-PromoteLine 'REFUSED: local tag creation failed and no matching tag exists.'
            $result.Refusal = 'tag-local-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
    }
    $push = Invoke-ReleaseGateGit -WorkingDirectory $CheckoutRoot -Arguments @('push', 'origin', ('refs/tags/{0}:refs/tags/{0}' -f $finalName))
    if ($null -eq $push -or [int]$push.ExitCode -ne 0) {
        $again = Get-ReleaseGateRemoteTag -WorkingDirectory $CheckoutRoot -Tag $finalName
        if ($again.Status -ne 'Found' -or -not [string]::Equals($again.Sha, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
            Write-PromoteLine 'REFUSED: tag push failed and the remote tag does not hold the peeled sha.'
            $result.Refusal = 'tag-push-failed'; $result.ExitCode = 1; return [pscustomobject]$result
        }
    }
    $verified = Get-ReleaseGateRemoteTag -WorkingDirectory $CheckoutRoot -Tag $finalName
    if ($verified.Status -ne 'Found' -or -not [string]::Equals($verified.Sha, $peeled, [StringComparison]::OrdinalIgnoreCase)) {
        Write-PromoteLine ('REFUSED: final tag peels to {0}, expected {1}.' -f $verified.Sha, $peeled)
        $result.Refusal = 'tag-peel'; $result.ExitCode = 3; return [pscustomobject]$result
    }
    Update-PromoteFinalTag -Root $ReleaseRoot -Id $CandidateId -FinalName $finalName
    $result.Promoted = $true
    $result.Resumed = $false
    $result.ExitCode = 0
    Write-PromoteLine ('promoted {0} to {1} at {2}' -f $Tag, $finalName, $peeled)
    return [pscustomobject]$result
}

$invokeResult = Invoke-AntiphonPromoteRelease -ReleaseRoot $ReleaseRoot -Tag $Tag -CandidateId $CandidateId `
    -CheckoutRoot $CheckoutRoot -SeamsPath $SeamsPath -WhatIf:$WhatIf
if ($PassThru) { return $invokeResult }
Write-Host (ConvertTo-Json -InputObject ([ordered]@{
    candidateId = [string]$invokeResult.CandidateId
    tag = [string]$invokeResult.Tag
    preReleaseTag = [string]$invokeResult.PreReleaseTag
    sha = [string]$invokeResult.Sha
    promoted = [bool]$invokeResult.Promoted
    resumed = [bool]$invokeResult.Resumed
    refusal = [string]$invokeResult.Refusal
    exitCode = [int]$invokeResult.ExitCode
}) -Compress)
exit ([int]$invokeResult.ExitCode)
