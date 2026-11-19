#requires -Version 5.1
# CARD-2005. The run summary must be streamed. At the base commit this exits 1:
# nightly-tests-impl.ps1 still pipes $summary through ConvertTo-Json, and the
# capped child prints OLD-OOM. ASCII-only. Windows PowerShell 5.1 syntax.
param(
    [ValidateSet('parent', 'old', 'new')]
    [string]$Mode = 'parent',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = Split-Path -Parent $PSScriptRoot
$WriterPath = Join-Path $Root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-summary-json.ps1'))
$TestsImpl = Join-Path $Root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-tests-impl.ps1'))
$RunImpl = Join-Path $Root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-run-impl.ps1'))

function New-C2005Rows {
    $reason = 'x' * 200000
    $rows = New-Object System.Collections.Generic.List[object]
    $chunkCount = 40
    $exclusionCount = 2
    for ($i = 0; $i -lt $chunkCount; $i++) {
        $excluded = New-Object System.Collections.Generic.List[object]
        for ($j = 0; $j -lt $exclusionCount; $j++) {
            $excluded.Add([ordered]@{
                uid = ('u{0}-{1}' -f $i, $j)
                class = 'Antiphon.Tests.Sample'
                method = 'Run'
                categories = @('Slow', 'Integration')
                disposition = 'Excluded'
                reason = $reason
                owner = 'nightly'
            })
        }
        $rows.Add([ordered]@{
            id = 'antiphon'
            chunk = ('c{0}' -f $i)
            name = 'antiphon'
            result = 'pass'
            exitCode = 0
            timedOut = $false
            skipped = $false
            coverageComplete = $true
            detail = 'quote " ok'
            exclusions = $excluded.ToArray()
            evidenceReasons = @('kept-on-suite')
            trx = 'antiphon.trx'
        })
    }
    $rows.Add([ordered]@{ id = 'manual'; name = 'manual'; result = 'skipped'; skipped = $true })
    return ,$rows.ToArray()
}

function New-C2005Summary {
    param($Rows, $ProgressSuites, [string]$LogDir = 'C:\Antiphon\nightly\logs\run-c2005')
    $sha = 'a' * 40
    $summary = [ordered]@{
        startedAt = '2026-11-19T01:02:03.0040000Z'
        completedAt = '2026-11-19T01:03:03.0040000Z'
        durationSeconds = [math]::Round(60, 3)
        sha = $sha
        gitRef = 'origin/master'
        trigger = 'scheduled'
        profile = 'rc'
        expectedSha = $sha
        requiredSuites = @('antiphon')
        runId = 'run-c2005'
        clone = 'C:\Antiphon\nightly\checkout'
        logDir = $LogDir
        selectedSuites = @('antiphon')
        selection = [ordered]@{ attempt = '1' }
        timingRoot = 'C:\timing'
        timingChunks = @()
        suiteTimeline = @()
        platform = 'windows'
        coverageScope = 'Full'
        outcome = 'green'
        succeeded = $true
        coverageComplete = $true
        testsPassed = $true
        policyHash = ('b' * 64)
        reasons = @()
        warnings = @()
        suites = $Rows
        invocationId = 'run-c2005'
    }
    $summary.dispatchProgress = [ordered]@{
        schemaVersion = 1
        liveness = 'closed'
        sha = $sha
        completedAt = '2026-11-19T01:03:03.0040000Z'
        canonicalPlanDigest = ('c' * 64)
        suites = $ProgressSuites
    }
    return $summary
}

function Write-C2005Old {
    $rows = New-C2005Rows
    $progress = @($rows | Where-Object { $_.chunk })
    $summary = New-C2005Summary $rows $progress
    try {
        $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
        Write-Output 'OLD-WROTE'
        exit 1
    } catch {
        Write-Output ('OLD-OOM ' + $_.Exception.GetType().FullName)
        exit 2
    }
}

function Write-C2005New {
    if (-not (Test-Path -LiteralPath $WriterPath)) {
        Write-Output 'NEW-MISSING'
        exit 3
    }
    . $WriterPath
    $rows = New-C2005Rows
    $progress = Get-NightlyDispatchProgressSuites $rows
    $summary = New-C2005Summary $rows $progress (Split-Path -Parent $OutputPath)
    Write-NightlyStreamingJson -Path $OutputPath -Object $summary -Depth 8
    $length = (Get-Item -LiteralPath $OutputPath).Length
    Write-Output ('NEW-BOUNDED {0}' -f $length)
    exit 0
}

function Test-C2005Structural {
    $testsText = [IO.File]::ReadAllText($TestsImpl)
    $runText = [IO.File]::ReadAllText($RunImpl)
    $reasons = New-Object System.Collections.Generic.List[string]
    if ($testsText.Contains('$summary | ConvertTo-Json')) { $reasons.Add('tests-impl-convertto-json') }
    if ($runText.Contains('Write-NightlyAtomicJson -Path $summaryPath')) { $reasons.Add('run-impl-atomic-json') }
    if (-not $testsText.Contains('Write-NightlyStreamingJson -Path $summaryPath')) { $reasons.Add('tests-impl-not-streaming') }
    if (-not $runText.Contains('Write-NightlyStreamingJson -Path $summaryPath')) { $reasons.Add('run-impl-not-streaming') }
    if (-not $testsText.Contains("Join-Path `$PSScriptRoot 'nightly-summary-json.ps1'")) { $reasons.Add('tests-impl-not-dotted') }
    if (-not $testsText.Contains('Get-NightlyDispatchProgressSuites')) { $reasons.Add('tests-impl-not-thin') }
    if ($testsText.Contains('suites=@($suiteResults|Where-Object {$_.chunk})')) { $reasons.Add('tests-impl-duplicate-rows') }
    $names = '$names = @(' + "'id', 'chunk', 'timedOut', 'result', 'exitCode', 'skipped', 'coverageComplete')"
    if (-not (Test-Path -LiteralPath $WriterPath)) {
        $reasons.Add('writer-missing')
    } else {
        $writerText = [IO.File]::ReadAllText($WriterPath)
        if (-not $writerText.Contains($names)) { $reasons.Add('writer-projection-keys') }
        $bytes = [IO.File]::ReadAllBytes($WriterPath)
        foreach ($byte in $bytes) {
            if ($byte -gt 127) { $reasons.Add('writer-non-ascii'); break }
        }
    }
    if ($reasons.Count -eq 0) { return 'STRUCTURAL-CLEAN' }
    return ('STRUCTURAL-RED ' + [string]::Join(',', $reasons.ToArray()))
}

function Invoke-C2005Capped {
    param([string]$Which, [string]$Path)
    $saved = $env:DOTNET_GCHeapHardLimit
    $env:DOTNET_GCHeapHardLimit = '4000000'
    $env:ANTIPHON_LINUX_COMPILER_POLICY = '0'
    try {
        $text = & pwsh -NoProfile -File $PSCommandPath -Mode $Which -OutputPath $Path 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        if ($null -eq $saved) { Remove-Item Env:\DOTNET_GCHeapHardLimit -ErrorAction SilentlyContinue }
        else { $env:DOTNET_GCHeapHardLimit = $saved }
    }
    return [pscustomobject]@{ Code = $code; Text = $text }
}

function Test-C2005Format {
    param([string]$Path)
    . $WriterPath
    $one = Get-NightlyDispatchProgressSuites @(
        [ordered]@{ id = 'antiphon'; chunk = 'only'; result = 'pass'; exitCode = 0; timedOut = $false; skipped = $false; coverageComplete = $true; exclusions = @([ordered]@{ uid = 'hidden' }) }
    )
    $none = Get-NightlyDispatchProgressSuites @([ordered]@{ id = 'manual'; result = 'skipped' })
    if ($one -isnot [System.Array] -or $one.Count -ne 1) { return 'ONE-ARRAY' }
    if ($one[0].Contains('exclusions')) { return 'ONE-EXCLUSIONS' }
    $miniPath = $Path + '.one.json'
    Write-NightlyStreamingJson -Path $miniPath -Object ([ordered]@{ suites = $one }) -Depth 4
    $miniRaw = [IO.File]::ReadAllText($miniPath)
    [IO.File]::Delete($miniPath)
    if (-not $miniRaw.StartsWith('{"suites":[{')) { return 'ONE-JSON' }
    if ($none -isnot [System.Array] -or $none.Count -ne 0) { return 'NONE-ARRAY' }
    $sample = [ordered]@{
        startedAt = '2026-11-19T01:02:03.0040000Z'
        durationSeconds = [math]::Round(60, 3)
        flag = $true
        no = $false
        nil = $null
        text = "a`"b"
        items = @('only')
        nested = [ordered]@{ categories = @('Slow', 'Integration') }
        row = [pscustomobject]@{ id = 'antiphon'; chunk = 'c' }
    }
    $path = $Path + '.format.json'
    Write-NightlyStreamingJson -Path $path -Object $sample -Depth 8
    Write-NightlyStreamingJson -Path $path -Object $sample -Depth 8
    $raw = [IO.File]::ReadAllText($path)
    [IO.File]::Delete($path)
    $expected = $sample | ConvertTo-Json -Depth 8 -Compress
    if ($raw -cne $expected) { return 'FORMAT-DIFF' }
    return ''
}

function Write-C2005Line {
    param([string]$Message)
    [Console]::Out.WriteLine($Message)
}

function Test-C2005ReadBack {
    param([string]$Path)
    $text = [IO.File]::ReadAllText($Path)
    $uid = ([regex]::Matches($text, [regex]::Escape('"uid":"u0-0"'))).Count
    $uids = ([regex]::Matches($text, '"uid":')).Count
    $evidence = ([regex]::Matches($text, [regex]::Escape('"evidenceReasons"'))).Count
    if ($uid -ne 1 -or $uids -ne 80 -or $evidence -ne 40) {
        Write-C2005Line ('UID-COUNT uid0={0} uids={1} evidence={2}' -f $uid, $uids, $evidence)
        return $false
    }
    Write-C2005Line 'UID-ONCE'
    $parsed = $text | ConvertFrom-Json
    $suites = @($parsed.suites)
    $progress = @($parsed.dispatchProgress.suites)
    if ($suites.Count -ne 41 -or $progress.Count -ne 40) {
        Write-C2005Line ('ROW-COUNT suites={0} progress={1}' -f $suites.Count, $progress.Count)
        return $false
    }
    if ([string]$suites[0].exclusions[0].uid -ne 'u0-0') { Write-C2005Line 'SUITE-UID'; return $false }
    if ([string]$suites[0].exclusions[0].categories[1] -ne 'Integration') { Write-C2005Line 'SUITE-CATEGORY'; return $false }
    if ([string]$suites[40].id -ne 'manual') { Write-C2005Line 'MANUAL-ROW'; return $false }
    foreach ($row in $progress) {
        $present = @($row.PSObject.Properties.Name)
        foreach ($forbidden in @('exclusions', 'evidenceReasons', 'trx', 'detail')) {
            if ($present -contains $forbidden) { Write-C2005Line ('PROGRESS-LEAK ' + $forbidden); return $false }
        }
        foreach ($required in @('id', 'chunk', 'timedOut', 'result', 'exitCode', 'skipped', 'coverageComplete')) {
            if ($present -notcontains $required) { Write-C2005Line ('PROGRESS-MISSING ' + $required); return $false }
        }
    }
    . (Join-Path $Root (Join-Path 'scripts' (Join-Path 'lib' 'nightly-failure-history.ps1')))
    Initialize-NightlyHistoryIdentity
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $cut = [Antiphon.NightlyHistory.SummaryPrefix]::CutBeforeBulk($bytes)
    if ($cut -lt 1) { Write-C2005Line 'PREFIX-CUT'; return $false }
    $prefix = [Text.Encoding]::UTF8.GetString($bytes, 0, $cut)
    $key = [Text.Encoding]::UTF8.GetString($bytes, $cut, 15)
    foreach ($field in @('runId', 'sha', 'timingRoot', 'startedAt', 'completedAt')) {
        if (-not $prefix.Contains('"' + $field + '"')) { Write-C2005Line ('PREFIX-MISSING ' + $field); return $false }
    }
    if ($prefix.Contains('"suites"') -or $key -ne '"timingChunks":') {
        Write-C2005Line ('PREFIX-ORDER key={0}' -f $key)
        return $false
    }
    Write-C2005Line 'PREFIX-OK'
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
    $report = Join-Path $Root (Join-Path 'scripts' 'nightly-report.ps1')
    $result = & $report -Summary $Path -PassThru -DryRun -HttpShim $shim -Board Antiphon
    if ($result.ExitCode -ne 0 -or $result.Action -ne 'none') {
        Write-C2005Line ('REPORT-REJECT exit={0} action={1}' -f $result.ExitCode, $result.Action)
        return $false
    }
    $card = Join-Path (Split-Path -Parent $Path) 'card.md'
    if (Test-Path -LiteralPath $card) { Write-C2005Line 'REPORT-WROTE-CARD'; return $false }
    Write-C2005Line 'REPORT-ACCEPTED'
    return $true
}

if ($Mode -eq 'old') { Write-C2005Old }
if ($Mode -eq 'new') { Write-C2005New }

$structural = [string](Test-C2005Structural)
Write-Output $structural

$dir = Join-Path ([IO.Path]::GetTempPath()) ('c2005-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($dir)
$oldPath = Join-Path $dir 'old.json'
$newPath = Join-Path $dir 'new.json'
$failed = $structural -ne 'STRUCTURAL-CLEAN'
try {
    $old = Invoke-C2005Capped -Which old -Path $oldPath
    Write-Output $old.Text.Trim()
    if ($old.Code -ne 2 -or $old.Text -notmatch 'OLD-OOM') { $failed = $true }
    $new = Invoke-C2005Capped -Which new -Path $newPath
    Write-Output $new.Text.Trim()
    if ($new.Code -ne 0 -or $new.Text -notmatch 'NEW-BOUNDED') { $failed = $true }
    else {
        $length = [int]([regex]::Match($new.Text, 'NEW-BOUNDED (\d+)')).Groups[1].Value
        if ($length -lt 10000000 -or $length -gt 25000000) {
            Write-Output ('SIZE-UNEXPECTED ' + $length)
            $failed = $true
        }
        $format = Test-C2005Format -Path $newPath
        if ($format) { Write-Output $format; $failed = $true }
        if (-not (Test-C2005ReadBack -Path $newPath)) { $failed = $true }
    }
} finally {
    if ([IO.File]::Exists($oldPath)) { [IO.File]::Delete($oldPath) }
    if ([IO.File]::Exists($newPath)) { [IO.File]::Delete($newPath) }
    $formatPath = $newPath + '.format.json'
    if ([IO.File]::Exists($formatPath)) { [IO.File]::Delete($formatPath) }
    if ([IO.Directory]::Exists($dir)) { [IO.Directory]::Delete($dir) }
}
if ($failed) { exit 1 }
Write-Output 'C2005-OK'
exit 0
