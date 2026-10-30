#requires -Version 5.1
# CARD-1514 portable witness harness. Test-only. Not a product script.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Case,
    [Parameter(Mandatory = $true)][string]$Work,
    [Parameter(Mandatory = $true)][string]$Repo
)

$ErrorActionPreference = 'Stop'
$script:MpResultPath = Join-Path $Work 'result.json'

function Write-MpResult {
    param([bool]$Ok, [string]$Reason)
    $payload = [ordered]@{ ok = [bool]$Ok; reason = [string]$Reason; case = [string]$Case }
    $json = $payload | ConvertTo-Json -Compress -Depth 4
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($script:MpResultPath, $json, $utf8)
}

function Fail-Mp {
    param([string]$Reason)
    Write-MpResult -Ok $false -Reason $Reason
    exit 0
}

function Initialize-MpCulture {
    $script:QualificationTimingRoot = $null
    [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
    [System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture
}

function Assert-MpOffset {
    $raw = [Environment]::GetEnvironmentVariable('MP_EXPECT_OFFSET')
    if ([string]::IsNullOrWhiteSpace($raw)) { return }
    $expect = [int]$raw
    $moment = New-Object System.DateTime 2026, 10, 30, 12, 0, 0, ([System.DateTimeKind]::Unspecified)
    $got = [int][System.TimeZoneInfo]::Local.GetUtcOffset($moment).TotalMinutes
    if ($got -ne $expect) {
        Fail-Mp ("timezone-offset expected=" + $expect + " actual=" + $got + " id=" + [System.TimeZoneInfo]::Local.Id)
    }
}

function Get-MpSha {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function New-MpTrx {
    param([string]$Path, [string[]]$Rows)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $body = New-Object System.Text.StringBuilder
    [void]$body.Append('<?xml version="1.0" encoding="utf-8"?><TestRun><Results>')
    foreach ($row in @($Rows)) {
        [void]$body.Append($row)
    }
    [void]$body.Append('</Results></TestRun>')
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $body.ToString(), $utf8)
}

function New-MpResultRow {
    param([string]$Id, [string]$Outcome)
    return '<UnitTestResult testId="' + $Id + '" testName="' + $Id + '" outcome="' + $Outcome + '" />'
}

function Write-MpLegacyPrefix {
    param([string]$Path, [int]$Chunks, [int]$OutcomesPerChunk = 1, [int]$WideOutcomes = 0, [int]$WideChunks = 0)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{"version":1,"runId":"run-v1","attempt":"1","suite":"antiphon","manufacturedPassed":false,"chunks":[')
    $outcomeIndex = 0
    for ($c = 0; $c -lt $Chunks; $c++) {
        if ($c -gt 0) { [void]$sb.Append(',') }
        $count = $OutcomesPerChunk
        if ($WideChunks -gt 0 -and $c -lt $WideChunks) { $count = $WideOutcomes }
        [void]$sb.Append('{"chunk":"p')
        [void]$sb.Append($c.ToString('0000'))
        [void]$sb.Append('","runId":"run-v1","attempt":"1","suite":"antiphon","hostWallSeconds":1.25,"trxMissing":true,"manufacturedPassed":false,"passedCount":')
        [void]$sb.Append([string]$count)
        [void]$sb.Append(',"outcomes":[')
        for ($o = 0; $o -lt $count; $o++) {
            if ($o -gt 0) { [void]$sb.Append(',') }
            [void]$sb.Append('{"testId":"t')
            [void]$sb.Append([string]$outcomeIndex)
            [void]$sb.Append('","outcome":"Passed"}')
            $outcomeIndex++
        }
        [void]$sb.Append('],"trx":"","trxDigest":"","exitCode":0,"coverageComplete":false,"testsPassed":false}')
    }
    [void]$sb.Append(']}')
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), $utf8)
}

function Install-MpCounters {
    if ($script:MpCountersInstalled) { return }
    $script:MpCountersInstalled = $true
    $script:MpChunkBlock = ${function:ConvertTo-TestPhaseExecutedChunk}
    $script:MpJsonBlock = ${function:ConvertTo-TestPhaseJsonText}
    $script:MpAtomicBlock = ${function:Write-TestPhaseTextAtomic}
    $script:MpHashBlock = ${function:Get-NightlyFileSha256}
    $script:MpPriorRows = 0
    $script:MpPriorChunks = 0
    $script:MpDepth0Bytes = 0
    $script:MpJsonDepth = 0
    $script:MpFlatWrites = 0
    $script:MpHashCount = 0
    $script:MpCountPrior = $false
    $script:MpCountBytes = $false
    Set-Item -Path 'function:ConvertTo-TestPhaseExecutedChunk' -Value {
        param($Item, [string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite, [double]$HostWallSeconds, [bool]$TrxMissing, [int]$PassedCount, $Outcomes, [string]$Trx, [string]$TrxDigest, $ExitCode, $CoverageComplete, $TestsPassed)
        if ($null -ne $Item -and $script:MpCountPrior) {
            $itemChunk = ''
            if ($Item.PSObject.Properties.Name -contains 'chunk') { $itemChunk = [string]$Item.chunk }
            if ($itemChunk -ne [string]$script:MpPublishedChunk) {
                $n = 0
                if ($Item.PSObject.Properties.Name -contains 'outcomes' -and $null -ne $Item.outcomes) { $n = @($Item.outcomes).Count }
                $script:MpPriorRows += $n
                $script:MpPriorChunks += 1
            }
        }
        & $script:MpChunkBlock @PSBoundParameters
    }
    Set-Item -Path 'function:ConvertTo-TestPhaseJsonText' -Value {
        param($Value, [int]$Depth = 0)
        $outer = ($script:MpJsonDepth -eq 0)
        $script:MpJsonDepth++
        try {
            $text = & $script:MpJsonBlock -Value $Value -Depth $Depth
            if ($outer -and $script:MpCountBytes) {
                $script:MpDepth0Bytes += [System.Text.Encoding]::UTF8.GetByteCount([string]$text)
            }
            return $text
        } finally {
            $script:MpJsonDepth--
        }
    }
    Set-Item -Path 'function:Write-TestPhaseTextAtomic' -Value {
        param([string]$Path, [string]$Text)
        if ($Path -like '*groups-executed.json') { $script:MpFlatWrites++ }
        & $script:MpAtomicBlock -Path $Path -Text $Text
    }
    Set-Item -Path 'function:Get-NightlyFileSha256' -Value {
        param([string]$Path)
        $script:MpHashCount++
        & $script:MpHashBlock -Path $Path
    }
}

function Reset-MpCounters {
    $script:MpPriorRows = 0
    $script:MpPriorChunks = 0
    $script:MpDepth0Bytes = 0
    $script:MpFlatWrites = 0
    $script:MpHashCount = 0
    $script:MpCountPrior = $true
    $script:MpCountBytes = $true
}

function Open-MpContext {
    param([string]$LogRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source)
    if (-not (Get-Command Open-TestPhaseExecutedContext -ErrorAction SilentlyContinue)) { return $null }
    return (Open-TestPhaseExecutedContext -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source $Source)
}

function Close-MpContext {
    param($Context)
    if ($null -eq $Context) { return }
    if (-not (Get-Command Close-TestPhaseExecutedContext -ErrorAction SilentlyContinue)) { return }
    Close-TestPhaseExecutedContext -Context $Context
}

# Pinned copy of the pre-change production writer. Uses the still-shipped helpers.
function Invoke-LegacyExecutedManifest {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    $rows = @()
    $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
    $trxMissing = ($trxIdentity.Kind -ne 'regular')
    if ($trxIdentity.Kind -eq 'regular') {
        $trxRead = Read-TestPhaseTrxRows -Path $TrxPath
        if ($null -ne $trxRead -and $trxRead.Complete -and -not $trxRead.Refused) {
            $rows = @($trxRead.Rows)
        }
    }
    $outcomes = New-Object System.Collections.Generic.List[object]
    $passed = 0
    foreach ($row in $rows) {
        $outcomes.Add([ordered]@{ testId = [string]$row.TestId; outcome = [string]$row.Outcome })
        if ([string]$row.Outcome -eq 'Passed') { $passed++ }
    }
    $digest = ''
    if ($trxIdentity.Kind -eq 'regular') {
        try { $digest = Get-NightlyFileSha256 -Path $TrxPath } catch { $digest = '' }
    }
    Write-TestPhaseOwnerObservation -Verdict $Verdict -LogRoot $LogRoot -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = ConvertTo-TestPhaseExecutedChunk -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite -HostWallSeconds $HostWallSeconds -TrxMissing $trxMissing -PassedCount $passed -Outcomes @($outcomes.ToArray()) -Trx (ConvertTo-TestPhaseRelativePath -LogRoot $LogRoot -Path $TrxPath) -TrxDigest $digest -ExitCode $exitCode -CoverageComplete $coverageComplete -TestsPassed $testsPassed
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'groups-executed.json'
    $kept = New-Object System.Collections.Generic.List[object]
    $topRun = [string]$RunId
    $topAttempt = [string]$Attempt
    $topSuite = [string]$Suite
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'runId' -and -not [string]::IsNullOrWhiteSpace([string]$existing.runId)) { $topRun = [string]$existing.runId }
        if ($existing.PSObject.Properties.Name -contains 'attempt') { $topAttempt = [string]$existing.attempt }
        if ($existing.PSObject.Properties.Name -contains 'suite' -and -not [string]::IsNullOrWhiteSpace([string]$existing.suite)) { $topSuite = [string]$existing.suite }
        $prior = @()
        if ($existing.PSObject.Properties.Name -contains 'chunks' -and $null -ne $existing.chunks) { $prior = @($existing.chunks) }
        elseif ($existing.PSObject.Properties.Name -contains 'chunk') { $prior = @($existing) }
        foreach ($item in $prior) {
            if ($null -eq $item) { continue }
            $same = ([string]$item.chunk -eq [string]$Chunk) -and ([string]$item.suite -eq [string]$Suite)
            if ($same) { continue }
            $kept.Add((ConvertTo-TestPhaseExecutedChunk -Item $item))
        }
    }
    $kept.Add($fresh)
    $manifest = [ordered]@{
        version = 1
        runId = $topRun
        attempt = $topAttempt
        suite = $topSuite
        manufacturedPassed = $false
        chunks = @($kept.ToArray())
    }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $manifest)
}

function Publish-MpChunk {
    param(
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds,
        [bool]$Coverage = $false,
        [bool]$Passed = $false,
        [int]$ExitCode = 0,
        [bool]$NullVerdict = $false
    )
    $script:MpPublishedChunk = $Chunk
    if ($NullVerdict) {
        $null = Write-TestPhaseExecutedManifest -Verdict $null -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
        return
    }
    $run = [pscustomobject]@{ ExitCode = $ExitCode }
    $null = Publish-NightlyChunkObservation -Run $run -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds -CoverageComplete $Coverage -TestsPassed $Passed
}

function Measure-MpPublish {
    param([string]$Label, [int]$PrefixChunks, [string]$TrxPath, [int]$WideOutcomes = 0, [int]$WideChunks = 0)
    $root = Join-Path $Work $Label
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $script:QualificationTimingRoot = $null
    $timing = Join-Path $root 'timing'
    $flat = Join-Path $timing 'groups-executed.json'
    if ($PrefixChunks -gt 0) {
        if ($WideChunks -gt 0) {
            Write-MpLegacyPrefix -Path $flat -Chunks $PrefixChunks -OutcomesPerChunk 89 -WideOutcomes $WideOutcomes -WideChunks $WideChunks
        } else {
            Write-MpLegacyPrefix -Path $flat -Chunks $PrefixChunks -OutcomesPerChunk 1
        }
    }
    $before = Get-MpSha -Path $flat
    # Prefix import is separately charged setup. Recursive counter wrappers add
    # substantial interpreter work to that import and are needed only while
    # observing the following steady-state publication.
    $originals = @{
        'ConvertTo-TestPhaseExecutedChunk' = $script:MpChunkBlock
        'ConvertTo-TestPhaseJsonText' = $script:MpJsonBlock
        'Write-TestPhaseTextAtomic' = $script:MpAtomicBlock
        'Get-NightlyFileSha256' = $script:MpHashBlock
    }
    $counters = @{}
    $initialization = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        foreach ($name in $originals.Keys) {
            $path = 'function:' + $name
            $counters[$name] = (Get-Item -LiteralPath $path).ScriptBlock
            Set-Item -LiteralPath $path -Value $originals[$name]
        }
        $ctx = Open-MpContext -LogRoot $root -RunId 'run-v1' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    } finally {
        foreach ($name in $counters.Keys) { Set-Item -LiteralPath ('function:' + $name) -Value $counters[$name] }
        $initialization.Stop()
    }
    if ($null -ne $ctx -and $ctx.Refused) { Fail-Mp ("open-refused " + [string]$ctx.Reason) }
    Reset-MpCounters
    $publication = [System.Diagnostics.Stopwatch]::StartNew()
    Publish-MpChunk -LogRoot $root -TrxPath $TrxPath -Suite 'antiphon' -Chunk 'live-1' -RunId 'run-v1' -Attempt '1' -HostWallSeconds 1.25
    $publication.Stop()
    $after = Get-MpSha -Path $flat
    $hashSame = ($PrefixChunks -eq 0) -or ($before -eq $after -and $before -ne '')
    $measured = [pscustomobject]@{
        PriorRows = [int]$script:MpPriorRows
        Bytes = [int]$script:MpDepth0Bytes
        HashSame = [bool]$hashSame
        FlatWrites = [int]$script:MpFlatWrites
        HashCount = [int]$script:MpHashCount
        InitializationSeconds = $initialization.Elapsed.TotalSeconds
        PublicationSeconds = $publication.Elapsed.TotalSeconds
    }
    [System.IO.File]::WriteAllText((Join-Path $root 'publication-measure.json'), ($measured | ConvertTo-Json -Compress))
    Close-MpContext -Context $ctx
    $script:QualificationTimingRoot = $null
    return $measured
}

function Test-MpSettledJournal {
    param([string]$Timing)
    $root = Join-Path $Timing 'groups-executed.journal'
    if (-not (Test-Path -LiteralPath $root)) { return 'settled-keys-absent: no-journal' }
    $hit = $false
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.json' -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        if ($file.Name -notmatch '^[0-9]{8}\.json$') { continue }
        $text = [System.IO.File]::ReadAllText($file.FullName)
        if ($text.Contains('"settledRowJson"') -and $text.Contains('"settledSuiteKey"') -and $text.Contains('"settledChunkKey"')) {
            $hit = $true
            break
        }
    }
    if (-not $hit) { return 'settled-keys-absent' }
    $currentPath = Join-Path $root 'current.json'
    if (-not (Test-Path -LiteralPath $currentPath)) { return 'settled-keys-absent: no-current' }
    $current = [System.IO.File]::ReadAllText($currentPath) | ConvertFrom-Json
    $generation = [string]$current.generation
    $flat = Join-Path $Timing 'groups-executed.json'
    if ((Test-Path -LiteralPath $flat) -and -not [string]::IsNullOrWhiteSpace($generation)) {
        $flatText = [System.IO.File]::ReadAllText($flat)
        if ($flatText.Contains($generation)) { return 'generation-leaked-into-v1' }
    }
    return ''
}

function Test-MpFixedPoint {
    param([string]$Timing)
    $root = Join-Path $Timing 'groups-executed.journal'
    if (-not (Test-Path -LiteralPath $root)) { return '' }
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.json' -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        if ($file.Name -notmatch '^[0-9]{8}\.json$') { continue }
        $envelope = [System.IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
        if ($envelope.PSObject.Properties.Name -notcontains 'settledRowJson') { continue }
        if ([string]::IsNullOrWhiteSpace([string]$envelope.settledRowJson)) { continue }
        $parsed = [string]$envelope.settledRowJson | ConvertFrom-Json
        $again = ConvertTo-TestPhaseExecutedChunk -Item $parsed
        $againText = ConvertTo-TestPhaseJsonText -Value $again
        if ($againText -ne [string]$envelope.settledRowJson) {
            return ('settlement-not-fixed-point ' + $file.Name)
        }
    }
    return ''
}

function Invoke-MpCorpus {
    param([string]$LogRoot, [bool]$Oracle)
    $trxDir = Join-Path $LogRoot 'trx'
    New-Item -ItemType Directory -Path $trxDir -Force | Out-Null
    $one = Join-Path $trxDir 'one.trx'
    $many = Join-Path $trxDir 'many.trx'
    $bad = Join-Path $trxDir 'bad.trx'
    $uni = 'id-' + [char]0x00E9 + '-slash\'
    New-MpTrx -Path $one -Rows @((New-MpResultRow -Id 'one-pass' -Outcome 'Passed'))
    New-MpTrx -Path $many -Rows @(
        (New-MpResultRow -Id 'many-pass' -Outcome 'Passed'),
        (New-MpResultRow -Id 'many-fail' -Outcome 'Failed'),
        (New-MpResultRow -Id 'many-skip' -Outcome 'NotExecuted'),
        (New-MpResultRow -Id $uni -Outcome 'Passed')
    )
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($bad, 'not-trx', $utf8)
    $missing = Join-Path $trxDir 'missing.trx'
    $soft = 'a' + [char]0x00AD + 'b'
    $composed = 'pre' + [char]0x00E9
    $decomposed = 'pre' + [char]0x0065 + [char]0x0301
    $date = '2026-10-30T03:02:00+05:00'
    $third = [double]1 / [double]3
    $steps = @(
        @{ Trx = $one; Suite = $date; Chunk = 'c-date'; Run = $date; Attempt = '2026-10-30T03:02:00Z'; Wall = $third; Null = $false; Exit = 0; Cov = $false; Pass = $false },
        @{ Trx = $missing; Suite = 'antiphon'; Chunk = 'c-miss'; Run = 'run-v3'; Attempt = '1'; Wall = 0; Null = $true; Exit = 0; Cov = $false; Pass = $false },
        @{ Trx = $bad; Suite = 'antiphon'; Chunk = 'c-bad'; Run = 'run-v3'; Attempt = '1'; Wall = -0.0; Null = $false; Exit = 1; Cov = $false; Pass = $false },
        @{ Trx = $many; Suite = 'suite "q" \'; Chunk = 'c-many'; Run = 'run-v3'; Attempt = '1'; Wall = 1.2345678901234567; Null = $false; Exit = 0; Cov = $true; Pass = $false },
        @{ Trx = $one; Suite = $soft; Chunk = 'k-soft'; Run = 'run-v3'; Attempt = '2'; Wall = 1; Null = $false; Exit = 0; Cov = $false; Pass = $true },
        @{ Trx = $many; Suite = 'ab'; Chunk = 'k-soft'; Run = 'run-v3'; Attempt = '9'; Wall = 2; Null = $false; Exit = 2; Cov = $false; Pass = $false },
        @{ Trx = $one; Suite = $composed; Chunk = 'k-accent'; Run = 'run-v3'; Attempt = '1'; Wall = 1; Null = $false; Exit = 0; Cov = $false; Pass = $false },
        @{ Trx = $one; Suite = $decomposed; Chunk = 'k-accent'; Run = 'run-v3'; Attempt = '1'; Wall = 4; Null = $false; Exit = 0; Cov = $false; Pass = $false },
        @{ Trx = $one; Suite = 'Antiphon'; Chunk = 'CaseChunk'; Run = 'run-v3'; Attempt = '1'; Wall = 1; Null = $false; Exit = 0; Cov = $false; Pass = $false },
        @{ Trx = $many; Suite = 'antiphon'; Chunk = 'casechunk'; Run = 'run-v3'; Attempt = '1'; Wall = 5; Null = $false; Exit = 0; Cov = $false; Pass = $true }
    )
    foreach ($step in $steps) {
        if ($Oracle) {
            $verdict = $null
            if (-not $step.Null) {
                $verdict = [pscustomobject]@{ ExitCode = [int]$step.Exit; coverageComplete = [bool]$step.Cov; testsPassed = [bool]$step.Pass }
            }
            Invoke-LegacyExecutedManifest -Verdict $verdict -LogRoot $LogRoot -TrxPath $step.Trx -Suite $step.Suite -Chunk $step.Chunk -RunId $step.Run -Attempt $step.Attempt -HostWallSeconds ([double]$step.Wall)
        } else {
            Publish-MpChunk -LogRoot $LogRoot -TrxPath $step.Trx -Suite $step.Suite -Chunk $step.Chunk -RunId $step.Run -Attempt $step.Attempt -HostWallSeconds ([double]$step.Wall) -Coverage ([bool]$step.Cov) -Passed ([bool]$step.Pass) -ExitCode ([int]$step.Exit) -NullVerdict ([bool]$step.Null)
        }
    }
}

function Invoke-MpTurkish {
    param([string]$LogRoot, [bool]$Oracle)
    $trx = Join-Path $LogRoot 'trx/tr.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'tr-one' -Outcome 'Passed'))
    foreach ($suite in @('I', 'i')) {
        if ($Oracle) {
            $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $false; testsPassed = $false }
            Invoke-LegacyExecutedManifest -Verdict $verdict -LogRoot $LogRoot -TrxPath $trx -Suite $suite -Chunk 'tr-chunk' -RunId 'run-tr' -Attempt '1' -HostWallSeconds 1
        } else {
            Publish-MpChunk -LogRoot $LogRoot -TrxPath $trx -Suite $suite -Chunk 'tr-chunk' -RunId 'run-tr' -Attempt '1' -HostWallSeconds 1
        }
    }
}

function Start-MpCase {
    param([string]$CaseName, [string]$ChildWork, [hashtable]$Env)
    New-Item -ItemType Directory -Path $ChildWork -Force | Out-Null
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'pwsh'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = '-NoProfile -NonInteractive -File "' + $PSCommandPath + '" -Case ' + $CaseName + ' -Work "' + $ChildWork + '" -Repo "' + $Repo + '"'
    foreach ($key in @($Env.Keys)) {
        $psi.Environment[[string]$key] = [string]$Env[$key]
    }
    return [System.Diagnostics.Process]::Start($psi)
}

function Wait-MpSignal {
    param([string]$Path, $Process, [int]$Milliseconds)
    $dir = Split-Path -Parent $Path
    $name = Split-Path -Leaf $Path
    if (Test-Path -LiteralPath $Path) { return 'signaled' }
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = $dir
    $watcher.Filter = $name
    $watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor [System.IO.NotifyFilters]::LastWrite
    $watcher.EnableRaisingEvents = $true
    $pending = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::Created -bor [System.IO.WatcherChangeTypes]::Changed, $Milliseconds)
    $watcher.Dispose()
    if (Test-Path -LiteralPath $Path) { return 'signaled' }
    if ($Process.HasExited) { return 'exited' }
    if ($pending.TimedOut) { return 'timeout' }
    if (Test-Path -LiteralPath $Path) { return 'signaled' }
    return 'absent'
}

function Stop-MpProcess {
    param($Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) { $Process.Kill($true) }
    } catch { }
    try { [void]$Process.WaitForExit(5000) } catch { }
}

function Read-MpChunkIds {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $doc = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
    $prior = @()
    if ($doc.PSObject.Properties.Name -contains 'chunks' -and $null -ne $doc.chunks) { $prior = @($doc.chunks) }
    elseif ($doc.PSObject.Properties.Name -contains 'chunk') { $prior = @($doc) }
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($item in $prior) {
        if ($null -eq $item) { continue }
        $ids.Add([string]$item.chunk)
    }
    return @($ids.ToArray())
}

function Invoke-V1 {
    Install-MpCounters
    $trx = Join-Path $Work 'one.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'live' -Outcome 'Passed'))
    $zero = Measure-MpPublish -Label 'p0' -PrefixChunks 0 -TrxPath $trx
    if ($zero.PriorRows -ne 0) { Fail-Mp ("prefix0 prior-rows=" + $zero.PriorRows) }
    $probe = Measure-MpPublish -Label 'p2' -PrefixChunks 2 -TrxPath $trx
    if ($probe.PriorRows -ne 0 -or -not $probe.HashSame -or $probe.Bytes -ne $zero.Bytes) {
        Fail-Mp ("prior-rows-or-cumulative-bytes prefix2 prior=" + $probe.PriorRows + " bytes=" + $probe.Bytes + " base=" + $zero.Bytes + " hashSame=" + $probe.HashSame)
    }
    $wide240 = Measure-MpPublish -Label 'p240' -PrefixChunks 240 -TrxPath $trx -WideOutcomes 90 -WideChunks 152
    if ($wide240.PriorRows -ne 0 -or -not $wide240.HashSame -or $wide240.Bytes -ne $zero.Bytes) {
        Fail-Mp ("prefix240 prior=" + $wide240.PriorRows + " bytes=" + $wide240.Bytes + " hashSame=" + $wide240.HashSame)
    }
    $wide480 = Measure-MpPublish -Label 'p480' -PrefixChunks 480 -TrxPath $trx -WideOutcomes 90 -WideChunks 304
    if ($wide480.PriorRows -ne 0 -or -not $wide480.HashSame -or $wide480.Bytes -ne $zero.Bytes) {
        Fail-Mp ("prefix480 prior=" + $wide480.PriorRows + " bytes=" + $wide480.Bytes + " hashSame=" + $wide480.HashSame)
    }
    Write-MpResult -Ok $true -Reason 'v1'
}

function Invoke-V2 {
    Install-MpCounters
    $trx = Join-Path $Work 'one.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'n' -Outcome 'Passed'))
    $totals = @{}
    foreach ($count in @(16, 32)) {
        $root = Join-Path $Work ("n" + $count)
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $script:QualificationTimingRoot = $null
        $ctx = Open-MpContext -LogRoot $root -RunId 'run-v2' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
        Reset-MpCounters
        for ($i = 0; $i -lt $count; $i++) {
            Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk ("c" + $i.ToString('00')) -RunId 'run-v2' -Attempt '1' -HostWallSeconds 1.0
        }
        $duringWrites = [int]$script:MpFlatWrites
        $duringPrior = [int]$script:MpPriorRows
        $duringBytes = [int]$script:MpDepth0Bytes
        $duringHash = [int]$script:MpHashCount
        if ($null -ne $ctx -and -not $ctx.Refused -and (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) {
            Complete-TestPhaseExecutedProjection -Context $ctx
        }
        $flat = Join-Path (Join-Path $root 'timing') 'groups-executed.json'
        $ids = @(Read-MpChunkIds -Path $flat)
        Close-MpContext -Context $ctx
        $script:QualificationTimingRoot = $null
        if ($duringPrior -ne 0 -or $duringWrites -ne 0 -or $duringHash -ne $count -or $ids.Count -ne $count) {
            Fail-Mp ("linear-bound n=" + $count + " prior=" + $duringPrior + " flatWrites=" + $duringWrites + " hashes=" + $duringHash + " chunks=" + $ids.Count + " bytes=" + $duringBytes)
        }
        $totals[[string]$count] = $duringBytes
    }
    $ratio = 99.0
    if ([int]$totals['16'] -gt 0) { $ratio = [double]$totals['32'] / [double]$totals['16'] }
    if ($ratio -ge 2.6) { Fail-Mp ("quadratic-bytes ratio=" + $ratio) }
    Write-MpResult -Ok $true -Reason 'v2'
}

function Invoke-V3Write {
    Assert-MpOffset
    $script:QualificationTimingRoot = $null
    $oracle = Join-Path $Work 'oracle'
    $prod = Join-Path $Work 'prod'
    New-Item -ItemType Directory -Path $oracle -Force | Out-Null
    New-Item -ItemType Directory -Path $prod -Force | Out-Null
    Invoke-MpCorpus -LogRoot $oracle -Oracle $true
    $ctx = Open-MpContext -LogRoot $prod -RunId '2026-10-30T03:02:00+05:00' -Attempt '2026-10-30T03:02:00Z' -Suite '2026-10-30T03:02:00+05:00' -Source 'unknown'
    Invoke-MpCorpus -LogRoot $prod -Oracle $false
    if ($null -ne $ctx -and -not $ctx.Refused -and (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) {
        Complete-TestPhaseExecutedProjection -Context $ctx
        Close-MpContext -Context $ctx
    }
    $script:QualificationTimingRoot = $null
    $left = Join-Path $oracle 'timing/groups-executed.json'
    $right = Join-Path $prod 'timing/groups-executed.json'
    if ((Get-MpSha $left) -ne (Get-MpSha $right)) { Fail-Mp 'legacy-bytes-differ' }
    $settled = Test-MpSettledJournal -Timing (Join-Path $prod 'timing')
    if ($settled) { Fail-Mp $settled }
    $fixed = Test-MpFixedPoint -Timing (Join-Path $prod 'timing')
    if ($fixed) { Fail-Mp $fixed }
    $prev = [System.Threading.Thread]::CurrentThread.CurrentCulture
    $prevUi = [System.Threading.Thread]::CurrentThread.CurrentUICulture
    $tr = [System.Globalization.CultureInfo]::GetCultureInfo('tr-TR')
    [System.Threading.Thread]::CurrentThread.CurrentCulture = $tr
    [System.Threading.Thread]::CurrentThread.CurrentUICulture = $tr
    try {
        $oracleTr = Join-Path $Work 'oracle-tr'
        $prodTr = Join-Path $Work 'prod-tr'
        $script:QualificationTimingRoot = $null
        Invoke-MpTurkish -LogRoot $oracleTr -Oracle $true
        $ctxTr = Open-MpContext -LogRoot $prodTr -RunId 'run-tr' -Attempt '1' -Suite 'I' -Source 'unknown'
        Invoke-MpTurkish -LogRoot $prodTr -Oracle $false
        if ($null -ne $ctxTr -and -not $ctxTr.Refused -and (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) {
            Complete-TestPhaseExecutedProjection -Context $ctxTr
            Close-MpContext -Context $ctxTr
        }
        $script:QualificationTimingRoot = $null
        if ((Get-MpSha (Join-Path $oracleTr 'timing/groups-executed.json')) -ne (Get-MpSha (Join-Path $prodTr 'timing/groups-executed.json'))) {
            Fail-Mp 'tr-TR-i-replacement-bytes-differ'
        }
        $ids = @(Read-MpChunkIds -Path (Join-Path $prodTr 'timing/groups-executed.json'))
        if ($ids.Count -ne 1) { Fail-Mp ("tr-TR-i-count=" + $ids.Count) }
    } finally {
        [System.Threading.Thread]::CurrentThread.CurrentCulture = $prev
        [System.Threading.Thread]::CurrentThread.CurrentUICulture = $prevUi
    }
    Write-MpResult -Ok $true -Reason 'v3-write'
}

function Invoke-V3Reduce {
    Assert-MpOffset
    $source = [Environment]::GetEnvironmentVariable('MP_SOURCE')
    $expected = [Environment]::GetEnvironmentVariable('MP_EXPECTED')
    if ([string]::IsNullOrWhiteSpace($source) -or -not (Test-Path -LiteralPath $source)) { Fail-Mp 'reduce-source-missing' }
    if (-not (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) { Fail-Mp 'projection-api-absent' }
    # Project the writer directory in place. Copying the journal would stale-bind current.json.
    $flat = Join-Path $source 'groups-executed.json'
    $state = Join-Path $source 'groups-executed.state.json'
    if (Test-Path -LiteralPath $flat) { Remove-Item -LiteralPath $flat -Force }
    if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Force }
    $logRoot = Split-Path -Parent $source
    $script:QualificationTimingRoot = $null
    Complete-TestPhaseExecutedProjection -LogRoot $logRoot
    if ((Get-MpSha $flat) -ne (Get-MpSha $expected)) { Fail-Mp 'reducer-timezone-bytes-differ' }
    $again = Get-MpSha $flat
    Complete-TestPhaseExecutedProjection -LogRoot $logRoot
    if ((Get-MpSha $flat) -ne $again) { Fail-Mp 'projection-not-stable' }
    Write-MpResult -Ok $true -Reason 'v3-reduce'
}

function Invoke-V4 {
    $script:QualificationTimingRoot = $null
    $oracle = Join-Path $Work 'oracle'
    $prod = Join-Path $Work 'prod'
    $trx = Join-Path $Work 'one.trx'
    $bad = Join-Path $Work 'bad.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 's' -Outcome 'Passed'), (New-MpResultRow -Id 'f' -Outcome 'Failed'))
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($bad, '{', $utf8)
    $single = '{"version":1,"runId":"","attempt":"","suite":"","manufacturedPassed":false,"chunk":"legacy-1","hostWallSeconds":1,"trxMissing":true,"passedCount":0,"outcomes":[],"trx":"","trxDigest":"","exitCode":null,"coverageComplete":null,"testsPassed":null}'
    foreach ($root in @($oracle, $prod)) {
        New-Item -ItemType Directory -Path (Join-Path $root 'timing') -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $root 'timing/groups-executed.json'), $single, $utf8)
    }
    $ctx = Open-MpContext -LogRoot $prod -RunId 'run-v4' -Attempt '1' -Suite 'antiphon' -Source 'src-a'
    for ($i = 0; $i -lt 64; $i++) {
        $suite = 'antiphon'
        if (($i % 2) -eq 1) { $suite = 'e2e' }
        $chunk = 'g' + ($i % 7)
        $trxPath = $trx
        if (($i % 11) -eq 0) { $trxPath = (Join-Path $Work 'missing.trx') }
        if (($i % 13) -eq 0) { $trxPath = $bad }
        $attempt = [string](1 + ($i % 3))
        $verdict = [pscustomobject]@{ ExitCode = ($i % 4); coverageComplete = (($i % 5) -eq 0); testsPassed = (($i % 4) -eq 0) }
        Invoke-LegacyExecutedManifest -Verdict $verdict -LogRoot $oracle -TrxPath $trxPath -Suite $suite -Chunk $chunk -RunId 'run-v4' -Attempt $attempt -HostWallSeconds (1.0 + $i)
        Publish-MpChunk -LogRoot $prod -TrxPath $trxPath -Suite $suite -Chunk $chunk -RunId 'run-v4' -Attempt $attempt -HostWallSeconds (1.0 + $i) -Coverage $verdict.coverageComplete -Passed $verdict.testsPassed -ExitCode ([int]$verdict.ExitCode)
    }
    if ($null -ne $ctx -and -not $ctx.Refused -and (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) {
        Complete-TestPhaseExecutedProjection -Context $ctx -Terminal 'success'
        Close-MpContext -Context $ctx
    }
    $script:QualificationTimingRoot = $null
    if ((Get-MpSha (Join-Path $oracle 'timing/groups-executed.json')) -ne (Get-MpSha (Join-Path $prod 'timing/groups-executed.json'))) {
        Fail-Mp 'legacy-seed-bytes-differ'
    }
    $settled = Test-MpSettledJournal -Timing (Join-Path $prod 'timing')
    if ($settled) { Fail-Mp $settled }
    $childWork = Join-Path $Work 'successor'
    $child = Start-MpCase -CaseName 'v4-successor' -ChildWork $childWork -Env @{ MP_SOURCE = (Join-Path $prod 'timing'); MP_RUN = 'run-v4'; MP_ATTEMPT = '1'; MP_SOURCE_ID = 'src-a' }
    if (-not $child.WaitForExit(60000)) { Stop-MpProcess $child; Fail-Mp 'successor-timeout' }
    $childResult = Join-Path $childWork 'result.json'
    if (-not (Test-Path -LiteralPath $childResult)) { Fail-Mp 'successor-no-result' }
    $parsed = [System.IO.File]::ReadAllText($childResult) | ConvertFrom-Json
    if (-not [bool]$parsed.ok) { Fail-Mp ("successor " + [string]$parsed.reason) }
    Write-MpResult -Ok $true -Reason 'v4'
}

function Invoke-V4Successor {
    $timing = [Environment]::GetEnvironmentVariable('MP_SOURCE')
    if ([string]::IsNullOrWhiteSpace($timing) -or -not (Test-Path -LiteralPath $timing)) { Fail-Mp 'successor-source-missing' }
    $logRoot = Split-Path -Parent $timing
    $ctx = Open-MpContext -LogRoot $logRoot -RunId ([Environment]::GetEnvironmentVariable('MP_RUN')) -Attempt ([Environment]::GetEnvironmentVariable('MP_ATTEMPT')) -Suite 'antiphon' -Source ([Environment]::GetEnvironmentVariable('MP_SOURCE_ID'))
    if ($null -eq $ctx) { Fail-Mp 'successor-api-absent' }
    if ($ctx.Refused) { Fail-Mp ("successor-refused " + [string]$ctx.Reason) }
    $trx = Join-Path $Work 'one.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'next' -Outcome 'Passed'))
    Publish-MpChunk -LogRoot $logRoot -TrxPath $trx -Suite 'antiphon' -Chunk 'g0' -RunId 'run-v4' -Attempt '1' -HostWallSeconds 3
    if (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue) {
        Complete-TestPhaseExecutedProjection -Context $ctx
    }
    Close-MpContext -Context $ctx
    $gens = @(Get-ChildItem -LiteralPath (Join-Path $timing 'groups-executed.journal') -Directory)
    if ($gens.Count -lt 2) { Fail-Mp ("successor-generations=" + $gens.Count) }
    Write-MpResult -Ok $true -Reason 'v4-successor'
}

function Invoke-V5Child {
    $stage = [Environment]::GetEnvironmentVariable('MP_BARRIER')
    $root = $Work
    $trx = Join-Path $root 'one.trx'
    New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'a' -Outcome 'Passed'))
    $script:TestPhaseCommitBarrier = $null
    Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk 'chunk-a' -RunId 'run-v5' -Attempt '1' -HostWallSeconds 1
    $script:TestPhaseCommitBarrier = [pscustomobject]@{
        Stage = $stage
        Signal = [Environment]::GetEnvironmentVariable('MP_SIGNAL')
        Release = [Environment]::GetEnvironmentVariable('MP_RELEASE')
    }
    Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk 'chunk-b' -RunId 'run-v5' -Attempt '1' -HostWallSeconds 1
    Write-MpResult -Ok $true -Reason 'v5-child-finished'
}

function Invoke-V5 {
    $stages = @('BeforeTempComplete', 'AfterFlushBeforeRename', 'AfterRenameBeforeAck', 'AfterAckBeforeProjection')
    foreach ($stage in $stages) {
        $childWork = Join-Path $Work $stage
        $signal = Join-Path $childWork 'signal'
        $release = Join-Path $childWork 'release'
        New-Item -ItemType Directory -Path $childWork -Force | Out-Null
        $child = Start-MpCase -CaseName 'v5-child' -ChildWork $childWork -Env @{ MP_BARRIER = $stage; MP_SIGNAL = $signal; MP_RELEASE = $release }
        $wait = Wait-MpSignal -Path $signal -Process $child -Milliseconds 20000
        if ($wait -ne 'signaled') {
            Stop-MpProcess $child
            Fail-Mp ("barrier-not-observed stage=" + $stage + " wait=" + $wait)
        }
        Stop-MpProcess $child
        # The killed process releases writer.lock. Recover this directory in place.
        $trx = Join-Path $childWork 'one.trx'
        $ctx = Open-MpContext -LogRoot $childWork -RunId 'run-v5' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
        Publish-MpChunk -LogRoot $childWork -TrxPath $trx -Suite 'antiphon' -Chunk 'chunk-c' -RunId 'run-v5' -Attempt '1' -HostWallSeconds 1
        if ($null -ne $ctx -and (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue)) {
            Complete-TestPhaseExecutedProjection -Context $ctx
        }
        Close-MpContext -Context $ctx
        $ids = @(Read-MpChunkIds -Path (Join-Path $childWork 'timing/groups-executed.json'))
        $expectB = ($stage -eq 'AfterRenameBeforeAck' -or $stage -eq 'AfterAckBeforeProjection')
        $hasA = $ids -contains 'chunk-a'
        $hasB = $ids -contains 'chunk-b'
        $hasC = $ids -contains 'chunk-c'
        if (-not $hasA -or -not $hasC -or ($hasB -ne $expectB) -or (($ids | Group-Object).Count -ne $ids.Count)) {
            Fail-Mp ("recovered-prefix stage=" + $stage + " ids=" + ($ids -join ','))
        }
        $savedTiming = $script:QualificationTimingRoot
        try {
            $script:QualificationTimingRoot = Join-Path $childWork 'oracle'
            $expectedChunks = @('chunk-a')
            if ($expectB) { $expectedChunks += 'chunk-b' }
            $expectedChunks += 'chunk-c'
            foreach ($chunkId in $expectedChunks) {
                $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $false; testsPassed = $false }
                Invoke-LegacyExecutedManifest -Verdict $verdict -LogRoot $childWork -TrxPath $trx -Suite 'antiphon' -Chunk $chunkId -RunId 'run-v5' -Attempt '1' -HostWallSeconds 1
            }
            if ((Get-MpSha (Join-Path $script:QualificationTimingRoot 'groups-executed.json')) -ne (Get-MpSha (Join-Path $childWork 'timing/groups-executed.json'))) { Fail-Mp ('recovered-prefix-bytes-differ ' + $stage) }
        } finally { $script:QualificationTimingRoot = $savedTiming }
    }
    Write-MpResult -Ok $true -Reason 'v5'
}


function Assert-MpSuccessorPrefixes {
    foreach ($mode in @('torn', 'sealed', 'identity')) {
        $root = Join-Path $Work ('prefix-' + $mode)
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $ctx = Open-MpContext -LogRoot $root -RunId 'prefix' -Attempt '1' -Suite 'antiphon' -Source 'src-a'
        if ($null -eq $ctx -or $ctx.Refused) { Fail-Mp ('prefix-parent-refused ' + $mode) }
        $trx = Join-Path $root 'one.trx'
        New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'kept' -Outcome 'Passed'))
        for ($i = 1; $i -le 3; $i++) {
            Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk ('kept-' + $i) -RunId 'prefix' -Attempt '1' -HostWallSeconds $i
        }
        $expected = Get-TestPhaseProjectedDocument -Context $ctx
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText((Join-Path $root 'expected.json'), $expected, $utf8)
        $parent = [string]$ctx.Generation
        if ($mode -eq 'sealed') { Complete-TestPhaseExecutedProjection -Context $ctx -Terminal 'success' | Out-Null }
        if ($mode -eq 'torn') {
            Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk 'excluded-4' -RunId 'prefix' -Attempt '1' -HostWallSeconds 4
            $bad = Join-Path (Join-Path (Join-Path $root 'timing/groups-executed.journal') $parent) '00000004.json'
            [System.IO.File]::WriteAllText($bad, '{"torn":', $utf8)
        }
        Close-MpContext -Context $ctx
        $before = @{}
        $parentDir = Join-Path (Join-Path $root 'timing/groups-executed.journal') $parent
        foreach ($file in @(Get-ChildItem -LiteralPath $parentDir -File)) { $before[$file.FullName] = Get-MpSha $file.FullName }
        $source = 'src-a'
        if ($mode -eq 'identity') { $source = 'src-b' }
        $childWork = Join-Path $root 'child'
        $child = Start-MpCase -CaseName 'prefix-successor' -ChildWork $childWork -Env @{ MP_SOURCE = $root; MP_SOURCE_ID = $source; MP_PARENT = $parent; MP_PREFIX_MODE = $mode }
        if (-not $child.WaitForExit(30000)) { Stop-MpProcess $child; Fail-Mp ('prefix-child-timeout ' + $mode) }
        $result = Get-Content -Raw (Join-Path $childWork 'result.json') | ConvertFrom-Json
        if (-not $result.ok) { Fail-Mp ([string]$result.reason) }
        foreach ($file in $before.Keys) { if ((Get-MpSha $file) -ne $before[$file]) { Fail-Mp ('prefix-parent-mutated ' + $mode) } }
    }
}

function Invoke-MpPrefixSuccessor {
    $root = $env:MP_SOURCE
    $mode = $env:MP_PREFIX_MODE
    $ctx = Open-MpContext -LogRoot $root -RunId 'prefix' -Attempt '1' -Suite 'antiphon' -Source $env:MP_SOURCE_ID
    if ($null -eq $ctx -or $ctx.Refused) { Fail-Mp ('prefix-successor-refused ' + $mode) }
    try {
        if ($ctx.Generation -eq $env:MP_PARENT) { Fail-Mp ('prefix-generation-not-advanced ' + $mode) }
        $expected = Get-MpSha (Join-Path $root 'expected.json')
        if (-not $ctx.HasSeed -or (Get-MpSha $ctx.SeedPath) -ne $expected) { Fail-Mp ('successor-seed-lost-valid-prefix ' + $mode) }
        Complete-TestPhaseExecutedProjection -Context $ctx | Out-Null
        $flat = Join-Path $root 'timing/groups-executed.json'
        if ((Get-MpSha $flat) -ne $expected) { Fail-Mp ('successor-projection-lost-valid-prefix ' + $mode) }
        $ids = @(Read-MpChunkIds -Path $flat)
        if (($ids -join ',') -ne 'kept-1,kept-2,kept-3') { Fail-Mp ('successor-prefix-rows ' + $mode) }
        if ($mode -eq 'torn' -and -not $ctx.Unresolved) { Fail-Mp 'successor-cleared-corruption' }
    } finally { Close-MpContext -Context $ctx }
    Write-MpResult -Ok $true -Reason ('prefix-' + $mode)
}

function Invoke-V6 {
    Assert-MpSuccessorPrefixes
    $malformed = 'NOT JSON {'
    $root = Join-Path $Work 'malformed'
    New-Item -ItemType Directory -Path (Join-Path $root 'timing') -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $flat = Join-Path $root 'timing/groups-executed.json'
    [System.IO.File]::WriteAllText($flat, $malformed, $utf8)
    [System.IO.File]::WriteAllText((Join-Path $root 'timing/groups-planned.json'), '{"version":1,"runId":"run-v6","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}', $utf8)
    $threw = ''
    try {
        $ctx = Open-MpContext -LogRoot $root -RunId 'run-v6' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
        if ($null -eq $ctx) { Fail-Mp 'corruption-successor-absent' }
        if ($ctx.Refused) { Fail-Mp ("corruption-refused " + [string]$ctx.Reason) }
        $trx = Join-Path $root 'one.trx'
        New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'kept' -Outcome 'Passed'))
        Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk 'c-new' -RunId 'run-v6' -Attempt '1' -HostWallSeconds 1
        if (Get-Command Complete-TestPhaseExecutedProjection -ErrorAction SilentlyContinue) {
            Complete-TestPhaseExecutedProjection -Context $ctx
        }
        Close-MpContext -Context $ctx
    } catch {
        $threw = [string]$_.Exception.Message
        Fail-Mp ("corruption-not-isolated " + $threw)
    }
    $preserved = $false
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'timing') -Recurse -File)
    foreach ($file in $files) {
        if ([System.IO.File]::ReadAllText($file.FullName) -eq $malformed) { $preserved = $true }
    }
    if (-not $preserved) { Fail-Mp 'malformed-bytes-not-preserved' }
    $statePath = Join-Path $root 'timing/groups-executed.state.json'
    if (-not (Test-Path -LiteralPath $statePath)) { Fail-Mp 'publication-state-missing-after-corruption' }
    $state = [System.IO.File]::ReadAllText($statePath) | ConvertFrom-Json
    if ($state.publicationComplete -ne $false) { Fail-Mp 'publicationComplete-not-false' }
    Invoke-TestPhaseRunSummary -RunDirectory $root -Top 5
    $summary = [System.IO.File]::ReadAllText((Join-Path $root 'timing/summary.json')) | ConvertFrom-Json
    if ($summary.complete -ne $false) { Fail-Mp 'summary-complete-despite-corruption' }
    $quality = @($summary.quality)
    $has = $false
    foreach ($token in $quality) { if ([string]$token -eq 'publication-corruption') { $has = $true } }
    if (-not $has) { Fail-Mp 'summary-missing-publication-corruption' }
    Write-MpResult -Ok $true -Reason 'v6'
}

function Invoke-V7Holder {
    $ctx = Open-MpContext -LogRoot $Work -RunId 'run-v7' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    if ($null -eq $ctx) { Fail-Mp 'exclusive-lock-absent' }
    if ($ctx.Refused) { Fail-Mp ("holder-refused " + [string]$ctx.Reason) }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText([Environment]::GetEnvironmentVariable('MP_SIGNAL'), 'held', $utf8)
    $release = [Environment]::GetEnvironmentVariable('MP_RELEASE')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $release)) {
        if ($watch.ElapsedMilliseconds -gt 30000) { Fail-Mp 'holder-release-timeout' }
        Start-Sleep -Milliseconds 30
    }
    Close-MpContext -Context $ctx
    Write-MpResult -Ok $true -Reason 'v7-holder'
}

function Invoke-V7 {
    if (-not (Get-Command Open-TestPhaseExecutedContext -ErrorAction SilentlyContinue)) {
        $root = Join-Path $Work 'touched'
        $trx = Join-Path $root 'one.trx'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        New-MpTrx -Path $trx -Rows @((New-MpResultRow -Id 'a' -Outcome 'Passed'))
        Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'antiphon' -Chunk 'c' -RunId 'run-v7' -Attempt '1' -HostWallSeconds 1
        Fail-Mp 'exclusive-lock-absent'
    }
    $holderWork = Join-Path $Work 'holder'
    $signal = Join-Path $holderWork 'signal'
    $release = Join-Path $holderWork 'release'
    $holder = Start-MpCase -CaseName 'v7-holder' -ChildWork $holderWork -Env @{ MP_SIGNAL = $signal; MP_RELEASE = $release }
    $wait = Wait-MpSignal -Path $signal -Process $holder -Milliseconds 20000
    if ($wait -ne 'signaled') { Stop-MpProcess $holder; Fail-Mp ("lock-holder-not-observed " + $wait) }
    $second = Open-MpContext -LogRoot $holderWork -RunId 'run-v7' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    $refused = ($null -ne $second -and [bool]$second.Refused)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($release, 'go', $utf8)
    if (-not $holder.WaitForExit(20000)) { Stop-MpProcess $holder }
    if (-not $refused) { Fail-Mp 'second-writer-not-refused' }
    $other = Join-Path $Work 'other-root'
    New-Item -ItemType Directory -Path $other -Force | Out-Null
    $otherCtx = Open-MpContext -LogRoot $other -RunId 'run-v7' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    if ($null -eq $otherCtx -or $otherCtx.Refused) { Fail-Mp 'independent-root-refused' }
    Close-MpContext -Context $otherCtx
    $after = Open-MpContext -LogRoot $holderWork -RunId 'run-v7' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    if ($null -eq $after -or $after.Refused) { Fail-Mp 'resume-after-release-refused' }
    Close-MpContext -Context $after
    Write-MpResult -Ok $true -Reason 'v7'
}

function Invoke-V8 {
    $childWork = Join-Path $Work 'held'
    $signal = Join-Path $childWork 'signal'
    $release = Join-Path $childWork 'release'
    New-Item -ItemType Directory -Path $childWork -Force | Out-Null
    $child = Start-MpCase -CaseName 'v5-child' -ChildWork $childWork -Env @{ MP_BARRIER = 'AfterRenameBeforeAck'; MP_SIGNAL = $signal; MP_RELEASE = $release }
    $wait = Wait-MpSignal -Path $signal -Process $child -Milliseconds 20000
    if ($wait -ne 'signaled') { Stop-MpProcess $child; Fail-Mp ("live-reader-barrier-absent " + $wait) }
    if (-not (Get-Command Read-TestPhaseExecutedPrefix -ErrorAction SilentlyContinue)) {
        Stop-MpProcess $child
        Fail-Mp 'live-reader-api-absent'
    }
    $prefix = Read-TestPhaseExecutedPrefix -LogRoot $childWork
    $json = $prefix | ConvertTo-Json -Compress -Depth 6
    $null = $json | ConvertFrom-Json
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $childWork 'timing/groups-planned.json'), '{"version":1,"runId":"run-v5","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}', $utf8)
    $before = Get-MpSha (Join-Path $childWork 'timing/groups-executed.journal/current.json')
    Invoke-TestPhaseRunSummary -RunDirectory $childWork -Top 5
    $after = Get-MpSha (Join-Path $childWork 'timing/groups-executed.journal/current.json')
    if ($before -ne $after) { Fail-Mp 'summary-mutated-writer-state' }
    [System.IO.File]::WriteAllText($release, 'go', $utf8)
    Stop-MpProcess $child
    Write-MpResult -Ok $true -Reason 'v8'
}

function New-MpDiagRecord {
    param([string]$Uid, [string]$TypeName, [string]$Method, [string]$StateName)
    $state = $StateName + 'TestNodeStateProperty { Explanation = ok }'
    return ('TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = ' + $Uid + ' }, DisplayName = ' + $Method + ', Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = ' + $TypeName + ', MethodName = ' + $Method + ', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ' + $state + ' ] } }')
}

function Write-MpDiagFile {
    param([string]$Directory, [string[]]$Records)
    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    }
    $text = "Version: '1.44.0.0'`nVersion: 2.2.2`n" + (@($Records) -join "`n") + "`n"
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $Directory 'fixture.diag'), $text, $utf8)
}

function Write-MpDefinedTrx {
    param([string]$Path, [string]$ClassName, [string]$Outcome)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions><UnitTest id="t1" name="Plain"><TestMethod className="' + $ClassName + '" name="Plain" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="t1" testName="Plain" outcome="' + $Outcome + '" /></Results></TestRun>'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $xml, $utf8)
}

function Get-MpArgValue {
    param([string[]]$ArgumentList, [string]$Name)
    $items = @($ArgumentList)
    for ($i = 0; $i -lt $items.Count; $i++) {
        if ([string]$items[$i] -eq $Name -and ($i + 1) -lt $items.Count) { return [string]$items[$i + 1] }
    }
    return ''
}

function Get-MpFilterClass {
    param([string]$Filter)
    if ($Filter -match '^/\*/([^/]+)/([^/]+)/\*$') { return ($Matches[1] + '.' + $Matches[2]) }
    return ''
}

function New-MpScopedPolicy {
    param([string]$Path, [string[]]$Suites, [hashtable]$ChunkClasses)
    $obj = Get-Content -Raw (Join-Path $Repo 'tests/test-execution-policy.json') | ConvertFrom-Json
    $obj.profiles.nightly.requiredSuites = @($Suites)
    $existingSuites = @($obj.suites.PSObject.Properties.Name)
    foreach ($name in @($Suites)) {
        if ($existingSuites -contains $name) { continue }
        $obj.suites | Add-Member -NotePropertyName $name -NotePropertyValue ([pscustomobject]@{
            processGroup = 'native-dotnet'
            name = $name
            mode = 'unattended'
            assembly = 'Antiphon.MarkdownPdf.Tests'
            project = 'tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj'
        }) -Force
    }
    $chunks = [pscustomobject]@{}
    foreach ($name in @($ChunkClasses.Keys)) {
        $classes = @($ChunkClasses[$name])
        $row = [pscustomobject]@{ id = 'same'; classes = @($classes) }
        $chunks | Add-Member -NotePropertyName $name -NotePropertyValue @($row) -Force
    }
    $obj | Add-Member -NotePropertyName chunks -NotePropertyValue $chunks -Force
    $obj.policyHash = Get-NightlyPolicyHash -Object $obj
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, ($obj | ConvertTo-Json -Depth 30), $utf8)
}

function Write-MpSeam {
    param([string]$Path)
    $text = @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $state = [string]$env:MP_STATE
        $items = @($ArgumentList)
        $argsText = $items -join " "
        Add-Content -LiteralPath (Join-Path $state "launches.txt") -Value $argsText -Encoding ascii
        $classes = @([System.IO.File]::ReadAllLines((Join-Path $state "classes.txt")))
        if ($argsText -match "--list-tests") {
            $diag = Get-MpArgValue -ArgumentList $items -Name "--diagnostic-output-directory"
            $records = @()
            foreach ($className in $classes) {
                if ([string]::IsNullOrWhiteSpace($className)) { continue }
                $records += (New-MpDiagRecord -Uid ("uid-" + $className) -TypeName $className -Method "Plain" -StateName "Discovered")
            }
            Write-MpDiagFile -Directory $diag -Records $records
            return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        if ($argsText -notmatch "--report-trx") {
            return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        $n = 0
        $countPath = Join-Path $state "report-count.txt"
        if (Test-Path -LiteralPath $countPath) { $n = [int][System.IO.File]::ReadAllText($countPath) }
        $n++
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($countPath, ([string]$n), $utf8)
        $timing = [string]$env:MP_TIMING
        $flat = Join-Path $timing "groups-executed.json"
        if ($n -ge 2) {
            $flag = "absent"
            if (Test-Path -LiteralPath $flat) { $flag = "present" }
            [System.IO.File]::WriteAllText((Join-Path $state "flat-at-barrier.txt"), $flag, $utf8)
            $logRoot = [string]$env:MP_LOG
            $stamp = ""
            if (Test-Path -LiteralPath $logRoot) {
                $logs = @(Get-ChildItem -LiteralPath $logRoot -Filter "*-tests.log" -File -ErrorAction SilentlyContinue)
                foreach ($log in $logs) {
                    $raw = [System.IO.File]::ReadAllText($log.FullName)
                    if ($raw -match "C487_INVOCATION=") { $stamp = "stamped" }
                }
            }
            if ([string]::IsNullOrWhiteSpace($stamp)) { $stamp = "unstamped" }
            [System.IO.File]::WriteAllText((Join-Path $state "stamp-at-barrier.txt"), $stamp, $utf8)
        }
        $mode = [string]$env:MP_MODE
        if ($mode -eq "throw" -and $n -eq 1) { throw "mp-orchestration" }
        if ($mode -eq "no-planned") {
            $planned = Join-Path $timing "groups-planned.json"
            if (Test-Path -LiteralPath $planned) { Remove-Item -LiteralPath $planned -Force }
            $index = Join-Path $timing "groups-planned-index.json"
            if (Test-Path -LiteralPath $index) { Remove-Item -LiteralPath $index -Force }
        }
        $faultAt = 0
        $faultPath = Join-Path $state "fault-at.txt"
        if (Test-Path -LiteralPath $faultPath) { $faultAt = [int][System.IO.File]::ReadAllText($faultPath) }
        $faultUntil = 0
        $untilPath = Join-Path $state "fault-until.txt"
        if (Test-Path -LiteralPath $untilPath) { $faultUntil = [int][System.IO.File]::ReadAllText($untilPath) }
        $stage = "Rename"
        $stagePath = Join-Path $state "fault-stage.txt"
        if (Test-Path -LiteralPath $stagePath) {
            $stageText = [System.IO.File]::ReadAllText($stagePath).Trim()
            if (-not [string]::IsNullOrWhiteSpace($stageText)) { $stage = $stageText }
        }
        $inWindow = $false
        if ($faultAt -gt 0 -and $n -ge $faultAt) {
            if ($faultUntil -le 0 -or $n -le $faultUntil) { $inWindow = $true }
        }
        if ($inWindow) {
            $remaining = 2
            $remainPath = Join-Path $state "fault-remaining.txt"
            if (Test-Path -LiteralPath $remainPath) { $remaining = [int][System.IO.File]::ReadAllText($remainPath) }
            $script:TestPhaseCommitFault = [pscustomobject]@{ Stage = $stage; Remaining = $remaining }
        } elseif ($faultAt -gt 0) {
            $script:TestPhaseCommitFault = $null
        }
        $filter = Get-MpArgValue -ArgumentList $items -Name "--treenode-filter"
        $className = Get-MpFilterClass -Filter $filter
        if ([string]::IsNullOrWhiteSpace($className)) { $className = [string]$classes[0] }
        $outcome = "Passed"
        $exitCode = 0
        $timedOut = $false
        if ($mode -eq "failed") { $outcome = "Failed"; $exitCode = 1 }
        if ($mode -eq "timeout") { $timedOut = $true }
        if ($mode -ne "missing-trx") {
            $trxName = Get-MpArgValue -ArgumentList $items -Name "--report-trx-filename"
            $results = Get-MpArgValue -ArgumentList $items -Name "--results-directory"
            if ($trxName -and $results) { Write-MpDefinedTrx -Path (Join-Path $results $trxName) -ClassName $className -Outcome $outcome }
            $exec = Get-MpArgValue -ArgumentList $items -Name "--diagnostic-output-directory"
            $stateName = "Passed"
            if ($outcome -eq "Failed") { $stateName = "Failed" }
            Write-MpDiagFile -Directory $exec -Records @((New-MpDiagRecord -Uid ("uid-" + $className) -TypeName $className -Method "Plain" -StateName $stateName))
        }
        $drain = 10 + $n
        return [pscustomobject]@{ ExitCode = $exitCode; TimedOut = $timedOut; ChildrenExited = $true; CleanupComplete = $true; Pid = (40 + $n); LogText = ""; RootStartedAtUtc = "2026-10-30T03:02:00Z"; RootExitedAtUtc = "2026-10-30T03:03:00Z"; JobEmptyObserved = $true; DrainMilliseconds = $drain }
    }
}
'@
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $text, $utf8)
}

function Invoke-MpQualification {
    param(
        [string]$Name,
        [string]$Mode,
        [string[]]$Suites,
        [hashtable]$ChunkClasses,
        [int]$FaultAt = 0,
        [int]$FaultUntil = 0,
        [string]$FaultStage = 'Rename',
        [int]$FaultRemaining = 2,
        [string]$PublicationMode = ''
    )
    $root = Join-Path $Work $Name
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $classes = New-Object System.Collections.Generic.List[string]
    foreach ($nameKey in @($ChunkClasses.Keys)) {
        foreach ($className in @($ChunkClasses[$nameKey])) { $classes.Add([string]$className) }
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines((Join-Path $state 'classes.txt'), $classes.ToArray(), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $state 'fault-at.txt'), ([string]$FaultAt), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $state 'fault-until.txt'), ([string]$FaultUntil), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $state 'fault-stage.txt'), ([string]$FaultStage), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $state 'fault-remaining.txt'), ([string]$FaultRemaining), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $state 'report-count.txt'), '0', $utf8)
    $policy = Join-Path $root 'policy.json'
    New-MpScopedPolicy -Path $policy -Suites $Suites -ChunkClasses $ChunkClasses
    $seam = Join-Path $root 'seams.ps1'
    Write-MpSeam -Path $seam
    $log = Join-Path $root 'log'
    $timing = Join-Path $root 'timing'
    $env:MP_STATE = $state
    $env:MP_TIMING = $timing
    $env:MP_LOG = $log
    $env:MP_MODE = $Mode
    $script:TestPhaseCommitFault = $null
    $script:TestPhaseExecutedContext = $null
    $openOriginal = ${function:Open-TestPhaseExecutedContext}
    $projectOriginal = ${function:Complete-TestPhaseExecutedProjection}
    $heldWriter = $null
    if ($PublicationMode -eq 'none') {
        Set-Item function:Open-TestPhaseExecutedContext -Value {
            param($LogRoot, $TimingRoot, $RunId, $Attempt, $Suite, $Source, $InvocationId, [switch]$ProjectOnly, [switch]$Recover)
            return (New-TestPhaseRefusedContext -Reason 'witness-no-journal')
        }
    } elseif ($PublicationMode -eq 'missing-state') {
        Set-Item function:Complete-TestPhaseExecutedProjection -Value {
            param($Context, $LogRoot, $TimingRoot, $Terminal)
            & $projectOriginal @PSBoundParameters | Out-Null
            if ($null -ne $Context -and -not $Context.Refused) {
                Remove-Item -LiteralPath (Join-Path $Context.TimingRoot 'groups-executed.state.json') -Force
            }
        }
    } elseif ($PublicationMode -eq 'torn') {
        $seed = Open-TestPhaseExecutedContext -LogRoot $log -TimingRoot $timing -RunId ('mp-' + $Name) -Attempt '1' -Suite '' -Source ('a' * 40)
        if ($seed.Refused) { Fail-Mp 'verdict-torn-setup-refused' }
        $bad = Join-Path (Join-Path (Join-Path $timing 'groups-executed.journal') $seed.Generation) '00000001.json'
        [System.IO.File]::WriteAllText($bad, '{"torn":', $utf8)
        Close-TestPhaseExecutedContext -Context $seed
    } elseif ($PublicationMode -eq 'held') {
        $journal = Join-Path $timing 'groups-executed.journal'
        New-Item -ItemType Directory -Path $journal -Force | Out-Null
        $heldWriter = [System.IO.File]::Open((Join-Path $journal 'writer.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $result = Invoke-AntiphonNightlyTests -RepoRoot $Repo -LogRoot $log -TimingRoot $timing -Suites $Suites -Profile 'nightly' -Sha ('a' * 40) -GitRef 'origin/master' -RunId ('mp-' + $Name) -Attempt 1 -Trigger 'manual' -SeamsPath $seam -PolicyPath $policy -AllowSharedTree
    } finally {
        Set-Item function:Open-TestPhaseExecutedContext -Value $openOriginal
        Set-Item function:Complete-TestPhaseExecutedProjection -Value $projectOriginal
        if ($null -ne $heldWriter) { $heldWriter.Dispose() }
        $ErrorActionPreference = $previous
        Remove-Item Env:MP_STATE -ErrorAction SilentlyContinue
        Remove-Item Env:MP_TIMING -ErrorAction SilentlyContinue
        Remove-Item Env:MP_LOG -ErrorAction SilentlyContinue
        Remove-Item Env:MP_MODE -ErrorAction SilentlyContinue
        $script:TestPhaseCommitFault = $null
        $script:TestPhaseExecutedContext = $null
    }
    $summaryPath = Join-Path $log 'summary.json'
    $summary = $null
    if (Test-Path -LiteralPath $summaryPath) { $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json }
    $seal = $null
    $currentPath = Join-Path (Join-Path $timing 'groups-executed.journal') 'current.json'
    if (Test-Path -LiteralPath $currentPath) {
        $current = Get-Content -Raw -LiteralPath $currentPath | ConvertFrom-Json
        $sealPath = Join-Path (Join-Path (Join-Path $timing 'groups-executed.journal') ([string]$current.generation)) 'seal.json'
        if (Test-Path -LiteralPath $sealPath) { $seal = Get-Content -Raw -LiteralPath $sealPath | ConvertFrom-Json }
    }
    $flatFlag = ''
    $flatPath = Join-Path $state 'flat-at-barrier.txt'
    if (Test-Path -LiteralPath $flatPath) { $flatFlag = [System.IO.File]::ReadAllText($flatPath).Trim() }
    $stampFlag = ''
    $stampPath = Join-Path $state 'stamp-at-barrier.txt'
    if (Test-Path -LiteralPath $stampPath) { $stampFlag = [System.IO.File]::ReadAllText($stampPath).Trim() }
    return [pscustomobject]@{
        Name = $Name
        Result = $result
        Summary = $summary
        Seal = $seal
        FlatAtBarrier = $flatFlag
        StampAtBarrier = $stampFlag
        Timing = $timing
        Log = $log
        State = $state
    }
}

function Assert-MpTerminalPublication {
    param($Run, [string]$Terminal, [switch]$RequireBarrier)
    if ($null -eq $Run.Summary) { Fail-Mp ($Run.Name + ' receipt-absent') }
    if ($RequireBarrier -and $Run.FlatAtBarrier -eq 'present') { Fail-Mp ($Run.Name + ' flat-present-before-terminal') }
    if ($RequireBarrier -and $Run.FlatAtBarrier -ne 'absent') { Fail-Mp ($Run.Name + ' barrier-not-reached ' + $Run.FlatAtBarrier) }
    if ($null -eq $Run.Seal) { Fail-Mp ($Run.Name + ' terminal-seal-absent') }
    if ([string]$Run.Seal.terminal -ne $Terminal) {
        $reasons = ''
        if ($null -ne $Run.Summary -and $Run.Summary.PSObject.Properties.Name -contains 'reasons') { $reasons = @($Run.Summary.reasons) -join ';' }
        Fail-Mp ($Run.Name + ' terminal=' + [string]$Run.Seal.terminal + ' expected=' + $Terminal + ' reasons=' + $reasons)
    }
    $records = @(Get-ChildItem -LiteralPath (Join-Path (Join-Path $Run.Timing 'groups-executed.journal') ([string]$Run.Seal.generation)) -Filter '????????.json' -File -ErrorAction SilentlyContinue)
    if ($records.Count -lt 1 -and $Terminal -ne 'orchestration-error') { Fail-Mp ($Run.Name + ' no-committed-chunk') }
    $flat = Join-Path $Run.Timing 'groups-executed.json'
    if (-not (Test-Path -LiteralPath $flat) -and $Terminal -ne 'orchestration-error') { Fail-Mp ($Run.Name + ' final-projection-absent') }
}

function Invoke-V9 {
    $one = @{ 'markdown-pdf' = @('Fixture.One', 'Fixture.Two') }
    $suites = @('markdown-pdf')
    $ok = Invoke-MpQualification -Name 'success' -Mode 'success' -Suites $suites -ChunkClasses $one
    Assert-MpTerminalPublication -Run $ok -Terminal 'success' -RequireBarrier
    $failed = Invoke-MpQualification -Name 'failed' -Mode 'failed' -Suites $suites -ChunkClasses $one
    Assert-MpTerminalPublication -Run $failed -Terminal 'failed-run' -RequireBarrier
    if ([int]$failed.Result.ExitCode -eq 0) { Fail-Mp 'failed-child-became-success' }
    $timeout = Invoke-MpQualification -Name 'timeout' -Mode 'timeout' -Suites $suites -ChunkClasses $one
    Assert-MpTerminalPublication -Run $timeout -Terminal 'timeout' -RequireBarrier
    $missing = Invoke-MpQualification -Name 'missing' -Mode 'missing-trx' -Suites $suites -ChunkClasses $one
    Assert-MpTerminalPublication -Run $missing -Terminal 'failed-run' -RequireBarrier
    $thrown = Invoke-MpQualification -Name 'thrown' -Mode 'throw' -Suites $suites -ChunkClasses @{ 'markdown-pdf' = @('Fixture.One') }
    Assert-MpTerminalPublication -Run $thrown -Terminal 'orchestration-error'
    if ([string]$thrown.Summary.outcome -eq '') { Fail-Mp 'orchestration-receipt-empty' }
    $bare = Invoke-MpQualification -Name 'bare' -Mode 'no-planned' -Suites $suites -ChunkClasses $one
    Assert-MpTerminalPublication -Run $bare -Terminal 'success' -RequireBarrier
    if (Test-Path -LiteralPath (Join-Path $bare.Timing 'groups-planned.json')) { Fail-Mp 'planned-document-returned' }
    Write-MpResult -Ok $true -Reason 'v9'
}

function Assert-MpSeededSummaryLabels {
    foreach ($mode in @('legacy', 'successor')) {
        $root = Join-Path $Work ('summary-seed-' + $mode)
        New-Item -ItemType Directory -Path (Join-Path $root 'timing') -Force | Out-Null
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText((Join-Path $root 'timing/groups-planned.json'), '{"version":1,"runId":"labels","attempt":"1","suite":"suite-a","assembly":"Antiphon.Tests","groups":[]}', $utf8)
        $trx = Join-Path $root 'a.trx'
        Write-MpDefinedTrx -Path $trx -ClassName 'Fixture.Seeded' -Outcome 'Passed'
        $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
        if ($mode -eq 'legacy') {
            Invoke-LegacyExecutedManifest -Verdict $verdict -LogRoot $root -TrxPath $trx -Suite 'suite-a' -Chunk 'chunk-a' -RunId 'labels' -Attempt '1' -HostWallSeconds 1
        } else {
            $ctx = Open-MpContext -LogRoot $root -RunId 'labels' -Attempt '1' -Suite 'suite-a' -Source 'unknown'
            Publish-MpChunk -LogRoot $root -TrxPath $trx -Suite 'suite-a' -Chunk 'chunk-a' -RunId 'labels' -Attempt '1' -HostWallSeconds 1
            Complete-TestPhaseExecutedProjection -Context $ctx -Terminal 'orchestration-error' | Out-Null
            Close-MpContext -Context $ctx
        }
        $ctx = Open-MpContext -LogRoot $root -RunId 'labels' -Attempt '1' -Suite 'suite-a' -Source 'unknown'
        if ($ctx.Refused -or -not $ctx.HasSeed) { Fail-Mp ('labels-seed-not-created ' + $mode) }
        $trxB = Join-Path $root 'b.trx'
        Write-MpDefinedTrx -Path $trxB -ClassName 'Fixture.Current' -Outcome 'Failed'
        Publish-MpChunk -LogRoot $root -TrxPath $trxB -Suite 'suite-b' -Chunk 'chunk-b' -RunId 'labels' -Attempt '1' -HostWallSeconds 2
        Complete-TestPhaseExecutedProjection -Context $ctx | Out-Null
        Close-MpContext -Context $ctx
        Invoke-TestPhaseRunSummary -RunDirectory $root -Top 5
        $summary = Get-Content -Raw (Join-Path $root 'timing/summary.json') | ConvertFrom-Json
        $seeded = @($summary.tests | Where-Object { $_.groupId -eq 'chunk-a' -and $_.suite -eq 'suite-a' })
        $current = @($summary.tests | Where-Object { $_.groupId -eq 'chunk-b' -and $_.suite -eq 'suite-b' })
        if ($seeded.Count -ne 1 -or $current.Count -ne 1) { Fail-Mp ('seeded-summary-labels-lost ' + $mode) }
        $csv = @(Import-Csv -LiteralPath (Join-Path $root 'timing/tests.csv'))
        if (@($csv | Where-Object { $_.groupId -eq 'chunk-a' }).Count -ne 1) { Fail-Mp ('seeded-csv-labels-lost ' + $mode) }
    }
}

function Invoke-V10 {
    Assert-MpSeededSummaryLabels
    $legacy = Join-Path $Work 'legacy'
    New-Item -ItemType Directory -Path (Join-Path $legacy 'timing') -Force | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $legacy 'timing/groups-planned.json'), '{"version":1,"runId":"run-v10","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}', $utf8)
    [System.IO.File]::WriteAllText((Join-Path $legacy 'timing/groups-executed.json'), '{"version":1,"runId":"run-v10","attempt":"1","suite":"antiphon","manufacturedPassed":false,"chunks":[{"chunk":"c1","runId":"run-v10","attempt":"1","suite":"antiphon","hostWallSeconds":1,"trxMissing":true,"manufacturedPassed":false,"passedCount":0,"outcomes":[],"trx":"one.trx","trxDigest":"","exitCode":0,"coverageComplete":false,"testsPassed":false}]}', $utf8)
    Write-MpDefinedTrx -Path (Join-Path $legacy 'one.trx') -ClassName 'Fixture.One' -Outcome 'Passed'
    & (Join-Path $Repo 'scripts/summarize-test-phases.ps1') -RunDirectory $legacy -Top 5
    $legacySummary = Get-Content -Raw (Join-Path $legacy 'timing/summary.json') | ConvertFrom-Json
    $legacyQuality = @($legacySummary.quality)
    if ([bool]$legacySummary.complete) { Fail-Mp 'legacy-complete-without-state' }
    if ($legacyQuality -notcontains 'publication-state-missing') { Fail-Mp 'legacy-missing-state-not-recorded' }
    $launch = Join-Path $Work 'launch'
    New-Item -ItemType Directory -Path (Join-Path $launch 'timing') -Force | Out-Null
    $launchTrx = Join-Path $launch 'launch.trx'
    Write-MpDefinedTrx -Path $launchTrx -ClassName 'Fixture.Launch' -Outcome 'Passed'
    [System.IO.File]::WriteAllText((Join-Path $launch 'timing/groups-planned.json'), '{"version":1,"runId":"run-v10","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}', $utf8)
    $script:TestPhaseExecutedContext = $null
    $launchVerdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
    Complete-TestPhaseLaunch -Verdict $launchVerdict -LogRoot $launch -TrxPath $launchTrx -Suite 'antiphon' -Chunk 'c-launch' -RunId 'run-v10' -Attempt '1' -HostWallSeconds 1
    if (-not (Test-Path -LiteralPath (Join-Path $launch 'timing/groups-executed.json'))) { Fail-Mp 'launch-snapshot-absent' }
    $journal = Join-Path $Work 'journal'
    New-Item -ItemType Directory -Path $journal -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $journal 'timing-hold'), 'x', $utf8)
    $ctx = Open-TestPhaseExecutedContext -LogRoot $journal -RunId 'run-v10' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    if ($null -eq $ctx -or $ctx.Refused) { Fail-Mp 'journal-context-refused' }
    $trx = Join-Path $journal 'alpha.trx'
    Write-MpDefinedTrx -Path $trx -ClassName 'Fixture.Alpha' -Outcome 'Passed'
    $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
    $script:TestPhaseExecutedContext = $ctx
    Write-TestPhaseExecutedManifest -Verdict $verdict -LogRoot $journal -TrxPath $trx -Suite 'antiphon' -Chunk 'c-alpha' -RunId 'run-v10' -Attempt '1' -HostWallSeconds 1
    $trxB = Join-Path $journal 'beta.trx'
    Write-MpDefinedTrx -Path $trxB -ClassName 'Fixture.Beta' -Outcome 'Failed'
    Write-TestPhaseExecutedManifest -Verdict $verdict -LogRoot $journal -TrxPath $trxB -Suite 'other' -Chunk 'c-alpha' -RunId 'run-v10' -Attempt '1' -HostWallSeconds 1
    Close-TestPhaseExecutedContext -Context $ctx
    $flat = Join-Path (Join-Path $journal 'timing') 'groups-executed.json'
    if (Test-Path -LiteralPath $flat) { Remove-Item -LiteralPath $flat -Force }
    [System.IO.File]::WriteAllText((Join-Path (Join-Path $journal 'timing') 'groups-planned.json'), '{"version":1,"runId":"run-v10","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests","groups":[]}', $utf8)
    & (Join-Path $Repo 'scripts/summarize-test-phases.ps1') -RunDirectory $journal -Top 5
    $joined = Get-Content -Raw (Join-Path (Join-Path $journal 'timing') 'summary.json') | ConvertFrom-Json
    $alpha = @($joined.tests | Where-Object { [string]$_.groupId -eq 'c-alpha' -and [string]$_.suite -eq 'antiphon' })
    $beta = @($joined.tests | Where-Object { [string]$_.groupId -eq 'c-alpha' -and [string]$_.suite -eq 'other' })
    if ($alpha.Count -ne 1 -or $beta.Count -ne 1) { Fail-Mp 'journal-only-lost-cross-suite-join' }
    if (Test-Path -LiteralPath $flat) { Fail-Mp 'summary-rewrote-projection' }
    $recover = Join-Path $Repo 'scripts/summarize-test-phases.ps1'
    $help = Get-Help $recover -Parameter RecoverExecutedManifest -ErrorAction SilentlyContinue
    if ($null -eq $help) { Fail-Mp 'recovery-switch-absent' }
    & $recover -RunDirectory $journal -Top 5 -RecoverExecutedManifest
    if (-not (Test-Path -LiteralPath $flat)) { Fail-Mp 'recovery-did-not-project' }
    $sealPath = Join-Path (Join-Path (Join-Path (Join-Path $journal 'timing') 'groups-executed.journal') '*') 'seal.json'
    if (@(Get-Item -Path $sealPath -ErrorAction SilentlyContinue).Count -gt 0) { Fail-Mp 'recovery-invented-seal' }
    $externalLog = Join-Path $Work 'external-log'
    $externalTiming = Join-Path $Work 'external-timing'
    $savedTiming = $script:QualificationTimingRoot
    try {
        $script:QualificationTimingRoot = $externalTiming
        $ctx = Open-TestPhaseExecutedContext -LogRoot $externalLog -TimingRoot $externalTiming -RunId 'external' -Attempt '1' -Suite 'external-suite' -Source 'unknown'
        $trx = Join-Path $externalLog 'external.trx'
        Write-MpDefinedTrx -Path $trx -ClassName 'Fixture.External' -Outcome 'Passed'
        Publish-MpChunk -LogRoot $externalLog -TrxPath $trx -Suite 'external-suite' -Chunk 'external-chunk' -RunId 'external' -Attempt '1' -HostWallSeconds 1
        Close-TestPhaseExecutedContext -Context $ctx
        $script:QualificationTimingRoot = $null
        $lines = & $recover -RunDirectory $externalLog -TimingRoot $externalTiming -RecoverExecutedManifest -Top 5
        if (($lines -join ',') -notmatch 'publication generation=.+ sequence=1') { Fail-Mp 'external-timing-progress-absent' }
        if (-not (Test-Path -LiteralPath (Join-Path $externalTiming 'groups-executed.json'))) { Fail-Mp 'external-timing-recovery-absent' }
        if (Test-Path -LiteralPath (Join-Path $externalLog 'timing/groups-executed.json')) { Fail-Mp 'external-timing-recovery-wrong-root' }
    } finally { $script:QualificationTimingRoot = $savedTiming }
    Write-MpResult -Ok $true -Reason 'v10'
}

function Get-MpJournalRows {
    param([string]$Timing)
    $rows = @()
    $curPath = Join-Path (Join-Path $Timing 'groups-executed.journal') 'current.json'
    if (-not (Test-Path -LiteralPath $curPath)) { return $rows }
    $cur = Get-Content -Raw -LiteralPath $curPath | ConvertFrom-Json
    $gen = Join-Path (Join-Path $Timing 'groups-executed.journal') ([string]$cur.generation)
    foreach ($rec in @(Get-ChildItem -LiteralPath $gen -Filter '????????.json' -File -ErrorAction SilentlyContinue)) {
        if ($rec.Name -notmatch '^[0-9]{8}\.json$') { continue }
        $obj = Get-Content -Raw -LiteralPath $rec.FullName | ConvertFrom-Json
        $suite = ''
        $chunk = ''
        if (-not [string]::IsNullOrWhiteSpace([string]$obj.freshSuiteKey)) {
            $suite = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$obj.freshSuiteKey))
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$obj.freshChunkKey)) {
            $chunk = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$obj.freshChunkKey))
        }
        $rows += [pscustomobject]@{ Kind = [string]$obj.kind; Suite = $suite; Chunk = $chunk }
    }
    return $rows
}

function Invoke-MpMatrixArm {
    param([string]$Name, [string]$Stage, [int]$Remaining)
    $root = Join-Path $Work ('matrix-' + $Name)
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $script:TestPhaseExecutedContext = $null
    $script:TestPhaseCommitFault = $null
    $script:QualificationTimingRoot = $null
    $ctx = Open-TestPhaseExecutedContext -LogRoot $root -RunId 'run-m' -Attempt '1' -Suite 'markdown-pdf' -Source 'unknown'
    if ($null -eq $ctx -or $ctx.Refused) { Fail-Mp ($Name + ' context-refused') }
    $verdict = [pscustomobject]@{ ExitCode = 0; coverageComplete = $true; testsPassed = $true }
    for ($i = 1; $i -le 5; $i++) {
        $trx = Join-Path $root ('c' + $i + '.trx')
        Write-MpDefinedTrx -Path $trx -ClassName 'Fixture.One' -Outcome 'Passed'
        if ($i -eq 2) {
            $script:TestPhaseCommitFault = [pscustomobject]@{ Stage = $Stage; Remaining = $Remaining }
        } else {
            $script:TestPhaseCommitFault = $null
        }
        Write-TestPhaseExecutedManifest -Verdict $verdict -LogRoot $root -TrxPath $trx -Suite 'markdown-pdf' -Chunk ('c' + $i) -RunId 'run-m' -Attempt '1' -HostWallSeconds 1
    }
    Close-TestPhaseExecutedContext -Context $ctx
    $script:TestPhaseCommitFault = $null
    return (Get-MpJournalRows -Timing (Join-Path $root 'timing'))
}

function Assert-MpMatrix {
    $held = @(Invoke-MpMatrixArm -Name 'held' -Stage 'Rename' -Remaining 4)
    $heldGaps = @($held | Where-Object { $_.Kind -eq 'gap' })
    if ($heldGaps.Count -ne 1) { Fail-Mp ('matrix-c-gap-dropped ' + $heldGaps.Count) }
    if ($heldGaps[0].Suite -ne 'markdown-pdf' -or $heldGaps[0].Chunk -ne 'c2') { Fail-Mp 'matrix-c-gap-identity' }
    foreach ($id in @('c1', 'c3', 'c4', 'c5')) {
        if (@($held | Where-Object { $_.Kind -eq 'chunk' -and $_.Chunk -eq $id }).Count -ne 1) { Fail-Mp ('matrix-c-missing-' + $id) }
    }
    $retry = @(Invoke-MpMatrixArm -Name 'retry' -Stage 'Rename' -Remaining 1)
    if (@($retry | Where-Object { $_.Kind -eq 'gap' }).Count -ne 0) { Fail-Mp 'matrix-a-spurious-gap' }
    if (@($retry | Where-Object { $_.Kind -eq 'chunk' }).Count -ne 5) { Fail-Mp 'matrix-a-lost-chunk' }
    $gap = @(Invoke-MpMatrixArm -Name 'gap' -Stage 'Rename' -Remaining 2)
    $gapRows = @($gap | Where-Object { $_.Kind -eq 'gap' })
    if ($gapRows.Count -ne 1) { Fail-Mp ('matrix-b-gap-count ' + $gapRows.Count) }
    if ($gapRows[0].Suite -ne 'markdown-pdf' -or $gapRows[0].Chunk -ne 'c2') { Fail-Mp ('matrix-b-gap-identity ' + $gapRows[0].Suite + '/' + $gapRows[0].Chunk) }
    foreach ($id in @('c1', 'c3', 'c4', 'c5')) {
        if (@($gap | Where-Object { $_.Kind -eq 'chunk' -and $_.Chunk -eq $id }).Count -ne 1) { Fail-Mp ('matrix-b-missing-' + $id) }
    }
    if (@($gap | Where-Object { $_.Kind -eq 'chunk' -and $_.Chunk -eq 'c2' }).Count -ne 0) { Fail-Mp 'matrix-b-duplicate-chunk' }
    $ack = @(Invoke-MpMatrixArm -Name 'ack' -Stage 'Acknowledge' -Remaining 1)
    if (@($ack | Where-Object { $_.Kind -eq 'gap' }).Count -ne 0) { Fail-Mp 'matrix-d-spurious-gap' }
    if (@($ack | Where-Object { $_.Chunk -eq 'c2' }).Count -ne 1) { Fail-Mp 'matrix-d-duplicate-or-missing' }
    $freshWork = Join-Path $Work 'matrix-gap'
    $reader = Join-Path $freshWork 'read-gap.ps1'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $readerText = @'
param([string]$Repo, [string]$Timing)
$ErrorActionPreference = "Stop"
. (Join-Path $Repo "scripts/lib/nightly-common.ps1")
. (Join-Path $Repo "scripts/lib/test-phase-evidence.ps1")
$cur = Get-Content -Raw (Join-Path $Timing "groups-executed.journal/current.json") | ConvertFrom-Json
$gen = Join-Path (Join-Path $Timing "groups-executed.journal") ([string]$cur.generation)
$gaps = 0
$chunk = ""
Get-ChildItem -LiteralPath $gen -Filter "????????.json" -File | ForEach-Object {
    if ($_.Name -notmatch "^[0-9]{8}\.json$") { return }
    $obj = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
    if ([string]$obj.kind -ne "gap") { return }
    $gaps++
    if ($obj.freshChunkKey) { $chunk = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$obj.freshChunkKey)) }
}
Write-Output ("gaps=" + $gaps + " chunk=" + $chunk)
'@
    [System.IO.File]::WriteAllText($reader, $readerText, $utf8)
    $fresh = & pwsh -NoProfile -NonInteractive -File $reader -Repo $Repo -Timing (Join-Path $freshWork 'timing')
    if ([string]$fresh -ne 'gaps=1 chunk=c2') { Fail-Mp ('matrix-b-fresh-process ' + [string]$fresh) }
}

function Invoke-V11 {
    $orig = ${function:Read-TestPhaseGenerationPrefix}
    $script:MpPrefixReads = 0
    Set-Item -Path 'function:Read-TestPhaseGenerationPrefix' -Value {
        param([string]$GenerationDir)
        if ($script:MpPrefixReads -ge 0 -and $env:MP_COUNT_PREFIX -eq '1') { $script:MpPrefixReads++ }
        & $orig -GenerationDir $GenerationDir
    }
    try {
        $classes = @()
        for ($i = 1; $i -le 34; $i++) { $classes += ('Fixture.C' + $i.ToString('00')) }
        $env:MP_COUNT_PREFIX = '1'
        $script:MpPrefixReads = 0
        $run = Invoke-MpQualification -Name 'outage' -Mode 'success' -Suites @('markdown-pdf') -ChunkClasses @{ 'markdown-pdf' = $classes } -FaultAt 2 -FaultUntil 33 -FaultRemaining 2
        if ($run.FlatAtBarrier -eq 'present') { Fail-Mp 'outage-flat-present-before-terminal' }
        if ($script:MpPrefixReads -gt 2) { Fail-Mp ('outage-rescanned-per-chunk reads=' + $script:MpPrefixReads) }
        if ($null -eq $run.Seal) { Fail-Mp 'outage-terminal-seal-absent' }
        $rows = @(Get-MpJournalRows -Timing $run.Timing)
        $restored = @($rows | Where-Object { $_.Kind -eq 'chunk' -and $_.Chunk -eq 'same-class-34' })
        if ($restored.Count -ne 1) { Fail-Mp 'outage-later-chunk-not-committed' }
        $gaps = @($rows | Where-Object { $_.Kind -eq 'gap' })
        if ($gaps.Count -lt 32) { Fail-Mp ('outage-gaps ' + $gaps.Count) }
        $statePath = Join-Path $run.Timing 'groups-executed.state.json'
        if (-not (Test-Path -LiteralPath $statePath)) { Fail-Mp 'outage-state-absent' }
        $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        if ([bool]$state.publicationComplete) { Fail-Mp 'outage-marked-publication-complete' }
        $summaryPath = Join-Path $run.Timing 'summary.json'
        if (-not (Test-Path -LiteralPath $summaryPath)) { Fail-Mp 'outage-summary-absent' }
        $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
        if ([bool]$summary.complete) { Fail-Mp 'outage-summary-complete' }
        if (@($summary.quality) -notcontains 'publication-gap') { Fail-Mp 'outage-summary-missing-gap' }
        if ([int]$run.Result.testsPassed -and [bool]$run.Result.coverageComplete) { } else { Fail-Mp 'publication-fault-changed-verdict' }
    } finally {
        Set-Item -Path 'function:Read-TestPhaseGenerationPrefix' -Value $orig
        Remove-Item Env:MP_COUNT_PREFIX -ErrorAction SilentlyContinue
    }
    Assert-MpMatrix
    Write-MpResult -Ok $true -Reason 'v11'
}

function Get-MpVerdictOutputs {
    param($Run)
    if ($null -eq $Run.Summary -or $null -eq $Run.Result) { Fail-Mp 'verdict-output-absent' }
    $ledger = @()
    foreach ($row in @($Run.Summary.suites)) {
        $entry = [ordered]@{}
        foreach ($key in @('id', 'name', 'suite', 'chunk', 'result', 'exitCode', 'timedOut', 'skipped', 'coverageComplete', 'testsPassed', 'evidenceReasons', 'requiredUidCount', 'excludedUidCount', 'censusDigest', 'exclusions')) {
            if ($row.PSObject.Properties.Name -contains $key) { $entry[$key] = $row.$key }
        }
        $ledger += $entry
    }
    $receipt = [ordered]@{}
    foreach ($key in @('outcome', 'succeeded', 'coverageComplete', 'testsPassed', 'reasons', 'policyHash', 'coverageScope', 'platform', 'requiredSuites', 'selectedSuites', 'nativeActiveMax')) {
        if ($Run.Summary.PSObject.Properties.Name -notcontains $key) { Fail-Mp ('receipt-field-absent ' + $key) }
        $receipt[$key] = $Run.Summary.$key
    }
    $returned = [ordered]@{}
    foreach ($key in @('ExitCode', 'coverageComplete', 'testsPassed', 'reasons', 'PolicyHash', 'coverageScope', 'platform', 'NativeActiveMax')) { $returned[$key] = $Run.Result.$key }
    # Timings, run/root identities and paths are observational. Verdicts, reasons,
    # UID counts/digests and policy identity remain literal production values.
    $originalEvidence = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Run.Log -Filter '*.trx' -File | Sort-Object Name)) {
        $originalEvidence += [ordered]@{ name = $file.Name; sha256 = (Get-MpSha $file.FullName) }
    }
    $json = [ordered]@{ ledger = $ledger; receipt = $receipt; returned = $returned; originalEvidence = $originalEvidence } | ConvertTo-Json -Depth 30 -Compress
    return $json.Replace((Split-Path -Parent $Run.Log), '<root>').Replace(('mp-' + $Run.Name), '<run>')
}

function Invoke-V12 {
    $classes = @{ 'markdown-pdf' = @('Fixture.One', 'Fixture.Two', 'Fixture.Three', 'Fixture.Four', 'Fixture.Five', 'Fixture.Six') }
    foreach ($outcome in @('success', 'failed', 'missing-trx')) {
        $baseline = Invoke-MpQualification -Name ($outcome + '-none') -Mode $outcome -Suites @('markdown-pdf') -ChunkClasses $classes -PublicationMode 'none'
        if (Test-Path -LiteralPath (Join-Path $baseline.Timing 'groups-executed.journal/current.json')) { Fail-Mp 'no-journal-control-created-journal' }
        if ($outcome -eq 'success' -and (-not $baseline.Summary.succeeded -or [int]$baseline.Result.ExitCode -ne 0)) { Fail-Mp 'verdict-baseline-not-green' }
        if ($outcome -ne 'success' -and ($baseline.Summary.succeeded -or [int]$baseline.Result.ExitCode -eq 0)) { Fail-Mp ('verdict-baseline-not-red ' + $outcome) }
        $expected = Get-MpVerdictOutputs -Run $baseline
        foreach ($publication in @('clean', 'gap', 'torn', 'missing-state', 'held')) {
            $fault = @{}
            if ($publication -eq 'gap') { $fault = @{ FaultAt = 2; FaultUntil = 5; FaultRemaining = 2 } }
            $run = Invoke-MpQualification -Name ($outcome + '-' + $publication) -Mode $outcome -Suites @('markdown-pdf') -ChunkClasses $classes -PublicationMode $publication @fault
            $actual = Get-MpVerdictOutputs -Run $run
            [System.IO.File]::WriteAllText((Join-Path $run.State 'verdict-outputs.json'), $actual)
            if ($actual -cne $expected) { Fail-Mp ('publication-changed-real-verdict ' + $outcome + '/' + $publication) }
            if ($run.FlatAtBarrier -ne 'absent') { Fail-Mp ('verdict-flat-before-terminal ' + $publication) }
            if ($publication -eq 'held') {
                if (Test-Path -LiteralPath (Join-Path $run.Timing 'groups-executed.journal/current.json')) { Fail-Mp 'held-writer-did-not-refuse' }
                if (-not (Test-Path -LiteralPath (Join-Path $run.Timing 'phase-summary-failed'))) { Fail-Mp 'held-writer-marker-absent' }
            } elseif ($publication -ne 'clean') {
                $quality = Get-TestPhasePublicationQuality -RunDirectory $run.Log
                $token = 'publication-' + $publication
                if ($publication -eq 'torn') { $token = 'publication-corruption' }
                if ($publication -eq 'missing-state') { $token = 'publication-state-missing' }
                if (@($quality) -notcontains $token) { Fail-Mp ('verdict-fault-not-observed ' + $publication + ' ' + (@($quality) -join ',')) }
            }
        }
    }
    Write-MpResult -Ok $true -Reason 'v12-real-verdict-matrix'
}

function Invoke-V13 {
    $run = Invoke-MpQualification -Name 'progress' -Mode 'success' -Suites @('markdown-pdf') -ChunkClasses @{ 'markdown-pdf' = @('Fixture.One', 'Fixture.Two') }
    if ($run.FlatAtBarrier -ne 'absent') { Fail-Mp ('progress-flat-present-before-terminal ' + $run.FlatAtBarrier) }
    if ($run.StampAtBarrier -ne 'stamped') { Fail-Mp ('progress-stamp-' + $run.StampAtBarrier) }
    $log = @(Get-ChildItem -LiteralPath $run.Log -Filter '*-tests.log' -File | Select-Object -First 1)
    if ($log.Count -lt 1) { Fail-Mp 'progress-log-absent' }
    $fresh = Test-NightlyFreshEvidence -Path $log[0].FullName -RunDirectory $run.Log -NotBeforeUtc ([datetime]'2020-01-01Z') -InvocationId 'mp-progress'
    if (-not $fresh.Ok) { Fail-Mp ('progress-freshness ' + $fresh.Reason) }
    $prefix = Read-TestPhaseExecutedPrefix -TimingRoot $run.Timing
    if ([int]$prefix.LastSequence -lt 2) { Fail-Mp ('progress-sequence ' + $prefix.LastSequence) }
    if ($null -eq $run.Seal) { Fail-Mp 'progress-seal-absent' }
    Write-MpResult -Ok $true -Reason 'v13'
}

function Invoke-V14 {
    $removed = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot '' -RunId ''
    if (-not [string]::IsNullOrWhiteSpace($removed)) { Fail-Mp 'timing-root-removed-key-invented' }
    $empty = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot (Join-Path $Work 'v14-log') -RunId ''
    if ($empty -ne (Join-Path (Join-Path $Work 'v14-log') 'timing')) { Fail-Mp 'timing-root-present-empty-mismatch' }
    $set = Resolve-QualificationTimingRoot -TimingRoot (Join-Path $Work 'v14-set') -LogRoot (Join-Path $Work 'v14-log') -RunId 'mp-v14'
    if ($set -ne (Join-Path $Work 'v14-set')) { Fail-Mp 'timing-root-set-mismatch' }
    $run = Invoke-MpQualification -Name 'identity' -Mode 'success' -Suites @('markdown-pdf', 'observability') -ChunkClasses @{ 'markdown-pdf' = @('Fixture.One'); 'observability' = @('Fixture.Two') }
    if ($run.FlatAtBarrier -ne 'absent') { Fail-Mp ('identity-flat-present-before-terminal ' + $run.FlatAtBarrier) }
    $owners = @(Get-ChildItem -LiteralPath $run.Timing -Filter 'owner-*.json' -File -ErrorAction SilentlyContinue)
    if ($owners.Count -lt 2) { Fail-Mp ('identity-owner-sidecars ' + $owners.Count) }
    $drains = @()
    foreach ($owner in $owners) {
        $doc = Get-Content -Raw -LiteralPath $owner.FullName | ConvertFrom-Json
        $drains += [double]$doc.drainMilliseconds
    }
    if (@($drains | Sort-Object -Unique).Count -lt 2) { Fail-Mp 'identity-owner-stamps-collapsed' }
    if (-not (Test-Path -LiteralPath (Join-Path $run.Timing 'groups-planned-index.json'))) { Fail-Mp 'identity-planned-index-absent' }
    $flat = Get-Content -Raw (Join-Path $run.Timing 'groups-executed.json') | ConvertFrom-Json
    $same = @($flat.chunks | Where-Object { [string]$_.chunk -eq 'same' })
    if ($same.Count -ne 2) { Fail-Mp ('identity-homonym-count ' + $same.Count) }
    $suites = @($same | ForEach-Object { [string]$_.suite } | Sort-Object -Unique)
    if ($suites.Count -ne 2) { Fail-Mp 'identity-suites-collapsed' }
    Write-MpResult -Ok $true -Reason 'v14'
}

try {
    # Same load order as scripts/nightly-tests.ps1. The impl file does not import policy or coverage.
    $lib = Join-Path $Repo 'scripts/lib'
    . (Join-Path $lib 'nightly-common.ps1')
    . (Join-Path $lib 'nightly-policy.ps1')
    . (Join-Path $lib 'nightly-coverage.ps1')
    . (Join-Path $lib 'release-gate.ps1')
    . (Join-Path $lib 'release-selection.ps1')
    . (Join-Path $lib 'nightly-tests-impl.ps1')
    Initialize-MpCulture
    switch ($Case) {
        'v1' { Invoke-V1 }
        'v2' { Invoke-V2 }
        'v3-write' { Invoke-V3Write }
        'v3-reduce' { Invoke-V3Reduce }
        'v4' { Invoke-V4 }
        'v4-successor' { Invoke-V4Successor }
        'v5' { Invoke-V5 }
        'v5-child' { Invoke-V5Child }
        'v6' { Invoke-V6 }
        'prefix-successor' { Invoke-MpPrefixSuccessor }
        'v7' { Invoke-V7 }
        'v7-holder' { Invoke-V7Holder }
        'v8' { Invoke-V8 }
        'v9' { Invoke-V9 }
        'v10' { Invoke-V10 }
        'v11' { Invoke-V11 }
        'v12' { Invoke-V12 }
        'v13' { Invoke-V13 }
        'v14' { Invoke-V14 }
        default { Fail-Mp ("unknown-case " + $Case) }
    }
} catch {
    Fail-Mp ([string]$_.Exception.Message)
}
