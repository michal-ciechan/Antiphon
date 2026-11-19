#requires -Version 7.0
# CARD-2005. Calls production Write-NightlyRunSummary (nightly-tests-impl.ps1) for a
# synthetic 1,274-row summary, in this process. The caller sets DOTNET_GCHeapHardLimit
# before start. Windows PowerShell 5.1 does not run the writer: launch-rc.ps1 re-executes
# the current pwsh 7 image for build-slot.ps1 and nightly-run.ps1.
param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-C2005Line {
    param([string]$Message)
    [Console]::Out.WriteLine($Message)
}

function Fail-C2005 {
    param([string]$Message)
    Write-C2005Line ('C2005-FAIL ' + $Message)
    exit 1
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Fail-C2005 'writer runs under pwsh 7; Windows PowerShell 5.1 does not run it'
}

$limit = [string]$env:DOTNET_GCHeapHardLimit
if ($limit -cne '0x30000000') { Fail-C2005 ('heap limit ' + $limit) }
$available = [int64][GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes
# 0x30000000 is 768 MiB. The runtime reads the variable as hexadecimal.
if ($available -lt 536870912 -or $available -gt 805306368) {
    Fail-C2005 ('heap available ' + $available)
}
Write-C2005Line ('HEAP limit=' + $limit + ' available=' + $available)

$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
. (Join-Path $root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-common.ps1')))
. (Join-Path $root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-tests-impl.ps1')))
if (-not (Test-NightlyCoreAvailable)) { Fail-C2005 'nightly-core-unavailable' }
if (-not (Get-Command Write-NightlyRunSummary -ErrorAction SilentlyContinue)) {
    Fail-C2005 'production writer missing'
}

$shape = @(
    @{ id = 'antiphon'; rows = 1100; exclusions = 500 }
    @{ id = 'session-runner'; rows = 40; exclusions = 8 }
    @{ id = 'pty-host'; rows = 20; exclusions = 8 }
    @{ id = 'agents-pty'; rows = 30; exclusions = 8 }
    @{ id = 'messaging'; rows = 20; exclusions = 8 }
    @{ id = 'e2e'; rows = 40; exclusions = 8 }
    @{ id = 'client'; rows = 12; exclusions = 8 }
    @{ id = 'scripts'; rows = 12; exclusions = 8 }
)
$expectedRows = 0
$expectedUids = New-Object 'System.Collections.Generic.List[string]'
$suiteRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($suite in $shape) {
    $expectedRows += [int]$suite.rows
    $excluded = New-Object object[] ([int]$suite.exclusions)
    for ($j = 0; $j -lt $excluded.Length; $j++) {
        $uid = ('{0}-u{1:d4}' -f $suite.id, $j)
        $expectedUids.Add($uid)
        $excluded[$j] = [ordered]@{
            uid = $uid
            class = 'Antiphon.Tests.Sample'
            method = 'Run'
            categories = @('Slow', 'Integration')
            disposition = 'Excluded'
            reason = 'policy'
            owner = 'nightly'
        }
    }
    for ($i = 0; $i -lt [int]$suite.rows; $i++) {
        $suiteRows.Add([ordered]@{
            id = [string]$suite.id
            chunk = ('{0}-c{1:d4}' -f $suite.id, $i)
            name = [string]$suite.id
            log = ''
            exitCode = 0
            timedOut = $false
            skipped = $false
            result = 'pass'
            trx = 'chunk.trx'
            evidenceReasons = @('kept-on-suite')
            requiredUidCount = 1
            excludedUidCount = $excluded.Length
            censusDigest = 'd5'
            exclusions = $excluded
            coverageComplete = $true
        })
    }
}
if ($expectedRows -ne 1274) { Fail-C2005 ('generator rows ' + $expectedRows) }

$dir = Join-Path ([IO.Path]::GetTempPath()) ('c2005-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($dir)
$summaryPath = Join-Path $dir 'summary.json'
$cardPath = Join-Path $dir 'card.md'
$sha = 'a' * 40
$completedAt = [datetime]::SpecifyKind([datetime]'2026-11-19T01:03:03.004', [DateTimeKind]::Utc)
$summary = [ordered]@{
    startedAt = '2026-11-19T01:02:03.0040000Z'
    completedAt = $completedAt.ToString('o')
    durationSeconds = [math]::Round(60, 3)
    sha = $sha
    gitRef = 'origin/master'
    trigger = 'scheduled'
    profile = 'nightly'
    expectedSha = $sha
    requiredSuites = @('antiphon')
    runId = 'c2005-synthetic-1274'
    clone = 'C:\Antiphon\nightly\checkout'
    logDir = $dir
    selectedSuites = @('antiphon')
    selection = [ordered]@{ attempt = '1' }
    timingRoot = 'C:\timing'
    timingChunks = @()
    suiteTimeline = @()
    platform = 'linux'
    coverageScope = 'Full'
    outcome = 'green'
    succeeded = $true
    coverageComplete = $true
    testsPassed = $true
    policyHash = ('b' * 64)
    reasons = @()
    warnings = @()
    expectedSkipRosterDigest = ''
    toleratedSkips = @()
    builds = @()
    suites = $suiteRows.ToArray()
    invocationId = 'c2005-synthetic-1274'
}
$schedule = [pscustomobject]@{
    preparationComplete = $true
    canonicalPlanDigest = ('c' * 64)
}
try {
    Write-NightlyRunSummary -summary $summary -suiteResults $summary.suites -dispatchSchedule $schedule -Sha $sha -completedAt $completedAt -summaryPath $summaryPath -LogRoot $dir
    if (-not [IO.File]::Exists($summaryPath)) { Fail-C2005 'summary missing' }
    $bytes = (Get-Item -LiteralPath $summaryPath).Length
    $text = [IO.File]::ReadAllText($summaryPath)
    $parsed = $text | ConvertFrom-Json
    $suites = @($parsed.suites)
    if ($suites.Count -ne 1274) { Fail-C2005 ('suites ' + $suites.Count) }
    # The reader adds suites in memory. The file itself must not already carry them.
    $onDiskNames = @($parsed.dispatchProgress.PSObject.Properties.Name)
    if ($onDiskNames -contains 'suites') { Fail-C2005 'dispatchProgress embeds a second suites array' }
    if (-not (Get-Command Get-NightlySummaryDispatchProgress -ErrorAction SilentlyContinue)) {
        Fail-C2005 'dispatch progress reader missing'
    }
    $progress = Get-NightlySummaryDispatchProgress -Summary $parsed
    $progressRows = @($progress.suites)
    if ($progressRows.Count -ne 1274) { Fail-C2005 ('dispatchProgress rows ' + $progressRows.Count) }
    $counts = @{}
    foreach ($match in [regex]::Matches($text, '"uid"\s*:\s*"([^"]+)"')) {
        $uid = [string]$match.Groups[1].Value
        if ($counts.ContainsKey($uid)) { $counts[$uid] = [int]$counts[$uid] + 1 }
        else { $counts[$uid] = 1 }
    }
    if ($counts.Count -ne $expectedUids.Count) {
        Fail-C2005 ('uid keys ' + $counts.Count + ' expected ' + $expectedUids.Count)
    }
    foreach ($uid in $expectedUids) {
        $seen = 0
        if ($counts.ContainsKey($uid)) { $seen = [int]$counts[$uid] }
        if ($seen -ne 1) { Fail-C2005 ('uid ' + $uid + ' count ' + $seen) }
    }
    foreach ($row in $suites) {
        if (@($row.PSObject.Properties.Name) -contains 'exclusions') { Fail-C2005 ('row kept exclusions ' + [string]$row.id) }
    }
    $keys = @($parsed.PSObject.Properties.Name)
    if ($keys -notcontains 'coverageComplete' -or $keys -notcontains 'runId') { Fail-C2005 'summary keys' }
    if ([string]$parsed.runId -cne 'c2005-synthetic-1274') { Fail-C2005 'runId' }
    if (-not [bool]$parsed.coverageComplete) { Fail-C2005 'coverageComplete' }
    if ([string]$parsed.dispatchProgress.liveness -cne 'closed') { Fail-C2005 'liveness' }
    Write-C2005Line ('WROTE bytes=' + $bytes + ' suites=1274 progress=1274')
    Write-C2005Line 'UID-ONCE'
    Write-C2005Line 'KEYS coverageComplete runId liveness'

    $shim = {
        param($Method, $Uri, $Headers, $Body)
        if ($Uri -like '*/api/boards/*/columns') {
            return [pscustomobject]@{ id = 'col'; name = 'Done'; isTerminal = $true; columnOrder = 1 }
        }
        if ($Uri -like '*/api/boards') {
            return [pscustomobject]@{ id = '8988ca03-7414-47ad-b0b6-51556c701703'; name = 'Antiphon' }
        }
        if ($Uri -like '*/api/cards*') {
            return [pscustomobject]@{ cards = @(); truncated = $false }
        }
        throw ('unexpected ' + $Uri)
    }
    $reportScript = Join-Path $root (Join-Path 'scripts' 'nightly-report.ps1')
    $report = & $reportScript -Summary $summaryPath -PassThru -DryRun -HttpShim $shim -Board 'Antiphon' -Api 'http://nightly.test'
    if ($report.ExitCode -ne 0 -or [string]$report.Action -cne 'none' -or [string]$report.Title -notlike 'Nightly green*') {
        Fail-C2005 ('report exit=' + $report.ExitCode + ' action=' + $report.Action + ' title=' + $report.Title)
    }
    if ([IO.File]::Exists($cardPath)) { Fail-C2005 'report wrote card.md' }
    Write-C2005Line 'REPORT-ACCEPTED'
    $peak = 0
    $statusPath = Join-Path '/proc' (Join-Path ([string]$PID) 'status')
    if ([IO.File]::Exists($statusPath)) {
        foreach ($statusLine in [IO.File]::ReadAllLines($statusPath)) {
            if ($statusLine.StartsWith('VmHWM:')) {
                $peak = [int]($statusLine -replace '[^0-9]', '')
            }
        }
    }
    Write-C2005Line ('PEAK-RSS-KB ' + $peak)
    Write-C2005Line 'C2005-OK'
    exit 0
} finally {
    if ([IO.File]::Exists($summaryPath)) { [IO.File]::Delete($summaryPath) }
    if ([IO.File]::Exists($cardPath)) { [IO.File]::Delete($cardPath) }
    if ([IO.Directory]::Exists($dir)) {
        $left = @(Get-ChildItem -LiteralPath $dir -Force)
        if ($left.Count -eq 0) { [IO.Directory]::Delete($dir) }
    }
}
