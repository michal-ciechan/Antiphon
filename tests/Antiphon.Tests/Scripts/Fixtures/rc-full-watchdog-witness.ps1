#requires -Version 5.1
# CARD-1514 S3 witness. Portable clock and matrices. No live test process and no sleep.
# ASCII-only.
param()
$ErrorActionPreference = 'Stop'
$repo = [string]$args[0]
$work = [string]$args[1]
$mode = [string]$args[2]
$startedUtc = [datetime]::UtcNow.ToString('o')
$problems = New-Object System.Collections.Generic.List[string]
function Add-Problem([string]$Text) { $script:problems.Add($Text) }

$out = [ordered]@{
    exitCode = 1
    text = ''
    mode = $mode
    startedUtc = $startedUtc
    finishedUtc = ''
    heartbeatIgnored = $false
    lastCompletedUid = ''
    diagStampStable = $false
    inFlightUid = ''
    lastPhase = ''
    idleContinued = $false
    growthResetIdle = $false
    noKillToken = $false
    heartbeatParsed = $false
    killed = $true
    shortCapKilled = $false
    assignedWatchdog = 0
    hangSuspected = $false
    elapsed = 0
    g103Watchdog = 0
    g103E95 = 0
    g103Emax = 0
    g103Killed = $true
    g103FailedKept = 0
    deadlineWatchdog = 0
    deadlineKilled = $true
    deadlineFailedKept = 0
    replayWatchdog = 0
    replayKilled = $true
    paceWatchdog = 0
    paceKilled = $true
    conservativeWatchdog = 0
    observedWatchdog = 0
    unknownWatchdog = 0
    unknownKilledAt700 = $true
    stratumMismatchWatchdog = 0
    n19Watchdog = 0
    hashMismatchWatchdog = 0
    censoredWatchdog = 0
    clampAdmitted = $true
    clampWatchdog = 0
    pAtAdmitted = $false
    pAboveAdmitted = $true
    e2eWatchdog = 0
    e2eAboveAdmitted = $true
    rounding2040 = $false
    monotonic = $false
    invalidRefused = $false
    legacyWatchdog = 0
    plannerWatchdog = 0
    plannerAdmitted = $false
    policyUntouched = $false
    wired = $false
    nanRefused = $false
    suitePairingOk = $false
    sessionRunnerAtAdmitted = $false
    sessionRunnerAboveAdmitted = $true
    ptyHostAboveAdmitted = $true
    agentsPtyAboveAdmitted = $true
    markdownPdfAboveAdmitted = $true
    messagingAboveAdmitted = $true
    coverageComplete = $true
    testsPassed = $true
    incomplete = $false
    reason = ''
    partialRetained = $false
    killedAtCap = $false
    notPass = $false
}

try {
    $lib = Join-Path $repo 'scripts/lib'
    . (Join-Path $lib 'nightly-common.ps1')
    . (Join-Path $lib 'nightly-policy.ps1')
    . (Join-Path $lib 'nightly-coverage.ps1')
    . (Join-Path $lib 'nightly-tests-impl.ps1')
    . (Join-Path $lib 'qualification-supervisor-heartbeat.ps1')

    $needed = @(
        'Resolve-NightlyLaunchWatchdog',
        'Get-NightlyWatchdogErrorFactors',
        'Get-NightlyWatchdogSeconds',
        'Test-NightlyWatchdogKilled',
        'Get-NightlyWatchdogTimeoutDisposition',
        'Read-QualificationDiagnosticProgress'
    )
    $missing = @()
    foreach ($name in $needed) {
        if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { $missing += $name }
    }
    if ($missing.Count -gt 0) {
        Add-Problem ('admission-proof-absent ' + ($missing -join ','))
    } else {
        $heartbeatPath = Join-Path $lib 'qualification-supervisor-heartbeat.ps1'
        $heartbeatText = [IO.File]::ReadAllText($heartbeatPath)
        $forbidden = @('Stop-Process', 'taskkill', 'TerminateProcess', '.Kill(')
        $foundForbidden = @()
        foreach ($token in $forbidden) {
            if ($heartbeatText.IndexOf($token) -ge 0) { $foundForbidden += $token }
        }
        $out.noKillToken = ($foundForbidden.Count -eq 0)
        if ($foundForbidden.Count -gt 0) { Add-Problem ('kill-token ' + ($foundForbidden -join ',')) }

        if ($mode -eq 'progress') {
            $diag = Join-Path $work 'chunk.diag'
            $beat = Join-Path $work 'supervisor-heartbeat.json'
            $line1 = "TestNodeUid { Value = uid-done } PassedTestNodeStateProperty { Explanation = ok }`n"
            [IO.File]::WriteAllText($diag, $line1)
            $read1 = Read-QualificationDiagnosticProgress -Path $diag -Offset 0 -HeartbeatPath $beat
            $origin = [datetime]::Parse('2026-10-29T00:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
            $proc = @([pscustomobject]@{ Pid = 10; ParentPid = 1; CpuSeconds = 1.0 })
            $first = Update-QualificationSupervisorHeartbeat -NowUtc $origin -Processes $proc -ChunkRootPid 10 -Previous $null -Trx 0 -Bytes 100 -Newest 'chunk.diag' -PlannedSeconds $null -DiagnosticProgress $read1
            Write-QualificationSupervisorHeartbeat -Record $first -Path $beat
            $parsedBeat = Read-QualificationDiagnosticProgress -Path $beat -Offset 0 -HeartbeatPath $beat
            $out.heartbeatParsed = [bool]$parsedBeat.Grew
            if ($parsedBeat.Grew) { Add-Problem 'heartbeat-write-counted-as-progress' }
            $endOffset = [int64]$read1.Offset
            $reread = Read-QualificationDiagnosticProgress -Path $diag -Offset $endOffset -HeartbeatPath $beat
            $later = $origin.AddMinutes(25)
            $second = Update-QualificationSupervisorHeartbeat -NowUtc $later -Processes $proc -ChunkRootPid 10 -Previous $first -Trx 0 -Bytes 100 -Newest 'chunk.diag' -PlannedSeconds $null -DiagnosticProgress $reread
            $out.heartbeatIgnored = (-not [bool]$parsedBeat.Grew)
            $out.lastCompletedUid = [string]$second.lastCompletedUid
            $out.idleContinued = ([double]$second.idleMinutes -ge 25 -and [bool]$second.hangSuspected)
            $out.diagStampStable = ([string]$second.lastDiagGrowthAtUtc -eq [string]$first.lastDiagGrowthAtUtc -and -not [string]::IsNullOrWhiteSpace([string]$first.lastDiagGrowthAtUtc))
            if ([string]$second.lastCompletedUid -ne 'uid-done') { Add-Problem 'completed-uid-lost' }
            if (-not $out.idleContinued) { Add-Problem 'heartbeat-reset-idle' }
            if (-not $out.diagStampStable) { Add-Problem 'diag-stamp-moved' }
            $line2 = "TestNodeUid { Value = uid-live } InProgressTestNodeStateProperty { Explanation = running }`n"
            [IO.File]::AppendAllText($diag, $line2)
            $read2 = Read-QualificationDiagnosticProgress -Path $diag -Offset $endOffset -HeartbeatPath $beat
            $after = Update-QualificationSupervisorHeartbeat -NowUtc $later.AddMinutes(1) -Processes $proc -ChunkRootPid 10 -Previous $second -Trx 0 -Bytes 100 -Newest 'chunk.diag' -PlannedSeconds $null -DiagnosticProgress $read2
            $out.inFlightUid = [string]$after.inFlightUid
            $out.lastPhase = [string]$after.lastPhase
            $out.growthResetIdle = ([double]$after.idleMinutes -lt 0.001)
            if ([string]$after.inFlightUid -ne 'uid-live') { Add-Problem 'inflight-missing' }
            if ([string]$after.lastPhase -ne 'InProgress') { Add-Problem 'phase-missing' }
            if (-not $out.growthResetIdle) { Add-Problem 'diagnostic-growth-did-not-reset-idle' }
            if ([string]$after.lastDiagGrowthAtUtc -eq [string]$first.lastDiagGrowthAtUtc) { Add-Problem 'diagnostic-growth-stamp-missing' }
        } elseif ($mode -eq 'quiet') {
            $origin = [datetime]::Parse('2026-10-29T00:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
            $proc = @([pscustomobject]@{ Pid = 10; ParentPid = 1; CpuSeconds = 3.0 })
            $first = Update-QualificationSupervisorHeartbeat -NowUtc $origin -Processes $proc -ChunkRootPid 10 -Previous $null -Trx 1 -Bytes 50 -Newest 'chunk.diag' -PlannedSeconds $null
            $later = $origin.AddMinutes(35)
            $second = Update-QualificationSupervisorHeartbeat -NowUtc $later -Processes $proc -ChunkRootPid 10 -Previous $first -Trx 1 -Bytes 50 -Newest 'chunk.diag' -PlannedSeconds $null
            $assigned = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'default'
            $out.assignedWatchdog = [double]$assigned.WatchdogSeconds
            $out.hangSuspected = [bool]$second.hangSuspected
            $out.elapsed = 2100
            $out.killed = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 2100 -WatchdogSeconds ([double]$assigned.WatchdogSeconds) -Terminal $false)
            $out.shortCapKilled = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 2100 -WatchdogSeconds 2100 -Terminal $false)
            if ($out.killed) { Add-Problem 'quiet-healthy-killed' }
            if (-not $out.shortCapKilled) { Add-Problem 'short-cap-predicate-stuck-false' }
            if ([double]$assigned.WatchdogSeconds -ne 3600) { Add-Problem 'assigned-watchdog-is-not-the-suite-cap' }
            if (-not $second.hangSuspected) { Add-Problem 'hang-not-recorded' }
            if ([int]$assigned.WatchdogSeconds -ge 1860 -and [int]$assigned.WatchdogSeconds -le 2100) { Add-Problem 'thirty-one-minute-cap' }
        } elseif ($mode -eq 'cap') {
            $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
            $hasCal = $policy.Object.PSObject.Properties.Name -contains 'watchdogCalibration'
            $out.policyUntouched = ((-not $hasCal) -and [int]$policy.Object.watchdogs.antiphon -eq 3600000 -and [int]$policy.Object.watchdogs.e2e -eq 7200000)
            if (-not $out.policyUntouched) { Add-Problem 'policy-watchdog-changed' }
            $impl = [IO.File]::ReadAllText((Join-Path $lib 'nightly-tests-impl.ps1'))
            $refuseAt = $impl.IndexOf('watchdog-admission-refused')
            $timeoutAt = $impl.IndexOf('admission.WatchdogMilliseconds')
            $out.wired = ($refuseAt -ge 0 -and $timeoutAt -gt $refuseAt -and $impl.Contains('Get-NightlyWatchdogTimeoutDisposition'))
            if (-not $out.wired) { Add-Problem 'watchdog-not-paired' }

            function New-Walls([int]$Count, [double]$Wall, [string]$Outcome) {
                $rows = New-Object System.Collections.Generic.List[object]
                for ($i = 0; $i -lt $Count; $i++) {
                    $rows.Add([pscustomobject]@{ Wall = $Wall; Outcome = $Outcome; Censored = $false })
                }
                return $rows
            }
            function New-Stratum {
                return [pscustomobject]@{
                    suite = 'antiphon'; uidDigest = 'uid'; costDigest = 'cost'; executionDigest = 'exec'
                    assemblyDigest = 'asm'; os = 'linux'; runtime = 'net9'; host = 'host'
                    loadBand = 'quiet'; fixtureMode = 'cache'; concurrency = '1'
                }
            }
            $stratum = New-Stratum
            $g103Samples = @(New-Walls 19 900 'Passed') + @([pscustomobject]@{ Wall = 1936.0; Outcome = 'Failed'; Censored = $false; FailureCount = 1 })
            $g103Factors = Get-NightlyWatchdogErrorFactors -Samples $g103Samples -PlannedSeconds 900
            $out.g103E95 = [double]$g103Factors.E95
            $out.g103Emax = [double]$g103Factors.Emax
            $out.g103FailedKept = [int]$g103Factors.FailedKept
            $g103 = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = [int]$g103Factors.N; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = [double]$g103Factors.E95; Emax = [double]$g103Factors.Emax
                M = 60.0; C = 900.0; Wmax = 2040.0; Censored = [bool]$g103Factors.Censored
            })
            $out.g103Watchdog = [double]$g103.WatchdogSeconds
            $out.g103Killed = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 1936 -WatchdogSeconds ([double]$g103.WatchdogSeconds) -Terminal $true)
            if ([int]$g103Factors.FailedKept -ne 1) { Add-Problem 'failed-wall-dropped' }
            if ([double]$g103.WatchdogSeconds -ne 2040) { Add-Problem ('g103-watchdog ' + $g103.WatchdogSeconds) }
            if ($out.g103Killed) { Add-Problem 'g103-killed' }
            if (-not $g103.Admitted) { Add-Problem 'g103-not-admitted' }

            $deadlineSamples = @(New-Walls 19 120 'Passed') + @([pscustomobject]@{ Wall = 900.0; Outcome = 'Failed'; Censored = $false; FailureCount = 3 })
            $deadlineFactors = Get-NightlyWatchdogErrorFactors -Samples $deadlineSamples -PlannedSeconds 120
            $out.deadlineFailedKept = [int]$deadlineFactors.FailedKept
            $deadline = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 120 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = [int]$deadlineFactors.N; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = [double]$deadlineFactors.E95; Emax = [double]$deadlineFactors.Emax
                M = 60.0; C = 120.0; Wmax = 960.0; Censored = $false
            })
            $out.deadlineWatchdog = [double]$deadline.WatchdogSeconds
            $out.deadlineKilled = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 900 -WatchdogSeconds ([double]$deadline.WatchdogSeconds) -Terminal $true)
            if ([double]$deadline.WatchdogSeconds -ne 960) { Add-Problem ('deadline-watchdog ' + $deadline.WatchdogSeconds) }
            if ($out.deadlineKilled) { Add-Problem 'deadline-failures-killed' }
            if ([int]$deadlineFactors.FailedKept -ne 1) { Add-Problem 'deadline-failure-dropped' }

            $replay = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'default' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = 1.0; Emax = (1936.0 / 900.0); M = 60.0; C = 900.0; Wmax = 2040.0; Censored = $false
            })
            $out.replayWatchdog = [double]$replay.WatchdogSeconds
            $out.replayKilled = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 1936 -WatchdogSeconds ([double]$replay.WatchdogSeconds) -Terminal $true)
            if ([double]$replay.WatchdogSeconds -ne 3600 -or [string]$replay.Reason -ne 'unmeasured-cost') { Add-Problem 'unmeasured-replay-not-3600' }
            if ($out.replayKilled) { Add-Problem 'unmeasured-replay-killed' }

            foreach ($basisName in @('pace', 'default', 'conservative-bound', 'observed-bound')) {
                $row = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 50 -Basis $basisName -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                    N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = 1.0; Emax = 1.0; M = 60.0; C = 2400.0; Wmax = 4800.0; Censored = $false
                })
                $killedPace = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 920 -WatchdogSeconds ([double]$row.WatchdogSeconds) -Terminal $true)
                if ($basisName -eq 'pace') { $out.paceWatchdog = [double]$row.WatchdogSeconds; $out.paceKilled = $killedPace }
                if ($basisName -eq 'conservative-bound') { $out.conservativeWatchdog = [double]$row.WatchdogSeconds }
                if ($basisName -eq 'observed-bound') { $out.observedWatchdog = [double]$row.WatchdogSeconds }
                if ([double]$row.WatchdogSeconds -ne 3600 -or $killedPace -or [string]$row.Reason -ne 'unmeasured-cost') {
                    Add-Problem ('basis-reduced ' + $basisName + ' ' + $row.WatchdogSeconds)
                }
            }
            $unknown = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 50 -Basis 'default'
            $out.unknownWatchdog = [double]$unknown.WatchdogSeconds
            $out.unknownKilledAt700 = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 700 -WatchdogSeconds ([double]$unknown.WatchdogSeconds) -Terminal $false)
            if ([double]$unknown.WatchdogSeconds -ne 3600) { Add-Problem 'unknown-cost-not-3600' }
            if ($out.unknownKilledAt700) { Add-Problem 'unknown-cost-used-600' }

            $mismatch = 0.0
            foreach ($key in @('suite', 'uidDigest', 'costDigest', 'executionDigest', 'assemblyDigest', 'os', 'runtime', 'host', 'loadBand', 'fixtureMode', 'concurrency')) {
                $flipped = New-Stratum
                $flipped.$key = 'other'
                $row = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $flipped -Calibration ([pscustomobject]@{
                    N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = [double]$g103Factors.E95; Emax = [double]$g103Factors.Emax
                    M = 60.0; C = 900.0; Wmax = 2040.0; Censored = $false
                })
                if ([double]$row.WatchdogSeconds -ne 3600) { Add-Problem ('stratum-mismatch-applied ' + $key) }
                $mismatch = [double]$row.WatchdogSeconds
            }
            $out.stratumMismatchWatchdog = $mismatch

            $n19Samples = @(New-Walls 19 900 'Passed')
            $n19Factors = Get-NightlyWatchdogErrorFactors -Samples $n19Samples -PlannedSeconds 900
            $n19 = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = [int]$n19Factors.N; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = 1.0; Emax = 1.0; M = 60.0; C = 900.0; Wmax = 2040.0; Censored = $false
            })
            $out.n19Watchdog = [double]$n19.WatchdogSeconds
            if ([double]$n19.WatchdogSeconds -ne 3600) { Add-Problem 'n19-reduced' }

            $hashRow = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-b' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = [double]$g103Factors.E95; Emax = [double]$g103Factors.Emax
                M = 60.0; C = 900.0; Wmax = 2040.0; Censored = $false
            })
            $out.hashMismatchWatchdog = [double]$hashRow.WatchdogSeconds
            if ([double]$hashRow.WatchdogSeconds -ne 3600) { Add-Problem 'hash-mismatch-applied' }

            $censoredSamples = @(New-Walls 20 900 'Passed') + @([pscustomobject]@{ Wall = 1936.0; Outcome = 'Failed'; Censored = $true })
            $censoredFactors = Get-NightlyWatchdogErrorFactors -Samples $censoredSamples -PlannedSeconds 900
            $censored = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = 1.0; Emax = 1.0; M = 60.0; C = 900.0; Wmax = 2040.0; Censored = [bool]$censoredFactors.Censored
            })
            $out.censoredWatchdog = [double]$censored.WatchdogSeconds
            if (-not $censoredFactors.Censored -or [double]$censored.WatchdogSeconds -ne 3600) { Add-Problem 'censored-wall-reduced' }

            $clamp = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 900 -Basis 'measured' -PolicyHash 'hash-a' -LaunchStratum $stratum -Calibration ([pscustomobject]@{
                N = 20; BoundPolicyHash = 'hash-a'; Stratum = $stratum; E95 = [double]$g103Factors.E95; Emax = [double]$g103Factors.Emax
                M = 60.0; C = 900.0; Wmax = 1800.0; Censored = $false
            })
            $out.clampAdmitted = [bool]$clamp.Admitted
            $out.clampWatchdog = [double]$clamp.WatchdogSeconds
            if ($clamp.Admitted -or [double]$clamp.WatchdogSeconds -eq 1800) { Add-Problem 'watchdog-clamped-down' }
            if ([string]$clamp.Reason -ne 'policy-max-mismatch') { Add-Problem 'policy-max-reason' }

            $at = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 2400 -Basis 'default'
            $above = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds 2401 -Basis 'default'
            $e2eAt = Resolve-NightlyLaunchWatchdog -SuiteId 'e2e' -PlannedSeconds 1800 -Basis 'default'
            $e2eAbove = Resolve-NightlyLaunchWatchdog -SuiteId 'e2e' -PlannedSeconds 1801 -Basis 'default'
            $legacy = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds $null -Basis 'default'
            $nan = Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon' -PlannedSeconds ([double]::NaN) -Basis 'default'
            $out.pAtAdmitted = [bool]$at.Admitted
            $out.pAboveAdmitted = [bool]$above.Admitted
            $out.e2eWatchdog = [double]$e2eAt.WatchdogSeconds
            $out.e2eAboveAdmitted = [bool]$e2eAbove.Admitted
            $out.legacyWatchdog = [double]$legacy.WatchdogSeconds
            $out.nanRefused = -not [bool]$nan.Admitted
            $out.invalidRefused = $out.nanRefused
            if (-not $at.Admitted -or [double]$at.WatchdogSeconds -ne 3600) { Add-Problem 'ceiling-at-refused' }
            if ($above.Admitted) { Add-Problem 'ceiling-above-admitted' }
            if (-not $e2eAt.Admitted -or [double]$e2eAt.WatchdogSeconds -ne 7200) { Add-Problem 'e2e-ceiling' }
            if ($e2eAbove.Admitted) { Add-Problem 'e2e-above-admitted' }
            if (-not $legacy.Admitted -or [double]$legacy.WatchdogSeconds -ne 3600) { Add-Problem 'legacy-estimate-refused' }
            if ($nan.Admitted) { Add-Problem 'nan-admitted' }

            $suiteIds = @($policy.Object.watchdogs.PSObject.Properties.Name | Sort-Object)
            $suitePairingOk = $true
            foreach ($suiteName in $suiteIds) {
                $capSeconds = Get-NightlySuiteWatchdogSeconds -PolicyObject $policy.Object -SuiteId $suiteName
                $ceiling = Get-NightlyLaunchEstimateCeilingSeconds -SuiteId $suiteName
                $atPlan = $capSeconds
                if ($ceiling -lt $atPlan) { $atPlan = $ceiling }
                $rowAt = Resolve-NightlyLaunchWatchdog -SuiteId $suiteName -PlannedSeconds $atPlan -Basis 'default' -PolicyObject $policy.Object
                if (-not $rowAt.Admitted -or [double]$rowAt.WatchdogSeconds -ne $capSeconds) {
                    Add-Problem ('suite-watchdog-pairing ' + $suiteName + ' at=' + $atPlan + ' W=' + $rowAt.WatchdogSeconds + ' adm=' + $rowAt.Admitted)
                    $suitePairingOk = $false
                }
                $rowOver = Resolve-NightlyLaunchWatchdog -SuiteId $suiteName -PlannedSeconds ($capSeconds + 1) -Basis 'default' -PolicyObject $policy.Object
                if ($rowOver.Admitted) {
                    Add-Problem ('suite-above-watchdog-admitted ' + $suiteName + ' P=' + ($capSeconds + 1) + ' W=' + $rowOver.WatchdogSeconds)
                    $suitePairingOk = $false
                }
            }
            $out.suitePairingOk = $suitePairingOk
            $sessionAt = Resolve-NightlyLaunchWatchdog -SuiteId 'session-runner' -PlannedSeconds 1800 -Basis 'default' -PolicyObject $policy.Object
            $sessionOver = Resolve-NightlyLaunchWatchdog -SuiteId 'session-runner' -PlannedSeconds 2400 -Basis 'default' -PolicyObject $policy.Object
            $out.sessionRunnerAtAdmitted = [bool]$sessionAt.Admitted
            $out.sessionRunnerAboveAdmitted = [bool]$sessionOver.Admitted
            if (-not $sessionAt.Admitted -or [double]$sessionAt.WatchdogSeconds -ne 1800) { Add-Problem ('session-runner-at-cap W=' + $sessionAt.WatchdogSeconds) }
            if ($sessionOver.Admitted) { Add-Problem ('session-runner-2400-admitted W=' + $sessionOver.WatchdogSeconds) }
            $namedOver = @{
                'pty-host' = 'ptyHostAboveAdmitted'
                'agents-pty' = 'agentsPtyAboveAdmitted'
                'markdown-pdf' = 'markdownPdfAboveAdmitted'
                'messaging' = 'messagingAboveAdmitted'
            }
            foreach ($suiteName in @($namedOver.Keys)) {
                $row = Resolve-NightlyLaunchWatchdog -SuiteId $suiteName -PlannedSeconds 1800 -Basis 'default' -PolicyObject $policy.Object
                $out[$namedOver[$suiteName]] = [bool]$row.Admitted
                if ($row.Admitted) { Add-Problem ($suiteName + '-1800-admitted W=' + $row.WatchdogSeconds) }
            }

            $rounded = Get-NightlyWatchdogSeconds -E95 1 -Emax (1936.0 / 900.0) -PlannedSeconds 900 -MarginSeconds 60
            $low = Get-NightlyWatchdogSeconds -E95 1 -Emax (1936.0 / 900.0) -PlannedSeconds 100 -MarginSeconds 60
            $out.rounding2040 = ([double]$rounded -eq 2040)
            $out.monotonic = ([double]$low -le [double]$rounded)
            if (-not $out.rounding2040) { Add-Problem ('rounding ' + $rounded) }
            if (-not $out.monotonic) { Add-Problem 'not-monotone' }

            $script:NightlyClassCostTable = @{ 'Antiphon.Tests.Application.BoundTests' = 2400.0 }
            $node = [pscustomobject]@{
                ClassName = 'Antiphon.Tests.Application.BoundTests'
                Namespace = 'Antiphon.Tests.Application'
                Type = 'BoundTests'
                Method = 'Run'
                Uid = 'bound'
                Categories = @('Integration')
                Excluded = $false
            }
            $plan = @(Get-NightlyProfileLaunchPlan @($node) @([pscustomobject]@{ id = 'all'; classes = @() }) @())
            if ($plan.Count -ne 1 -or $plan[0].PSObject.Properties.Name -notcontains 'watchdogSeconds') {
                Add-Problem 'planner-watchdog-unpaired'
            } else {
                $out.plannerWatchdog = [double]$plan[0].watchdogSeconds
                $out.plannerAdmitted = [bool]$plan[0].watchdogAdmitted
                if ([double]$plan[0].watchdogSeconds -ne 3600 -or -not [bool]$plan[0].watchdogAdmitted) {
                    Add-Problem 'planner-watchdog-short'
                }
            }
        } elseif ($mode -eq 'timeout') {
            $partial = Join-Path $work 'partial.diag'
            [IO.File]::WriteAllText($partial, "TestNodeUid { Value = uid-partial } InProgressTestNodeStateProperty { Explanation = running }`n")
            $disp = Get-NightlyWatchdogTimeoutDisposition -TrxPresent $false -TerminalAccounting $false
            $out.coverageComplete = [bool]$disp.CoverageComplete
            $out.testsPassed = [bool]$disp.TestsPassed
            $out.incomplete = [bool]$disp.Incomplete
            $out.reason = [string]$disp.Reason
            $out.notPass = (-not [bool]$disp.Pass)
            $out.partialRetained = [IO.File]::Exists($partial)
            $out.killedAtCap = [bool](Test-NightlyWatchdogKilled -ElapsedSeconds 3600 -WatchdogSeconds 3600 -Terminal $false)
            if ($disp.CoverageComplete -or $disp.TestsPassed -or -not $disp.Incomplete -or [string]$disp.Reason -ne 'watchdog-timeout' -or $disp.Pass) {
                Add-Problem 'timeout-marked-complete'
            }
            if (-not $out.partialRetained) { Add-Problem 'partial-diagnostic-deleted' }
            if (-not $out.killedAtCap) { Add-Problem 'cap-did-not-fire' }
        } else {
            Add-Problem 'unknown-mode'
        }
    }
} catch {
    Add-Problem ([string]$_.Exception.Message)
}

if ($problems.Count -eq 0) { $out.exitCode = 0 }
$out.text = [string]($problems -join '; ')
$out.finishedUtc = [datetime]::UtcNow.ToString('o')
$json = $out | ConvertTo-Json -Depth 6 -Compress
[IO.File]::WriteAllText((Join-Path $work 'observation.json'), $json)
exit 0
