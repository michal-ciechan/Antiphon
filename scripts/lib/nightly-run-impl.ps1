#requires -Version 5.1
# CARD-0487: native nightly bootstrap implementation (lock, state, hop, coverage gates).
# ASCII-only.

if ($script:AntiphonNightlyRunImplLoaded) { return }
$script:AntiphonNightlyRunImplLoaded = $true

function Get-NightlyDefaultStateRoot { return 'C:\Antiphon\nightly' }

function New-NightlyRunDirectoryName {
    param([datetime]$Utc, [string]$RunId)
    $stamp = $Utc.ToUniversalTime().ToString('yyyy-MM-dd-HHmm')
    $short = $RunId
    if ($short.Length -gt 8) { $short = $short.Substring(0, 8) }
    return ('{0}-{1}' -f $stamp, $short)
}

function Get-NightlyStatePaths {
    param([string]$StateRoot)
    return [pscustomobject]@{
        StateRoot = $StateRoot
        LastAttempt = Join-Path $StateRoot 'last-run.json'
        LastGreen = Join-Path $StateRoot 'last-complete-green.json'
        Lock = Join-Path $StateRoot 'run.lock'
        Logs = Join-Path $StateRoot 'logs'
    }
}

function Test-NightlyCompleteGreenPredicate {
    <#
      CARD-0599 D-2. The predicate now names the credit kind it is being asked about.
      Called without -CreditKind it answers the historical question - does this state
      earn MASTER readiness credit - so every existing caller keeps its meaning and an
      RC run can never back into master's own readiness. -CreditKind 'rc-release' asks
      the separate release question. An unknown profile/trigger/ref combination earns
      nothing; there is no default kind.
    #>
    param($State, [string]$CreditKind = 'master-scheduled')
    if (-not $State) { return $false }
    $want = ([string]$CreditKind).Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($want)) { $want = 'master-scheduled' }
    if ($script:ReleaseGateCreditKinds -notcontains $want) { return $false }
    $verdict = Get-ReleaseGateCreditVerdict -State $State
    if (-not $verdict.Ok) { return $false }
    return ([string]$verdict.CreditKind -eq $want)
}

function Write-NightlyRunState {
    param(
        [string]$Path,
        $State,
        [switch]$AllowFailure
    )
    try {
        Write-NightlyAtomicJson -Path $Path -Object $State
        return $true
    } catch {
        Write-NightlyRunLine ('state write failed: {0}' -f $_.Exception.Message)
        if ($AllowFailure) { return $false }
        throw
    }
}

function Invoke-NightlyWatchedCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds = 0,
        [hashtable]$Environment = $null
    )
    Write-NightlyTrace -Kind 'exec' -Message ($FilePath + ' ' + ($ArgumentList -join ' '))
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess) {
        return $script:NightlySeams.StartProcess.Invoke($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
    }
    $start = @{
        FilePath = $FilePath
        WorkingDirectory = $WorkingDirectory
        PassThru = $true
        NoNewWindow = $true
        Wait = $true
    }
    if ($ArgumentList -and $ArgumentList.Count -gt 0) { $start.ArgumentList = $ArgumentList }
    $proc = Start-NightlyProcess -StartParams $start -Environment $Environment
    $code = 1
    if ($null -ne $proc) { $code = [int]$proc.ExitCode }
    return [pscustomobject]@{ ExitCode = $code; TimedOut = $false; Pid = $(if ($proc) { $proc.Id } else { 0 }) }
}

function Invoke-AntiphonNightlyRunCore {
    param(
        [string[]]$Suites,
        [switch]$NoReport,
        [switch]$NoSync,
        [string]$CheckoutRoot = 'C:\Antiphon\nightly\checkout',
        [string]$LogRoot = '',
        [string]$Ref = 'master',
        [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon',
        [string]$StateRoot = '',
        [string]$SeamsPath = '',
        [string]$Trigger = 'scheduled',
        [string]$Profile = 'nightly',
        [string]$ExpectedSha = '',
        [string]$CandidateId = '',
        [string]$ReleaseRoot = '',
        [string]$CoordinationRoot = '',
        [string]$RunId = '',
        [string]$ContinueRunId = '',
        [int]$ContinueParentPid = 0,
        [string]$ContinueParentStartedAt = '',
        [switch]$PassThru,
        [switch]$WhatIf
    )

    Import-NightlySeams -SeamsPath $SeamsPath
    if ([string]::IsNullOrWhiteSpace($StateRoot)) { $StateRoot = Get-NightlyDefaultStateRoot }
    if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = New-NightlyRunId }
    if ([string]::IsNullOrWhiteSpace($Profile)) { $Profile = 'nightly' }
    $Profile = $Profile.Trim().ToLowerInvariant()
    $paths = Get-NightlyStatePaths -StateRoot $StateRoot
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { $LogRoot = $paths.Logs }

    $startedAt = Get-NightlyUtcNow
    # CARD-0545 D-10: the London calendar date of the run start is the run's due-day identity.
    $localDueDate = (ConvertTo-NightlyLondonLocal -Utc $startedAt).ToString('yyyy-MM-dd')
    $exitCode = 0
    $ownsLock = $false
    $recordAttempt = $false
    $sha = ''
    $testExit = 0
    $reportExit = 0
    $coverageComplete = $false
    $testsPassed = $false
    $reportDelivered = $false
    $runDir = $null
    $phase = 'init'
    $hopped = $false
    $result = [ordered]@{
        ExitCode = 1
        RunId = $RunId
        Phase = $phase
        Refusal = ''
        Sha = ''
        coverageComplete = $false
        testsPassed = $false
        reportDelivered = $false
        trigger = $Trigger
        profile = $Profile
        ref = $Ref
        CreditKind = ''
        CandidateId = $CandidateId
        StateRoot = $StateRoot
        LogDir = $null
        OwnsLock = $false
        LocalDueDate = $localDueDate
        PolicyHash = ''
        SummaryPath = ''
    }

    try {
        if ($script:ReleaseGateProfiles -notcontains $Profile) {
            Write-Host ('REFUSED: unknown profile {0}.' -f $Profile)
            $result.Refusal = 'unknown-profile'
            $result.Phase = 'profile'
            $result.ExitCode = 3
            return [pscustomobject]$result
        }
        $lane = $(if ($Profile -eq 'rc') { 'rc' } else { 'master' })
        if ($Profile -eq 'rc') {
            # D-2: an RC must never be pointed at the master state root; its attempt,
            # green, monitor and receipt files all live under its own candidate root.
            $candidate = Test-ReleaseGateCandidateRef -Ref $Ref
            if (-not $candidate.Ok) {
                Write-Host ('REFUSED: rc profile needs a release/rc-<stamp> ref ({0}).' -f $candidate.Reason)
                $result.Refusal = 'rc-ref'
                $result.Phase = 'profile'
                $result.ExitCode = 3
                return [pscustomobject]$result
            }
            if ([string]::IsNullOrWhiteSpace($CandidateId)) { $CandidateId = $candidate.CandidateId; $result.CandidateId = $CandidateId }
            if (-not (Test-ReleaseGateFullSha -Sha $ExpectedSha)) {
                Write-Host 'REFUSED: rc profile needs -ExpectedSha pinned to the candidate full sha.'
                $result.Refusal = 'rc-expected-sha'
                $result.Phase = 'profile'
                $result.ExitCode = 3
                return [pscustomobject]$result
            }
            $masterRoot = ConvertTo-NightlyCanonicalPath -Path (Get-NightlyDefaultStateRoot)
            $wantRoot = ConvertTo-NightlyCanonicalPath -Path $StateRoot
            if ([string]::Equals($masterRoot, $wantRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Host 'REFUSED: rc profile may not use the master state root.'
                $result.Refusal = 'rc-master-state-root'
                $result.Phase = 'profile'
                $result.ExitCode = 3
                return [pscustomobject]$result
            }
        } elseif ($Trigger -eq 'rc') {
            Write-Host 'REFUSED: trigger rc requires the rc profile.'
            $result.Refusal = 'trigger-profile-mismatch'
            $result.Phase = 'profile'
            $result.ExitCode = 3
            return [pscustomobject]$result
        }
        try {
            $CheckoutRoot = ConvertTo-NightlyResolvedPath -Path $CheckoutRoot
        } catch {
            $exitCode = 3
            $result.Refusal = 'reparse'
            $result.Phase = 'ownership'
            $result.ExitCode = $exitCode
            Write-Host ('REFUSED: reparse: {0}' -f $_.Exception.Message)
            return [pscustomobject]$result
        }

        if (Test-NightlySharedTree -Path $CheckoutRoot) {
            Write-Host 'REFUSED: -CheckoutRoot is the shared tree or a worktree.'
            Write-Host ('  CheckoutRoot: {0}' -f $CheckoutRoot)
            $exitCode = 3
            $result.Refusal = 'shared-tree'
            $result.Phase = 'ownership'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }
        if (Test-NightlyGitWorktree -Path $CheckoutRoot) {
            Write-Host 'REFUSED: linked Git worktree is not an independent clone.'
            $exitCode = 3
            $result.Refusal = 'linked-worktree'
            $result.Phase = 'ownership'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }

        $ownership = Test-NightlyRunContextOwnership -ClonePath $CheckoutRoot -StateRoot $StateRoot -LogRoot $LogRoot -ExpectedOrigin $RemoteUrl
        if (-not $ownership.Ok -and $ownership.Reason -ne 'unmarked-clone') {
            # unmarked is allowed only before first clone; existing path without marker is refused.
            if (Test-Path -LiteralPath $CheckoutRoot) {
                Write-Host ('REFUSED: run-context ownership ({0}/{1}).' -f $ownership.Field, $ownership.Reason)
                $exitCode = 3
                $result.Refusal = $ownership.Reason
                $result.Phase = 'ownership'
                $result.ExitCode = $exitCode
                return [pscustomobject]$result
            }
        } elseif (-not $ownership.Ok -and $ownership.Reason -eq 'unmarked-clone' -and (Test-Path -LiteralPath (Join-Path $CheckoutRoot '.git'))) {
            Write-Host 'REFUSED: unmarked clone is not a proven nightly checkout.'
            $exitCode = 3
            $result.Refusal = 'unmarked-clone'
            $result.Phase = 'ownership'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }

        $lock = Enter-NightlyExclusiveLock -StateRoot $StateRoot -RunId $RunId -ContinueRunId $ContinueRunId -ContinueParentPid $ContinueParentPid -ContinueParentStartedAt $ContinueParentStartedAt
        if (-not $lock.Ok) {
            Write-Host ('REFUSED: nightly lock ({0}).' -f $lock.Reason)
            $exitCode = 3
            $result.Refusal = $lock.Reason
            $result.Phase = 'lock'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }
        $ownsLock = [bool]$lock.OwnsLock
        $result.OwnsLock = $ownsLock
        $recordAttempt = $true

        # D-4: one shared verification lock across the master and RC lanes, taken in the
        # same order by both entrypoints. A live owner is never stolen on age; a busy
        # scheduled RC records deferred-busy and waits for its next slot.
        $sharedOwnerPid = 0
        $sharedOwnerStart = ''
        if (-not [string]::IsNullOrWhiteSpace($ContinueRunId)) {
            $sharedOwnerPid = $ContinueParentPid
            $sharedOwnerStart = $ContinueParentStartedAt
        }
        $shared = Enter-ReleaseGateNativeLock -CoordinationRoot $CoordinationRoot -RunId $RunId -Lane $lane `
            -ContinuationId $ContinueRunId -ContinueOwnerPid $sharedOwnerPid -ContinueOwnerStartedAt $sharedOwnerStart
        if (-not $shared.Ok) {
            Write-Host ('REFUSED: shared native lock ({0}).' -f $shared.Reason)
            $busy = [ordered]@{
                runId = $RunId
                profile = $Profile
                lane = $lane
                ref = $Ref
                trigger = $Trigger
                phase = 'deferred-busy'
                reason = [string]$shared.Reason
                startedAt = $startedAt.ToString('o')
                localDueDate = $localDueDate
                coverageComplete = $false
                testsPassed = $false
                reportDelivered = $false
                succeeded = $false
                exitCode = 3
            }
            [void](Write-NightlyRunState -Path $paths.LastAttempt -State $busy -AllowFailure)
            $exitCode = 3
            $result.Refusal = [string]$shared.Reason
            $result.Phase = 'shared-lock'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }

        $stateObj = [ordered]@{
            runId = $RunId
            sha = ''
            ref = $Ref
            trigger = $Trigger
            profile = $Profile
            expectedSha = $ExpectedSha
            candidateId = $CandidateId
            lane = $lane
            phase = 'Started'
            startedAt = $startedAt.ToString('o')
            localDueDate = $localDueDate
            lastActivityAt = $startedAt.ToString('o')
            coverageComplete = $false
            testsPassed = $false
            reportDelivered = $false
            succeeded = $false
            testExit = $null
            reportExit = $null
            exitCode = $null
            logDir = $null
            policyHash = $null
            noReport = [bool]$NoReport
        }
        $phase = 'Started'
        $wroteStarted = Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure
        if (-not $wroteStarted) {
            $exitCode = 1
            $result.Phase = 'Started'
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }

        if (-not $NoSync) {
            $phase = 'fetch'
            $stateObj.phase = $phase
            $stateObj.lastActivityAt = (Get-NightlyUtcNow).ToString('o')
            if (-not (Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure)) {
                $exitCode = 1
                $result.Phase = $phase
                $result.ExitCode = $exitCode
                return [pscustomobject]$result
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $CheckoutRoot) -Force | Out-Null
            $gitDir = Join-Path $CheckoutRoot '.git'
            if (-not (Test-Path -LiteralPath $gitDir)) {
                $clone = Invoke-NightlyGit -WorkingDirectory (Split-Path -Parent $CheckoutRoot) -Arguments @('clone', $RemoteUrl, $CheckoutRoot)
                if ([int]$clone.ExitCode -ne 0) { throw ('git clone failed with exit {0}' -f $clone.ExitCode) }
                $marker = Join-Path $CheckoutRoot '.antiphon-nightly-owned'
                Write-NightlyAtomicJson -Path $marker -Object ([ordered]@{ origin = $RemoteUrl; kind = 'nightly-clone' })
            }
            $fetch = Invoke-NightlyGit -WorkingDirectory $CheckoutRoot -Arguments @('fetch', 'origin', $Ref)
            if ([int]$fetch.ExitCode -ne 0) { throw ('git fetch origin {0} failed with exit {1}' -f $Ref, $fetch.ExitCode) }
            $reset = Invoke-NightlyGit -WorkingDirectory $CheckoutRoot -Arguments @('reset', '--hard', 'FETCH_HEAD')
            if ([int]$reset.ExitCode -ne 0) { throw ('git reset --hard FETCH_HEAD failed with exit {0}' -f $reset.ExitCode) }
            $clean = Invoke-NightlyGit -WorkingDirectory $CheckoutRoot -Arguments @('clean', '-fdx')
            if ([int]$clean.ExitCode -ne 0) { throw ('git clean -fdx failed with exit {0}' -f $clean.ExitCode) }
            $rev = Invoke-NightlyGit -WorkingDirectory $CheckoutRoot -Arguments @('rev-parse', 'HEAD')
            $sha = ([string]$rev.Output).Trim()
            $stateObj.sha = $sha
            if (-not [string]::IsNullOrWhiteSpace($ExpectedSha) -and -not [string]::Equals($sha, $ExpectedSha, [StringComparison]::OrdinalIgnoreCase)) {
                # D-3: a moved candidate ref fails; never retest implicitly under its old identity.
                Write-Host ('REFUSED: candidate moved, head={0} expected={1}' -f $sha, $ExpectedSha)
                $stateObj.phase = 'candidate-moved'
                $stateObj.exitCode = 3
                [void](Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure)
                $exitCode = 3
                $result.Refusal = 'candidate-moved'
                $result.Phase = 'candidate-moved'
                $result.Sha = $sha
                $result.ExitCode = $exitCode
                return [pscustomobject]$result
            }

            $cloneScript = Join-Path $CheckoutRoot (Join-Path 'scripts' 'nightly-run.ps1')
            $running = $PSCommandPath
            if ([string]::IsNullOrWhiteSpace($running)) { $running = $MyInvocation.MyCommand.Path }
            if ((Test-Path -LiteralPath $cloneScript) -and -not [string]::IsNullOrWhiteSpace($running) -and (Test-Path -LiteralPath $running)) {
                $left = (Get-FileHash -LiteralPath $running -Algorithm SHA256).Hash
                $right = (Get-FileHash -LiteralPath $cloneScript -Algorithm SHA256).Hash
                if ($left -ne $right) {
                    $phase = 'hop'
                    $pwshArgs = @(
                        '-NoProfile', '-NonInteractive', '-File', $cloneScript,
                        '-NoSync',
                        '-CheckoutRoot', $CheckoutRoot,
                        '-LogRoot', $LogRoot,
                        '-Ref', $Ref,
                        '-StateRoot', $StateRoot,
                        '-Trigger', $Trigger,
                        '-Profile', $Profile,
                        '-RunId', $RunId,
                        '-ContinueRunId', $RunId,
                        '-ContinueParentPid', ([string]$PID),
                        '-ContinueParentStartedAt', $startedAt.ToString('o')
                    )
                    if ($NoReport) { $pwshArgs += '-NoReport' }
                    # D-2/D-3: profile, pinned sha and candidate identity must survive the hop.
                    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) { $pwshArgs += '-ExpectedSha'; $pwshArgs += $ExpectedSha }
                    if (-not [string]::IsNullOrWhiteSpace($CandidateId)) { $pwshArgs += '-CandidateId'; $pwshArgs += $CandidateId }
                    if (-not [string]::IsNullOrWhiteSpace($ReleaseRoot)) { $pwshArgs += '-ReleaseRoot'; $pwshArgs += $ReleaseRoot }
                    if (-not [string]::IsNullOrWhiteSpace($CoordinationRoot)) { $pwshArgs += '-CoordinationRoot'; $pwshArgs += $CoordinationRoot }
                    if ($Suites -and $Suites.Count -gt 0) {
                        $pwshArgs += '-Suites'
                        $pwshArgs += ($Suites -join ',')
                    }
                    if (-not [string]::IsNullOrWhiteSpace($SeamsPath)) {
                        $pwshArgs += '-SeamsPath'
                        $pwshArgs += $SeamsPath
                    }
                    $hop = Invoke-NightlyWatchedCommand -FilePath 'pwsh' -ArgumentList $pwshArgs -WorkingDirectory $CheckoutRoot
                    $hopped = $true
                    $exitCode = [int]$hop.ExitCode
                    if ($null -eq $hop.CompletionRecord -and $hop.PSObject.Properties['ExitCode'] -eq $null -and $hop -is [int]) {
                        $exitCode = [int]$hop
                    }
                    $result.ExitCode = $exitCode
                    $result.Phase = 'hop'
                    $result.Sha = $sha
                    return [pscustomobject]$result
                }
            }
        } else {
            $rev = Invoke-NightlyGit -WorkingDirectory $CheckoutRoot -Arguments @('rev-parse', 'HEAD')
            $sha = ([string]$rev.Output).Trim()
            $stateObj.sha = $sha
        }

        if ($WhatIf) {
            $result.ExitCode = 0
            $result.Phase = 'whatif'
            $result.Sha = $sha
            return [pscustomobject]$result
        }

        $phase = 'tests'
        $runName = New-NightlyRunDirectoryName -Utc (Get-NightlyUtcNow) -RunId $RunId
        $runDir = Join-Path $LogRoot $runName
        New-Item -ItemType Directory -Path $runDir -Force | Out-Null
        $stateObj.logDir = $runDir
        $stateObj.phase = $phase
        $stateObj.lastActivityAt = (Get-NightlyUtcNow).ToString('o')
        if (-not (Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure)) {
            $exitCode = 1
            $result.Phase = $phase
            $result.ExitCode = $exitCode
            return [pscustomobject]$result
        }

        $testsScript = Join-Path $CheckoutRoot (Join-Path 'scripts' 'nightly-tests.ps1')
        if (-not (Test-Path -LiteralPath $testsScript)) {
            $testsScript = Join-Path $PSScriptRoot '..\nightly-tests.ps1'
        }
        $gitRefLabel = ('origin/{0}' -f $Ref)
        $testArgs = @(
            '-NoProfile', '-NonInteractive', '-File', $testsScript,
            '-RepoRoot', $CheckoutRoot,
            '-LogRoot', $runDir,
            '-Sha', $sha,
            '-GitRef', $gitRefLabel,
            '-Trigger', $Trigger,
            '-Profile', $Profile,
            '-RunId', $RunId
        )
        if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
            $testArgs += '-ExpectedSha'
            $testArgs += $ExpectedSha
        }
        if ($Suites -and $Suites.Count -gt 0) {
            $testArgs += '-Suites'
            $testArgs += ($Suites -join ',')
        }
        if (-not [string]::IsNullOrWhiteSpace($SeamsPath)) {
            $testArgs += '-SeamsPath'
            $testArgs += $SeamsPath
        }
        Write-NightlyRunLine ('running {0}' -f $testsScript)
        $testRun = Invoke-NightlyWatchedCommand -FilePath 'pwsh' -ArgumentList $testArgs -WorkingDirectory $CheckoutRoot
        $testExit = [int]$testRun.ExitCode
        if ($null -eq $testExit) { $testExit = 1 }

        $summaryPath = Join-Path $runDir 'summary.json'
        $summary = $null
        if (Test-Path -LiteralPath $summaryPath) {
            try { $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $summary = $null }
        }
        if ($summary) {
            $coverageComplete = [bool]$summary.coverageComplete
            $testsPassed = [bool]$summary.testsPassed
            if ($summary.policyHash) { $stateObj.policyHash = [string]$summary.policyHash }
        } else {
            $coverageComplete = $false
            $testsPassed = ($testExit -eq 0)
        }
        if ($testExit -ne 0) { $testsPassed = $false }

        $reportExit = 0
        if (-not $NoReport) {
            $phase = 'report'
            $reportScript = Join-Path $CheckoutRoot (Join-Path 'scripts' 'nightly-report.ps1')
            if (-not (Test-Path -LiteralPath $reportScript)) {
                $reportScript = Join-Path $PSScriptRoot '..\nightly-report.ps1'
            }
            $prevSummary = ''
            if (Test-Path -LiteralPath $paths.LastGreen) {
                try {
                    $green = Get-Content -LiteralPath $paths.LastGreen -Raw -Encoding UTF8 | ConvertFrom-Json
                    if ($green.summaryPath -and (Test-Path -LiteralPath ([string]$green.summaryPath))) {
                        $prevSummary = [string]$green.summaryPath
                    }
                } catch { }
            }
            $reportArgs = @(
                '-NoProfile', '-NonInteractive', '-File', $reportScript,
                '-Summary', $summaryPath
            )
            if ($prevSummary) {
                $reportArgs += '-PreviousSummary'
                $reportArgs += $prevSummary
            }
            Write-NightlyRunLine ('running {0}' -f $reportScript)
            try {
                $rep = Invoke-NightlyWatchedCommand -FilePath 'pwsh' -ArgumentList $reportArgs -WorkingDirectory $CheckoutRoot
                $reportExit = [int]$rep.ExitCode
                if ($null -eq $reportExit) { $reportExit = 3 }
            } catch {
                $reportExit = 3
                Write-NightlyRunLine ('REPORTING: {0}' -f $_.Exception.Message)
            }
        } else {
            $reportDelivered = $false
        }

        if ($NoReport) {
            $reportDelivered = $false
        } else {
            $reportDelivered = ($reportExit -eq 0)
        }
        # -NoReport never claims delivery, whatever summary.json says (CARD-0545 D-10 no-report row).
        if (-not $NoReport -and $summary -and $null -ne $summary.reportDelivered) {
            $reportDelivered = [bool]$summary.reportDelivered -and ($reportExit -eq 0)
        }

        if ($reportExit -ne 0) {
            $exitCode = $reportExit
        } elseif ($testExit -ne 0) {
            $exitCode = $testExit
        } else {
            $exitCode = 0
        }
        if ($testExit -ne 0 -and $reportExit -eq 0) {
            $exitCode = $testExit
        }

        $stateObj.phase = 'final'
        $stateObj.testExit = $testExit
        $stateObj.reportExit = $reportExit
        $stateObj.exitCode = $exitCode
        $stateObj.coverageComplete = $coverageComplete
        $stateObj.testsPassed = $testsPassed
        $stateObj.reportDelivered = $reportDelivered
        $stateObj.succeeded = ($exitCode -eq 0 -and $coverageComplete -and $testsPassed)
        $stateObj.completedAt = (Get-NightlyUtcNow).ToString('o')
        $stateObj.lastActivityAt = $stateObj.completedAt
        $stateObj.summaryPath = $summaryPath
        if (-not (Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure)) {
            if ($exitCode -eq 0) { $exitCode = 1 }
        } else {
            # D-2: the state itself names the kind of credit it earns. Master green is the
            # only thing that ever reaches <StateRoot>\last-complete-green.json; RC green
            # lands in the candidate's own store and never touches master readiness.
            $verdict = Get-ReleaseGateCreditVerdict -State $stateObj
            $creditKind = [string]$verdict.CreditKind
            $result.CreditKind = $creditKind
            $stateObj.creditKind = $creditKind
            if ($creditKind -eq 'master-scheduled') {
                Write-NightlyRunState -Path $paths.LastGreen -State $stateObj -AllowFailure | Out-Null
            } elseif ($creditKind -eq 'rc-release') {
                $credit = Get-ReleaseGateStatePaths -CreditKind $creditKind -StateRoot $StateRoot -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
                New-Item -ItemType Directory -Path $credit.Root -Force | Out-Null
                Write-NightlyRunState -Path $credit.GreenPath -State $stateObj -AllowFailure | Out-Null
                $result.GreenPath = $credit.GreenPath
            }
        }
    } catch {
        if ($exitCode -eq 0) { $exitCode = 1 }
        Write-NightlyRunLine $_.Exception.Message
        $stateObj.phase = $phase
        $stateObj.exitCode = $exitCode
        $stateObj.error = $_.Exception.Message
        [void](Write-NightlyRunState -Path $paths.LastAttempt -State $stateObj -AllowFailure)
    } finally {
        # D-4: released only after this run's owned children are accounted for, which the
        # tests hop guarantees by waiting for its child before returning.
        Exit-ReleaseGateNativeLock -CoordinationRoot $CoordinationRoot -RunId $RunId
        Exit-NightlyExclusiveLock -StateRoot $StateRoot -RunId $RunId
    }

    $result.ExitCode = $exitCode
    $result.Phase = $phase
    $result.Sha = $sha
    $result.coverageComplete = $coverageComplete
    $result.testsPassed = $testsPassed
    $result.reportDelivered = $reportDelivered
    $result.LogDir = $runDir
    if ($stateObj -and $stateObj.policyHash) { $result.PolicyHash = [string]$stateObj.policyHash }
    if ($stateObj -and $stateObj.summaryPath) { $result.SummaryPath = [string]$stateObj.summaryPath }
    $result.testExit = $testExit
    $result.reportExit = $reportExit
    return [pscustomobject]$result
}


function ConvertTo-NightlyResultRecord {
    # CARD-0545 D-10: the compact completion record Windmill takes as the job result. Key order is
    # fixed. A refusal before the run started carries exitCode and empty identity; otherwise the
    # durable last-run.json (written by this run or, after the self-update hop, by the child) is the
    # source so the line and the state file never disagree.
    param($Result)
    $record = [ordered]@{
        nativeRunId = ''
        sha = ''
        ref = ''
        trigger = ''
        profile = ''
        candidateId = ''
        creditKind = ''
        localDueDate = ''
        policyHash = ''
        coverageComplete = $false
        testsPassed = $false
        reportDelivered = $false
        exitCode = 1
        summaryPath = ''
    }
    if ($null -eq $Result) { return $record }
    if ($null -ne $Result.ExitCode) { $record.exitCode = [int]$Result.ExitCode }
    if (-not [string]::IsNullOrWhiteSpace([string]$Result.Refusal)) { return $record }
    $state = $null
    if (-not [string]::IsNullOrWhiteSpace([string]$Result.StateRoot)) {
        $last = Join-Path ([string]$Result.StateRoot) 'last-run.json'
        if (Test-Path -LiteralPath $last) {
            try { $state = Get-Content -LiteralPath $last -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $state = $null }
        }
    }
    if ($state -and [string]$state.runId -eq [string]$Result.RunId) {
        $record.nativeRunId = [string]$state.runId
        $record.sha = [string]$state.sha
        $record.ref = [string]$state.ref
        $record.trigger = [string]$state.trigger
        $record.profile = [string]$state.profile
        $record.candidateId = [string]$state.candidateId
        $record.creditKind = [string]$state.creditKind
        $record.localDueDate = [string]$state.localDueDate
        $record.policyHash = [string]$state.policyHash
        $record.coverageComplete = [bool]$state.coverageComplete
        $record.testsPassed = [bool]$state.testsPassed
        $record.reportDelivered = [bool]$state.reportDelivered
        $record.summaryPath = [string]$state.summaryPath
        return $record
    }
    $record.nativeRunId = [string]$Result.RunId
    $record.sha = [string]$Result.Sha
    $record.ref = [string]$Result.ref
    $record.trigger = [string]$Result.trigger
    $record.profile = [string]$Result.profile
    $record.candidateId = [string]$Result.CandidateId
    $record.creditKind = [string]$Result.CreditKind
    $record.localDueDate = [string]$Result.LocalDueDate
    $record.policyHash = [string]$Result.PolicyHash
    $record.coverageComplete = [bool]$Result.coverageComplete
    $record.testsPassed = [bool]$Result.testsPassed
    $record.reportDelivered = [bool]$Result.reportDelivered
    $record.summaryPath = [string]$Result.SummaryPath
    return $record
}

function Invoke-AntiphonNightlyRun {
    # CARD-0545 D-10: every exit path, refusals included, ends stdout with the JSON result line.
    param(
        [string[]]$Suites,
        [switch]$NoReport,
        [switch]$NoSync,
        [string]$CheckoutRoot = 'C:\Antiphon\nightly\checkout',
        [string]$LogRoot = '',
        [string]$Ref = 'master',
        [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon',
        [string]$StateRoot = '',
        [string]$SeamsPath = '',
        [string]$Trigger = 'scheduled',
        [string]$Profile = 'nightly',
        [string]$ExpectedSha = '',
        [string]$CandidateId = '',
        [string]$ReleaseRoot = '',
        [string]$CoordinationRoot = '',
        [string]$RunId = '',
        [string]$ContinueRunId = '',
        [int]$ContinueParentPid = 0,
        [string]$ContinueParentStartedAt = '',
        [switch]$PassThru,
        [switch]$WhatIf
    )
    $result = Invoke-AntiphonNightlyRunCore @PSBoundParameters
    $record = ConvertTo-NightlyResultRecord -Result $result
    Write-Host (ConvertTo-Json -InputObject $record -Compress)
    return $result
}
