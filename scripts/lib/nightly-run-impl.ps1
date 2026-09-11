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
    param($State)
    if (-not $State) { return $false }
    if (-not [bool]$State.coverageComplete) { return $false }
    if (-not [bool]$State.testsPassed) { return $false }
    if (-not [bool]$State.reportDelivered) { return $false }
    $ref = [string]$State.ref
    if ($ref -ne 'master' -and $ref -ne 'origin/master') { return $false }
    $trigger = [string]$State.trigger
    if ($trigger -ne 'scheduled') { return $false }
    return $true
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
    $proc = Start-Process @start
    $code = 1
    if ($null -ne $proc) { $code = [int]$proc.ExitCode }
    return [pscustomobject]@{ ExitCode = $code; TimedOut = $false; Pid = $(if ($proc) { $proc.Id } else { 0 }) }
}

function Invoke-AntiphonNightlyRun {
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
    $paths = Get-NightlyStatePaths -StateRoot $StateRoot
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { $LogRoot = $paths.Logs }

    $startedAt = Get-NightlyUtcNow
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
        ref = $Ref
        StateRoot = $StateRoot
        LogDir = $null
        OwnsLock = $false
    }

    try {
        try {
            $CheckoutRoot = ConvertTo-NightlyResolvedPath -Path $CheckoutRoot
        } catch {
            $exitCode = 3
            $result.Refusal = 'reparse'
            $result.Phase = 'ownership'
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

        $stateObj = [ordered]@{
            runId = $RunId
            sha = ''
            ref = $Ref
            trigger = $Trigger
            phase = 'Started'
            startedAt = $startedAt.ToString('o')
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
                        '-RunId', $RunId,
                        '-ContinueRunId', $RunId,
                        '-ContinueParentPid', ([string]$PID),
                        '-ContinueParentStartedAt', $startedAt.ToString('o')
                    )
                    if ($NoReport) { $pwshArgs += '-NoReport' }
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
            '-RunId', $RunId
        )
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
        if ($summary -and $null -ne $summary.reportDelivered) {
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
            if (Test-NightlyCompleteGreenPredicate -State $stateObj) {
                Write-NightlyRunState -Path $paths.LastGreen -State $stateObj -AllowFailure | Out-Null
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
        Exit-NightlyExclusiveLock -StateRoot $StateRoot -RunId $RunId
    }

    $result.ExitCode = $exitCode
    $result.Phase = $phase
    $result.Sha = $sha
    $result.coverageComplete = $coverageComplete
    $result.testsPassed = $testsPassed
    $result.reportDelivered = $reportDelivered
    $result.LogDir = $runDir
    $result.testExit = $testExit
    $result.reportExit = $reportExit
    return [pscustomobject]$result
}
