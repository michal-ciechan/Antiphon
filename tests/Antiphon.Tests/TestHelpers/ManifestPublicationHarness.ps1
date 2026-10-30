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
    $ctx = Open-MpContext -LogRoot $root -RunId 'run-v1' -Attempt '1' -Suite 'antiphon' -Source 'unknown'
    if ($null -ne $ctx -and $ctx.Refused) { Fail-Mp ("open-refused " + [string]$ctx.Reason) }
    Reset-MpCounters
    Publish-MpChunk -LogRoot $root -TrxPath $TrxPath -Suite 'antiphon' -Chunk 'live-1' -RunId 'run-v1' -Attempt '1' -HostWallSeconds 1.25
    $after = Get-MpSha -Path $flat
    $hashSame = ($PrefixChunks -eq 0) -or ($before -eq $after -and $before -ne '')
    $measured = [pscustomobject]@{
        PriorRows = [int]$script:MpPriorRows
        Bytes = [int]$script:MpDepth0Bytes
        HashSame = [bool]$hashSame
        FlatWrites = [int]$script:MpFlatWrites
        HashCount = [int]$script:MpHashCount
    }
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
    }
    Write-MpResult -Ok $true -Reason 'v5'
}

function Invoke-V6 {
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

try {
    # Script scope, not a function: a dot-source inside a function does not keep its commands.
    . (Join-Path $Repo 'scripts/lib/nightly-tests-impl.ps1')
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
        'v7' { Invoke-V7 }
        'v7-holder' { Invoke-V7Holder }
        'v8' { Invoke-V8 }
        default { Fail-Mp ("unknown-case " + $Case) }
    }
} catch {
    Fail-Mp ([string]$_.Exception.Message)
}
