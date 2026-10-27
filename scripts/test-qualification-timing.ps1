#requires -Version 7.0
# Qualification timing retention. ASCII-only.
# A blank timing root, a nightly chunk, a chunk with no TRX, and a timed-out
# chunk must all leave a sidecar and a summary table.
$ErrorActionPreference = 'Stop'
$failed = 0

function Assert-Check {
    param([bool]$Condition, [string]$Name)
    if ($Condition) {
        Write-Host ('PASS ' + $Name)
        return
    }
    Write-Host ('FAIL ' + $Name)
    $script:failed++
}

$repo = Split-Path -Parent $PSScriptRoot
$lib = Join-Path $repo 'scripts/lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'release-selection.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')

Assert-Check ([bool](Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue)) 'resolver-exists'
Assert-Check ([bool](Get-Command Update-QualificationChunkTiming -ErrorAction SilentlyContinue)) 'chunk-writer-exists'
Assert-Check ([bool](Get-Command Get-QualificationTimingReport -ErrorAction SilentlyContinue)) 'report-exists'

if ((Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue) -and
    (Get-Command Update-QualificationChunkTiming -ErrorAction SilentlyContinue) -and
    (Get-Command Get-QualificationTimingReport -ErrorAction SilentlyContinue)) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $resolved = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot $root -RunId 'run-1'
        Assert-Check (-not [string]::IsNullOrWhiteSpace([string]$resolved)) 'blank-root-resolved'
        Assert-Check ($resolved.Replace('\', '/').EndsWith('timing-run-1') -or $resolved.Replace('\', '/').EndsWith('/timing')) 'blank-root-retained'
        $kept = Resolve-QualificationTimingRoot -TimingRoot (Join-Path $root 'explicit') -LogRoot $root -RunId 'run-1'
        Assert-Check ($kept -eq (Join-Path $root 'explicit')) 'explicit-root-kept'

        $envMap = @{}
        Enable-TestPhaseExecutionEnvironment -Environment $envMap -LogRoot $root -RunId 'run-1' -GroupId 'g1' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot $resolved
        Assert-Check ($envMap['ANTIPHON_TEST_PHASE_ROOT'] -eq $resolved) 'nightly-profile-enables-phase-root'
        $buildEnv = @{ ANTIPHON_TEST_PHASE_ROOT = $resolved; ANTIPHON_TEST_PHASE_RUN_ID = 'run-1' }
        Remove-TestPhaseVariables -Environment $buildEnv
        Assert-Check (-not $buildEnv.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')) 'build-child-strips-phase-root'

        $log = Join-Path $root 'logs'
        New-Item -ItemType Directory -Path $log -Force | Out-Null
        $timing = Join-Path $log 'timing'
        Update-QualificationChunkTiming -LogRoot $log -TimingRoot $timing -Suite 'antiphon' -Chunk 'g1' -LastPhase 'run' -StartedAt '2026-10-27T00:00:00.0000000Z' -QueueWaitSeconds 1.5 -BuildSeconds 2 -DiscoverySeconds 0.25
        $side = Join-Path $log 'antiphon-g1.timing.json'
        Assert-Check (Test-Path -LiteralPath $side) 'sidecar-beside-trx-slot'
        $heart = Get-Content -LiteralPath $side -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Check ([string]$heart.lastPhase -eq 'run') 'heartbeat-last-phase'
        Assert-Check (-not [string]::IsNullOrWhiteSpace([string]$heart.lastPhaseUtc)) 'heartbeat-utc'
        Update-QualificationChunkTiming -LogRoot $log -TimingRoot $timing -Suite 'antiphon' -Chunk 'g1' -LastPhase 'timeout' -Why 'timeout' -RunWallSeconds 30 -Terminal
        $dead = Get-Content -LiteralPath $side -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Check ([string]$dead.lastPhase -eq 'timeout') 'timeout-keeps-last-phase'
        Assert-Check ([string]$dead.why -eq 'timeout') 'timeout-why'
        $keptStart = [string]$dead.startedAt
        if ($dead.startedAt -is [datetime]) { $keptStart = $dead.startedAt.ToUniversalTime().ToString('o') }
        Assert-Check ($keptStart -eq '2026-10-27T00:00:00.0000000Z') 'timeout-keeps-start'
        Assert-Check ([IO.File]::ReadAllText($side).Contains('2026-10-27T00:00:00.0000000Z')) 'timeout-keeps-start-text'
        Assert-Check ([bool]$dead.trxMissing) 'timeout-without-trx'

        $trx = Join-Path $log 'antiphon-g2.trx'
        $trxXml = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun><Results>
<UnitTestResult testId="a" testName="Slow" duration="00:00:03" outcome="Passed" />
<UnitTestResult testId="b" testName="Mid" duration="00:00:02" outcome="Passed" />
<UnitTestResult testId="c" testName="Fast" duration="00:00:01" outcome="Passed" />
</Results></TestRun>
'@
        [IO.File]::WriteAllText($trx, $trxXml)
        Update-QualificationChunkTiming -LogRoot $log -TimingRoot $timing -Suite 'antiphon' -Chunk 'g2' -TrxPath $trx -LastPhase 'complete' -StartedAt '2026-10-27T00:01:00.0000000Z' -CompletedAt '2026-10-27T00:02:00.0000000Z' -QueueWaitSeconds 0.1 -BuildSeconds 4 -DiscoverySeconds 0.2 -RunWallSeconds 9 -Terminal
        $report = Get-QualificationTimingReport -LogRoot $log -TimingRoot $timing
        Assert-Check ([string]$report.TimingRoot -eq $timing) 'report-timing-root'
        $g2 = @($report.Chunks | Where-Object { [string]$_.chunk -eq 'g2' })
        Assert-Check ($g2.Count -eq 1) 'report-has-trx-chunk'
        if ($g2.Count -eq 1) {
            Assert-Check ([math]::Abs([double]$g2[0].p50Seconds - 2) -lt 0.001) 'p50'
            Assert-Check ([math]::Abs([double]$g2[0].p95Seconds - 3) -lt 0.001) 'p95'
            Assert-Check ([math]::Abs([double]$g2[0].maxSeconds - 3) -lt 0.001) 'max'
            Assert-Check (@($g2[0].slowest).Count -eq 3) 'slowest-three'
            Assert-Check ([string]@($g2[0].slowest)[0].name -eq 'Slow') 'slowest-order'
        }
        $line = @($report.Timeline | Where-Object { [string]$_.suite -eq 'antiphon' })
        Assert-Check ($line.Count -eq 1) 'suite-timeline'
        if ($line.Count -eq 1) {
            Assert-Check ([string]$line[0].startedAt -eq '2026-10-27T00:00:00.0000000Z') 'timeline-start'
            Assert-Check ([string]$line[0].completedAt -eq '2026-10-27T00:02:00.0000000Z') 'timeline-end'
        }
        $missing = @($report.Chunks | Where-Object { [string]$_.chunk -eq 'g1' })
        Assert-Check ($missing.Count -eq 1 -and [string]$missing[0].why -eq 'timeout') 'no-trx-row-kept'
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Wrapper path: nightly profile, one native chunk, seam instead of Windows job custody.
$wrap = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-wrap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $wrap -Force | Out-Null
$seamPath = Join-Path $wrap 'seams.ps1'
$captures = Join-Path $wrap 'launches.txt'
$seamText = @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join ' '
        $phase = ''
        if ($Environment -and $Environment.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')) { $phase = [string]$Environment['ANTIPHON_TEST_PHASE_ROOT'] }
        Add-Content -LiteralPath $env:QTIMING_CAPTURES -Value ($FilePath + ' | ' + $argsText + ' | ' + $phase) -Encoding ascii
        $trxName = ''
        $results = ''
        for ($i = 0; $i -lt @($ArgumentList).Count; $i++) {
            if ([string]$ArgumentList[$i] -eq '--report-trx-filename') { $trxName = [string]$ArgumentList[$i + 1] }
            if ([string]$ArgumentList[$i] -eq '--results-directory') { $results = [string]$ArgumentList[$i + 1] }
        }
        if ($trxName -and $results -and ($argsText -notmatch '--list-tests')) {
            $trxPath = Join-Path $results $trxName
            $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun><Results>
<UnitTestResult testId="a" testName="Slow" duration="00:00:03" outcome="Passed" />
<UnitTestResult testId="b" testName="Mid" duration="00:00:02" outcome="Passed" />
<UnitTestResult testId="c" testName="Fast" duration="00:00:01" outcome="Passed" />
</Results></TestRun>
"@
            [IO.File]::WriteAllText($trxPath, $xml)
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
    }
}
'@
[IO.File]::WriteAllText($seamPath, $seamText)
$env:QTIMING_CAPTURES = $captures
$logRoot = Join-Path $wrap 'run'
try {
    $result = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $logRoot -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'timing-wrap' -Trigger 'manual' -SeamsPath $seamPath -AllowSharedTree
    $summaryPath = Join-Path $logRoot 'summary.json'
    Assert-Check (Test-Path -LiteralPath $summaryPath) 'wrapper-summary-exists'
    if (Test-Path -LiteralPath $summaryPath) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Check (-not [string]::IsNullOrWhiteSpace([string]$summary.timingRoot)) 'wrapper-summary-timing-root'
        $chunks = @()
        if ($summary.PSObject.Properties.Name -contains 'timingChunks' -and $null -ne $summary.timingChunks) {
            $chunks = @($summary.timingChunks)
        }
        Assert-Check ($chunks.Count -ge 1) 'wrapper-chunk-table'
        $native = @($chunks | Where-Object { [string]$_.suite -eq 'antiphon' -and [string]$_.chunk -ne 'unstarted' })
        Assert-Check ($native.Count -ge 1) 'wrapper-native-row'
        if ($native.Count -ge 1) {
            Assert-Check ($null -ne $native[0].queueWaitSeconds) 'wrapper-queue'
            Assert-Check ($null -ne $native[0].buildSeconds) 'wrapper-build'
            Assert-Check ($null -ne $native[0].runWallSeconds) 'wrapper-run-wall'
            Assert-Check ($null -ne $native[0].p50Seconds) 'wrapper-p50'
            Assert-Check (@($native[0].slowest).Count -ge 1) 'wrapper-slowest'
            $sideName = '{0}-{1}.timing.json' -f $native[0].suite, $native[0].chunk
            Assert-Check (Test-Path -LiteralPath (Join-Path $logRoot $sideName)) 'wrapper-sidecar-beside-trx'
        }
        $timeline = @($summary.suiteTimeline)
        Assert-Check ($timeline.Count -ge 1 -and -not [string]::IsNullOrWhiteSpace([string]$timeline[0].startedAt)) 'wrapper-suite-timeline'
    }
    $launchText = ''
    if (Test-Path -LiteralPath $captures) { $launchText = [IO.File]::ReadAllText($captures) }
    $launches = @()
    foreach ($line in @($launchText -split "`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split ' \| ', 3
        $phase = ''
        if ($parts.Count -gt 2) { $phase = [string]$parts[2] }
        $launches += [pscustomobject]@{ File = [string]$parts[0]; Args = [string]$parts[1]; Phase = $phase.Trim() }
    }
    Assert-Check ($launches.Count -ge 1) 'wrapper-recorded-launches'
    $enabled = @($launches | Where-Object { $_.Args -match '--report-trx' -and $_.Args -notmatch '--list-tests' -and -not [string]::IsNullOrWhiteSpace($_.Phase) })
    Assert-Check ($enabled.Count -ge 1) 'wrapper-native-child-has-phase-root'
    $buildLines = @($launches | Where-Object { $_.Args -match 'build Antiphon.sln' })
    $buildLeaked = @($buildLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Phase) })
    Assert-Check ($buildLines.Count -ge 1 -and $buildLeaked.Count -eq 0) 'wrapper-build-has-no-phase-root'
    Assert-Check ($null -ne $result) 'wrapper-returned'
} catch {
    Write-Host ('FAIL wrapper-threw ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item Env:QTIMING_CAPTURES -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $wrap -Recurse -Force -ErrorAction SilentlyContinue
}

# F1: Linux process-group custody. A root that exits must not report cleanup
# while a descendant is alive, a timeout must join the group, and a writer
# that left the group must fail the drain without hanging.
# The wrapper above installs a StartProcess seam. Real custody must not use it.
$script:NightlySeams = $null
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    $own = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-own-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $own -Force | Out-Null
    try {
        $pidFile = Join-Path $own 'child.pid'
        $ownWatch = [Diagnostics.Stopwatch]::StartNew()
        $owned = Invoke-NightlyOwnedProcess -FilePath '/bin/sh' -ArgumentList @('-c', 'sleep 30 >/dev/null 2>&1 & echo $! > "$1"', 'owned', $pidFile) -WorkingDirectory $own -TimeoutMilliseconds 2000 -LogPath (Join-Path $own 'root.log')
        $ownWatch.Stop()
        $childAlive = $false
        if (Test-Path -LiteralPath $pidFile) {
            $childId = 0
            if ([int]::TryParse(([IO.File]::ReadAllText($pidFile).Trim()), [ref]$childId)) {
                $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
                $childAlive = ($null -ne $child -and -not $child.HasExited)
                if ($childAlive) { try { $child.Kill() } catch { } }
            }
        }
        Assert-Check ((-not $childAlive) -and [bool]$owned.CleanupComplete -and -not [bool]$owned.TimedOut -and $ownWatch.Elapsed.TotalSeconds -lt 10) 'linux-root-exit-joins-descendants'

        $timeWatch = [Diagnostics.Stopwatch]::StartNew()
        $timed = Invoke-NightlyOwnedProcess -FilePath '/bin/sh' -ArgumentList @('-c', 'sleep 30 & wait') -WorkingDirectory $own -TimeoutMilliseconds 400 -LogPath (Join-Path $own 'timeout.log')
        $timeWatch.Stop()
        Assert-Check ([bool]$timed.TimedOut -and [bool]$timed.CleanupComplete -and [bool]$timed.ChildrenExited -and $timeWatch.Elapsed.TotalSeconds -lt 12) 'linux-timeout-joins-group'

        $holder = Join-Path $own 'holder.pid'
        $retain = Join-Path $own 'retain.sh'
        $retainBody = "#!/bin/sh`nsetsid sh -c 'echo `$`$ > ""$holder""; exec sleep 30' &`necho ROOT-DONE`nexit 0`n"
        [IO.File]::WriteAllText($retain, $retainBody.Replace("`r", ''))
        $retainWatch = [Diagnostics.Stopwatch]::StartNew()
        $retained = Invoke-NightlyOwnedProcess -FilePath '/bin/sh' -ArgumentList @($retain) -WorkingDirectory $own -TimeoutMilliseconds 2000 -LogPath (Join-Path $own 'retain.log')
        $retainWatch.Stop()
        $holderAlive = $false
        if (Test-Path -LiteralPath $holder) {
            $holderId = 0
            if ([int]::TryParse(([IO.File]::ReadAllText($holder).Trim()), [ref]$holderId)) {
                $holderProc = Get-Process -Id $holderId -ErrorAction SilentlyContinue
                $holderAlive = ($null -ne $holderProc -and -not $holderProc.HasExited)
                if ($holderAlive) { try { $holderProc.Kill() } catch { } }
            }
        }
        Assert-Check ((-not [bool]$retained.CleanupComplete) -and $retainWatch.Elapsed.TotalSeconds -lt 12) 'linux-retained-writer-drain-is-incomplete'
    } finally {
        Remove-Item -LiteralPath $own -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# F4: one explicit timing root overrides an inherited phase root.
$phaseRootBefore = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
try {
    $envRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-env-' + [guid]::NewGuid().ToString('N'))
    $explicitRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-explicit-' + [guid]::NewGuid().ToString('N'))
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $envRoot)
    $forced = @{}
    Enable-TestPhaseExecutionEnvironment -Environment $forced -LogRoot $envRoot -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot $explicitRoot
    Assert-Check ($forced['ANTIPHON_TEST_PHASE_ROOT'] -eq $explicitRoot) 'explicit-timing-root-overrides-environment'
    $blankMap = @{}
    Enable-TestPhaseExecutionEnvironment -Environment $blankMap -LogRoot $envRoot -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot ''
    Assert-Check ($blankMap['ANTIPHON_TEST_PHASE_ROOT'] -eq $envRoot) 'blank-timing-root-uses-environment'
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', '   ')
    $absentMap = @{}
    $absentLog = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-absent-' + [guid]::NewGuid().ToString('N'))
    Enable-TestPhaseExecutionEnvironment -Environment $absentMap -LogRoot $absentLog -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot ''
    Assert-Check ($absentMap['ANTIPHON_TEST_PHASE_ROOT'] -eq (Join-Path $absentLog 'timing')) 'absent-timing-root-uses-log'
} finally {
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $phaseRootBefore)
}

# F5: shared fixtures count once; another run's rows do not; unobserved phases stay null.
$phaseDir = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-phase-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $phaseDir -Force | Out-Null
try {
    $currentLines = @(
        '{"recordKind":"header","version":1,"pid":11,"nonce":"abc","runId":"new-run","groupId":"group-1","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests"}',
        '{"recordKind":"fixture","fixtureId":"fix-1","phases":[{"phase":"fixture-setup","startOffsetMs":0,"endOffsetMs":1000}]}',
        '{"recordKind":"complete","phases":[{"phase":"fixture-setup","startOffsetMs":0,"endOffsetMs":500}]}'
    )
    [IO.File]::WriteAllLines((Join-Path $phaseDir 'cases-11-abc.jsonl'), $currentLines)
    [IO.File]::WriteAllLines((Join-Path $phaseDir 'cases-13-ghi.jsonl'), @(
        '{"recordKind":"header","version":1,"pid":13,"nonce":"ghi","runId":"new-run","groupId":"group-1","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests"}',
        '{"recordKind":"fixture","fixtureId":"fix-1","phases":[{"phase":"fixture-setup","startOffsetMs":0,"endOffsetMs":1000}]}'
    ))
    [IO.File]::WriteAllLines((Join-Path $phaseDir 'cases-12-def.jsonl'), @(
        '{"recordKind":"header","version":1,"pid":12,"nonce":"def","runId":"old-run","groupId":"group-1","attempt":"0","suite":"other","assembly":"Other"}',
        '{"recordKind":"complete","phases":[{"phase":"process-wait","startOffsetMs":0,"endOffsetMs":9000}]}'
    ))
    $sums = $null
    $sumError = ''
    try {
        $sums = Get-QualificationPhaseSums -TimingRoot $phaseDir -Chunk 'group-1' -RunId 'new-run' -Suite 'antiphon' -Attempt '1'
    } catch {
        $sumError = $_.Exception.Message
    }
    $fixtureOk = $false
    $waitNull = $false
    $spawnNull = $false
    $tearNull = $false
    if ($null -ne $sums) {
        $fixtureOk = ($null -ne $sums.fixtureSetupSeconds -and [math]::Abs([double]$sums.fixtureSetupSeconds - 1.0) -lt 0.001)
        $waitNull = ($null -eq $sums.processWaitSeconds)
        $spawnNull = ($null -eq $sums.processSpawnSeconds)
        $tearNull = ($null -eq $sums.fixtureTeardownSeconds)
    }
    Assert-Check ($sumError -eq '' -and $fixtureOk) 'shared-fixture-counted-once'
    Assert-Check ($sumError -eq '' -and $waitNull) 'stale-run-excluded'
    Assert-Check ($sumError -eq '' -and $spawnNull -and $tearNull) 'unobserved-phases-stay-null'
} finally {
    Remove-Item -LiteralPath $phaseDir -Recurse -Force -ErrorAction SilentlyContinue
}

# F6: malformed telemetry cannot stall a heartbeat or a terminal read.
$boundRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-bound-' + [guid]::NewGuid().ToString('N'))
$boundLog = Join-Path $boundRoot 'logs'
$boundTiming = Join-Path $boundLog 'timing'
New-Item -ItemType Directory -Path $boundTiming -Force | Out-Null
try {
    $malformed = ('{invalid' + "`n") * 4000
    [IO.File]::WriteAllText((Join-Path $boundTiming 'cases-99-zzzz.jsonl'), $malformed)
    $heartWatch = [Diagnostics.Stopwatch]::StartNew()
    Update-QualificationChunkTiming -LogRoot $boundLog -TimingRoot $boundTiming -Suite 'antiphon' -Chunk 'g1' -LastPhase 'run' -StartedAt '2026-10-27T00:00:00.0000000Z'
    $heartWatch.Stop()
    Assert-Check ($heartWatch.Elapsed.TotalSeconds -lt 0.75 -and (Test-Path -LiteralPath (Join-Path $boundLog 'antiphon-g1.timing.json'))) 'heartbeat-parse-is-bounded'
    $readWatch = [Diagnostics.Stopwatch]::StartNew()
    $boundedSums = $null
    $boundedError = ''
    try {
        $boundedSums = Get-QualificationPhaseSums -TimingRoot $boundTiming -Chunk 'g1' -RunId 'run-bound' -Suite 'antiphon' -Attempt '1'
    } catch {
        $boundedError = $_.Exception.Message
    }
    $readWatch.Stop()
    $boundedNull = ($null -ne $boundedSums -and $null -eq $boundedSums.processWaitSeconds)
    Assert-Check ($boundedError -eq '' -and $readWatch.Elapsed.TotalSeconds -lt 0.75 -and $boundedNull) 'terminal-parse-is-bounded'
} finally {
    Remove-Item -LiteralPath $boundRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# F3: discovery and launch failures still leave a terminal timing row.
$failRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-fail-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $failRoot -Force | Out-Null
$failSeam = Join-Path $failRoot 'seam.ps1'
@'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join " "
        if ($argsText -match "--list-tests" -and $env:QTIMING_FAIL -eq "discovery-timeout") {
            return @{ ExitCode = 1; TimedOut = $true; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        if ($argsText -match "--list-tests" -and $env:QTIMING_FAIL -eq "discovery") { throw "REVIEW_DISCOVERY_FAILURE" }
        if ($argsText -match "--report-trx" -and $env:QTIMING_FAIL -eq "launch") { throw "REVIEW_LAUNCH_FAILURE" }
        if ($argsText -match "--report-trx" -and $env:QTIMING_FAIL -eq "missing-trx") {
            return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
    }
}
'@ | Set-Content -LiteralPath $failSeam -Encoding ascii
try {
    foreach ($pair in @(
        @('discovery', 'rc', 'discovery', 'discovery-failed'),
        @('discovery-timeout', 'rc', 'discovery', 'discovery-timeout'),
        @('launch', 'nightly', 'launch', 'launch-failed'),
        @('missing-trx', 'nightly', 'complete', 'missing-trx')
    )) {
        $mode = $pair[0]
        $env:QTIMING_FAIL = $mode
        $one = Join-Path $failRoot $mode
        $ran = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $one -Suites @('antiphon') -Profile $pair[1] -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId ('fail-' + $mode) -Trigger 'manual' -SeamsPath $failSeam -AllowSharedTree
        $summaryPath = Join-Path $one 'summary.json'
        $rowOk = $false
        if (Test-Path -LiteralPath $summaryPath) {
            $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $rows = @()
            if ($summary.PSObject.Properties.Name -contains 'timingChunks' -and $null -ne $summary.timingChunks) { $rows = @($summary.timingChunks) }
            $match = @($rows | Where-Object { [string]$_.lastPhase -eq $pair[2] -and [string]$_.why -eq $pair[3] })
            $rowOk = ($match.Count -ge 1)
            if ($mode -eq 'launch') {
                $rowOk = $rowOk -and ([string]$summary.preflight.unhandledError -match 'REVIEW_LAUNCH_FAILURE')
            }
            if ($mode -eq 'discovery') {
                $rowOk = $rowOk -and ([string]$summary.preflight.unhandledError -match 'REVIEW_DISCOVERY_FAILURE')
            }
        }
        Assert-Check ($ran.ExitCode -eq 1 -and $rowOk) ('failure-row-' + $mode)
    }
} finally {
    Remove-Item Env:QTIMING_FAIL -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $failRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# F2: replaying a sealed blank timingRoot through release-cut must keep the seal.
$cutRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-cut-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cutRoot -Force | Out-Null
try {
    $release = Join-Path $cutRoot 'release'
    $checkout = Join-Path $release 'checkout'
    New-Item -ItemType Directory -Path $checkout -Force | Out-Null
    $git = {
        param([string]$Dir, [string[]]$GitArgs)
        & git -C $Dir @GitArgs 1> $null
        if ($LASTEXITCODE -ne 0) { throw ('git failed ' + ($GitArgs -join ' ')) }
    }
    & $git $checkout @('init', '-q', '-b', 'rc/2026-10-27')
    & $git $checkout @('config', 'user.email', 'fixture@example.invalid')
    & $git $checkout @('config', 'user.name', 'qtiming')
    foreach ($rel in @(
        'tests/test-execution-policy.json',
        'tests/test-selection-policy.json',
        'global.json',
        'scripts/select-test-rosters.ps1',
        'scripts/lib/test-selection.ps1'
    )) {
        $dest = Join-Path $checkout $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo $rel) -Destination $dest
    }
    & $git $checkout @('add', '--', '.')
    & $git $checkout @('commit', '-qm', 'fixture')
    $sha = (& git -C $checkout rev-parse HEAD).Trim()
    [IO.File]::WriteAllText((Join-Path $checkout '.antiphon-release-owned'), '{"kind":"release-clone"}')
    $origin = Join-Path $cutRoot 'origin.git'
    & git -C $cutRoot clone --bare $checkout $origin 1> $null
    if ($LASTEXITCODE -ne 0) { throw 'origin clone failed' }
    & git -C $checkout remote add origin $origin
    if ($LASTEXITCODE -ne 0) { throw 'origin remote failed' }
    $shaFile = Join-Path $cutRoot 'sha.txt'
    [IO.File]::WriteAllText($shaFile, $sha)
    $cutSeam = Join-Path $cutRoot 'seam.ps1'
    $seamTemplate = @'
$ReleaseGateSeams = @{
    Git = {
        param($WorkingDirectory, $Arguments)
        $joined = @($Arguments) -join ' '
        if ($joined -match 'ls-remote') {
            $value = [IO.File]::ReadAllText('__SHA_FILE__').Trim()
            return @{ ExitCode = 0; Output = ($value + " refs/heads/rc/2026-10-27`n") }
        }
        return @{ ExitCode = 0; Output = '' }
    }
    GitHub = { return @{ ExitCode = 0; Output = '' } }
}
'@
    [IO.File]::WriteAllText($cutSeam, $seamTemplate.Replace('__SHA_FILE__', $shaFile).Replace("`r", ''))
    $candidate = Test-ReleaseGateCandidateRef -Ref 'rc/2026-10-27' -Sha $sha
    $blankRun = 'blank-replay'
    $blankRequest = @{
        RepositoryRoot = $checkout; Repository = 'michal-ciechan/Antiphon'; ReleaseRoot = $release
        CandidateSha = $sha; CandidateId = [string]$candidate.CandidateId; CandidateRef = [string]$candidate.Ref
        Profile = 'rc'; RunId = $blankRun; SelectionMode = 'Full'; Attempt = 1
        BaselineSha = ''; BaselineReceiptPath = ''; QualificationPath = ''; TimingRoot = ''
        TrainManifestPath = ''; ReleaseIntentPath = ''
    }
    $seed = New-ReleaseSelectionAttempt $blankRequest
    $seedDigest = [string]$seed.SelectionAttemptDigest
    $blankCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId $blankRun -TimingRoot '' -SeamsPath $cutSeam -SkipPublish -PassThru
    $afterDigest = Get-NightlyFileSha256 $seed.SelectionAttemptPath
    $sealedText = [IO.File]::ReadAllText($seed.SelectionAttemptPath)
    $blankRefusal = [string]$blankCut.refusal
    Assert-Check ($afterDigest -eq $seedDigest -and $sealedText.Contains('"timingRoot":""') -and $blankRefusal -notmatch 'selection-attempt-conflict') 'blank-sealed-replay-preserves-attempt'

    $explicitTiming = Join-Path $release 'explicit-timing'
    $explicitCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId 'explicit-run' -TimingRoot $explicitTiming -SeamsPath $cutSeam -SkipPublish -PassThru
    $explicitAttempt = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $release 'candidates') $candidate.CandidateId) 'attempts') 'explicit-run') 'selection-attempt.json'
    $explicitOk = $false
    if (Test-Path -LiteralPath $explicitAttempt) {
        $explicitDoc = Get-Content -LiteralPath $explicitAttempt -Raw -Encoding UTF8 | ConvertFrom-Json
        $explicitOk = ([string]$explicitDoc.timingRoot -eq $explicitTiming)
    }
    Assert-Check ($explicitOk -and [string]$explicitCut.refusal -notmatch 'selection-attempt-conflict') 'explicit-timing-root-is-sealed'

    $absentCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId 'absent-run' -TimingRoot '' -SeamsPath $cutSeam -SkipPublish -PassThru
    $absentAttempt = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $release 'candidates') $candidate.CandidateId) 'attempts') 'absent-run') 'selection-attempt.json'
    $absentLog = Join-Path (Join-Path (Join-Path $release 'candidates') ([string]$candidate.CandidateId)) 'logs'
    $absentExpected = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot $absentLog -RunId 'absent-run'
    $absentOk = $false
    if (Test-Path -LiteralPath $absentAttempt) {
        $absentDoc = Get-Content -LiteralPath $absentAttempt -Raw -Encoding UTF8 | ConvertFrom-Json
        $absentOk = ([string]$absentDoc.timingRoot -eq $absentExpected)
    }
    Assert-Check ($absentOk -and [string]$absentCut.refusal -ne 'selection-attempt-conflict') 'absent-timing-root-defaults-for-new-attempt'
} catch {
    Write-Host ('FAIL release-cut-timing ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item -LiteralPath $cutRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed -gt 0) {
    Write-Host ('FAILED ' + $failed)
    exit 1
}
Write-Host 'OK'
exit 0
