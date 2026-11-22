#requires -Version 5.1
# CARD-0487: nightly suite execution, evidence and coverage completeness.
# ASCII-only. Does not clone, fetch, reset or change git state.

if ($script:AntiphonNightlyTestsImplLoaded) { return }
$script:AntiphonNightlyTestsImplLoaded = $true
. (Join-Path $PSScriptRoot 'nightly-coverage.ps1')
. (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable

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
# the table read from it stay here: the planned manifest (TestPhaseManifests.WritePlanned) is given both, and a fixture
# seam may replace either.
$script:NightlyClassCostPath = Join-Path $PSScriptRoot 'nightly-class-costs.json'
$script:NightlyClassCostTable = $null

# W2-nightly S12: the chunk runner, the owned launch, the observation verdict, the measured phases and the completion
# grace are Antiphon.Nightly/NightlyChunkRunner.cs. These wrappers stay for the still-PowerShell callers (the
# prerequisite builds, the e2e install, the discovery producer, the suite preparation and the test drivers).
function Write-NightlyMeasuredPhase {
    param([string]$LogRoot, [string]$Name, $StartedAt, $Seconds)
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { return }
    [Antiphon.Nightly.NightlyChunkRunner]::WriteMeasuredPhase($LogRoot, (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot), $Name, $StartedAt, $Seconds)
}

function Write-NightlyPrerequisitePhases {
    param([string]$LogRoot, $NpmCiStarted, $NpmCiSeconds, $NpmBuildStarted, $NpmBuildSeconds, $LintStarted, $LintSeconds, $DotnetStarted, $DotnetSeconds)
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { return }
    [Antiphon.Nightly.NightlyChunkRunner]::WritePrerequisitePhases($LogRoot, (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot), $NpmCiStarted, $NpmCiSeconds,
        $NpmBuildStarted, $NpmBuildSeconds, $LintStarted, $LintSeconds, $DotnetStarted, $DotnetSeconds)
}

function Publish-NightlyChunkObservation {
    param($Run, [string]$LogRoot, [string]$TrxPath, [string]$Suite, [string]$Chunk, [string]$RunId, [string]$Attempt,
        [double]$HostWallSeconds = 0, $CoverageComplete = $false, $TestsPassed = $false)
    $phaseVerdict = [Antiphon.Nightly.NightlyChunkRunner]::ObservationVerdict($Run, $CoverageComplete, $TestsPassed)
    # The executed manifest: compiled (TestPhaseManifests.WriteExecuted), into the bound writer context when there is one.
    [void][Antiphon.Nightly.TestPhaseManifests]::WriteExecuted($phaseVerdict, $LogRoot, $TrxPath, $Suite, $Chunk, $RunId, $Attempt, $HostWallSeconds, $script:TestPhaseExecutedContext, (Get-TestPhasePublicationScope))
    return $phaseVerdict
}

# The NightlyHost of an owned launch or a chunk, from the seams: the UtcNow seam is its clock, the TracePath seam its
# trace, and the StartProcess, WaitCleanup and NativeActiveCount seams its launcher (DelegateLauncher reads the record
# the StartProcess seam answers). Without them OwnedLauncher starts the child under native custody. Either lands the
# queued evidence writes first: a child reads the disk.
function Get-NightlyRunHost {
    $seams = $script:NightlySeams
    $clock = $null
    if ($seams -and $seams.UtcNow) { $clock = [Antiphon.Nightly.DelegateTimeProvider]::new([Func[datetime]]{ Get-NightlyUtcNow }) }
    $trace = $null
    if ($seams -and $seams.TracePath) {
        $trace = [Antiphon.Nightly.AppendingTraceWriter]::new($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath([string]$seams.TracePath))
    }
    if ($seams -and $seams.StartProcess) {
        $start = [Func[Antiphon.Nightly.NightlyLaunch, object]]{
            param($Launch)
            Sync-NightlyDeferredEvidenceWrites
            $seamEnvironment = $null
            if ($null -ne $Launch.Environment) {
                $seamEnvironment = @{}
                foreach ($pair in $Launch.Environment.GetEnumerator()) { $seamEnvironment[$pair.Key] = $pair.Value }
            }
            return $script:NightlySeams.StartProcess.Invoke($Launch.FileName, [string[]]@($Launch.Arguments), $Launch.WorkingDirectory, $Launch.TimeoutMilliseconds, $seamEnvironment)
        }
        $wait = $null
        if ($seams.WaitCleanup) { $wait = [Func[object, bool]]{ param($Record) [bool]$script:NightlySeams.WaitCleanup.Invoke($Record) } }
        $active = $null
        if ($seams.NativeActiveCount) { $active = [Func[int]]{ [int]$script:NightlySeams.NativeActiveCount.Invoke() } }
        $launcher = [Antiphon.Nightly.DelegateLauncher]::new($start, $wait, $active, [Console]::Error)
    } else {
        $driverScript = Join-Path $PSScriptRoot 'build-driver-environment.ps1'
        $prepare = { param($StartInfo) . $driverScript; Set-AntiphonBuildDriverEnvironment -StartInfo $StartInfo }.GetNewClosure()
        $launcher = [Antiphon.Nightly.OwnedLauncher]::new([Action]{ Sync-NightlyDeferredEvidenceWrites }, [Action[Diagnostics.ProcessStartInfo]]$prepare)
    }
    return [Antiphon.Nightly.NightlyHost]::Create($clock, $launcher, [Console]::Out, [Console]::Error, $trace, $null, $null, $null)
}

# Invoke-NightlyOwnedProcess: what the launch observed (the native owner's result, or the StartProcess seam's record).
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
    $launch = [Antiphon.Nightly.NightlyChunkRunner]::OwnedLaunch($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment, $LogPath,
        $TrxPath, $DiagnosticDirectory, $HangCapturePath, $ExecutionGraceMilliseconds)
    try { $result = (Get-NightlyRunHost).Launch($launch) } catch { throw (Get-NightlyRefusalText $_) }
    return $result.Observed
}

. (Join-Path $PSScriptRoot 'release-selection.ps1')

# W2-nightly S13: suite preparation, chunks and completion (Prepare-NightlySuiteExecution, Invoke-NightlyScheduledChunk's
# wrapper, Complete-NightlySuiteExecution) are Antiphon.Nightly/NightlySuiteRunner.cs; the coordinator, the prepared schedule
# and the schedule file (nightly-dispatch.ps1) are Antiphon.Nightly/NightlyDispatch.cs. They share an explicit
# NightlyRunState in place of this executor's scope. This binds the collaborators that are still PowerShell: the run host
# (Get-NightlyRunHost), the evidence recorder, the discovery producer, the E2E prerequisite seam, the executed manifest into
# the bound writer context, the active-chunk identity, the publication scope and the warning line.
function Set-NightlyRunStateBindings {
    param([Antiphon.Nightly.NightlyRunState]$State)
    $State.Host = Get-NightlyRunHost
    $State.RcEvidence = Get-NightlyRcRunEvidence
    $State.QualificationTimingRoot = [string]$script:QualificationTimingRoot
    $State.ProduceDiscovery = [Func[Antiphon.Nightly.NightlyDiscoveryRequest, object]]{ param($Ask)
        Invoke-NightlyProduceDiscovery -ExePath $Ask.ExePath -ArgumentPrefix @($Ask.ArgumentPrefix) -AssemblyPath $Ask.AssemblyPath -OutputPath $Ask.OutputPath `
            -DiagnosticDirectory $Ask.DiagnosticDirectory -LogPath $Ask.LogPath -WorkingDirectory $Ask.WorkingDirectory -Environment $Ask.Environment }
    if ($script:NightlySeams -and $script:NightlySeams.E2EPrerequisites) {
        $State.E2EPrerequisites = [Func[string, string, Collections.IDictionary, string, object]]{ param($Root, $Log, $Environment, $Bundle)
            @($script:NightlySeams.E2EPrerequisites.Invoke($Root, $Log, $Environment, $Bundle))[0] }
    }
    # The executed manifest is compiled (TestPhaseManifests.WriteExecuted, W2-nightly S09), into the bound writer context.
    $State.PublishObservation = [Action[Antiphon.Nightly.NightlyExecutedObservation]]{ param($Seen)
        [void][Antiphon.Nightly.TestPhaseManifests]::WriteExecuted($Seen.Verdict, $Seen.LogRoot, $Seen.TrxPath, $Seen.Suite, $Seen.Chunk, $Seen.RunId, $Seen.Attempt,
            $Seen.HostWallSeconds, $script:TestPhaseExecutedContext, (Get-TestPhasePublicationScope)) }
    # The active-chunk identity is compiled (QualificationHeartbeat.ActiveChunkWriter, W2-nightly S10).
    $State.ActiveChunk = [Antiphon.Nightly.QualificationHeartbeat]::ActiveChunkWriter([Action[string]]{ param($Text) Write-Warning -WarningAction Continue $Text })
    $State.PublicationScope = [Func[Antiphon.Nightly.TestPhasePublicationScope]]{ Get-TestPhasePublicationScope }
    $State.Warning = [Action[string]]{ param($Text) Write-Warning -WarningAction Continue $Text }
}

# CARD-2005. The run calls this to finish summary.json, the one writer (Antiphon.Nightly/NightlyRunSummary.cs since
# W2-nightly S12): D-5 keeps each suite's exclusion list once, dispatchProgress points at summary.suites, and the core
# streams the document (a full run's summary is tens of MB). The phase summary it runs when the phase plan exists is
# the compiled TestPhaseRunSummary (W2-nightly S09).
function Write-NightlyRunSummary {
    param($summary, $suiteResults, $dispatchSchedule, [string]$Sha, $completedAt, [string]$summaryPath, [string]$LogRoot)
    $phaseSummary = [Action[string]]{ param($Root) [void][Antiphon.Nightly.TestPhaseRunSummary]::Run($Root, 50, (Get-TestPhasePublicationScope)) }
    try {
        [Antiphon.Nightly.NightlyRunSummary]::Write($summary, @($suiteResults), $dispatchSchedule, $Sha, $completedAt, $summaryPath, $LogRoot,
            (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot), $phaseSummary)
    } catch { throw (Get-NightlyRefusalText $_) }
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
        $publicationContext = [Antiphon.Nightly.TestPhasePublication]::Open($LogRoot, $TimingRoot, $invocationId, ([string]$Attempt), '', $publicationSource, '', $false, $false, (Get-TestPhasePublicationScope))
        if (-not $publicationContext.Refused) { $script:TestPhaseExecutedContext = $publicationContext }
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
            # W2-nightly S13: the dispatch runs in C# over this run's state; what it changed comes back to these locals,
            # also when it fails. Its failure is thrown here as it was (the catch below records its message, as the
            # dot-sourced coordinator's error left it).
            $runState = [Antiphon.Nightly.NightlyRunState]::new()
            Set-NightlyRunStateBindings $runState
            $runState.RepoRoot = $RepoRoot; $runState.LogRoot = $LogRoot; $runState.TimingRoot = $TimingRoot; $runState.InvocationId = $invocationId
            $runState.Attempt = [string]$Attempt; $runState.Profile = $Profile; $runState.Sha = $Sha; $runState.GitRef = $GitRef
            $runState.Policy = $policy.Object; $runState.PolicyHash = $policyHash; $runState.Platform = $platform; $runState.RcEvidenceRoot = $RcEvidenceRoot
            $runState.StartedAt = $startedAt; $runState.BuildSeconds = $qualificationBuildSeconds; $runState.ProfileAware = $profileAware
            $runState.RequiredSuites = [object[]]@($requiredSuites); $runState.ChunksBySuite = $chunksBySuite; $runState.Preflight = $preflight
            $runState.ExpectedSkipRoster = [object[]]@($expectedSkipRoster); $runState.RcSkipRoster = [Antiphon.Nightly.RcSkipEntry[]]@($rcSkipRoster)
            $runState.ClassCostPath = [string]$script:NightlyClassCostPath; $runState.ClassCostTable = $script:NightlyClassCostTable
            $runState.NpmOk = $npmOk; $runState.DotnetOk = $dotnetOk; $runState.LintBlocksPipeline = $lintBlocksPipeline
            $runState.CoverageComplete = $coverageComplete; $runState.TestsPassed = $testsPassed; $runState.OverallFailed = $overallFailed
            $runState.ActiveNative = $activeNative; $runState.NativeActiveMax = $nativeActiveMax; $runState.TestsOutcome = $script:NightlyTestsOutcome
            $runState.HarnessHangReason = $harnessHangReason
            $runState.Reasons = [Collections.Generic.List[object]]::new([object[]]@($reasons))
            $runState.SuiteResults = [Collections.Generic.List[object]]::new([object[]]@($suiteResults))
            $runState.ToleratedSkipRows = [Collections.Generic.List[object]]::new([object[]]@($toleratedSkipRows))
            $runState.ExpectedSkipWarnings = [Collections.Generic.List[object]]::new([object[]]@($expectedSkipWarnings))
            $dispatchFailure = $null
            try {
                $dispatchFailure = [Antiphon.Nightly.NightlyDispatch]::CoordinateOrFailure($runState, ([Antiphon.Nightly.NightlySuiteRunner]::new($runState)).Steps(),
                    [object[]]@($selectedSuites), $null, $null, [string]$dispatchInput.Digest, $dispatchInput.History, $dispatchInput.PreviousRun, [string]$dispatchInput.FallbackReason)
            } finally {
                $coverageComplete = $runState.CoverageComplete; $testsPassed = $runState.TestsPassed; $overallFailed = $runState.OverallFailed
                $activeNative = $runState.ActiveNative; $nativeActiveMax = $runState.NativeActiveMax; $script:NightlyTestsOutcome = $runState.TestsOutcome
                $harnessHangReason = $runState.HarnessHangReason
                $reasons = @($runState.Reasons); $suiteResults = @($runState.SuiteResults)
                $toleratedSkipRows = @($runState.ToleratedSkipRows); $expectedSkipWarnings = @($runState.ExpectedSkipWarnings)
                $dispatchSchedule = $runState.DispatchSchedule; $script:NightlyClassCostTable = $runState.ClassCostTable
            }
            if ($null -ne $dispatchFailure) { throw $dispatchFailure }

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
            [void][Antiphon.Nightly.TestPhasePublication]::CompleteProjection($publicationContext, $publicationTerminal, (Get-TestPhasePublicationScope))
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
            [Antiphon.Nightly.TestPhasePublication]::Close($publicationContext)
            if ($publicationContext.Closed -and $null -ne $script:TestPhaseExecutedContext -and [string]$script:TestPhaseExecutedContext.Id -eq [string]$publicationContext.Id) { $script:TestPhaseExecutedContext = $null }
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
