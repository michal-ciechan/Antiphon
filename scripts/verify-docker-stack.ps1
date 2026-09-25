# CARD-0590 stack verifier. -Case selects one literal checkpoint or guard.
param(
    [Parameter(Mandatory = $true)][string]$Case,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [switch]$RefreshClaudeToken
)
$ErrorActionPreference = 'Stop'
# Copy before dot-sourcing c590-real.ps1: that script's param block runs in this scope and
# would clear a same-named switch.
$c737RefreshClaudeToken = [bool]$RefreshClaudeToken
. (Join-Path $PSScriptRoot 'c590-command.ps1')
if (-not (Test-Path -LiteralPath $Manifest)) { Write-Error 'Manifest is required'; exit 2 }
$m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
$root = [string]$m.evidenceRoot
if (-not $env:ANTIPHON_C590_STUB) {
    . (Join-Path $PSScriptRoot 'c590-real.ps1')
    $script:C628RefreshClaudeToken = $c737RefreshClaudeToken
    if (Test-C590LiveCase -Case $Case) {
        Invoke-C590LiveCase -Case $Case -Manifest $m
    }
}

function Test-Boundary {
    param([string]$PathValue, [string]$RootValue, [string]$Canonical)
    $candidate = if ($Canonical) { $Canonical } else { [System.IO.Path]::GetFullPath($PathValue) }
    $base = [System.IO.Path]::GetFullPath($RootValue).TrimEnd('\', '/')
    $normalized = $candidate.TrimEnd('\', '/')
    if ($normalized.Length -ge $base.Length -and $normalized.Substring(0, $base.Length).Equals($base, [StringComparison]::OrdinalIgnoreCase)) {
        if ($normalized.Length -eq $base.Length) { return $true }
        $next = $normalized[$base.Length]
        if ($next -eq '\' -or $next -eq '/') { return $true }
    }
    return $false
}

switch ($Case) {
    'version-check' {
        if ([string]$m.observedRevision -ne [string]$m.sourceSha) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RevisionMismatch' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'foreign-state' {
        if ([bool]$m.foreignOwner) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ForeignStateOwner' -ExitCode 2
        }
        Invoke-C590 -Exe 'chown' -ArgumentList @('1654:1654') | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('compose', 'up') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'managed-secrets' {
        $ready = $false
        if ([string]$m.keyProtection -eq 'X509Certificate' -and [bool]$m.certificatePresent) { $ready = $true }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0 -Extra @{ managedSecretReady = $ready }
    }
    'parent-child' {
        if ([string]$m.childProject -eq [string]$m.parentProject) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ParentAsChild' -ExitCode 2
        }
        Invoke-C590 -Exe 'docker' -ArgumentList @('create') | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('rm') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'manifest-order' {
        Invoke-C590 -Exe 'write-manifest' -ArgumentList @('initial') | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('create') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'source-sha' {
        if ([string]$m.contextSha -ne [string]$m.sourceSha) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SourceMismatch' -ExitCode 2
        }
        Invoke-C590 -Exe 'docker' -ArgumentList @('build') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'bind-source' {
        if ([string]$m.bindSource -eq '/work/test-evidence') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RunnerLocalBind' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'foreign-id' {
        if ([string]$m.inspectedId -ne [string]$m.ownedId) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ForeignResource' -ExitCode 2
        }
        Invoke-C590 -Exe 'docker' -ArgumentList @('rm', [string]$m.ownedId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'foreign-label' {
        if ([string]$m.inspectedLabel -ne [string]$m.ownedLabel) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ForeignLabel' -ExitCode 2
        }
        Invoke-C590 -Exe 'docker' -ArgumentList @('rm', [string]$m.ownedId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'normal-down' {
        Invoke-C590 -Exe 'docker' -ArgumentList @('compose', 'down', '--remove-orphans') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'global-cleanup' {
        Invoke-C590 -Exe 'docker' -ArgumentList @('compose', 'down') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'raw-challenge' {
        if (-not [bool]$m.rawMarker) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RawChallengeMissing' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'raw-exit' {
        if (-not [bool]$m.rawExit) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RawExitMissing' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'session-origin' {
        if (-not [string]$m.sessionId) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SessionOriginMissing' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'child-probe' {
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'linux-custody' {
        # CARD-0604 D-17 (Cut B), V-29/R-5. The persistent runner is now a supported producer,
        # so this case inverts: it REQUIRES verificationCustodyV1 with backend linux-cgroup-v1
        # and a store id, and refuses the Windows backend outright. A Linux runner advertising
        # windows-job-v1 is the exact fabrication CARD-0598 asked to be made impossible.
        if ([string]$m.capabilities -notmatch 'VerificationCustodyV1') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CustodyNotAdvertised' -ExitCode 2
        }
        if ([string]$m.verificationCustodyBackend -eq 'windows-job-v1') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'WindowsBackendAdvertisedOnLinux' -ExitCode 2
        }
        if ([string]$m.verificationCustodyBackend -ne 'linux-cgroup-v1') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'UnsupportedCustodyAdvertised' -ExitCode 2
        }
        if (-not [string]$m.runnerStoreId) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RunnerStoreIdMissing' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'custody-containment' {
        # CARD-0604 S9, CP-15 / V-28 on cgroup v1. The live half runs the four measurements inside
        # the persistent runner through `docker exec -u 1654`; this is the same ordered set of
        # refusals, so a manifest that already knows the answer never reaches the remote. Every
        # one of them is a refusal the remote makes BEFORE it runs the probe.
        if (-not [bool]$m.runnerRunning) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RunnerNotRunning' -ExitCode 2
        }
        if ([string]$m.verificationCustodyBackend -ne 'linux-cgroup-v1') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CustodyNotAdvertised' -ExitCode 2
        }
        if ([bool]$m.windowsBackendAdvertised) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'WindowsBackendAdvertisedOnLinux' -ExitCode 2
        }
        if (-not [bool]$m.custodyHelpersRootOwned) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CustodyHelpersNotRootOwned' -ExitCode 2
        }
        if ([string]$m.containment -ne 'ok') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ContainmentFailed' -ExitCode 2
        }
        # server2 is kernel 4.15. A v2 reading means the freezer path went unmeasured, which is
        # the half the Windows lane can say nothing about.
        if ([string]$m.cgroupVersion -ne 'v1') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ExpectedCgroupV1' -ExitCode 2
        }
        if ([int]$m.custodyResidue -ne 0) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CustodyResidue' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    # --- CARD-0604 S4 boundaries. Each is the refusal the live case makes before any command. ---
    'deploy-parent' {
        if (-not [bool]$m.deployKeyPresent) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'DeployKeyMissing' -ExitCode 2
        }
        if (-not [bool]$m.phoneHomeSecretPresent) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'PhoneHomeSecretMissing' -ExitCode 2
        }
        if ([string]$m.restartPolicy -ne 'unless-stopped') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'NotPersistent' -ExitCode 2
        }
        if ([string]$m.daemonName -ne [string]$m.runnerHostname) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SiblingDaemonRefused' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'session-nested-stack' {
        # The session cases launch through the PRODUCTION server, so a runner that is not
        # dispatch-eligible must refuse here rather than leave a session queued forever.
        if (-not [bool]$m.dispatchEligible) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RunnerNotDispatchEligible' -ExitCode 2
        }
        if (-not [string]$m.sessionOrigin) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SessionOriginMissing' -ExitCode 2
        }
        if ([string]$m.hostResidue) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'HostResidue' -ExitCode 2
        }
        if ([string]$m.nestedResidue) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'NestedResidue' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'await-nested-stack' {
        # A session that is still running is not a result, and a dead session with no marker is
        # incomplete: neither may be reported as an outcome.
        if ([string]$m.sessionState -eq 'Working') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SessionStillRunning' -ExitCode 2
        }
        if (-not [bool]$m.markerSeen) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SessionIncomplete' -ExitCode 2
        }
        foreach ($sub in @($m.subordinates)) {
            if (-not [bool]$sub.accepted) {
                Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis ('SubordinateFailed ' + [string]$sub.name) -ExitCode 2
            }
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'persistent-restart' {
        if (-not [string]$m.storeIdBefore) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'LostNestedStore' -ExitCode 2
        }
        if ([string]$m.storeIdAfter -ne [string]$m.storeIdBefore) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ChangedStoreId' -ExitCode 2
        }
        if (-not [bool]$m.imagesRetained) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'NestedImagesLost' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'nested-residue' {
        if ([string]$m.hostResidue) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'HostResidue' -ExitCode 2
        }
        if ([string]$m.nestedResidue) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'NestedResidue' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'session-git-smoke' {
        if (-not [string]$m.pushedSha -or [string]$m.pushedSha.Length -ne 40) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'GitPushShaMissing' -ExitCode 2
        }
        if (-not [bool]$m.branchDeleted) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SmokeBranchNotDeleted' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'cleanup-residue' {
        if ([string]$m.survivingId) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CleanupIncomplete' -ExitCode 2 -Extra @{ surviving = [string]$m.survivingId }
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'interrupted-cleanup' {
        if ([string]$m.replacementId -and [string]$m.replacementId -ne [string]$m.ownedId) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'IdentityChanged' -ExitCode 2
        }
        Invoke-C590 -Exe 'docker' -ArgumentList @('rm', [string]$m.ownedId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'expectation-before-post' {
        Invoke-C590 -Exe 'write-expectation' -ArgumentList @([string]$m.bodySha) | Out-Null
        Invoke-C590 -Exe 'http' -ArgumentList @('POST', 'WhenIdle') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'unknown-ack' {
        Invoke-C590 -Exe 'http' -ArgumentList @('POST', 'WhenIdle') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'UnknownAck' -ExitCode 2
    }
    'live-absence' {
        if ([bool]$m.requestOwnerAlive) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'Incomplete' -ExitCode 2
        }
        Invoke-C590 -Exe 'http' -ArgumentList @('POST', 'WhenIdle') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'refused-retry' {
        if ([bool]$m.nativePrompt -or [bool]$m.bodyWritten -or [bool]$m.enterWritten) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'AbsenceRequired' -ExitCode 2
        }
        Invoke-C590 -Exe 'http' -ArgumentList @('POST', 'WhenIdle') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'crash-export' {
        Invoke-C590 -Exe 'export-reached' -ArgumentList @('digest') | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('kill', [string]$m.fixtureServerId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'crash-target' {
        Invoke-C590 -Exe 'docker' -ArgumentList @('kill', [string]$m.fixtureServerId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'crash-intent' {
        Invoke-C590 -Exe 'write-intent' -ArgumentList @('kill') | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('kill', [string]$m.fixtureServerId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'await-exit' {
        Invoke-C590 -Exe 'observe-exited' -ArgumentList @([string]$m.fixtureServerId) | Out-Null
        Invoke-C590 -Exe 'docker' -ArgumentList @('start', [string]$m.fixtureServerId) | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'restart-identity' {
        if ([string]$m.runnerIdAfter -ne [string]$m.runnerIdBefore) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RestartIdentity' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'retype-charge' {
        if ([int]$m.attempts -ne 2 -or -not [bool]$m.floor1 -or -not [bool]$m.floor2) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RetypeCharge' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'enter-only-body' {
        if ([int]$m.extraBody -gt 0) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'SecondBody' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'zero-writes' {
        if ([int]$m.additionalBody -ne 0 -or [int]$m.additionalEnter -ne 0) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ExtraWrite' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'resume-manifest' {
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0 -Extra @{ post = 0 }
    }
    'failure-summary' {
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode ([int]$m.runExit) -Extra @{ delivery = 'confirmed'; runExit = [int]$m.runExit }
    }
    'missing-cut' {
        if (-not [bool]$m.reached) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'CutNotReached' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'stock-gates' {
        if (-not [bool]$m.stockIdle -or -not [bool]$m.stockBusy) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'StockGateMissing' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'interrupted-export' {
        if (-not [bool]$m.exportComplete) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ExportIncomplete' -ExitCode 2
        }
        Invoke-C590 -Exe 'commit-manifest' -ArgumentList @('result-ready') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'when-idle' {
        Invoke-C590 -Exe 'http' -ArgumentList @('POST', 'WhenIdle') | Out-Null
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'enter-only-tuple' {
        if ([int]$m.attempts -ne 1 -or [string]$m.floor -ne [string]$m.originalFloor -or [string]$m.generation -ne [string]$m.originalGeneration) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'TupleChanged' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'retype-second-tuple' {
        if (-not [bool]$m.attempt2Snapshot) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'MissingSecondTuple' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'foreign-resume' {
        $canonical = ''
        if ($m.PSObject.Properties.Name -contains 'canonicalResume' -and $m.canonicalResume) { $canonical = [string]$m.canonicalResume }
        if (-not (Test-Boundary -PathValue ([string]$m.resumePath) -RootValue ([string]$m.evidenceRoot) -Canonical $canonical)) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'ForeignResume' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    'private-credential' {
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0 -Extra @{ credential = 'redacted' }
    }
    'e2e-staged-apphost' {
        $source = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\tests\Antiphon.E2E\Fixtures\IsolatedSessionRunner.cs')
        if ($source -notmatch 'TestAppHostPath') {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'MissingConsumer' -ExitCode 2
        }
        $staged = [string]$m.stagedApphost
        if (-not (Test-Path -LiteralPath $staged)) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'MissingApphost' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $true -ExitCode 0
    }
    default {
        if ($env:ANTIPHON_C590_STUB) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'UnknownCase' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis ('RealCasePending ' + $Case) -ExitCode 2
    }
}
