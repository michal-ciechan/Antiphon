#requires -Version 5.1
# CARD-0487: nightly suite execution, evidence and coverage completeness.
# ASCII-only. Does not clone, fetch, reset or change git state.

if ($script:AntiphonNightlyTestsImplLoaded) { return }
$script:AntiphonNightlyTestsImplLoaded = $true
. (Join-Path $PSScriptRoot 'nightly-dispatch.ps1')
. (Join-Path $PSScriptRoot 'nightly-coverage.ps1')
. (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
. (Join-Path $PSScriptRoot 'qualification-supervisor-heartbeat.ps1')

# S6b shadow run evidence (RcRunEvidenceRecorder): one recorder per tests run, made at its first use, after the
# seams are imported. Off by ANTIPHON_RC_RUN_EVIDENCE, suppressed under the process or git seams unless it is
# 'record'; a refusal prints one line per reason and never changes the verdict. Recording grants no gate credit.
function Get-NightlyRcRunEvidence {
    if ($null -eq $script:NightlyRcRunEvidence) {
        $seams = $script:NightlySeams
        $script:NightlyRcRunEvidence = [Antiphon.Nightly.RcRunEvidenceRecorder]::new([string]$env:ANTIPHON_RC_RUN_EVIDENCE,
            [bool]($seams -and ($null -ne $seams.StartProcess -or $null -ne $seams.Git)), $false,
            [Action[string]]{ param([string]$Line) Write-NightlyRunLine $Line })
    }
    return $script:NightlyRcRunEvidence
}

if (-not (Get-Command Remove-TestPhaseVariables -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'test-phase-evidence.ps1')
}
# CARD-1698: toolchain versions, floors and warnings for summary.json preflight.toolchain.
# The compiled twin (Antiphon.Nightly/ToolchainFloor.cs) checks; its rows keep the field names
# toolchain-floor.ps1 wrote. The ToolchainVersions seam replaces the real git/dotnet probes in
# fixtures. Never throws.
function Get-NightlyToolchainPreflight {
    param([string]$RepoRoot)
    $source = $null
    if ($script:NightlySeams -and $script:NightlySeams.ToolchainVersions) {
        $seam = $script:NightlySeams.ToolchainVersions
        $source = [Func[string, string]]({ param([string]$Tool) [string](& $seam $Tool) }.GetNewClosure())
    }
    try {
        $report = [Antiphon.Nightly.ToolchainFloor]::Report($RepoRoot, $source)
        $tools = @(foreach ($t in $report.Tools) {
                [pscustomobject][ordered]@{ tool = $t.Tool; found = $t.Found; floor = $t.Floor; source = $t.Source; enforced = [bool]$t.Enforced; status = $t.Status }
            })
        $warnings = @(foreach ($w in $report.Warnings) {
                [pscustomobject][ordered]@{ tool = $w.Tool; found = $w.Found; floor = $w.Floor; impact = $w.Impact; fix = $w.Fix; reason = $w.Reason; message = $w.Message }
            })
        foreach ($w in $warnings) { Write-Host $w.message }
        return [ordered]@{ tools = $tools; warnings = $warnings }
    } catch {
        $message = ('WARNING: the toolchain floor check failed: ' + $_.Exception.Message)
        Write-Host $message
        return [ordered]@{ tools = @(); warnings = @([ordered]@{ tool = 'toolchain'; reason = 'check-failed'; message = $message }) }
    }
}

# W2-nightly S11: the launch prices (NightlyLaunchPrices, CARD-1410), the class-cost table, the launch groups and plans,
# native classification, the E2E prerequisites, the test host, the chunk discovery seconds, the native argv, the npm
# launch, the execution platform and the run outcome are Antiphon.Nightly/NightlyTestsPlan.cs. The cost file path and
# the table read from it stay here: test-phase-evidence.ps1 reads both, and a fixture seam may replace either.
# Once total ran and request execute stop are on disk, a leftover tree is killed after this grace instead of the
# full launch watchdog. Unmeasured placeholder: chunk logs record execution-stop-to-exit-seconds; set this near 2x
# that p95 (docs/test-performance-standards.md). 60000 matches NightlyCompletionGrace.DefaultMilliseconds.
$script:NightlyCompletionGraceMilliseconds = 60000
$script:NightlyClassCostPath = Join-Path $PSScriptRoot 'nightly-class-costs.json'
$script:NightlyClassCostTable = $null
function Add-NightlyOwnerObservation {
    param($Verdict, $Run)
    if ($null -eq $Verdict -or $null -eq $Run) { return }
    $map = @(
        @('RootStartedAtUtc', 'rootStartedAtUtc'),
        @('rootStartedAtUtc', 'rootStartedAtUtc'),
        @('RootExitedAtUtc', 'rootExitedAtUtc'),
        @('rootExitedAtUtc', 'rootExitedAtUtc'),
        @('JobEmptyObserved', 'jobEmpty'),
        @('jobEmpty', 'jobEmpty'),
        @('DrainMilliseconds', 'drainMilliseconds'),
        @('drainMilliseconds', 'drainMilliseconds'),
        @('ExecutionMilliseconds', 'executionMilliseconds'),
        @('executionMilliseconds', 'executionMilliseconds'),
        @('LaunchMilliseconds', 'launchMilliseconds'),
        @('launchMilliseconds', 'launchMilliseconds'),
        @('CleanupMilliseconds', 'cleanupMilliseconds'),
        @('cleanupMilliseconds', 'cleanupMilliseconds'),
        @('ChildrenExited', 'childrenExited'),
        @('childrenExited', 'childrenExited'),
        @('OutputDrained', 'outputDrained'),
        @('outputDrained', 'outputDrained'),
        @('RemainingProcessCount', 'remainingProcessCount'),
        @('remainingProcessCount', 'remainingProcessCount'),
        @('MissedDescendantPids', 'missedDescendantPids'),
        @('missedDescendantPids', 'missedDescendantPids'),
        @('DescendantIdentities', 'descendantIdentities'),
        @('descendantIdentities', 'descendantIdentities'),
        @('DescendantImages', 'descendantImages'),
        @('descendantImages', 'descendantImages'),
        @('DescendantExitedAtUtc', 'descendantExitedAtUtc'),
        @('descendantExitedAtUtc', 'descendantExitedAtUtc'),
        @('Descendants', 'descendants'),
        @('descendants', 'descendants')
    )
    # The indexer is the same case-insensitive member lookup as -contains over the names, without listing them.
    $runProperties = $Run.PSObject.Properties
    $verdictProperties = $Verdict.PSObject.Properties
    foreach ($pair in $map) {
        if ($null -eq $runProperties[$pair[0]]) { continue }
        if ($null -ne $verdictProperties[$pair[1]]) { continue }
        $verdictProperties.Add([System.Management.Automation.PSNoteProperty]::new($pair[1], $Run.($pair[0])))
    }
}

function New-NightlyChunkDirectory([string]$Path) {
    # New-Item -ItemType Directory -Force without the cmdlet's cost: creates the directory and its parents. As the
    # cmdlet did, it says nothing when a file is in the way, and reports any other failure without stopping the
    # chunk (unless errors stop the caller).
    try { [void][System.IO.Directory]::CreateDirectory($Path) } catch {
        $failure = $_
        $blocked = $Path
        try {
            while (-not [string]::IsNullOrEmpty($blocked) -and -not [System.IO.File]::Exists($blocked)) { $blocked = [System.IO.Path]::GetDirectoryName($blocked) }
        } catch { $blocked = '' }
        if ([string]::IsNullOrEmpty($blocked)) { Write-Error -ErrorRecord $failure }
    }
}

function Write-NightlyMeasuredPhase {
    param(
        [string]$LogRoot,
        [string]$Name,
        $StartedAt,
        $Seconds
    )
    if ([string]::IsNullOrWhiteSpace($LogRoot) -or [string]::IsNullOrWhiteSpace($Name)) { return }
    if ($null -eq $StartedAt -or $null -eq $Seconds) { return }
    if ($Name -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
    try {
        $started = [datetime]$StartedAt
        $ended = $started.AddSeconds([double]$Seconds)
        [Antiphon.Nightly.QualificationTiming]::WriteRunPhase((Get-TestPhaseTimingDirectory -RunDirectory $LogRoot), $Name, $started.ToString('o'), $ended.ToString('o'), ([double]$Seconds))
    } catch { }
}

function Write-NightlyPrerequisitePhases {
    param(
        [string]$LogRoot,
        $NpmCiStarted,
        $NpmCiSeconds,
        $NpmBuildStarted,
        $NpmBuildSeconds,
        $LintStarted,
        $LintSeconds,
        $DotnetStarted,
        $DotnetSeconds
    )
    Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name 'npm-ci' -StartedAt $NpmCiStarted -Seconds $NpmCiSeconds
    Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name 'npm-build' -StartedAt $NpmBuildStarted -Seconds $NpmBuildSeconds
    Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name 'lint' -StartedAt $LintStarted -Seconds $LintSeconds
    Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name 'dotnet-build' -StartedAt $DotnetStarted -Seconds $DotnetSeconds
}

function Publish-NightlyChunkObservation {
    param(
        $Run,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0,
        $CoverageComplete = $false,
        $TestsPassed = $false
    )
    $exitCode = 0
    if ($null -ne $Run -and $Run.PSObject.Properties.Name -contains 'ExitCode' -and $null -ne $Run.ExitCode) { $exitCode = [int]$Run.ExitCode }
    $coverage = $false
    $passed = $false
    if ($null -ne $CoverageComplete) { $coverage = [bool]$CoverageComplete }
    if ($null -ne $TestsPassed) { $passed = [bool]$TestsPassed }
    $phaseVerdict = [pscustomobject]@{ ExitCode = [int]$exitCode; coverageComplete = [bool]$coverage; testsPassed = [bool]$passed }
    Add-NightlyOwnerObservation -Verdict $phaseVerdict -Run $Run
    Write-TestPhaseExecutedManifest -Verdict $phaseVerdict -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
    return $phaseVerdict
}

function Invoke-NightlyOwnedProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [hashtable]$Environment = $null,
        [string]$LogPath,
        [string]$TrxPath = '',
        [string]$DiagnosticDirectory = '',
        [string]$HangCapturePath = '',
        [int]$ExecutionGraceMilliseconds = 0
    )
    Write-NightlyTrace -Kind 'proc' -Message ($FilePath + ' ' + (($ArgumentList) -join ' '))
    # A child (or the seam standing in for one) reads the disk: queued evidence writes land first.
    Sync-NightlyDeferredEvidenceWrites
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess) {
        $r = $script:NightlySeams.StartProcess.Invoke($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        if ($LogPath -and $r.LogText) {
            Set-Content -LiteralPath $LogPath -Value ([string]$r.LogText) -Encoding UTF8
        }
        return $r
    }
    # Import only at the actual launch boundary. The controlled-I/O seam above
    # remains portable, and cannot earn native custody credit.
    . (Join-Path $PSScriptRoot 'nightly-owned-process.ps1')
    $hooks = $null
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $hooks = New-Object Antiphon.Nightly.NativeProcessHooks
        if (-not [string]::IsNullOrWhiteSpace($TrxPath)) { $hooks.TrxPath = $TrxPath }
        if (-not [string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { $hooks.DiagnosticDirectory = $DiagnosticDirectory }
        if (-not [string]::IsNullOrWhiteSpace($HangCapturePath)) { $hooks.HangCapturePath = $HangCapturePath }
        if ($ExecutionGraceMilliseconds -gt 0) { $hooks.ExecutionGraceMilliseconds = $ExecutionGraceMilliseconds }
    }
    $owner = Start-NightlyNativeOwner -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory -TimeoutMilliseconds $TimeoutMilliseconds `
        -Environment $Environment -LogPath $LogPath -Hooks $hooks
    return $owner.Completion.GetAwaiter().GetResult()
}

function Wait-NightlyOwnedCleanup {
    param($RunResult)
    if ($script:NightlySeams -and $script:NightlySeams.WaitCleanup) {
        return [bool]$script:NightlySeams.WaitCleanup.Invoke($RunResult)
    }
    # Retain pre-existing controlled-I/O decision fixtures. Their synthetic
    # ChildrenExited bit cannot establish native custody or qualification.
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess -and $null -eq $RunResult.CleanupComplete) {
        return [bool]$RunResult.ChildrenExited
    }
    return [bool]$RunResult.CleanupComplete
}

function Get-NightlyNativeActiveCount {
    if ($script:NightlySeams -and $script:NightlySeams.NativeActiveCount) {
        return [int]$script:NightlySeams.NativeActiveCount.Invoke()
    }
    return 0
}

. (Join-Path $PSScriptRoot 'release-selection.ps1')

# Dot-source these helpers: keyed suite state survives interleaving; run verdicts,
# reasons and the publication context remain in the caller's execution scope.
function Prepare-NightlySuiteExecution {
    param([string]$ExecutionSuiteId)
    $suiteId = $ExecutionSuiteId
    $suite = $suiteMap[$suiteId]
    $profileExclusions = @([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($policy.Object, $Profile, $suiteId, $platform))
    $label = [string]$suite.name
    if ([string]::IsNullOrWhiteSpace($label)) { $label = $suiteId }
    $isNative = [Antiphon.Nightly.NightlyTestsPlan]::SuiteIsNative($suite, $suiteId)
    $mode = [string]$suite.mode
    # D-6: 'manual' keeps a suite out of the nightly default lane. A suite the
    # selected profile explicitly requires runs; it is never skipped as manual.
    if ($mode -eq 'manual' -and ($requiredSuites -notcontains $suiteId)) {
        $suiteResults += [ordered]@{
            id = $suiteId; name = $label; skipped = $true; result = 'manual'; exitCode = 0
            coverageComplete = $false; detail = 'manual exclusion'
        }
        return $null
    }

    $chunks = @()
    if ($chunksBySuite.ContainsKey($suiteId) -and @($chunksBySuite[$suiteId]).Count -gt 0) {
        $chunks = @($chunksBySuite[$suiteId])
        $eligible = @()
        if ($suite.eligibleClasses) { $eligible = @($suite.eligibleClasses) }
        elseif ($chunks.Count -gt 0) {
            foreach ($ch in $chunks) { foreach ($c in @($ch.classes)) { $eligible += [string]$c } }
        }
        $member = [Antiphon.Nightly.NightlyChunkVerdict]::ChunkMembership($eligible, $chunks)
        if (-not $member.Ok) {
            if ($member.Duplicates.Count -gt 0) {
                throw ('duplicate-class {0}' -f ($member.Duplicates -join ','))
            }
            throw ('missing-class {0}' -f ($member.Missing -join ','))
        }
    } else {
        $chunks = @([pscustomobject]@{ id = 'all'; classes = @() })
    }

    # The pinned filter parser requires prefix forms for combined operands.
    # Separate exact class chunks so no suffix class can ever be launched.
    $exactChunks = @()
    foreach ($chunk in $chunks) {
        if (@($chunk.classes).Count -gt 1) {
            $classIndex = 0
            foreach ($class in $chunk.classes) {
                $exactChunks += [pscustomobject]@{ id = ('{0}-class-{1}' -f $chunk.id, (++$classIndex)); classes = @($class) }
            }
        } else { $exactChunks += $chunk }
    }
    $chunks = $exactChunks
    $suiteDiscoverySeconds = 0.0
    $suiteHost = $null
    if ($isNative) {
        $assemblyName = [string]$suite.assembly
        if ([string]::IsNullOrWhiteSpace($assemblyName)) { $assemblyName = $label }
        $suiteHost = [Antiphon.Nightly.NightlyTestsPlan]::TestHost($RepoRoot, $assemblyName)
    }

    if ($suiteId -eq 'e2e') {
        $bundle = Join-Path $RepoRoot (Join-Path 'client' (Join-Path 'dist' 'index.html'))
        $prereqEnv = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, $suiteId, $null)
        Remove-TestPhaseVariables -Environment $prereqEnv
        if ($script:NightlySeams -and $script:NightlySeams.E2EPrerequisites) {
            $prereq = @($script:NightlySeams.E2EPrerequisites.Invoke($RepoRoot, $LogRoot, $prereqEnv, $bundle))[0]
        } else {
            # The generated playwright.ps1 installs Chromium under this run's owned-process custody.
            $prereqInstall = [Func[Antiphon.Nightly.NightlyLaunch, int]]{
                param($Launch)
                $installRun = Invoke-NightlyOwnedProcess -FilePath $Launch.FileName -ArgumentList @($Launch.Arguments) -WorkingDirectory $Launch.WorkingDirectory `
                    -TimeoutMilliseconds $Launch.TimeoutMilliseconds -Environment $prereqEnv -LogPath $Launch.LogPath
                [int]$installRun.ExitCode
            }
            try { $prereq = [Antiphon.Nightly.NightlyTestsPlan]::E2EPrerequisites($RepoRoot, $LogRoot, $prereqEnv, $bundle, $prereqInstall) }
            catch { throw (Get-NightlyRefusalText $_) }
        }
        $preflight.e2e = @($prereq.Rows)
        if (-not $prereq.Ok) {
            # D-6: a missing browser or stale bundle is red/incomplete, not a skip.
            $coverageComplete = $false
            $testsPassed = $false
            $overallFailed = $true
            $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'PREFLIGHT')
            $reasons += 'e2e-prerequisite-failed'
            $suiteResults += [ordered]@{
                id = $suiteId; name = $label; skipped = $false; result = 'FAIL'; exitCode = 1
                coverageComplete = $false; detail = 'e2e-prerequisite-failed'
            }
            [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = 'prereq'; LastPhase = 'preflight'; Why = 'e2e-prerequisite-failed'; BuildSeconds = $qualificationBuildSeconds; StartedAt = (Get-NightlyUtcNow).ToString('o'); RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
            return $null
        }
    }

    $suiteDiscoveryNodes = @()
    $profileDiscovery = $null
    $profileDiscoveryPath = ''
    $suiteCensus = $null
    # CARD-1419: one discovery parse and census per suite, shared by every launch verdict.
    $suiteDiscoveryIndex = [Antiphon.Nightly.NightlyDiscoveryIndex]::new()
    if ($isNative -and (($profileAware -and $profileExclusions.Count -gt 0) -or $dispatchNeedsDiscovery)) {
        $profileDiscoveryPath = Join-Path $LogRoot ($suiteId + '-profile.discovery.json')
        $profileEnv = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, $suiteId, $null)
        Remove-TestPhaseVariables -Environment $profileEnv
        $profileDiscStarted = Get-NightlyUtcNow
        $profileDiscWatch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $profileDiscovery = Invoke-NightlyProduceDiscovery -ExePath $suiteHost.FilePath -ArgumentPrefix @($suiteHost.Prefix) -AssemblyPath $suiteHost.AssemblyPath -OutputPath $profileDiscoveryPath `
                -DiagnosticDirectory (Join-Path $LogRoot ($suiteId + '-profile-discovery')) `
                -LogPath (Join-Path $LogRoot ($suiteId + '-profile-discovery.log')) -WorkingDirectory $RepoRoot `
                -Environment $profileEnv
            $profileDiscWatch.Stop()
            $suiteDiscoverySeconds = [math]::Round($profileDiscWatch.Elapsed.TotalSeconds, 3)
            $planningName = 'planning'
            if ($suiteId -match '^[a-z0-9][a-z0-9-]{0,40}$') { $planningName = 'planning-' + $suiteId }
            Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name $planningName -StartedAt $profileDiscStarted -Seconds $suiteDiscoverySeconds
            $suiteDiscoveryEntry = [Antiphon.Nightly.NightlyDiscoveryIndex]::EntryOf($suiteDiscoveryIndex, $profileDiscoveryPath)
            $suiteDiscoveryNodes = @($suiteDiscoveryEntry.Document.Nodes)
            $suiteCensus = ([Antiphon.Nightly.NightlyDiscoveryIndex]::View($suiteDiscoveryEntry, $profileExclusions, $true)).Census
            if (-not $suiteCensus.Ok) { throw 'profile-discovery-census-incomplete' }
            # Compiled (NightlyTestsPlan.SuiteChunks); the cost table read for it is kept for the phase manifest.
            $suiteCosts = [Antiphon.Nightly.NightlyClassCosts]::new([string]$script:NightlyClassCostPath, $script:NightlyClassCostTable)
            try { $chunks = @([Antiphon.Nightly.NightlyTestsPlan]::SuiteChunks(@($suiteDiscoveryNodes), @($chunks), @($profileExclusions), [bool]$profileAware, [bool]$isNative, $suiteId, $policy.Object, $suiteCosts)) }
            catch { throw (Get-NightlyRefusalText $_) }
            if ($suiteCosts.Loaded) { $script:NightlyClassCostTable = $suiteCosts.Table }
            # S6b shadow: one discovery per suite. Carry cannot skip it. Recording grants no gate credit.
            [void](Get-NightlyRcRunEvidence).DiscoveryOnce($RcEvidenceRoot, $LogRoot, [string]$suiteId, $profileDiscoveryPath)
            if ($chunks.Count -eq 0) { throw 'zero-required-uid' }
        } catch {
            if ($profileDiscWatch.IsRunning) { $profileDiscWatch.Stop() }
            $suiteDiscoverySeconds = [math]::Round($profileDiscWatch.Elapsed.TotalSeconds, 3)
            $planningName = 'planning'
            if ($suiteId -match '^[a-z0-9][a-z0-9-]{0,40}$') { $planningName = 'planning-' + $suiteId }
            Write-NightlyMeasuredPhase -LogRoot $LogRoot -Name $planningName -StartedAt $profileDiscStarted -Seconds $suiteDiscoverySeconds
            $discWhy = 'discovery-failed'
            if ([string]$_.Exception.Message -match 'timed out') { $discWhy = 'discovery-timeout' }
            $discTargets = @($chunks)
            if ($discTargets.Count -lt 1) {
                $discTargets = @([pscustomobject]@{ id = 'discovery' })
            }
            foreach ($discChunk in $discTargets) {
                [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = ([string]$discChunk.id); LastPhase = 'discovery'; Why = $discWhy; BuildSeconds = $qualificationBuildSeconds; DiscoverySeconds = $suiteDiscoverySeconds; StartedAt = (Get-NightlyUtcNow).ToString('o'); RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
            }
            throw
        }
    }
    if ($isNative) {
        try {
            $phaseNodes = @()
            if ($suiteDiscoveryNodes) { $phaseNodes = @($suiteDiscoveryNodes) }
            $phaseAssembly = [string]$suite.assembly
            $phaseAssemblyDigest = ''
            $phaseDll = [IO.Path]::Combine($RepoRoot, 'tests', $phaseAssembly, 'bin', 'Debug', 'net9.0', ($phaseAssembly + '.dll'))
            if (-not [string]::IsNullOrWhiteSpace($phaseAssembly) -and (Test-Path -LiteralPath $phaseDll)) {
                $phaseAssemblyDigest = Get-NightlyAssemblyFileSha256 -Path $phaseDll
            }
            $phaseCostDigest = ''
            if (-not [string]::IsNullOrWhiteSpace([string]$script:NightlyClassCostPath) -and (Test-Path -LiteralPath $script:NightlyClassCostPath)) {
                $phaseCostDigest = Get-NightlyFileSha256 -Path $script:NightlyClassCostPath
            }
            [void](Write-TestPhasePlannedManifest -LogRoot $LogRoot -RunId $invocationId -Attempt ([string]$Attempt) -Suite $suiteId -SourceDigest ([string]$Sha) -AssemblyDigest $phaseAssemblyDigest -PolicyDigest ([string]$policyHash) -CostFileDigest $phaseCostDigest -AssemblyId $phaseAssembly -Groups $chunks -DiscoveryNodes $phaseNodes -ProfileExclusions $profileExclusions -DiscoveryIndex $suiteDiscoveryIndex -DiscoveryPath $profileDiscoveryPath)
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
        }
    }
    $chunkIndex = 0
    $suiteTerminalNodes = @()
    $executionVariables = @{}
    # Get-Variable -ValueOnly -ErrorAction SilentlyContinue without the cmdlet's cost: the value as it is (not
    # enumerated), and no value at all for a name that is not set.
    $executionVariableGet = $ExecutionContext.SessionState.PSVariable
    foreach ($executionName in @('suiteId','suite','label','mode','isNative','chunks','profileExclusions','suiteHost','suiteDiscoveryNodes','profileDiscovery','profileDiscoveryPath','suiteDiscoveryIndex','suiteCensus','suiteDiscoverySeconds','suiteTerminalNodes','chunkIndex','phaseAssembly','phaseAssemblyDigest','phaseCostDigest')) {
        $executionVariable = $executionVariableGet.Get($executionName)
        if ($null -eq $executionVariable) { $executionVariables[$executionName] = [System.Management.Automation.Internal.AutomationNull]::Value }
        else { $executionVariables[$executionName] = $executionVariable.Value }
    }
    return [pscustomobject]@{ id=$suiteId; chunks=@($chunks); nodes=@($suiteDiscoveryNodes); exclusions=@($profileExclusions); Variables=$executionVariables; Completed=$false }
}

function Invoke-NightlyScheduledChunk {
    param($ExecutionSuiteState, $ExecutionChunk)
    # Set-Variable's local-scope set (the caller's scope: this runs dot-sourced) without the cmdlet's cost.
    $executionVariableSet = $ExecutionContext.SessionState.PSVariable
    foreach ($executionName in @($ExecutionSuiteState.Variables.Keys)) {
        $executionVariableSet.Set($executionName, $ExecutionSuiteState.Variables[$executionName])
    }
    $chunk = $ExecutionChunk
    $chunkTestsPassed=$true
    $chunkCoverageComplete=$true
    $exitTolerated = $false
    $recordNativeChunk = $false
    try {
    $chunkIndex++
    $chunkStartedAt = (Get-NightlyUtcNow).ToString('o')
    $queueWatch = [Diagnostics.Stopwatch]::StartNew()
    $logPath = Join-Path $LogRoot ('{0}-{1}-tests.log' -f $suiteId, $chunk.id)
    $trxPath = Join-Path $LogRoot ('{0}-{1}.trx' -f $suiteId, $chunk.id)
    $jsonPath = Join-Path $LogRoot ('{0}-{1}.json' -f $suiteId, $chunk.id)
    $envMap = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, $suiteId, $null)
    Remove-TestPhaseVariables -Environment $envMap
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
    $execDiagDir = ''
    $queueWatch.Stop()
    $queueWaitSeconds = [math]::Round($queueWatch.Elapsed.TotalSeconds, 3)
    $resolvedDiscovery = [Antiphon.Nightly.NightlyTestsPlan]::ChunkDiscoverySeconds([double]$suiteDiscoverySeconds, $null, $false)
    $chunkDiscoverySeconds = $resolvedDiscovery.chunkDiscoverySeconds
    $heartbeatTrx = ''
    if ($isNative) { $heartbeatTrx = $trxPath }
    [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = ([string]$chunk.id); LastPhase = 'run'; StartedAt = $chunkStartedAt; QueueWaitSeconds = $queueWaitSeconds; BuildSeconds = $qualificationBuildSeconds; DiscoverySeconds = $chunkDiscoverySeconds; TrxPath = $heartbeatTrx; RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
    $runWatch = [Diagnostics.Stopwatch]::StartNew()
    if ($suiteId -eq 'client') {
        $wrapper = Join-Path $RepoRoot (Join-Path 'scripts' 'test-client.ps1')
        $args = @('-NoProfile', '-NoLogo', '-File', $wrapper, '-JsonResultPath', $jsonPath)
        try {
            $run = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $args -WorkingDirectory $RepoRoot `
                -TimeoutMilliseconds 1200000 -Environment $envMap -LogPath $logPath
        } catch {
            [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = ([string]$chunk.id); LastPhase = 'launch'; Why = 'launch-failed'; StartedAt = $chunkStartedAt; QueueWaitSeconds = $queueWaitSeconds; BuildSeconds = $qualificationBuildSeconds; DiscoverySeconds = $chunkDiscoverySeconds; TrxPath = $heartbeatTrx; RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
            throw
        }
    } elseif ($suiteId -eq 'scripts') {
        $census = @([Antiphon.Nightly.NightlyPolicy]::ScriptCensus($policy.Object))
        $scriptFail = $false
        foreach ($row in $census) {
            $disp = [string]$row.disposition
            if ($disp -ne 'unattended') { continue }
            if ([bool]$row.wildcard) { throw 'wildcard script execution refused' }
            $scriptPath = Join-Path $RepoRoot (([string]$row.path) -replace '/', '\')
            $sargs = @('-NoProfile', '-NonInteractive', '-File', $scriptPath)
            if ($row.arguments) { foreach ($a in @($row.arguments)) { $sargs += [string]$a } }
            try {
                $sr = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $sargs -WorkingDirectory $RepoRoot `
                    -TimeoutMilliseconds 600000 -Environment $envMap -LogPath (Join-Path $LogRoot ('script-{0}.log' -f $row.id))
            } catch {
                [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = ([string]$chunk.id); LastPhase = 'launch'; Why = 'launch-failed'; StartedAt = $chunkStartedAt; QueueWaitSeconds = $queueWaitSeconds; BuildSeconds = $qualificationBuildSeconds; DiscoverySeconds = $chunkDiscoverySeconds; TrxPath = $heartbeatTrx; RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
                throw
            }
            if ($sr.ExitCode -ne 0) { $scriptFail = $true }
        }
        $run = [pscustomobject]@{ ExitCode = $(if ($scriptFail) { 1 } else { 0 }); TimedOut = $false; ChildrenExited = $true }
    } else {
        if ($null -eq $suiteHost) {
            $exeName = [string]$suite.assembly
            if ([string]::IsNullOrWhiteSpace($exeName)) { $exeName = $label }
            $suiteHost = [Antiphon.Nightly.NightlyTestsPlan]::TestHost($RepoRoot, $exeName)
        }
        $execDiagDir = Join-Path $LogRoot ('{0}-{1}-execution' -f $suiteId, $chunk.id)
        New-NightlyChunkDirectory $execDiagDir
        $chunkClasses = @()
        if ($chunk.classes -and @($chunk.classes).Count -gt 0) { $chunkClasses = @($chunk.classes) }
        $groupFilter = ''
        if ($chunk.PSObject.Properties.Name -contains 'groupFilter') { $groupFilter = [string]$chunk.groupFilter }
        $methodFilter = ''
        if ($chunk.PSObject.Properties.Name -contains 'methodFilter') { $methodFilter = [string]$chunk.methodFilter }
        $nativeArgs = @()
        if ($suiteHost.Prefix) {
            foreach ($prefixArg in @($suiteHost.Prefix)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$prefixArg)) { $nativeArgs += [string]$prefixArg }
            }
        }
        try { $nativeArgs += @([Antiphon.Nightly.NightlyTestsPlan]::NativeExecutionArguments($trxPath, $execDiagDir, [string[]]@($chunkClasses), ([string]$chunk.exactFilter), $groupFilter, $methodFilter)) }
        catch { throw (Get-NightlyRefusalText $_) }
        $phaseEnv = @{}
        foreach ($phaseKey in @($envMap.Keys)) { $phaseEnv[[string]$phaseKey] = [string]$envMap[$phaseKey] }
        Enable-TestPhaseExecutionEnvironment -Environment $phaseEnv -LogRoot $LogRoot -RunId $invocationId -GroupId ([string]$chunk.id) -Attempt ([string]$Attempt) -Suite $suiteId -Profile $Profile -TimingRoot $TimingRoot
        $plannedSeconds = $null
        if ($null -ne $chunk.PSObject.Properties['seconds'] -and $null -ne $chunk.seconds) {
            $plannedSeconds = $chunk.seconds
        }
        $costBasis = 'default'
        if ($null -ne $chunk.PSObject.Properties['costBasis'] -and -not [string]::IsNullOrWhiteSpace([string]$chunk.costBasis)) {
            $costBasis = [string]$chunk.costBasis
        }
        $admission = [Antiphon.Nightly.NightlyWatchdog]::ResolveLaunch($suiteId, $plannedSeconds, $costBasis, '', $null, $null, $policy.Object)
        if (-not $admission.Admitted) {
            throw ('watchdog-admission-refused id={0} reason={1}' -f $chunk.id, $admission.Reason)
        }
        [void](Write-QualificationActiveChunkIdentity -Directory $TimingRoot -SuiteId $suiteId -ChunkId ([string]$chunk.id) -RunId $invocationId -Attempt ([string]$Attempt))
        # S6b shadow: join the build seal before the timed launch. A refusal stays unproven and does not change the verdict.
        [void](Get-NightlyRcRunEvidence).Launch($RcEvidenceRoot, $LogRoot, [string]$suiteId, [string]$chunk.id, [IO.Path]::GetDirectoryName([string]$suiteHost.AssemblyPath), [string[]]@($nativeArgs), '', $RepoRoot, [string]$Sha)
        $phaseWatch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $run = Invoke-NightlyOwnedProcess -FilePath $suiteHost.FilePath -ArgumentList $nativeArgs -WorkingDirectory $RepoRoot `
                -TimeoutMilliseconds ([int]$admission.WatchdogMilliseconds) `
                -Environment $phaseEnv -LogPath $logPath `
                -TrxPath $trxPath -DiagnosticDirectory $execDiagDir `
                -HangCapturePath ($logPath + '.hang.txt') `
                -ExecutionGraceMilliseconds ([int]$script:NightlyCompletionGraceMilliseconds)
        } catch {
            [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = $suiteId; Chunk = ([string]$chunk.id); LastPhase = 'launch'; Why = 'launch-failed'; StartedAt = $chunkStartedAt; QueueWaitSeconds = $queueWaitSeconds; BuildSeconds = $qualificationBuildSeconds; DiscoverySeconds = $chunkDiscoverySeconds; TrxPath = $heartbeatTrx; RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
            throw
        }
        $phaseWatch.Stop()
        $recordNativeChunk = $true
        try {
            [void](Publish-NightlyChunkObservation -Run $run -LogRoot $LogRoot -TrxPath $trxPath -Suite $suiteId -Chunk ([string]$chunk.id) -RunId $invocationId -Attempt ([string]$Attempt) -HostWallSeconds ([double]$phaseWatch.Elapsed.TotalSeconds) -CoverageComplete $coverageComplete -TestsPassed $testsPassed)
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
        }
    }
    $runWatch.Stop()
    $runWallSeconds = [math]::Round($runWatch.Elapsed.TotalSeconds, 3)

    $cleaned = Wait-NightlyOwnedCleanup -RunResult $run
    if ($isNative) {
        $activeNative--
        if (-not $cleaned) { throw 'owned-child still running' }
    }

    if (Test-NightlyPathExists $logPath) {
        $stamp = 'C487_INVOCATION=' + $invocationId
        $rawLog = [System.IO.File]::ReadAllText($logPath)
        if ($rawLog -notmatch [regex]::Escape($invocationId)) {
            [System.IO.File]::WriteAllText($logPath, ($stamp + [Environment]::NewLine + $rawLog))
        }
    } else {
        # Set-Content -Encoding UTF8 (pwsh 7: no BOM, one line): a failure is reported and the chunk goes on.
        try { [System.IO.File]::WriteAllText($logPath, ('C487_INVOCATION=' + $invocationId + [Environment]::NewLine)) } catch { Write-Error -ErrorRecord $_ }
    }
    $fresh = [Antiphon.Nightly.NightlyChunkVerdict]::Fresh($logPath, $LogRoot, $startedAt, $invocationId)
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
        $chunkTestsPassed=$false
        $chunkCoverageComplete=$false
        $coverageComplete = $false
        $testsPassed = $false
        $overallFailed = $true
        $row.detail = $fresh.Reason
        $reasons += $fresh.Reason
    }
    if ($isNative) {
        $discPath = Join-Path $LogRoot ('{0}-{1}.discovery.json' -f $suiteId, $chunk.id)
        if ($profileDiscovery) {
            $discPath = $profileDiscoveryPath
            if ($chunk.exactFilter -or (($chunk.PSObject.Properties.Name -contains 'methodFilter') -and $chunk.methodFilter)) {
                # The full discovery remains the suite union authority;
                # the leaf verdict checks just this chunk's method expansions.
                $discPath = Join-Path $LogRoot ('{0}-{1}-selection.discovery.json' -f $suiteId, $chunk.id)
                $leaf = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument($chunk.nodes, $profileDiscovery.assemblyHash)
                [Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument($discPath, $leaf)
            }
        }
        if ((Test-NightlyPathExists $trxPath) -and -not (Test-NightlyPathExists $discPath)) {
            try {
                $diagDir = Join-Path $LogRoot ('{0}-{1}-discovery' -f $suiteId, $chunk.id)
                $discLog = Join-Path $LogRoot ('{0}-{1}-discovery.log' -f $suiteId, $chunk.id)
                $chunkDiscWatch = [Diagnostics.Stopwatch]::StartNew()
                [void](Invoke-NightlyProduceDiscovery -ExePath $suiteHost.FilePath -ArgumentPrefix @($suiteHost.Prefix) -AssemblyPath $suiteHost.AssemblyPath -OutputPath $discPath `
                    -DiagnosticDirectory $diagDir -LogPath $discLog -WorkingDirectory $RepoRoot `
                    -Environment $envMap)
                $chunkDiscWatch.Stop()
                $resolvedDiscovery = [Antiphon.Nightly.NightlyTestsPlan]::ChunkDiscoverySeconds([double]$suiteDiscoverySeconds, $chunkDiscWatch.Elapsed.TotalSeconds, $true)
                $chunkDiscoverySeconds = $resolvedDiscovery.chunkDiscoverySeconds
                [void](Get-NightlyRcRunEvidence).DiscoveryOnce($RcEvidenceRoot, $LogRoot, [string]$suiteId, $discPath)
            } catch {
                $reasons += ('discovery-produce-failed {0}' -f $_.Exception.Message)
            }
        }
        $required = @()
        if ($chunk.classes) { foreach ($c in @($chunk.classes)) { $required += [string]$c } }
        $execDiagPath = [Antiphon.Nightly.NightlyDiagnosticLog]::Latest($execDiagDir)
        $nativeVerdict = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trxPath, $discPath, $execDiagPath, $LogRoot, $startedAt, ([int]$run.ExitCode), $Sha, $Sha, $GitRef, $GitRef, $policyHash, $policyHash, $required, $profileExclusions, [bool]($profileAware), $suiteDiscoveryIndex, $platform, $expectedSkipRoster)
        if ($nativeVerdict.PSObject.Properties.Name -contains 'processExitTolerated' -and [bool]$nativeVerdict.processExitTolerated) {
            $exitTolerated = $true
            $row.processExitTolerated = $true
        }
        if ($nativeVerdict.PSObject.Properties.Name -contains 'toleratedSkips') {
            foreach ($toleratedSkip in @($nativeVerdict.toleratedSkips)) {
                if ($null -eq $toleratedSkip) { continue }
                $toleratedSkipRows += ,$toleratedSkip
            }
        }
        if ($nativeVerdict.PSObject.Properties.Name -contains 'warnings') {
            foreach ($skipWarning in @($nativeVerdict.warnings)) {
                $skipWarningText = [string]$skipWarning
                if ([string]::IsNullOrWhiteSpace($skipWarningText)) { continue }
                if ($expectedSkipWarnings -notcontains $skipWarningText) { $expectedSkipWarnings += $skipWarningText }
            }
        }
        if ($nativeVerdict.discoveryNodes -and @($nativeVerdict.discoveryNodes).Count -gt 0 -and $suiteDiscoveryNodes.Count -eq 0) {
            $suiteDiscoveryNodes = @($nativeVerdict.discoveryNodes)
        }
        if ($nativeVerdict.terminalNodes) {
            # One concatenation per chunk, not one array copy per terminal node (19,000 in the main suite).
            $suiteTerminalNodes = @($suiteTerminalNodes) + @($nativeVerdict.terminalNodes)
        }
        $row.trx = $trxPath
        $row.evidenceReasons = @($nativeVerdict.reasons)
        $census = if ($suiteCensus) { $suiteCensus } else { $nativeVerdict.census }
        if ($census) {
            # D-5: expanded-UID accounting is manifest evidence; record both
            # counts and the digest, plus every exclusion with its reason.
            $row.requiredUidCount = [int]$census.RequiredCount
            $row.excludedUidCount = [int]$census.ExcludedCount
            $row.censusDigest = [string]$census.Digest
            $row.exclusions = @($census.Excluded)
        }
        if (-not [bool]$nativeVerdict.coverageComplete) {
            $chunkCoverageComplete=$false
            $coverageComplete = $false
            $row.coverageComplete = $false
            foreach ($ev in @($nativeVerdict.reasons)) { $reasons += $ev }
        }
        if (-not [bool]$nativeVerdict.testsPassed) {
            $chunkTestsPassed=$false
            $testsPassed = $false
            $overallFailed = $true
            $row.result = 'FAIL'
        } elseif ($exitTolerated -and [bool]$nativeVerdict.coverageComplete) {
            $row.result = 'pass'
        }
    }
    $rcEvidence = Get-NightlyRcRunEvidence
    if ($recordNativeChunk -and $rcEvidence.Recording) {
        # The shadow parse of this launch, bound to the native verdict and the log freshness above.
        $chunkRecord = [Antiphon.Nightly.RcChunkOutcome]::Read($run.ExitCode, [bool]$run.OutputDrainTimedOut, $trxPath, $rcSkipRoster, [string]$platform)
        if ($isNative) {
            $chunkRecord = [Antiphon.Nightly.RcChunkOutcome]::Bind($chunkRecord, $nativeVerdict.testsPassed, $nativeVerdict.coverageComplete, [string[]]@($nativeVerdict.reasons), $fresh.Ok, [string]$fresh.Reason)
        }
        [void]$rcEvidence.ChunkRevision($RcEvidenceRoot, $LogRoot, [string]$suiteId, [string]$chunk.id, $chunkRecord.Outcome, $chunkRecord.Reason, $false)
    }
    if ($suiteId -eq 'client') {
        if (-not (Test-Path -LiteralPath $jsonPath)) {
            $chunkTestsPassed=$false
            $chunkCoverageComplete=$false
            $coverageComplete = $false
            $testsPassed = $false
            $overallFailed = $true
            $reasons += 'missing-client-json'
            $row.coverageComplete = $false
        }
    }
    $completedHang = $false
    if ($run.TimedOut) {
        $overallFailed = $true
        $finished = $false
        $childText = ''
        $rootPid = 0
        if ($null -ne $run.PSObject.Properties['Pid'] -and $null -ne $run.Pid) { $rootPid = [int]$run.Pid }
        if ($isNative) {
            $hangDiag = [Antiphon.Nightly.NightlyDiagnosticLog]::Latest($execDiagDir)
            try { $finished = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($trxPath, $hangDiag)) } catch { $finished = $false }
            $identities = @()
            $images = @()
            if ($null -ne $run.PSObject.Properties['DescendantIdentities']) { $identities = @($run.DescendantIdentities) }
            if ($null -ne $run.PSObject.Properties['DescendantImages']) { $images = @($run.DescendantImages) }
            $childText = [string]([Antiphon.Nightly.NightlyWatchdog]::HangChildren($identities, $images, $rootPid))
            if ($null -ne $run.PSObject.Properties['CompletedButHung'] -and [bool]$run.CompletedButHung) { $finished = $true }
        }
        $resolvedHang = [Antiphon.Nightly.NightlyWatchdog]::CompletedHang($true, $finished, $suiteId, $childText)
        $completedHang = [bool]$resolvedHang.CompletedButHung
        $timeoutDisposition = [Antiphon.Nightly.NightlyWatchdog]::TimeoutDisposition((Test-Path -LiteralPath $trxPath), $false, [bool]($completedHang))
        $coverageComplete = [bool]$timeoutDisposition.CoverageComplete
        $testsPassed = [bool]$timeoutDisposition.TestsPassed
        if ($completedHang) {
            $row.result = 'FAIL'
            $row.evidenceReasons = @(@($row.evidenceReasons) | Where-Object { [string]$_ -notmatch '^process-exit-' })
            $killReason = 'process-exit-' + [string][int]$run.ExitCode
            $keptReasons = New-Object System.Collections.Generic.List[string]
            $droppedKill = $false
            for ($reasonIndex = @($reasons).Count - 1; $reasonIndex -ge 0; $reasonIndex--) {
                $reasonText = [string]$reasons[$reasonIndex]
                if (-not $droppedKill -and $reasonText -eq $killReason) { $droppedKill = $true; continue }
                $keptReasons.Insert(0, $reasonText)
            }
            $reasons = @($keptReasons.ToArray())
            $reasons += [string]$resolvedHang.Reason
            $capturePath = ''
            if ($null -ne $run.PSObject.Properties['HangCapturePath'] -and -not [string]::IsNullOrWhiteSpace([string]$run.HangCapturePath)) {
                $capturePath = [string]$run.HangCapturePath
            }
            if ([string]::IsNullOrWhiteSpace($capturePath) -or -not (Test-Path -LiteralPath $capturePath)) {
                $capturePath = $logPath + '.hang.txt'
                if (-not (Test-Path -LiteralPath $capturePath)) {
                    [Antiphon.Nightly.NightlyWatchdog]::WriteHangCapture($capturePath, $suiteId, $childText, $rootPid)
                }
            }
            $row.hangCapture = $capturePath
            # TESTS, not TIMEOUT, so publicationTerminal is failed-run.
            $harnessHangReason = 'completed-but-hung'
            $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'TESTS')
        } else {
            $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'TIMEOUT')
            $reasons += ('timeout {0}' -f $suiteId)
        }
    } elseif ($run.ExitCode -ne 0 -and -not $exitTolerated) {
        $overallFailed = $true
        $testsPassed = $false
        $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'TESTS')
    } elseif (-not $chunkTestsPassed -or -not $chunkCoverageComplete) {
        $overallFailed = $true
        $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'TESTS')
    }
    $timingPhase = 'complete'
    $timingWhy = ''
    $timingTrx = ''
    if ($isNative) { $timingTrx = $trxPath }
    if ($run.TimedOut) {
        $timingPhase = 'timeout'
        $timingWhy = 'timeout'
    } elseif ($isNative -and -not (Test-NightlyPathExists $trxPath)) {
        $timingWhy = 'missing-trx'
    } elseif ($suiteId -eq 'client' -and -not (Test-Path -LiteralPath $jsonPath)) {
        $timingWhy = 'missing-client-json'
    } elseif ($suiteId -eq 'client') {
        $timingWhy = 'no-trx:client'
    } elseif ($suiteId -eq 'scripts' -and [int]$run.ExitCode -ne 0) {
        $timingWhy = 'script-failed'
    } elseif ($suiteId -eq 'scripts') {
        $timingWhy = 'no-trx:scripts'
    }
    if ($completedHang) {
        $timingWhy = 'completed-but-hung'
    }
    $timingUpdate = @{
        LogRoot = $LogRoot
        TimingRoot = $TimingRoot
        Suite = $suiteId
        Chunk = [string]$chunk.id
        LastPhase = $timingPhase
        Why = $timingWhy
        RunWallSeconds = $runWallSeconds
        DiscoverySeconds = $chunkDiscoverySeconds
        BuildSeconds = $qualificationBuildSeconds
        QueueWaitSeconds = $queueWaitSeconds
        TrxPath = $timingTrx
        RunId = $invocationId
        Attempt = [string]$Attempt
        Terminal = $true
    }
    if (-not $run.TimedOut) { $timingUpdate.CompletedAt = (Get-NightlyUtcNow).ToString('o') }
    [Antiphon.Nightly.QualificationTiming]::Update($timingUpdate, (Get-NightlyUtcNow))
    $suiteResults += $row
    } finally {
        if($isNative){[void](Write-QualificationActiveChunkIdentity -Directory $TimingRoot -SuiteId $suiteId -ChunkId ([string]$chunk.id) -RunId $invocationId -Attempt ([string]$Attempt) -State Idle)}
        # Get-Variable -ValueOnly -ErrorAction SilentlyContinue without the cmdlet's cost (as at suite start).
        foreach ($executionName in @($ExecutionSuiteState.Variables.Keys)) {
            $executionVariable = $executionVariableSet.Get($executionName)
            if ($null -eq $executionVariable) { $ExecutionSuiteState.Variables[$executionName] = [System.Management.Automation.Internal.AutomationNull]::Value }
            else { $ExecutionSuiteState.Variables[$executionName] = $executionVariable.Value }
        }
        # The same nodes as @($suiteDiscoveryNodes); the array itself when it is one, not a copy per chunk.
        if ($suiteDiscoveryNodes -is [object[]]) { $ExecutionSuiteState.nodes = $suiteDiscoveryNodes }
        else { $ExecutionSuiteState.nodes = @($suiteDiscoveryNodes) }
    }
}

function Complete-NightlySuiteExecution {
    param($CompletionSuiteState)
    # Set-Variable's local-scope set (the caller's scope: this runs dot-sourced) without the cmdlet's cost.
    $completionVariableSet = $ExecutionContext.SessionState.PSVariable
    foreach ($completionName in @($CompletionSuiteState.Variables.Keys)) {
        $completionVariableSet.Set($completionName, $CompletionSuiteState.Variables[$completionName])
    }
    if ($isNative -and @($suiteDiscoveryNodes).Count -gt 0) {
        $unionNodes = [Collections.Generic.List[object]]::new()
        if ($null -ne $profileDiscovery -and -not [string]::IsNullOrWhiteSpace($profileDiscoveryPath)) {
            $unionEntry = [Antiphon.Nightly.NightlyDiscoveryIndex]::EntryOf($suiteDiscoveryIndex, $profileDiscoveryPath)
            $unionView = [Antiphon.Nightly.NightlyDiscoveryIndex]::View($unionEntry, $profileExclusions, [bool]($profileAware))
            Assert-NightlyCoreAvailable 'Complete-NightlySuiteExecution'
            # The view's nodes that are not excluded, in discovery order (SuiteCompletionParts.IncludedNodes).
            $unionNodes.AddRange([Antiphon.Nightly.SuiteCompletionParts]::IncludedNodes($unionView.Nodes, $unionView.Excluded))
        } else {
            foreach ($dn in @($suiteDiscoveryNodes)) {
                if ([Antiphon.Nightly.NightlyCensus]::Excluded($dn, $profileExclusions, [bool]($profileAware))) { continue }
                $unionNodes.Add($dn)
            }
        }
        $union = [Antiphon.Nightly.NightlyChunkVerdict]::SuiteUidUnion($unionNodes.ToArray(), $suiteTerminalNodes)
        if (-not $union.Ok) {
            $coverageComplete = $false
            $overallFailed = $true
            $reasons += ('missing-expanded-row union {0}' -f ($union.Missing -join ','))
        }
    }
    if ($isNative -and $null -ne $profileDiscovery -and -not [string]::IsNullOrWhiteSpace([string]$profileDiscoveryPath) -and @($suiteDiscoveryNodes).Count -gt 0 -and @($expectedSkipRoster).Count -gt 0) {
        $discoveryKnown = @([Antiphon.Nightly.SuiteCompletionParts]::KnownIdentities(@($suiteDiscoveryNodes)))
        foreach ($discoveryStale in @([Antiphon.Nightly.NightlyChunkVerdict]::StaleWarnings(([string]$platform), $expectedSkipRoster, $discoveryKnown))) {
            $discoveryStaleText = [string]$discoveryStale
            if ([string]::IsNullOrWhiteSpace($discoveryStaleText)) { continue }
            if ($expectedSkipWarnings -notcontains $discoveryStaleText) { $expectedSkipWarnings += $discoveryStaleText }
        }
    }
    $CompletionSuiteState.Completed = $true
}

# CARD-2005. The run calls this to finish summary.json: D-5 keeps each suite's exclusion
# list once, dispatchProgress points at summary.suites, and Write-NightlyJsonFile streams
# the document. A full run's summary is tens of MB. There is no second writer.
function Write-NightlyRunSummary {
    param(
        $summary,
        $suiteResults,
        $dispatchSchedule,
        [string]$Sha,
        $completedAt,
        [string]$summaryPath,
        [string]$LogRoot
    )
    # D-5: every chunk row records its suite's exclusions, and with one census per suite the lists are the
    # same - most of a full run's summary. Each suite's list is kept once, in suiteExclusions[<suite id>], and
    # dropped from its rows only when every row's list is identical; rows keep their counts and censusDigest.
    $suiteExclusions=[ordered]@{}
    foreach($exclusionGroup in @($suiteResults | Where-Object { $_.Contains('exclusions') } | Group-Object { [string]$_['id'] })){
        # The element-by-element reference check runs in the core (ExclusionDedupe.cs): every row of a full
        # run carries the same few hundred objects, so the interpreted loop was most of this pass.
        Assert-NightlyCoreAvailable 'D5ExclusionDedupe'
        $groupRows=@($exclusionGroup.Group)
        $firstList=@($groupRows[0]['exclusions'])
        $firstText=$null
        $sameList=$true
        foreach($groupRow in $groupRows){
            $list=@($groupRow['exclusions'])
            if($list.Count -ne $firstList.Count){ $sameList=$false; break }
            if([Antiphon.Nightly.ExclusionDedupe]::SameElementReferences($list,$firstList)){ continue }
            if($null -eq $firstText){ $firstText=ConvertTo-Json -InputObject $firstList -Depth 8 -Compress }
            if((ConvertTo-Json -InputObject $list -Depth 8 -Compress) -cne $firstText){ $sameList=$false; break }
        }
        if($sameList){
            $suiteExclusions[$exclusionGroup.Name]=$firstList
            foreach($groupRow in $groupRows){ $groupRow.Remove('exclusions') }
        }
    }
    if($suiteExclusions.Count -gt 0){ $summary.suiteExclusions=$suiteExclusions }
    # Bound by the exported summary digest. This diagnostic snapshot can order a
    # later complete attempt; it never supplies pass credit or skips a launch.
    if($null -ne $dispatchSchedule -and $dispatchSchedule.preparationComplete){
        # The snapshot's chunk rows are not written a second time (CARD-2005): they are summary.suites' rows that
        # have a chunk, and the reader rebuilds them (PriorFailureHistory.SummaryDispatchProgress in the core).
        $summary.dispatchProgress=[ordered]@{schemaVersion=1;liveness='closed';sha=$Sha;completedAt=$completedAt.ToString('o');canonicalPlanDigest=$dispatchSchedule.canonicalPlanDigest;suitesFromSummary=$true}
    }
    try {
        $phaseDir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
        $phasePlanned = Join-Path $phaseDir 'groups-planned.json'
        $phaseIndex = Join-Path $phaseDir 'groups-planned-index.json'
        if ((Test-NightlyEvidencePath $phasePlanned) -or (Test-NightlyEvidencePath $phaseIndex)) {
            Invoke-TestPhaseRunSummary -RunDirectory $LogRoot -Top 50
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    # Streamed by the core (Write-NightlyJsonFile, nightly-core.ps1): a full run's summary is tens of MB.
    Write-NightlyJsonFile -Path $summaryPath -Value $summary -Depth 8
}

function Invoke-AntiphonNightlyTests {
    param(
        [string]$RepoRoot = '',
        [string]$LogRoot = '',
        [string[]]$Suites,
        [string]$Sha = '',
        [string]$GitRef = 'origin/master',
        [string]$Trigger = 'scheduled',
        [string]$Profile = 'nightly',
        [string]$ExpectedSha = '',
        [string]$RunId = '',
        [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',
        [string]$ReleaseRoot = '',
        [string]$CandidateId = '',
        [string]$Repository = 'michal-ciechan/Antiphon',
        [string]$BaselineSha = '',
        [string]$BaselineReceiptPath = '',
        [string]$QualificationPath = '',
        [string]$TimingRoot = '',
        [string]$TrainManifestPath = '',
        [string]$ReleaseIntentPath = '',
        [string]$SelectionAttemptPath = '',
        [string]$SelectionAttemptDigest = '',
        [string]$SelectionPath = '',
        [string]$SelectionDigest = '',
        [int]$Attempt = 1,
        [string]$PriorFailuresPath = '',
        [string]$PriorFailuresDigest = '',
        [switch]$AcceptCrossCandidateSeed,
        [string]$SeedSourceCandidateId = '',
        [string]$SeamsPath = '',
        [string]$RcEvidenceRoot = '',
        [string]$PolicyPath = '',
        [switch]$AllowSharedTree,
        [switch]$WhatIf
    )

    $script:NightlyRcRunEvidence = $null
    Import-NightlySeams -SeamsPath $SeamsPath
    $script:QualificationTimingRoot = ''
    # The HostPlatform seam replaces only the OS observation (NightlyHostProbe.Platform), never the decision.
    $observedPlatform = ''
    if ($script:NightlySeams -and $script:NightlySeams.HostPlatform) { $observedPlatform = [string]$script:NightlySeams.HostPlatform.Invoke() }
    else { $observedPlatform = [Antiphon.Nightly.NightlyHostProbe]::new().Platform() }
    try { $platform = [Antiphon.Nightly.NightlyTestsPlan]::ExecutionPlatform($observedPlatform) }
    catch { return [pscustomobject]@{ ExitCode = 3; Refusal = 'unsupported-host-platform'; coverageComplete = $false; testsPassed = $false; LaunchCount = 0 } }
    if ([string]::IsNullOrWhiteSpace($Profile)) { $Profile = 'nightly' }
    $Profile = $Profile.Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Split-Path -Parent $PSScriptRoot
        if ((Split-Path -Leaf $RepoRoot) -eq 'lib') { $RepoRoot = Split-Path -Parent $RepoRoot }
        $RepoRoot = Split-Path -Parent $RepoRoot
    }
    $RepoRoot = ConvertTo-NightlyCanonicalPath -Path $RepoRoot
    if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = New-NightlyRunId }

    $selection = $null
    try {
        [void](Assert-ReleaseSelectionCapability $SelectionMode)
        if ($SelectionAttemptPath) {
            $selectionRequest = @{RepositoryRoot=$RepoRoot;CandidateSha=$Sha;CandidateRef=$GitRef;Repository=$Repository;Profile=$Profile;
            ReleaseRoot=$ReleaseRoot;CandidateId=$CandidateId;RunId=$RunId;
            SelectionMode=$SelectionMode;BaselineSha=$BaselineSha;BaselineReceiptPath=$BaselineReceiptPath;QualificationPath=$QualificationPath;TimingRoot=$TimingRoot;TrainManifestPath=$TrainManifestPath;ReleaseIntentPath=$ReleaseIntentPath;SelectionAttemptPath=$SelectionAttemptPath;SelectionAttemptDigest=$SelectionAttemptDigest;SelectionPath=$SelectionPath;SelectionDigest=$SelectionDigest;Attempt=$Attempt}
            $selection = Read-ReleaseSelectionAttempt $selectionRequest
            # Independently read HEAD at the receiving implementation, not just argv.
            $head = Invoke-NightlyGit -WorkingDirectory $RepoRoot -Arguments @('rev-parse','HEAD')
            if ($head.ExitCode -ne 0 -or $head.Output.Trim() -cne $Sha) { throw 'selection-hop-head' }
        } elseif ($SelectionMode -cne 'Full') { throw 'selection-attempt-required' }
    } catch { return [pscustomobject]@{ExitCode=3;Refusal=$_.Exception.Message;coverageComplete=$false;testsPassed=$false;LaunchCount=0} }

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
    $expectedSkipWarnings = @()
    $expectedSkipRoster = @()
    $rcSkipRoster = @()
    $expectedSkipScan = $null
    $toleratedSkipRows = @()
    $harnessHangReason = ''
    $nativeActiveMax = 0
    $dispatchSchedule = $null

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
        $policy = [Antiphon.Nightly.NightlyPolicy]::Read($RepoRoot, $PolicyPath)
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
    # D-5: the selected profile is authoritative for required suites; defaultSuites stays
    # the compatible nightly answer. An unknown profile, or rc asked of a v1 policy, is a
    # hard refusal rather than a silent fall back to the nightly seven.
    try {
        $profileRow = [Antiphon.Nightly.NightlyPolicy]::Profile($policy.Object, $Profile)
        $requiredSuites = @([Antiphon.Nightly.NightlyPolicy]::ProfileRequiredSuites($policy.Object, $Profile))
        # Validate every exclusion before any child command; each suite resolves
        # its own rows below so same-named classes in other suites stay required.
        [void]([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($policy.Object, $Profile, '', $platform))
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
            Profile = $Profile
        }
    }
    $profileAware = ($Profile -ne 'nightly')
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha) -and -not [string]::IsNullOrWhiteSpace($Sha) -and
        -not [string]::Equals($Sha, $ExpectedSha, [StringComparison]::OrdinalIgnoreCase)) {
        $msg = ('expected-sha-mismatch sha={0} expected={1}' -f $Sha, $ExpectedSha)
        Write-Error $msg
        return [pscustomobject]@{ ExitCode = 2; coverageComplete = $false; testsPassed = $false; Error = $msg; Profile = $Profile }
    }
    $universe = [Antiphon.Nightly.NightlyPolicy]::TestProjectUniverse($RepoRoot, $policy.Object)
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
            $selectedSuites = @($requiredSuites)
            if ($selection) { $selectedSuites = @(Resolve-ReleaseSelectionMembers $selection $requiredSuites @()) }
        } else {
            $joined = ($Suites | ForEach-Object { [string]$_ }) -join ','
            if ($joined -match '^[\s,]*$') { throw 'empty suite selection' }
            $selectedSuites = @([Antiphon.Nightly.NightlyPolicy]::ResolveSelectedSuites($policy.Object, $Suites))
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

    $suiteUniverse = [Antiphon.Nightly.NightlyPolicy]::RequiredSuitesPresent($selectedSuites, $requiredSuites)
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
    $dispatchInput=[Antiphon.Nightly.PriorFailureHistory]::InitializeDispatch($PriorFailuresPath,$PriorFailuresDigest,$LogRoot,$Sha,[bool]$AcceptCrossCandidateSeed,$SeedSourceCandidateId,(Get-NightlyUtcNow))
    if ($dispatchInput.Warning) { Write-Warning -WarningAction Continue $dispatchInput.Warning }
    $summaryPath = Join-Path $LogRoot 'summary.json'
    $invocationId = $RunId
    $TimingRoot = [string](Resolve-QualificationTimingRoot -TimingRoot $TimingRoot -LogRoot $LogRoot -RunId $RunId)
    $script:QualificationTimingRoot = $TimingRoot
    $publicationContext = $null
    $publicationSource = [string]$Sha
    if ([string]::IsNullOrWhiteSpace($publicationSource)) { $publicationSource = 'unknown' }
    try {
        $publicationContext = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $invocationId -Attempt ([string]$Attempt) -Suite '' -Source $publicationSource
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    try {
    # A4 host sampling is not wired. Do not load a probe DLL from the build output.
    $qualificationBuildSeconds = $null

    $chunksBySuite = @{}
    if ($policy.Object.Chunks) {
        foreach ($p in $policy.Object.Chunks.GetEnumerator()) { $chunksBySuite[$p.Key] = @($p.Value) }
    }


    $dockerOk = $true
    $diskOk = $true
    $npmOk = $true
    $dotnetOk = $true
    $lintOk = $true
    $lintBlocksPipeline = $false
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
        if ([int]$preflight.docker.exitCode -ne 0) { $dockerOk = $false; $overallFailed = $true; $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'PREFLIGHT'); $coverageComplete = $false }

        if ($script:NightlySeams -and $script:NightlySeams.DiskFree) {
            $preflight.disk = $script:NightlySeams.DiskFree.Invoke()
        } else {
            $preflight.disk = [ordered]@{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB }
        }
        # CARD-1698: record the toolchain versions and floor warnings; a warning never fails a run.
        $preflight.toolchain = Get-NightlyToolchainPreflight -RepoRoot $RepoRoot
        if ([int]$preflight.disk.exitCode -ne 0) { $diskOk = $false; $overallFailed = $true; $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'PREFLIGHT'); $coverageComplete = $false }

        if ($dockerOk -and $diskOk) {
            $buildWatch = [Diagnostics.Stopwatch]::StartNew()
            $npmCiStarted = $null
            $npmCiSeconds = $null
            $npmBuildStarted = $null
            $npmBuildSeconds = $null
            $lintStarted = $null
            $lintSeconds = $null
            $dotnetStarted = $null
            $dotnetSeconds = $null
            $clientDir = Join-Path $RepoRoot 'client'
            try { $npmCiLaunch = [Antiphon.Nightly.NightlyTestsPlan]::NpmLaunch([string[]]@('ci'), $env:PATH, $env:ComSpec) } catch { throw (Get-NightlyRefusalText $_) }
            $npmCiStarted = Get-NightlyUtcNow
            $npmCiWatch = [Diagnostics.Stopwatch]::StartNew()
            $npmCi = Invoke-NightlyOwnedProcess -FilePath $npmCiLaunch.FilePath -ArgumentList $npmCiLaunch.ArgumentList `
                -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-ci.log')
            $npmCiWatch.Stop()
            $npmCiSeconds = [math]::Round($npmCiWatch.Elapsed.TotalSeconds, 3)
            $buildResults += [ordered]@{ name = 'npm ci'; exitCode = $npmCi.ExitCode; timedOut = $npmCi.TimedOut; result = $(if ($npmCi.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
            if ($npmCi.ExitCode -ne 0) {
                $npmOk = $false
                $overallFailed = $true
                $testsPassed = $false
                $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'BUILD')
                $coverageComplete = $false
                $reasons += 'prerequisite-failed:npm ci'
            }

            if ($npmOk) {
                try { $npmBuildLaunch = [Antiphon.Nightly.NightlyTestsPlan]::NpmLaunch([string[]]@('run', 'build'), $env:PATH, $env:ComSpec) } catch { throw (Get-NightlyRefusalText $_) }
                $npmBuildStarted = Get-NightlyUtcNow
                $npmBuildWatch = [Diagnostics.Stopwatch]::StartNew()
                $npmBuild = Invoke-NightlyOwnedProcess -FilePath $npmBuildLaunch.FilePath -ArgumentList $npmBuildLaunch.ArgumentList `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-build.log')
                $npmBuildWatch.Stop()
                $npmBuildSeconds = [math]::Round($npmBuildWatch.Elapsed.TotalSeconds, 3)
                $buildResults += [ordered]@{ name = 'npm run build'; exitCode = $npmBuild.ExitCode; timedOut = $npmBuild.TimedOut; result = $(if ($npmBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($npmBuild.ExitCode -ne 0) {
                    $npmOk = $false
                    $overallFailed = $true
                    $testsPassed = $false
                    $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'BUILD')
                    $coverageComplete = $false
                    $reasons += 'prerequisite-failed:npm run build'
                }
                else {
                    $dist = Join-Path $clientDir (Join-Path 'dist' 'index.html')
                    $receipt = Join-Path $LogRoot 'client-build-receipt.json'
                    Write-NightlyAtomicJson -Path $receipt -Object ([ordered]@{ sha = $Sha; runId = $invocationId; dist = $dist; builtAt = (Get-NightlyUtcNow).ToString('o') })
                }
            }

            if ($npmOk) {
                try { $lintLaunch = [Antiphon.Nightly.NightlyTestsPlan]::NpmLaunch([string[]]@('run', 'lint'), $env:PATH, $env:ComSpec) } catch { throw (Get-NightlyRefusalText $_) }
                $lintStarted = Get-NightlyUtcNow
                $lintWatch = [Diagnostics.Stopwatch]::StartNew()
                $lint = Invoke-NightlyOwnedProcess -FilePath $lintLaunch.FilePath -ArgumentList $lintLaunch.ArgumentList `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'client-lint.log')
                $lintWatch.Stop()
                $lintSeconds = [math]::Round($lintWatch.Elapsed.TotalSeconds, 3)
                $buildResults += [ordered]@{ name = 'client lint'; exitCode = $lint.ExitCode; timedOut = $lint.TimedOut; result = $(if ($lint.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($lint.ExitCode -ne 0) {
                    $lintOk = $false
                    $overallFailed = $true
                    # Profile nightly records the lint failure and still runs dotnet
                    # and the suites. Every other profile, including rc, stops here.
                    # Do not mark testsPassed yet on nightly: a later suite failure
                    # must be able to set TESTS, and a green suite stays BUILD.
                    if ($Profile -eq 'nightly') {
                        $reasons += 'client-lint-failed'
                    } else {
                        $lintBlocksPipeline = $true
                        $testsPassed = $false
                        $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'BUILD')
                        $coverageComplete = $false
                        $reasons += 'prerequisite-failed:client lint'
                    }
                }
            }

            if ($npmOk -and -not $lintBlocksPipeline) {
                $dotnetArgs = @{
                    FilePath = 'dotnet'
                    ArgumentList = @('build', 'Antiphon.sln', '-c', 'Debug', '--nologo')
                    WorkingDirectory = $RepoRoot
                    TimeoutMilliseconds = 1200000
                    LogPath = (Join-Path $LogRoot 'dotnet-build.log')
                }
                $phaseInherited = $false
                foreach ($phaseName in @($script:TestPhaseVariableNames)) {
                    if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable([string]$phaseName))) { $phaseInherited = $true }
                }
                if ($phaseInherited) {
                    $dotnetArgs.Environment = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, 'antiphon', $null)
                    Remove-TestPhaseVariables -Environment $dotnetArgs.Environment
                }
                $dotnetStarted = Get-NightlyUtcNow
                $dotnetWatch = [Diagnostics.Stopwatch]::StartNew()
                $dotnetBuild = Invoke-NightlyOwnedProcess @dotnetArgs
                $dotnetWatch.Stop()
                $dotnetSeconds = [math]::Round($dotnetWatch.Elapsed.TotalSeconds, 3)
                $buildResults += [ordered]@{ name = 'dotnet build Antiphon.sln'; exitCode = $dotnetBuild.ExitCode; timedOut = $dotnetBuild.TimedOut; result = $(if ($dotnetBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($dotnetBuild.ExitCode -ne 0) {
                    $dotnetOk = $false
                    $overallFailed = $true
                    $testsPassed = $false
                    $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'BUILD')
                    $coverageComplete = $false
                    $reasons += 'prerequisite-failed:dotnet build'
                }
            }
            $buildWatch.Stop()
            $qualificationBuildSeconds = [math]::Round($buildWatch.Elapsed.TotalSeconds, 3)
            Write-NightlyPrerequisitePhases -LogRoot $LogRoot -NpmCiStarted $npmCiStarted -NpmCiSeconds $npmCiSeconds -NpmBuildStarted $npmBuildStarted -NpmBuildSeconds $npmBuildSeconds -LintStarted $lintStarted -LintSeconds $lintSeconds -DotnetStarted $dotnetStarted -DotnetSeconds $dotnetSeconds

            $suiteMap = [Antiphon.Nightly.NightlyPolicy]::SuiteMap($policy.Object)
    if ($dotnetOk) {
        foreach ($evidenceSuiteId in @($selectedSuites)) {
            $evidenceSuite = $null
            if ($suiteMap.ContainsKey($evidenceSuiteId)) { $evidenceSuite = $suiteMap[$evidenceSuiteId] }
            $evidenceAssembly = ''
            if ($evidenceSuite) { $evidenceAssembly = [string]$evidenceSuite.assembly }
            if ([string]::IsNullOrWhiteSpace($evidenceAssembly)) { continue }
            $evidenceOut = [IO.Path]::Combine($RepoRoot, 'tests', $evidenceAssembly, 'bin', 'Debug', 'net9.0')
            [void](Get-NightlyRcRunEvidence).Build($RcEvidenceRoot, $LogRoot, [string]$evidenceSuiteId, 'dotnet build Antiphon.sln -c Debug --nologo', $evidenceOut, ($evidenceAssembly + '.dll'), '', $RepoRoot, [string]$Sha)
        }
    }
            $rosterFile = Join-Path $RepoRoot (Join-Path 'tests' 'expected-platform-skips.json')
            if (Test-Path -LiteralPath $rosterFile -PathType Leaf) {
                $expectedSkipRoster = @([Antiphon.Nightly.NightlyChunkVerdict]::SkipRoster($rosterFile))
                $rcSkipRoster = @(foreach ($skipEntry in $expectedSkipRoster) { [Antiphon.Nightly.RcSkipEntry]::new([string]$skipEntry.test, [string]$skipEntry.platform, [string]$skipEntry.reasonPrefix, [string]$skipEntry.reasonClass) })
                # The end-of-run skip scans read only tests/ and the roster: they run while the chunks do.
                $expectedSkipScan = [Antiphon.Nightly.NightlyExpectedSkips]::Start($RepoRoot, $platform, $expectedSkipRoster)
            }
            . Invoke-NightlyDispatchCoordinator -DispatchSuites $selectedSuites -DispatchHistory $dispatchInput.History -DispatchHistoryDigest $dispatchInput.Digest -DispatchPreviousRun $dispatchInput.PreviousRun -DispatchFallbackReason $dispatchInput.FallbackReason

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

    $suiteLoopBlocked = (-not $dockerOk) -or (-not $diskOk) -or (-not $npmOk) -or $lintBlocksPipeline -or (-not $dotnetOk)
    if ($suiteLoopBlocked) {
        foreach ($unstartedSuite in @($selectedSuites)) {
            $unstartedWhy = 'unstarted'
            $unstartedPhase = 'preflight'
            if ((-not $npmOk) -or $lintBlocksPipeline -or (-not $dotnetOk)) {
                $unstartedWhy = 'build-failed'
                $unstartedPhase = 'build'
            }
            [Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $LogRoot; TimingRoot = $TimingRoot; Suite = ([string]$unstartedSuite); Chunk = 'unstarted'; LastPhase = $unstartedPhase; Why = $unstartedWhy; BuildSeconds = $qualificationBuildSeconds; StartedAt = $startedAt.ToString('o'); RunId = $invocationId; Attempt = ([string]$Attempt) }, (Get-NightlyUtcNow))
        }
    }

    if (-not $lintOk) {
        $coverageComplete = $false
        $testsPassed = $false
        if ($Profile -eq 'nightly' -and $script:NightlyTestsOutcome -eq 'green') { $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'BUILD') }
    }
    if ($nativeActiveMax -gt 1) { $coverageComplete = $false; $reasons += 'native-overlap' }
    $rosterFile = Join-Path $RepoRoot (Join-Path 'tests' 'expected-platform-skips.json')
    $testsProject = Join-Path $RepoRoot (Join-Path 'tests' 'Antiphon.Tests')
    if ((Test-Path -LiteralPath $rosterFile -PathType Leaf) -and (Test-Path -LiteralPath $testsProject -PathType Container) -and @($expectedSkipRoster).Count -gt 0) {
        foreach ($gap in @([Antiphon.Nightly.NightlyExpectedSkips]::SourceGaps($RepoRoot, $platform, $expectedSkipRoster, $expectedSkipScan))) {
            $gapWarning = 'stale-roster:' + [string]$gap
            if ($expectedSkipWarnings -notcontains $gapWarning) { $expectedSkipWarnings += $gapWarning }
        }
        foreach ($reasonGap in @([Antiphon.Nightly.NightlyExpectedSkips]::ReasonGaps($RepoRoot, $platform, $expectedSkipRoster, $expectedSkipScan))) {
            $reasonWarning = 'skip-reason:' + [string]$reasonGap
            if ($expectedSkipWarnings -notcontains $reasonWarning) { $expectedSkipWarnings += $reasonWarning }
        }
        $rosterDrift = [Antiphon.Nightly.NightlyExpectedSkips]::RosterDrift($RepoRoot, $expectedSkipRoster, $expectedSkipScan)
        foreach ($driftId in @($rosterDrift.Mismatches)) {
            $driftText = [string]$driftId
            if ([string]::IsNullOrWhiteSpace($driftText)) { continue }
            $driftWarning = 'roster-drift:' + $driftText
            if ($expectedSkipWarnings -notcontains $driftWarning) { $expectedSkipWarnings += $driftWarning }
        }
    }
    # A scan whose results were not wanted (the condition above is false) is still waited for here; one left by
    # an exception before this point never faults and is waited for by the next scan or in-line walk.
    if ($null -ne $expectedSkipScan) { [Antiphon.Nightly.NightlyExpectedSkips]::Wait($expectedSkipScan.Task) }

    $coverageScope = 'Full'
    if ($Profile -eq 'rc' -and $platform -eq 'linux') {
        $coverageScope = 'PlatformDiagnostic'
        $coverageComplete = $false
        $reasons += 'windows-rc-obligations-outstanding'
    }

    $completedAt = Get-NightlyUtcNow
    $selectionForSummary = $selection
    if ($null -ne $selection) {
        try { $selectionForSummary = ($selection | ConvertTo-Json -Depth 8 | ConvertFrom-Json) } catch { $selectionForSummary = $selection }
        if ($selectionForSummary.PSObject.Properties.Name -contains 'timingRoot' -and [string]::IsNullOrWhiteSpace([string]$selectionForSummary.timingRoot)) {
            $selectionForSummary.timingRoot = [string]$TimingRoot
        }
    }
    if ($null -ne $publicationContext -and -not $publicationContext.Refused -and -not $publicationContext.Closed) {
        $publicationTerminal = 'success'
        if (-not [string]::IsNullOrWhiteSpace([string]$preflight.unhandledError)) {
            $publicationTerminal = 'orchestration-error'
        } elseif ([string]$script:NightlyTestsOutcome -eq 'TIMEOUT') {
            $publicationTerminal = 'timeout'
        } elseif ($overallFailed) {
            $publicationTerminal = 'failed-run'
        }
        try {
            Complete-TestPhaseExecutedProjection -Context $publicationContext -Terminal $publicationTerminal | Out-Null
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
        }
    }
    $timingChunks = @()
    $suiteTimeline = @()
    try {
        $timingReport = [Antiphon.Nightly.QualificationTiming]::Report($LogRoot, $TimingRoot)
        if ($null -ne $timingReport) {
            $timingChunks = @($timingReport.Chunks)
            $suiteTimeline = @($timingReport.Timeline)
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    $expectedSkipRosterDigest = ''
    if (@($expectedSkipRoster).Count -gt 0) {
        $expectedSkipRosterDigest = [string]([Antiphon.Nightly.NightlyChunkVerdict]::RosterDigest($expectedSkipRoster))
    }
    $evidenceWriteVerdict = [Antiphon.Nightly.EvidenceFiles]::MergeDeferredWriteVerdict(@($reasons))
    $reasons = @($evidenceWriteVerdict.reasons)
    if (-not $evidenceWriteVerdict.coverageComplete) {
        $coverageComplete = $false
        $testsPassed = $false
        $overallFailed = $true
        $script:NightlyTestsOutcome = [Antiphon.Nightly.NightlyTestsPlan]::Outcome($script:NightlyTestsOutcome, 'TESTS')
    }
    $summary = [ordered]@{
        startedAt = $startedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        sha = $Sha
        gitRef = $GitRef
        trigger = $Trigger
        profile = $Profile
        expectedSha = $ExpectedSha
        requiredSuites = $requiredSuites
        runId = $invocationId
        clone = $RepoRoot
        logDir = $LogRoot
        selectedSuites = $selectedSuites
        selection = $selectionForSummary
        timingRoot = [string]$TimingRoot
        timingChunks = @($timingChunks)
        suiteTimeline = @($suiteTimeline)
        platform = $platform
        coverageScope = $coverageScope
        outcome = $script:NightlyTestsOutcome
        harnessHang = ([Antiphon.Nightly.NightlyWatchdog]::HarnessHangField([bool](($harnessHangReason -eq 'completed-but-hung'))))
        succeeded = (-not $overallFailed -and $coverageComplete)
        coverageComplete = [bool]$coverageComplete
        testsPassed = [bool]$testsPassed
        policyHash = $policyHash
        reasons = $reasons
        warnings = @($expectedSkipWarnings)
        expectedSkipRosterDigest = $expectedSkipRosterDigest
        toleratedSkips = @($toleratedSkipRows)
        nativeActiveMax = $nativeActiveMax
        preflight = $preflight
        builds = $buildResults
        suites = $suiteResults
        invocationId = $invocationId
    }
    # D-5, dispatchProgress and the streamed summary.json write. The run and the CARD-2005 witness call this.
    Write-NightlyRunSummary -summary $summary -suiteResults $suiteResults -dispatchSchedule $dispatchSchedule -Sha $Sha -completedAt $completedAt -summaryPath $summaryPath -LogRoot $LogRoot
    Write-NightlyRunLine ('wrote {0}' -f $summaryPath)

    $exit = 0
    if ($overallFailed) { $exit = 1 }
    return [pscustomobject]@{
        ExitCode = $exit
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        platform = $platform
        coverageScope = $coverageScope
        reasons = $reasons
        Profile = $Profile
        RequiredSuites = $requiredSuites
        Selection = $selection
        SummaryPath = $summaryPath
        Selected = $selectedSuites
        NativeActiveMax = $nativeActiveMax
        PolicyHash = $policyHash
    }
    } finally {
        if ($null -ne $publicationContext) {
            Close-TestPhaseExecutedContext -Context $publicationContext
        }
    }
}

# Exact selected execution front door. Full-profile publication accounting is unchanged;
# a caller cannot promote this receipt to Full or activate ChangeAware with it.
function Invoke-NightlyExactRoster {
    param([string]$AssemblyPath, $Discovery, [object[]]$Members, [string]$ResultsDirectory,
          [string[]]$ClassifiedUids = $null, [hashtable]$Environment = $null)
    $roster = [Antiphon.Nightly.NightlyChunkVerdict]::ExactRoster($Discovery, $Members, $ClassifiedUids)
    if ($roster.AssemblyHash -cne ([Antiphon.Nightly.NightlyDiagnosticLog]::AssemblyHash($AssemblyPath))) { throw 'discovery-build-mismatch' }
    if ($roster.UnresolvedUids.Count -gt 0) { throw 'new-uid-required: classification required before launch' }
    if (Test-Path -LiteralPath $ResultsDirectory) { throw 'execution-output-already-exists' }
    [void][IO.Directory]::CreateDirectory($ResultsDirectory)
    . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
    $terminal = @(); $receipts = @(); $index = 0
    foreach ($chunk in $roster.Chunks) {
        if ($roster.AssemblyHash -cne ([Antiphon.Nightly.NightlyDiagnosticLog]::AssemblyHash($AssemblyPath))) { throw 'discovery-build-mismatch' }
        $dir = Join-Path $ResultsDirectory ('leaf-' + (++$index))
        [void][IO.Directory]::CreateDirectory($dir)
        $trx = Join-Path $dir 'run.trx'
        try { $args = @([Antiphon.Nightly.NightlyTestsPlan]::NativeExecutionArguments($trx, $dir, $null, [string]$chunk.Filter, '', '')) }
        catch { throw (Get-NightlyRefusalText $_) }
        $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $start.WorkingDirectory = $ResultsDirectory
        $start.ArgumentList.Add([IO.Path]::GetFullPath($AssemblyPath))
        foreach ($arg in $args) { $start.ArgumentList.Add($arg) }
        if ($Environment) { foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] } }
        . (Join-Path $PSScriptRoot 'build-driver-environment.ps1')
        Set-AntiphonBuildDriverEnvironment -StartInfo $start
        # CARD-1313 timing journal (antiphon-test-timing-v1) through the compiled twin
        # (Antiphon.Nightly/TestTimings.cs); test-timings.ps1 stays for run-checkpoint. Persistence
        # failure is not a test result.
        $timingRoot = $env:ANTIPHON_TEST_TIMING_ROOT
        if ($timingRoot) {
            try {
                $stamp = [DateTimeOffset]::UtcNow.ToString('o')
                $timingEvent = [ordered]@{
                    schemaVersion = 'antiphon-test-timing-v1'; eventId = ('nightly-start-' + $index); eventKind = 'run-started'
                    runId = 'nightly'; attempt = 1; producer = 'nightly'; setKind = 'chunk'; setId = [string]$chunk.Filter
                    outcome = 'started'; startedAt = $stamp; timingPersistence = 'complete'
                }
                [Antiphon.Nightly.TestTimings]::WriteEvent($timingRoot, $timingEvent)
            } catch { }
        }
        $run = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start, 600000, $false)
        if ($timingRoot) {
            try {
                $stamp = [DateTimeOffset]::UtcNow.ToString('o')
                $timingEvent = [ordered]@{
                    schemaVersion = 'antiphon-test-timing-v1'; eventId = ('nightly-done-' + $index); eventKind = 'set-completed'
                    runId = 'nightly'; attempt = 1; producer = 'nightly'; setKind = 'chunk'; setId = [string]$chunk.Filter
                    outcome = $(if ($run.Exit -eq 0) { 'passed' } else { 'failed' }); exitCode = $run.Exit
                    startedAt = $stamp; endedAt = $stamp; category = 'unavailable'; timingPersistence = 'complete'
                }
                [Antiphon.Nightly.TestTimings]::WriteEvent($timingRoot, $timingEvent)
            } catch { }
        }
        [IO.File]::WriteAllText((Join-Path $dir 'console.log'), $run.Stdout + $run.Stderr)
        if ($run.TimedOut) { throw 'exact-execution-timeout' }
        $actual = [Antiphon.Nightly.NightlyDiagnosticLog]::Read(([Antiphon.Nightly.NightlyDiagnosticLog]::Latest($dir)), 'execution')
        $trxRows = @([Antiphon.Nightly.NightlyTrx]::ReadIdentities($trx))
        $cross = [Antiphon.Nightly.NightlyVerdicts]::CrossCheck($trxRows, $actual.Nodes, @())
        $verdict = [Antiphon.Nightly.NightlyChunkVerdict]::ExactCoverage($chunk.RequiredUids, $actual.Nodes)
        $receipts += [ordered]@{ filter = $chunk.Filter; requiredUids = @($chunk.RequiredUids); executedUids = @($actual.Nodes.Uid); exitCode = $run.Exit; trx = $trx; complete = ($cross.Ok -and $verdict.Ok -and $run.Exit -eq 0) }
        $terminal += @($actual.Nodes)
        if (-not $cross.Ok -or -not $verdict.Ok -or $run.Exit -ne 0) {
            Write-NightlyAtomicJson (Join-Path $ResultsDirectory 'receipt.json') @{ format='antiphon-exact-roster-v1'; complete=$false; assemblyHash=$roster.AssemblyHash; chunks=$receipts; reasons=@($verdict.Reasons) }
            throw ('exact-execution-incomplete:' + ($verdict.Reasons -join ','))
        }
    }
    $verdict = [Antiphon.Nightly.NightlyChunkVerdict]::ExactCoverage($roster.RequiredUids, $terminal)
    $result = [ordered]@{ format='antiphon-exact-roster-v1'; complete=$verdict.Ok; executionEvidence=$true; releaseCredit=$false; assemblyHash=$roster.AssemblyHash; requiredUids=@($roster.RequiredUids); executedUids=@($terminal.Uid); chunks=$receipts }
    Write-NightlyAtomicJson (Join-Path $ResultsDirectory 'receipt.json') $result
    return $result
}
