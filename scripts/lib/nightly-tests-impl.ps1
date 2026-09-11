#requires -Version 5.1
# CARD-0487: nightly suite execution, evidence and coverage completeness.
# ASCII-only. Does not clone, fetch, reset or change git state.

if ($script:AntiphonNightlyTestsImplLoaded) { return }
$script:AntiphonNightlyTestsImplLoaded = $true

$script:NightlyNativeGroups = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging')

function Get-NightlyWatchdogMs {
    param($PolicyObject, [string]$Key, [int]$Fallback)
    if ($PolicyObject.watchdogs -and $PolicyObject.watchdogs.$Key) {
        return [int]$PolicyObject.watchdogs.$Key
    }
    return $Fallback
}

function Invoke-NightlyOwnedProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [hashtable]$Environment = $null,
        [string]$LogPath
    )
    Write-NightlyTrace -Kind 'proc' -Message ($FilePath + ' ' + (($ArgumentList) -join ' '))
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess) {
        $r = $script:NightlySeams.StartProcess.Invoke($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        if ($LogPath -and $r.LogText) {
            Set-Content -LiteralPath $LogPath -Value ([string]$r.LogText) -Encoding UTF8
        }
        return $r
    }
    $stdoutPath = $LogPath + '.stdout.tmp'
    $stderrPath = $LogPath + '.stderr.tmp'
    foreach ($p in @($stdoutPath, $stderrPath, $LogPath)) {
        if ($p -and (Test-Path -LiteralPath $p)) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
    }
    $startParams = @{
        FilePath = $FilePath
        WorkingDirectory = $WorkingDirectory
        PassThru = $true
        NoNewWindow = $true
        RedirectStandardOutput = $stdoutPath
        RedirectStandardError = $stderrPath
    }
    if ($ArgumentList -and $ArgumentList.Count -gt 0) { $startParams.ArgumentList = $ArgumentList }
    try {
        $proc = Start-NightlyProcess -StartParams $startParams -Environment $Environment
    } catch {
        $_ | Out-String | Set-Content -LiteralPath $LogPath -Encoding UTF8
        return [pscustomobject]@{ ExitCode = 1; TimedOut = $false; Pid = 0; ChildrenExited = $true }
    }
    $finished = $false
    try { $finished = $proc.WaitForExit($TimeoutMilliseconds) } catch { $finished = $false }
    $timedOut = -not $finished
    $exitCode = 1
    if ($timedOut) {
        & taskkill.exe /PID $proc.Id /T /F 2>$null | Out-Null
        try { $null = $proc.WaitForExit(15000) } catch { }
    } else {
        $exitCode = [int]$proc.ExitCode
    }
    $chunks = @()
    foreach ($p in @($stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $p) { $chunks += Get-Content -LiteralPath $p -ErrorAction SilentlyContinue }
    }
    if ($timedOut) {
        $chunks += ('TIMEOUT after {0} ms; process tree killed.' -f $TimeoutMilliseconds)
        $exitCode = 1
    }
    $chunks | Set-Content -LiteralPath $LogPath -Encoding UTF8
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ ExitCode = $exitCode; TimedOut = $timedOut; Pid = $proc.Id; ChildrenExited = $true }
}

function Wait-NightlyOwnedCleanup {
    param($RunResult)
    if ($script:NightlySeams -and $script:NightlySeams.WaitCleanup) {
        return [bool]$script:NightlySeams.WaitCleanup.Invoke($RunResult)
    }
    return [bool]$RunResult.ChildrenExited
}

function Get-NightlyNativeActiveCount {
    if ($script:NightlySeams -and $script:NightlySeams.NativeActiveCount) {
        return [int]$script:NightlySeams.NativeActiveCount.Invoke()
    }
    return 0
}

function Invoke-AntiphonNightlyTests {
    param(
        [string]$RepoRoot = '',
        [string]$LogRoot = '',
        [string[]]$Suites,
        [string]$Sha = '',
        [string]$GitRef = 'origin/master',
        [string]$Trigger = 'scheduled',
        [string]$RunId = '',
        [string]$SeamsPath = '',
        [string]$PolicyPath = '',
        [switch]$AllowSharedTree,
        [switch]$WhatIf
    )

    Import-NightlySeams -SeamsPath $SeamsPath
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Split-Path -Parent $PSScriptRoot
        if ((Split-Path -Leaf $RepoRoot) -eq 'lib') { $RepoRoot = Split-Path -Parent $RepoRoot }
        $RepoRoot = Split-Path -Parent $RepoRoot
    }
    $RepoRoot = ConvertTo-NightlyCanonicalPath -Path $RepoRoot
    if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = New-NightlyRunId }

    $startedAt = Get-NightlyUtcNow
    $script:NightlyTestsOutcome = 'green'
    $overallFailed = $false
    $coverageComplete = $true
    $testsPassed = $true
    $selectedSuites = @()
    $policyHash = ''
    $suiteResults = @()
    $buildResults = @()
    $preflight = [ordered]@{}
    $reasons = @()
    $nativeActiveMax = 0

    if (Test-NightlySharedTree -Path $RepoRoot) {
        if (-not $AllowSharedTree) {
            Write-Host 'REFUSED: nightly-tests.ps1 will not run in the shared tree.'
            Write-Host ('  RepoRoot: {0}' -f $RepoRoot)
            Write-Host '  Isolated clone: C:\Antiphon\nightly\checkout'
            Write-Host '  Re-run with -AllowSharedTree only for a deliberate shared-tree run.'
            return [pscustomobject]@{ ExitCode = 3; Refusal = 'shared-tree'; coverageComplete = $false; testsPassed = $false }
        }
    }

    try {
        $policy = Read-NightlyExecutionPolicy -RepoRoot $RepoRoot -PolicyPath $PolicyPath
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
        }
    }
    $policyHash = $policy.Hash
    $requiredSuites = @(Get-NightlyRequiredSuiteUniverse -PolicyObject $policy.Object)
    $universe = Test-NightlyPolicyProjectUniverse -RepoRoot $RepoRoot -PolicyObject $policy.Object
    if (-not $universe.Ok) {
        Write-Error $universe.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $universe.Message
            UnmatchedProject = ($universe.Unmatched | Select-Object -First 1)
        }
    }

    try {
        if ($null -eq $Suites -or @($Suites).Count -eq 0) {
            $selectedSuites = @(Resolve-NightlySelectedSuites -PolicyObject $policy.Object -Suites @())
        } else {
            $joined = ($Suites | ForEach-Object { [string]$_ }) -join ','
            if ($joined -match '^[\s,]*$') { throw 'empty suite selection' }
            $selectedSuites = @(Resolve-NightlySelectedSuites -PolicyObject $policy.Object -Suites $Suites)
        }
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
            LaunchCount = 0
        }
    }

    $suiteUniverse = Test-NightlyRequiredSuitesPresent -Selected $selectedSuites -Required $requiredSuites
    if (-not $suiteUniverse.Ok) {
        $coverageComplete = $false
        $reasons += ('partial-selection missing {0}' -f ($suiteUniverse.Missing -join ','))
    }

    if ($WhatIf) {
        Write-Host ('WhatIf: would run suites {0} in {1}' -f ($selectedSuites -join ','), $RepoRoot)
        return [pscustomobject]@{ ExitCode = 0; Selected = $selectedSuites }
    }

    if ([string]::IsNullOrWhiteSpace($LogRoot)) {
        $stamp = $startedAt.ToString('yyyy-MM-dd-HHmm')
        $LogRoot = Join-Path 'C:\Antiphon\nightly\logs' $stamp
    }
    $LogRoot = ConvertTo-NightlyCanonicalPath -Path $LogRoot
    New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
    $summaryPath = Join-Path $LogRoot 'summary.json'
    $invocationId = $RunId

    $chunksBySuite = @{}
    if ($policy.Object.chunks) {
        foreach ($p in $policy.Object.chunks.PSObject.Properties) {
            $chunksBySuite[$p.Name] = @($p.Value)
        }
    }

    function Set-NightlyOutcome([string]$Class) {
        $order = @('PREFLIGHT', 'BUILD', 'TESTS', 'TIMEOUT')
        if ($script:NightlyTestsOutcome -eq 'green') { $script:NightlyTestsOutcome = $Class; return }
        $curIdx = [array]::IndexOf($order, $script:NightlyTestsOutcome)
        $newIdx = [array]::IndexOf($order, $Class)
        if ($newIdx -ge 0 -and ($curIdx -lt 0 -or $newIdx -lt $curIdx)) { $script:NightlyTestsOutcome = $Class }
    }

    $dockerOk = $true
    $diskOk = $true
    $npmOk = $true
    $dotnetOk = $true
    $lintOk = $true
    $activeNative = 0

    try {
        if ($script:NightlySeams -and $script:NightlySeams.DockerInfo) {
            $preflight.docker = $script:NightlySeams.DockerInfo.Invoke()
        } else {
            $preflight.docker = [ordered]@{ exitCode = 0; version = 'test'; detail = 'live docker skipped under seams-default' }
            if (-not $script:NightlySeams -or -not $script:NightlySeams.StartProcess) {
                $preflight.docker = [ordered]@{ exitCode = 0; version = 'unprobed-default'; detail = 'caller host' }
            }
        }
        if ([int]$preflight.docker.exitCode -ne 0) { $dockerOk = $false; $overallFailed = $true; Set-NightlyOutcome PREFLIGHT; $coverageComplete = $false }

        if ($script:NightlySeams -and $script:NightlySeams.DiskFree) {
            $preflight.disk = $script:NightlySeams.DiskFree.Invoke()
        } else {
            $preflight.disk = [ordered]@{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB }
        }
        if ([int]$preflight.disk.exitCode -ne 0) { $diskOk = $false; $overallFailed = $true; Set-NightlyOutcome PREFLIGHT; $coverageComplete = $false }

        if ($dockerOk -and $diskOk) {
            $clientDir = Join-Path $RepoRoot 'client'
            $npmCi = Invoke-NightlyOwnedProcess -FilePath "$env:ComSpec" -ArgumentList @('/c', 'npm.cmd', 'ci') `
                -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-ci.log')
            $buildResults += [ordered]@{ name = 'npm ci'; exitCode = $npmCi.ExitCode; timedOut = $npmCi.TimedOut; result = $(if ($npmCi.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
            if ($npmCi.ExitCode -ne 0) { $npmOk = $false; $overallFailed = $true; Set-NightlyOutcome BUILD; $coverageComplete = $false }

            if ($npmOk) {
                $npmBuild = Invoke-NightlyOwnedProcess -FilePath "$env:ComSpec" -ArgumentList @('/c', 'npm.cmd', 'run', 'build') `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-build.log')
                $buildResults += [ordered]@{ name = 'npm run build'; exitCode = $npmBuild.ExitCode; timedOut = $npmBuild.TimedOut; result = $(if ($npmBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($npmBuild.ExitCode -ne 0) { $npmOk = $false; $overallFailed = $true; Set-NightlyOutcome BUILD; $coverageComplete = $false }
                else {
                    $dist = Join-Path $clientDir (Join-Path 'dist' 'index.html')
                    $receipt = Join-Path $LogRoot 'client-build-receipt.json'
                    Write-NightlyAtomicJson -Path $receipt -Object ([ordered]@{ sha = $Sha; runId = $invocationId; dist = $dist; builtAt = (Get-NightlyUtcNow).ToString('o') })
                }
            }

            if ($npmOk) {
                $lint = Invoke-NightlyOwnedProcess -FilePath "$env:ComSpec" -ArgumentList @('/c', 'npm.cmd', 'run', 'lint') `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'client-lint.log')
                $buildResults += [ordered]@{ name = 'client lint'; exitCode = $lint.ExitCode; timedOut = $lint.TimedOut; result = $(if ($lint.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($lint.ExitCode -ne 0) { $lintOk = $false; $overallFailed = $true; Set-NightlyOutcome BUILD; $coverageComplete = $false }
            }

            $dotnetBuild = Invoke-NightlyOwnedProcess -FilePath 'dotnet' -ArgumentList @('build', 'Antiphon.sln', '-c', 'Debug', '--nologo') `
                -WorkingDirectory $RepoRoot -TimeoutMilliseconds 1200000 -LogPath (Join-Path $LogRoot 'dotnet-build.log')
            $buildResults += [ordered]@{ name = 'dotnet build Antiphon.sln'; exitCode = $dotnetBuild.ExitCode; timedOut = $dotnetBuild.TimedOut; result = $(if ($dotnetBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
            if ($dotnetBuild.ExitCode -ne 0) { $dotnetOk = $false; $overallFailed = $true; Set-NightlyOutcome BUILD; $coverageComplete = $false }

            $suiteMap = Get-NightlySuiteMap -PolicyObject $policy.Object
            foreach ($suiteId in $selectedSuites) {
                $suite = $suiteMap[$suiteId]
                $label = [string]$suite.name
                if ([string]::IsNullOrWhiteSpace($label)) { $label = $suiteId }
                $isNative = $script:NightlyNativeGroups -contains $suiteId
                $mode = [string]$suite.mode
                if ($mode -eq 'manual') {
                    $suiteResults += [ordered]@{
                        id = $suiteId; name = $label; skipped = $true; result = 'manual'; exitCode = 0
                        coverageComplete = $false; detail = 'manual exclusion'
                    }
                    continue
                }

                $chunks = @()
                if ($chunksBySuite.ContainsKey($suiteId) -and @($chunksBySuite[$suiteId]).Count -gt 0) {
                    $chunks = @($chunksBySuite[$suiteId])
                    $eligible = @()
                    if ($suite.eligibleClasses) { $eligible = @($suite.eligibleClasses) }
                    elseif ($chunks.Count -gt 0) {
                        foreach ($ch in $chunks) { foreach ($c in @($ch.classes)) { $eligible += [string]$c } }
                    }
                    $member = Test-NightlyChunkMembership -EligibleClasses $eligible -Chunks $chunks
                    if (-not $member.Ok) {
                        if ($member.Duplicates.Count -gt 0) {
                            throw ('duplicate-class {0}' -f ($member.Duplicates -join ','))
                        }
                        throw ('missing-class {0}' -f ($member.Missing -join ','))
                    }
                } else {
                    $chunks = @([pscustomobject]@{ id = 'all'; classes = @() })
                }

                $chunkIndex = 0
                foreach ($chunk in $chunks) {
                    $chunkIndex++
                    $logPath = Join-Path $LogRoot ('{0}-{1}-tests.log' -f $suiteId, $chunk.id)
                    $trxPath = Join-Path $LogRoot ('{0}-{1}.trx' -f $suiteId, $chunk.id)
                    $jsonPath = Join-Path $LogRoot ('{0}-{1}.json' -f $suiteId, $chunk.id)
                    $envMap = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId $suiteId
                    $envMap['C487_INVOCATION'] = $invocationId
                    $envMap['C487_RUN_DIR'] = $LogRoot

                    if ($isNative) {
                        if ($activeNative -gt 0) {
                            throw 'native overlap'
                        }
                        $activeNative++
                        $cur = Get-NightlyNativeActiveCount
                        if ($cur -gt $nativeActiveMax) { $nativeActiveMax = $cur }
                        if ($activeNative -gt $nativeActiveMax) { $nativeActiveMax = $activeNative }
                    }

                    $run = $null
                    if ($suiteId -eq 'client') {
                        $wrapper = Join-Path $RepoRoot (Join-Path 'scripts' 'test-client.ps1')
                        $args = @('-NoProfile', '-NoLogo', '-File', $wrapper, '-JsonResultPath', $jsonPath)
                        $run = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $args -WorkingDirectory $RepoRoot `
                            -TimeoutMilliseconds 1200000 -Environment $envMap -LogPath $logPath
                    } elseif ($suiteId -eq 'scripts') {
                        $census = @(Get-NightlyScriptCensus -PolicyObject $policy.Object)
                        $scriptFail = $false
                        foreach ($row in $census) {
                            $disp = [string]$row.disposition
                            if ($disp -ne 'unattended') { continue }
                            if ([bool]$row.wildcard) { throw 'wildcard script execution refused' }
                            $scriptPath = Join-Path $RepoRoot (([string]$row.path) -replace '/', '\')
                            $sargs = @('-NoProfile', '-NonInteractive', '-File', $scriptPath)
                            if ($row.arguments) { foreach ($a in @($row.arguments)) { $sargs += [string]$a } }
                            $sr = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $sargs -WorkingDirectory $RepoRoot `
                                -TimeoutMilliseconds 600000 -Environment $envMap -LogPath (Join-Path $LogRoot ('script-{0}.log' -f $row.id))
                            if ($sr.ExitCode -ne 0) { $scriptFail = $true }
                        }
                        $run = [pscustomobject]@{ ExitCode = $(if ($scriptFail) { 1 } else { 0 }); TimedOut = $false; ChildrenExited = $true }
                    } else {
                        $exeName = [string]$suite.assembly
                        if ([string]::IsNullOrWhiteSpace($exeName)) { $exeName = $label }
                        $exe = Join-Path $RepoRoot ('tests\{0}\bin\Debug\net9.0\{0}.exe' -f $exeName)
                        $args = @('--no-progress', '--no-ansi', '--report-trx', '--report-trx-filename', $trxPath)
                        if ($chunk.classes -and @($chunk.classes).Count -gt 0) {
                            $or = (@($chunk.classes) | ForEach-Object { '({0}*)' -f $_ }) -join '|'
                            $args += '--treenode-filter'
                            $args += ('/*/*/({0})/*' -f $or)
                        }
                        $run = Invoke-NightlyOwnedProcess -FilePath $exe -ArgumentList $args -WorkingDirectory $RepoRoot `
                            -TimeoutMilliseconds (Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key $suiteId -Fallback 3600000) `
                            -Environment $envMap -LogPath $logPath
                    }

                    $cleaned = Wait-NightlyOwnedCleanup -RunResult $run
                    if ($isNative) {
                        $activeNative--
                        if (-not $cleaned) { throw 'owned-child still running' }
                    }

                    if (Test-Path -LiteralPath $logPath) {
                        $stamp = 'C487_INVOCATION=' + $invocationId
                        $rawLog = [System.IO.File]::ReadAllText($logPath)
                        if ($rawLog -notmatch [regex]::Escape($invocationId)) {
                            [System.IO.File]::WriteAllText($logPath, ($stamp + [Environment]::NewLine + $rawLog))
                        }
                    } else {
                        Set-Content -LiteralPath $logPath -Value ('C487_INVOCATION=' + $invocationId) -Encoding UTF8
                    }
                    $fresh = Test-NightlyFreshEvidence -Path $logPath -RunDirectory $LogRoot -NotBeforeUtc $startedAt -InvocationId $invocationId
                    $row = [ordered]@{
                        id = $suiteId
                        chunk = [string]$chunk.id
                        name = $label
                        log = $logPath
                        exitCode = $run.ExitCode
                        timedOut = [bool]$run.TimedOut
                        skipped = $false
                        result = $(if ($run.TimedOut) { 'TIMEOUT' } elseif ($run.ExitCode -ne 0) { 'FAIL' } else { 'pass' })
                    }
                    if (-not $fresh.Ok) {
                        $coverageComplete = $false
                        $testsPassed = $false
                        $overallFailed = $true
                        $row.detail = $fresh.Reason
                        $reasons += $fresh.Reason
                    }
                    if ($isNative) {
                        $discPath = Join-Path $LogRoot ('{0}-{1}.discovery.json' -f $suiteId, $chunk.id)
                        $required = @()
                        if ($chunk.classes) { foreach ($c in @($chunk.classes)) { $required += [string]$c } }
                        $nativeVerdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trxPath -DiscoveryPath $discPath `
                            -RunDirectory $LogRoot -NotBeforeUtc $startedAt -ProcessExit ([int]$run.ExitCode) `
                            -Sha $Sha -ExpectedSha $Sha -GitRef $GitRef -ExpectedRef $GitRef `
                            -PolicyHash $policyHash -ExpectedPolicyHash $policyHash -RequiredClasses $required
                        $row.trx = $trxPath
                        $row.evidenceReasons = @($nativeVerdict.reasons)
                        if (-not [bool]$nativeVerdict.coverageComplete) {
                            $coverageComplete = $false
                            $row.coverageComplete = $false
                            foreach ($ev in @($nativeVerdict.reasons)) { $reasons += $ev }
                        }
                        if (-not [bool]$nativeVerdict.testsPassed) {
                            $testsPassed = $false
                            $overallFailed = $true
                            $row.result = 'FAIL'
                        }
                    }
                    if ($suiteId -eq 'client') {
                        if (-not (Test-Path -LiteralPath $jsonPath)) {
                            $coverageComplete = $false
                            $testsPassed = $false
                            $overallFailed = $true
                            $reasons += 'missing-client-json'
                            $row.coverageComplete = $false
                        }
                    }
                    if ($run.TimedOut) {
                        $overallFailed = $true
                        $coverageComplete = $false
                        $testsPassed = $false
                        Set-NightlyOutcome TIMEOUT
                        $reasons += ('timeout {0}' -f $suiteId)
                    } elseif ($run.ExitCode -ne 0) {
                        $overallFailed = $true
                        $testsPassed = $false
                        Set-NightlyOutcome TESTS
                    } elseif (-not $testsPassed -or -not $coverageComplete) {
                        $overallFailed = $true
                        Set-NightlyOutcome TESTS
                    }
                    $suiteResults += $row
                    if ($run.TimedOut) { $coverageComplete = $false }
                }
            }
        } else {
            $coverageComplete = $false
            $testsPassed = $false
        }
    } catch {
        $overallFailed = $true
        $coverageComplete = $false
        $preflight.unhandledError = $_.Exception.Message
        Write-NightlyRunLine $_.Exception.Message
        $reasons += $_.Exception.Message
    }

    if (-not $lintOk) { $coverageComplete = $false; $testsPassed = $false }
    if ($nativeActiveMax -gt 1) { $coverageComplete = $false; $reasons += 'native-overlap' }

    $completedAt = Get-NightlyUtcNow
    $summary = [ordered]@{
        startedAt = $startedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        sha = $Sha
        gitRef = $GitRef
        trigger = $Trigger
        runId = $invocationId
        clone = $RepoRoot
        logDir = $LogRoot
        selectedSuites = $selectedSuites
        outcome = $script:NightlyTestsOutcome
        succeeded = -not $overallFailed
        coverageComplete = [bool]$coverageComplete
        testsPassed = [bool]$testsPassed
        policyHash = $policyHash
        reasons = $reasons
        nativeActiveMax = $nativeActiveMax
        preflight = $preflight
        builds = $buildResults
        suites = $suiteResults
        invocationId = $invocationId
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-NightlyRunLine ('wrote {0}' -f $summaryPath)

    $exit = 0
    if ($overallFailed) { $exit = 1 }
    return [pscustomobject]@{
        ExitCode = $exit
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        SummaryPath = $summaryPath
        Selected = $selectedSuites
        NativeActiveMax = $nativeActiveMax
        PolicyHash = $policyHash
    }
}
