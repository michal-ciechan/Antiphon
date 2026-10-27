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
        Assert-Check ([string]$dead.startedAt -eq '2026-10-27T00:00:00.0000000Z') 'timeout-keeps-start'
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

if ($failed -gt 0) {
    Write-Host ('FAILED ' + $failed)
    exit 1
}
Write-Host 'OK'
exit 0
