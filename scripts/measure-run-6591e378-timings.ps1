#requires -Version 7
<#
.SYNOPSIS
  Read-only measurement of qualification run 6591e378 chunk wall time against plannedSeconds.

.DESCRIPTION
  Reads completed *.timing.json and TRX files, plus groups-planned-<suite>.json.
  Writes a Markdown report. Does not write under the run directory, the timing
  directory, or anywhere outside -OutputPath.
#>
[CmdletBinding()]
param(
    [string]$RunDirectory = 'C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378',
    [string]$TimingDirectory = 'C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\timing-6591e378c04d47bc898cf0927352b01b',
    [string]$OutputPath = '',
    [string]$SourceCommit = 'ac6d00022e940ee3b90584c7d313567b4491c498'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'docs\investigations\2026-11-10-run-6591e378-timing-measurement.md'
}
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$repoFull = [IO.Path]::GetFullPath($repoRoot)
if (-not $outputFull.StartsWith($repoFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must stay inside the worktree: $outputFull"
}
$runFull = [IO.Path]::GetFullPath($RunDirectory)
$timingFull = [IO.Path]::GetFullPath($TimingDirectory)
if ($outputFull.StartsWith($runFull, [StringComparison]::OrdinalIgnoreCase) -or
    $outputFull.StartsWith($timingFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write into the qualification logs: $outputFull"
}

function Read-SharedText {
    param([Parameter(Mandatory)][string]$Path, [int]$MaxBytes = 67108864)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -gt $MaxBytes) { throw "file exceeds $MaxBytes bytes: $Path" }
        $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false), $true, 4096, $true)
        try {
            $text = $reader.ReadToEnd()
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
    return $text
}

function Read-TrxHeadTail {
    param([Parameter(Mandatory)][string]$Path, [int]$HeadBytes = 8192, [int]$TailBytes = 262144)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $length = $stream.Length
        $headTake = [int][Math]::Min($HeadBytes, $length)
        $headBuf = New-Object byte[] $headTake
        $headRead = $stream.Read($headBuf, 0, $headTake)
        $head = [Text.Encoding]::UTF8.GetString($headBuf, 0, $headRead)
        $tailTake = [int][Math]::Min($TailBytes, $length)
        $tailStart = $length - $tailTake
        $stream.Position = $tailStart
        $tailBuf = New-Object byte[] $tailTake
        $tailRead = $stream.Read($tailBuf, 0, $tailTake)
        $tail = [Text.Encoding]::UTF8.GetString($tailBuf, 0, $tailRead)
        return [pscustomobject]@{ Head = $head; Tail = $tail; Length = $length }
    }
    finally { $stream.Dispose() }
}

function Get-XmlAttributes {
    param([string]$Fragment)
    $map = @{}
    foreach ($match in [regex]::Matches($Fragment, '([A-Za-z0-9_]+)="([^"]*)"')) {
        $map[$match.Groups[1].Value] = $match.Groups[2].Value
    }
    return $map
}

function Get-Percentile {
    param([double[]]$Values, [double]$P)
    if ($null -eq $Values -or $Values.Length -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 1) { return [double]$sorted[0] }
    $rank = ($n - 1) * $P
    $lo = [int][Math]::Floor($rank)
    $hi = [int][Math]::Ceiling($rank)
    if ($lo -eq $hi) { return [double]$sorted[$lo] }
    $fraction = $rank - $lo
    return (([double]$sorted[$lo]) * (1.0 - $fraction)) + (([double]$sorted[$hi]) * $fraction)
}

function Get-Mean {
    param([double[]]$Values)
    if ($null -eq $Values -or $Values.Length -eq 0) { return $null }
    $sum = 0.0
    foreach ($value in $Values) { $sum += $value }
    return $sum / $Values.Length
}

function Format-Fixed {
    param($Value, [int]$Digits)
    if ($null -eq $Value) { return '' }
    return ([double]$Value).ToString('F' + $Digits, $invariant)
}

function Format-Int {
    param($Value)
    if ($null -eq $Value) { return '' }
    return ([int64][Math]::Round([double]$Value)).ToString($invariant)
}

function Format-Span {
    param([double]$Seconds)
    $sign = ''
    if ($Seconds -lt 0) { $sign = '-'; $Seconds = -$Seconds }
    $days = [int][Math]::Floor($Seconds / 86400.0)
    $hours = [int][Math]::Floor(($Seconds - ($days * 86400.0)) / 3600.0)
    $minutes = [int][Math]::Floor(($Seconds - ($days * 86400.0) - ($hours * 3600.0)) / 60.0)
    $secs = $Seconds - ($days * 86400.0) - ($hours * 3600.0) - ($minutes * 60.0)
    if ($secs -lt 0) { $secs = 0 }
    return ('{0}{1}d {2:00}h {3:00}m {4:00.0}s' -f $sign, $days, $hours, $minutes, $secs)
}

function Format-Cell {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return ($Text -replace '\|', '/' -replace "`r|`n", ' ')
}

function Convert-FromIso {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return [DateTimeOffset]::Parse($Text, $invariant, [Globalization.DateTimeStyles]::RoundtripKind)
}

function Get-JsonProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

$measuredAt = [DateTimeOffset]::UtcNow

# --- class cost file at the run's source commit (read-only) ---
$costNote = 'plannedSeconds is the chunk sum of per-class seconds from scripts/lib/nightly-class-costs.json (platform windows, one number per class).'
$costClassCount = $null
$costPlatform = ''
$costFileSha = ''
$costDigestMatch = $null
$costError = ''
$tempCost = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-cost-' + [guid]::NewGuid().ToString('n') + '.json')
$errFile = $tempCost + '.err'
try {
    $blob = (& git -C $repoRoot rev-parse --verify ($SourceCommit + ':scripts/lib/nightly-class-costs.json')).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($blob)) { throw "cost blob missing at $SourceCommit" }
    $proc = Start-Process -FilePath 'git' -ArgumentList @('-C', $repoRoot, 'cat-file', 'blob', $blob) `
        -RedirectStandardOutput $tempCost -RedirectStandardError $errFile -NoNewWindow -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        $stderr = ''
        if (Test-Path -LiteralPath $errFile) { $stderr = [IO.File]::ReadAllText($errFile) }
        throw "git cat-file exit $($proc.ExitCode) $stderr"
    }
    if (Test-Path -LiteralPath $errFile) { Remove-Item -LiteralPath $errFile -Force }
    $costFileSha = (Get-FileHash -LiteralPath $tempCost -Algorithm SHA256).Hash.ToLowerInvariant()
    $costJson = Read-SharedText -Path $tempCost
    $costDoc = $costJson | ConvertFrom-Json
    $costPlatform = [string]$costDoc.platform
    $costClassCount = @($costDoc.classes.PSObject.Properties).Count
    $oneNumber = $true
    foreach ($property in @($costDoc.classes.PSObject.Properties)) {
        $secondsValue = $property.Value.seconds
        if ($null -eq $secondsValue -or -not ($secondsValue -is [ValueType])) { $oneNumber = $false; break }
    }
    if (-not $oneNumber) { $costError = 'at least one class row has no single numeric seconds value' }
}
catch {
    $costError = $_.Exception.Message
}
finally {
    if (Test-Path -LiteralPath $tempCost) { Remove-Item -LiteralPath $tempCost -Force }
    if (Test-Path -LiteralPath $errFile) { Remove-Item -LiteralPath $errFile -Force }
}

$worktreeCostPath = Join-Path $repoRoot 'scripts\lib\nightly-class-costs.json'
$worktreeCostSha = ''
$worktreeCostBytes = $null
$worktreeCostClasses = $null
$worktreeCostPlatform = ''
if (Test-Path -LiteralPath $worktreeCostPath) {
    $worktreeCostBytes = (Get-Item -LiteralPath $worktreeCostPath).Length
    $worktreeCostSha = (Get-FileHash -LiteralPath $worktreeCostPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $worktreeCostDoc = (Read-SharedText -Path $worktreeCostPath) | ConvertFrom-Json
    $worktreeCostPlatform = [string]$worktreeCostDoc.platform
    $worktreeCostClasses = @($worktreeCostDoc.classes.PSObject.Properties).Count
}

# --- plan ---
$planFiles = @(Get-ChildItem -LiteralPath $timingFull -File -Filter 'groups-planned-*.json' |
    Where-Object { $_.Name -ne 'groups-planned-index.json' })
if ($planFiles.Count -eq 0) { throw "no groups-planned-<suite>.json under $timingFull" }

$planned = New-Object System.Collections.Generic.List[object]
$planDigest = ''
$planRunId = ''
$duplicatePlanKeys = New-Object System.Collections.Generic.List[string]
foreach ($file in $planFiles) {
    $doc = (Read-SharedText -Path $file.FullName) | ConvertFrom-Json
    $suite = [string]$doc.suite
    if ([string]::IsNullOrWhiteSpace($planRunId)) { $planRunId = [string]$doc.runId }
    if ([string]::IsNullOrWhiteSpace($planDigest)) { $planDigest = [string]$doc.costFileDigest }
    foreach ($group in @($doc.groups)) {
        $chunk = [string]$group.chunk
        $key = $suite + '|' + $chunk
        $seconds = $null
        if ($null -ne $group.plannedSeconds) { $seconds = [double]$group.plannedSeconds }
        $exists = $false
        foreach ($prior in $planned) {
            if ($prior.Key -eq $key) { $exists = $true; break }
        }
        if ($exists) { $duplicatePlanKeys.Add($key); continue }
        $planned.Add([pscustomobject]@{
            Key = $key
            Suite = $suite
            Chunk = $chunk
            PlannedSeconds = $seconds
            ClassCount = @($group.classes).Count
            File = $file.Name
        }) | Out-Null
    }
}
if (-not [string]::IsNullOrWhiteSpace($costFileSha) -and -not [string]::IsNullOrWhiteSpace($planDigest)) {
    $costDigestMatch = ($costFileSha -eq $planDigest.ToLowerInvariant())
}
$planDigestMatchesWorktree = (-not [string]::IsNullOrWhiteSpace($worktreeCostSha) -and -not [string]::IsNullOrWhiteSpace($planDigest) -and $worktreeCostSha -eq $planDigest.ToLowerInvariant())

$planByKey = @{}
foreach ($row in $planned) { $planByKey[$row.Key] = $row }

# --- completed timings ---
$timingFiles = @(Get-ChildItem -LiteralPath $runFull -File -Filter '*.timing.json')
$completed = New-Object System.Collections.Generic.List[object]
$inFlight = New-Object System.Collections.Generic.List[object]
$timingUnreadable = New-Object System.Collections.Generic.List[string]
$buildValueCounts = @{}
foreach ($file in $timingFiles) {
    try {
        $timing = (Read-SharedText -Path $file.FullName -MaxBytes 8388608) | ConvertFrom-Json
    }
    catch {
        $timingUnreadable.Add($file.Name + ': ' + $_.Exception.Message)
        continue
    }
    $phase = [string](Get-JsonProp $timing 'lastPhase')
    $suite = [string](Get-JsonProp $timing 'suite')
    $chunk = [string](Get-JsonProp $timing 'chunk')
    $key = $suite + '|' + $chunk
    $wallValue = Get-JsonProp $timing 'runWallSeconds'
    $wall = $null
    if ($null -ne $wallValue) { $wall = [double]$wallValue }
    $started = Convert-FromIso ([string](Get-JsonProp $timing 'startedAt'))
    $ended = Convert-FromIso ([string](Get-JsonProp $timing 'completedAt'))
    $testCountValue = Get-JsonProp $timing 'testCount'
    $p50Value = Get-JsonProp $timing 'p50Seconds'
    $p95Value = Get-JsonProp $timing 'p95Seconds'
    $trxMissingValue = Get-JsonProp $timing 'trxMissing'
    $buildValue = Get-JsonProp $timing 'buildSeconds'
    $record = [pscustomobject]@{
        Key = $key
        Suite = $suite
        Chunk = $chunk
        Name = if ($suite -and $chunk) { $suite + '/' + $chunk } else { $file.BaseName }
        Phase = $phase
        Wall = $wall
        Started = $started
        Ended = $ended
        TestCount = if ($null -ne $testCountValue) { [int]$testCountValue } else { $null }
        IntraP50 = if ($null -ne $p50Value) { [double]$p50Value } else { $null }
        IntraP95 = if ($null -ne $p95Value) { [double]$p95Value } else { $null }
        TrxMissing = [bool]$trxMissingValue
        TrxPath = [string](Get-JsonProp $timing 'trx')
        TimingFile = $file.Name
        BuildSeconds = if ($null -ne $buildValue) { [string]$buildValue } else { '' }
    }
    if (-not [string]::IsNullOrWhiteSpace($record.BuildSeconds)) {
        if (-not $buildValueCounts.ContainsKey($record.BuildSeconds)) { $buildValueCounts[$record.BuildSeconds] = 0 }
        $buildValueCounts[$record.BuildSeconds]++
    }
    if ($phase -eq 'complete') { $completed.Add($record) | Out-Null }
    else { $inFlight.Add($record) | Out-Null }
}

function Resolve-TrxPath {
    param($Record)
    $candidate = $Record.TrxPath
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $full = [IO.Path]::GetFullPath($candidate)
        if ($full.StartsWith($runFull, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $full)) {
            return $full
        }
    }
    $fallback = Join-Path $runFull ($Record.Suite + '-' + $Record.Chunk + '.trx')
    if (Test-Path -LiteralPath $fallback) { return $fallback }
    return $null
}

$trxUnreadable = New-Object System.Collections.Generic.List[string]
$trxTargets = New-Object System.Collections.Generic.List[object]
foreach ($record in $completed) { $trxTargets.Add($record) | Out-Null }
foreach ($record in $inFlight) { $trxTargets.Add($record) | Out-Null }
foreach ($record in $trxTargets) {
    $record | Add-Member -NotePropertyName TrxStart -NotePropertyValue $null
    $record | Add-Member -NotePropertyName TrxFinish -NotePropertyValue $null
    $record | Add-Member -NotePropertyName TrxWall -NotePropertyValue $null
    $record | Add-Member -NotePropertyName Executed -NotePropertyValue $null
    $record | Add-Member -NotePropertyName Passed -NotePropertyValue $null
    $record | Add-Member -NotePropertyName Failed -NotePropertyValue $null
    $record | Add-Member -NotePropertyName NotExecuted -NotePropertyValue $null
    $record | Add-Member -NotePropertyName TrxTotal -NotePropertyValue $null
    if ($record.TrxMissing) { continue }
    $trxPath = Resolve-TrxPath $record
    if ([string]::IsNullOrWhiteSpace($trxPath)) { continue }
    try {
        $parts = Read-TrxHeadTail -Path $trxPath
        $times = [regex]::Match($parts.Head, '<Times\b[^>]*>')
        if ($times.Success) {
            $attrs = Get-XmlAttributes $times.Value
            if ($attrs.ContainsKey('start')) { $record.TrxStart = Convert-FromIso $attrs['start'] }
            if ($attrs.ContainsKey('finish')) { $record.TrxFinish = Convert-FromIso $attrs['finish'] }
            if ($null -ne $record.TrxStart -and $null -ne $record.TrxFinish) {
                $record.TrxWall = ($record.TrxFinish - $record.TrxStart).TotalSeconds
            }
        }
        $counters = [regex]::Match($parts.Tail, '<Counters\b[^>]*/?>')
        if (-not $counters.Success) {
            $whole = Read-SharedText -Path $trxPath -MaxBytes 33554432
            $counters = [regex]::Match($whole, '<Counters\b[^>]*/?>')
        }
        if ($counters.Success) {
            $attrs = Get-XmlAttributes $counters.Value
            if ($attrs.ContainsKey('executed')) { $record.Executed = [int]$attrs['executed'] }
            if ($attrs.ContainsKey('passed')) { $record.Passed = [int]$attrs['passed'] }
            if ($attrs.ContainsKey('failed')) { $record.Failed = [int]$attrs['failed'] }
            if ($attrs.ContainsKey('notExecuted')) { $record.NotExecuted = [int]$attrs['notExecuted'] }
            if ($attrs.ContainsKey('total')) { $record.TrxTotal = [int]$attrs['total'] }
        }
    }
    catch {
        $trxUnreadable.Add($record.TimingFile + ': ' + $_.Exception.Message)
    }
}

$matched = New-Object System.Collections.Generic.List[object]
$unmatched = New-Object System.Collections.Generic.List[object]
$completedKeys = @{}
foreach ($record in $completed) {
    if ($planByKey.ContainsKey($record.Key)) {
        $plan = $planByKey[$record.Key]
        $ratio = $null
        $errorSeconds = $null
        if ($null -ne $record.Wall -and $null -ne $plan.PlannedSeconds) {
            $errorSeconds = $record.Wall - $plan.PlannedSeconds
            if ($plan.PlannedSeconds -gt 0) { $ratio = $record.Wall / $plan.PlannedSeconds }
        }
        $tests = $record.Executed
        if ($null -eq $tests) { $tests = $record.TestCount }
        $matched.Add([pscustomobject]@{
            Key = $record.Key
            Suite = $record.Suite
            Chunk = $record.Chunk
            Name = $record.Name
            Wall = $record.Wall
            Planned = $plan.PlannedSeconds
            Ratio = $ratio
            ErrorSeconds = $errorSeconds
            Tests = $tests
            Executed = $record.Executed
            Passed = $record.Passed
            Failed = $record.Failed
            NotExecuted = $record.NotExecuted
            IntraP50 = $record.IntraP50
            IntraP95 = $record.IntraP95
            Started = $record.Started
            Ended = $record.Ended
            TrxWall = $record.TrxWall
            ClassCount = $plan.ClassCount
        }) | Out-Null
        $completedKeys[$record.Key] = $true
    }
    else {
        $unmatched.Add($record) | Out-Null
    }
}

$remaining = New-Object System.Collections.Generic.List[object]
foreach ($plan in $planned) {
    if (-not $completedKeys.ContainsKey($plan.Key)) { $remaining.Add($plan) | Out-Null }
}

$walls = New-Object System.Collections.Generic.List[double]
foreach ($record in $completed) {
    if ($null -ne $record.Wall) { $walls.Add($record.Wall) | Out-Null }
}
$wallArray = $walls.ToArray()
$wallSum = 0.0
foreach ($value in $wallArray) { $wallSum += $value }

$ratioValues = New-Object System.Collections.Generic.List[double]
$zeroPlanned = 0
$missingWall = 0
foreach ($row in $matched) {
    if ($null -eq $row.Wall) { $missingWall++; continue }
    if ($null -eq $row.Planned -or $row.Planned -le 0) { $zeroPlanned++; continue }
    $ratioValues.Add([double]$row.Ratio) | Out-Null
}
$ratioArray = $ratioValues.ToArray()
$meanRatio = Get-Mean $ratioArray
$ratioP50 = Get-Percentile $ratioArray 0.50
$ratioP90 = Get-Percentile $ratioArray 0.90
$ratioP95 = Get-Percentile $ratioArray 0.95

$plannedOnMatched = 0.0
$wallOnRatio = 0.0
foreach ($row in $matched) {
    if ($null -eq $row.Wall -or $null -eq $row.Planned -or $row.Planned -le 0) { continue }
    $plannedOnMatched += $row.Planned
    $wallOnRatio += $row.Wall
}
$weightedRatio = $null
if ($plannedOnMatched -gt 0) { $weightedRatio = $wallOnRatio / $plannedOnMatched }

function Get-WeightedRatioPercentile {
    param($Rows, [double]$P)
    $eligible = @($Rows | Where-Object { $null -ne $_.Ratio -and $null -ne $_.Planned -and $_.Planned -gt 0 } | Sort-Object Ratio)
    $total = 0.0
    foreach ($row in $eligible) { $total += [double]$row.Planned }
    if ($total -le 0 -or $eligible.Count -eq 0) { return $null }
    $cursor = 0.0
    foreach ($row in $eligible) {
        $cursor += [double]$row.Planned
        if ($cursor -ge ($P * $total)) { return [double]$row.Ratio }
    }
    return [double]$eligible[$eligible.Count - 1].Ratio
}
$weightedP50 = Get-WeightedRatioPercentile $matched 0.50
$weightedP95 = Get-WeightedRatioPercentile $matched 0.95

# concurrency sweep over completed intervals plus in-flight [started, measuredAt]
$events = New-Object System.Collections.Generic.List[object]
$intervals = 0
$missingInterval = 0
foreach ($record in $completed) {
    if ($null -eq $record.Started -or $null -eq $record.Ended) { $missingInterval++; continue }
    if ($record.Ended -le $record.Started) { $missingInterval++; continue }
    $intervals++
    $events.Add([pscustomobject]@{ Ticks = $record.Started.UtcTicks; Delta = 1 }) | Out-Null
    $events.Add([pscustomobject]@{ Ticks = $record.Ended.UtcTicks; Delta = -1 }) | Out-Null
}
$inFlightOpen = 0
foreach ($record in $inFlight) {
    if ($null -eq $record.Started) { continue }
    $endTick = $measuredAt.UtcTicks
    if ($null -ne $record.Ended -and $record.Ended.UtcTicks -lt $endTick) { $endTick = $record.Ended.UtcTicks }
    if ($endTick -le $record.Started.UtcTicks) { continue }
    $inFlightOpen++
    $events.Add([pscustomobject]@{ Ticks = $record.Started.UtcTicks; Delta = 1 }) | Out-Null
    $events.Add([pscustomobject]@{ Ticks = $endTick; Delta = -1 }) | Out-Null
}
$orderedEvents = @($events | Sort-Object Ticks, Delta)
$level = 0
$peak = 0
$integral = 0.0
$busy = 0.0
$timeAt = @{}
$previous = $null
foreach ($event in $orderedEvents) {
    if ($null -ne $previous) {
        $dt = ($event.Ticks - $previous) / [double][TimeSpan]::TicksPerSecond
        if ($dt -gt 0) {
            $integral += $level * $dt
            if ($level -gt 0) { $busy += $dt }
            $keyLevel = [string]$level
            if (-not $timeAt.ContainsKey($keyLevel)) { $timeAt[$keyLevel] = 0.0 }
            $timeAt[$keyLevel] = $timeAt[$keyLevel] + $dt
        }
    }
    $level += [int]$event.Delta
    if ($level -gt $peak) { $peak = $level }
    $previous = $event.Ticks
}
$span = 0.0
if ($orderedEvents.Count -ge 2) {
    $span = ($orderedEvents[$orderedEvents.Count - 1].Ticks - $orderedEvents[0].Ticks) / [double][TimeSpan]::TicksPerSecond
}
$meanOverlap = $null
if ($span -gt 0) { $meanOverlap = $integral / $span }
$busyMean = $null
if ($busy -gt 0) { $busyMean = $integral / $busy }
$mode = $null
$modeSeconds = 0.0
foreach ($entry in $timeAt.GetEnumerator()) {
    $lvl = [int]$entry.Key
    if ($lvl -le 0) { continue }
    if ($entry.Value -gt $modeSeconds) { $modeSeconds = [double]$entry.Value; $mode = $lvl }
}
$modeShare = $null
if ($busy -gt 0 -and $null -ne $mode) { $modeShare = $modeSeconds / $busy }
$observedConcurrency = $mode
if ($null -eq $observedConcurrency -and $null -ne $busyMean -and $busyMean -gt 0) {
    $observedConcurrency = $busyMean
}

$remainingPlanned = 0.0
$remainingKnown = 0
$remainingUnknown = 0
foreach ($plan in $remaining) {
    if ($null -eq $plan.PlannedSeconds) { $remainingUnknown++; continue }
    $remainingPlanned += $plan.PlannedSeconds
    $remainingKnown++
}
$planPlannedSum = 0.0
foreach ($plan in $planned) {
    if ($null -ne $plan.PlannedSeconds) { $planPlannedSum += $plan.PlannedSeconds }
}

function Get-Eta {
    param($Scale, $Concurrency)
    if ($null -eq $Scale -or $null -eq $Concurrency -or [double]$Concurrency -le 0) { return $null }
    return ($remainingPlanned * [double]$Scale) / [double]$Concurrency
}
$etaNaive = Get-Eta $meanRatio $observedConcurrency
$etaP50 = Get-Eta $ratioP50 $observedConcurrency
$etaP95 = Get-Eta $ratioP95 $observedConcurrency
$etaWeighted = Get-Eta $weightedRatio $observedConcurrency
$etaWeightedP50 = Get-Eta $weightedP50 $observedConcurrency
$etaWeightedP95 = Get-Eta $weightedP95 $observedConcurrency
$etaMeanOverlap = Get-Eta $meanRatio $busyMean

$suites = @($planned | Select-Object -ExpandProperty Suite -Unique | Sort-Object)
$suiteRows = New-Object System.Collections.Generic.List[object]
foreach ($suite in $suites) {
    $suitePlan = @($planned | Where-Object { $_.Suite -eq $suite })
    $suiteDone = @($matched | Where-Object { $_.Suite -eq $suite })
    $suiteLeft = @($remaining | Where-Object { $_.Suite -eq $suite })
    $sumPlan = 0.0
    foreach ($row in $suitePlan) { if ($null -ne $row.PlannedSeconds) { $sumPlan += $row.PlannedSeconds } }
    $sumLeft = 0.0
    foreach ($row in $suiteLeft) { if ($null -ne $row.PlannedSeconds) { $sumLeft += $row.PlannedSeconds } }
    $sumWall = 0.0
    $suiteRatios = New-Object System.Collections.Generic.List[double]
    $suitePlanPos = 0.0
    $suiteWallPos = 0.0
    foreach ($row in $suiteDone) {
        if ($null -ne $row.Wall) { $sumWall += $row.Wall }
        if ($null -ne $row.Ratio) { $suiteRatios.Add([double]$row.Ratio) | Out-Null }
        if ($null -ne $row.Wall -and $null -ne $row.Planned -and $row.Planned -gt 0) {
            $suitePlanPos += $row.Planned
            $suiteWallPos += $row.Wall
        }
    }
    $suiteWeighted = $null
    if ($suitePlanPos -gt 0) { $suiteWeighted = $suiteWallPos / $suitePlanPos }
    $suiteWalls = @($suiteDone | Where-Object { $null -ne $_.Wall } | ForEach-Object { [double]$_.Wall })
    $suiteRows.Add([pscustomobject]@{
        Suite = $suite
        PlannedChunks = $suitePlan.Count
        Completed = $suiteDone.Count
        Remaining = $suiteLeft.Count
        SumPlanned = $sumPlan
        SumRemaining = $sumLeft
        SumWall = $sumWall
        MeanRatio = (Get-Mean $suiteRatios.ToArray())
        P50Ratio = (Get-Percentile $suiteRatios.ToArray() 0.50)
        P95Ratio = (Get-Percentile $suiteRatios.ToArray() 0.95)
        WeightedRatio = $suiteWeighted
        WallP50 = (Get-Percentile ([double[]]$suiteWalls) 0.50)
        WallP95 = (Get-Percentile ([double[]]$suiteWalls) 0.95)
    }) | Out-Null
}

$slowest = @($completed | Where-Object { $null -ne $_.Wall } | Sort-Object Wall -Descending | Select-Object -First 15)
$under = @($matched | Where-Object { $null -ne $_.ErrorSeconds -and $null -ne $_.Ratio } | Sort-Object ErrorSeconds -Descending | Select-Object -First 10)
$over = @($matched | Where-Object { $null -ne $_.ErrorSeconds -and $null -ne $_.Ratio } | Sort-Object ErrorSeconds | Select-Object -First 10)

$executedSum = 0
$passedSum = 0
$failedSum = 0
$notExecutedSum = 0
$counterChunks = 0
$testCountMismatch = 0
foreach ($record in $completed) {
    if ($null -ne $record.Executed) {
        $counterChunks++
        $executedSum += $record.Executed
        if ($null -ne $record.Passed) { $passedSum += $record.Passed }
        if ($null -ne $record.Failed) { $failedSum += $record.Failed }
        if ($null -ne $record.NotExecuted) { $notExecutedSum += $record.NotExecuted }
        if ($null -ne $record.TestCount -and $record.TestCount -ne $record.Executed) { $testCountMismatch++ }
    }
}

$trxDelta = New-Object System.Collections.Generic.List[double]
foreach ($record in $completed) {
    if ($null -ne $record.Wall -and $null -ne $record.TrxWall) {
        $trxDelta.Add([double]($record.TrxWall - $record.Wall)) | Out-Null
    }
}
$trxDeltaP50 = Get-Percentile $trxDelta.ToArray() 0.50

$sharedBuild = $null
$sharedBuildCount = 0
foreach ($entry in $buildValueCounts.GetEnumerator()) {
    if ($entry.Value -gt $sharedBuildCount) { $sharedBuildCount = [int]$entry.Value; $sharedBuild = [string]$entry.Key }
}

$hostName = $env:COMPUTERNAME
$logical = [Environment]::ProcessorCount
$ramBytes = $null
try {
    $computer = Get-CimInstance -ClassName Win32_ComputerSystem
    if ($null -ne $computer.NumberOfLogicalProcessors) { $logical = [int]$computer.NumberOfLogicalProcessors }
    if ($null -ne $computer.TotalPhysicalMemory) { $ramBytes = [int64]$computer.TotalPhysicalMemory }
}
catch {
    $hostName = $hostName + ' (CIM unavailable)'
}
$ramGiB = $null
if ($null -ne $ramBytes) { $ramGiB = $ramBytes / 1GB }

$shortCount = 0
$longCount = 0
$shortRatioSum = 0.0
$longRatioSum = 0.0
$shortPlan = 0.0
$longPlan = 0.0
$shortWall = 0.0
$longWall = 0.0
foreach ($row in $matched) {
    if ($null -eq $row.Ratio -or $null -eq $row.Planned -or $null -eq $row.Wall) { continue }
    if ($row.Planned -lt 60.0) {
        $shortCount++
        $shortRatioSum += [double]$row.Ratio
        $shortPlan += [double]$row.Planned
        $shortWall += [double]$row.Wall
    }
    else {
        $longCount++
        $longRatioSum += [double]$row.Ratio
        $longPlan += [double]$row.Planned
        $longWall += [double]$row.Wall
    }
}
$shortMean = $null
$longMean = $null
if ($shortCount -gt 0) { $shortMean = $shortRatioSum / $shortCount }
if ($longCount -gt 0) { $longMean = $longRatioSum / $longCount }
$shortMeanShare = $null
if ($ratioArray.Length -gt 0) { $shortMeanShare = $shortRatioSum / $ratioArray.Length }

$suiteScaledSeconds = 0.0
$suiteScaledOk = $true
foreach ($row in $suiteRows) {
    $scale = $row.WeightedRatio
    if ($null -eq $scale) { $scale = $weightedRatio }
    if ($null -eq $scale) { $suiteScaledOk = $false; break }
    $suiteScaledSeconds += [double]$row.SumRemaining * [double]$scale
}
$suiteScale = $null
$etaSuite = $null
if ($suiteScaledOk -and $remainingPlanned -gt 0) { $suiteScale = $suiteScaledSeconds / $remainingPlanned }
if ($suiteScaledOk -and $null -ne $observedConcurrency -and [double]$observedConcurrency -gt 0) {
    $etaSuite = $suiteScaledSeconds / [double]$observedConcurrency
}

$lines = New-Object System.Collections.Generic.List[string]
function Add-Line { param([string]$Text) $lines.Add($Text) | Out-Null }

Add-Line '# Run 6591e378 chunk timing against plannedSeconds'
Add-Line ''
Add-Line 'CARD-1842 slice S7. Read-only snapshot of the live Windows qualification. The run was still in progress when these numbers were taken, so remaining work and the ETA move as later chunks finish.'
Add-Line ''
Add-Line ('- Measured at: `{0}`' -f $measuredAt.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', $invariant))
Add-Line ('- Run directory: `{0}`' -f $runFull)
Add-Line ('- Timing directory: `{0}`' -f $timingFull)
Add-Line ('- Plan run id: `{0}`' -f $planRunId)
Add-Line ('- Source commit on the plan: `{0}`' -f $SourceCommit)
Add-Line ('- Host: `{0}`, {1} logical CPUs, {2} GiB RAM ({3} bytes)' -f $hostName, $logical, (Format-Fixed $ramGiB 1), $(if ($null -ne $ramBytes) { $ramBytes } else { 'unknown' }))
Add-Line ''
Add-Line '## Definitions'
Add-Line ''
Add-Line '- A completed chunk is a top-level `*.timing.json` whose `lastPhase` is `complete`. Any other phase is in flight and is left out of the wall distribution.'
Add-Line '- Actual wall is `runWallSeconds`. Intra-chunk `p50Seconds` and `p95Seconds` are per-test percentiles inside that chunk. They are not the chunk wall.'
Add-Line '- `plannedSeconds` comes from each `groups-planned-<suite>.json` group. The singleton `groups-planned.json` is the last suite copy and is not added again. The plan attributes those seconds to `scripts/lib/nightly-class-costs.json`.'
Add-Line '- Ratio is `runWallSeconds / plannedSeconds` for plannedSeconds greater than zero. Percentiles are Excel PERCENTILE.INC (R-7, linear). The unweighted mean gives each chunk one vote. The duration-weighted ratio is `sum(wall) / sum(plannedSeconds)` over those same chunks.'
Add-Line '- Over-estimate means the plan was high (`plannedSeconds - wall` large). Under-estimate means the plan was low (`wall - plannedSeconds` large).'
Add-Line '- Observed concurrency is the overlap level that occupied the most time while at least one counted chunk was running. Intervals are timing `startedAt`..`completedAt` for completed chunks, and `startedAt`..snapshot for in-flight chunks. Ends sort before starts at the same timestamp. The naive ETA divides by that integer.'
Add-Line '- Remaining planned seconds is the full `plannedSeconds` of every planned chunk that does not yet have a completed timing file. In-flight chunks are still inside that sum.'
Add-Line '- Naive ETA = remaining planned seconds x unweighted mean ratio / observed concurrency. The p50 and p95 finish estimates use the same divisor and the unweighted percentile of the per-chunk ratio. That is a single scale factor applied to the whole remainder, not a confidence interval of the mean.'
Add-Line ''
Add-Line '## Host and cost file'
Add-Line ''
Add-Line ('| Item | Value |')
Add-Line ('| --- | --- |')
Add-Line ('| Hostname | {0} |' -f (Format-Cell $hostName))
Add-Line ('| Logical CPUs | {0} |' -f $logical)
Add-Line ('| RAM bytes | {0} |' -f $(if ($null -ne $ramBytes) { $ramBytes } else { '' }))
Add-Line ('| RAM GiB | {0} |' -f (Format-Fixed $ramGiB 2))
Add-Line ('| Plan costFileDigest | {0} |' -f (Format-Cell $planDigest))
Add-Line ('| Source-commit blob SHA-256 | {0} |' -f $(if ($costFileSha) { $costFileSha } else { '' }))
Add-Line ('| Source-commit blob matches plan | {0} |' -f $(if ($null -eq $costDigestMatch) { '' } elseif ($costDigestMatch) { 'true' } else { 'false' }))
Add-Line ('| Source-commit platform | {0} |' -f (Format-Cell $costPlatform))
Add-Line ('| Source-commit classes | {0} |' -f $(if ($null -ne $costClassCount) { $costClassCount } else { '' }))
Add-Line ('| Worktree cost file SHA-256 | {0} |' -f $(if ($worktreeCostSha) { $worktreeCostSha } else { '' }))
Add-Line ('| Worktree cost file bytes | {0} |' -f $(if ($null -ne $worktreeCostBytes) { $worktreeCostBytes } else { '' }))
Add-Line ('| Worktree cost file matches plan | {0} |' -f $(if ($planDigestMatchesWorktree) { 'true' } else { 'false' }))
Add-Line ('| Worktree platform | {0} |' -f (Format-Cell $worktreeCostPlatform))
Add-Line ('| Worktree classes | {0} |' -f $(if ($null -ne $worktreeCostClasses) { $worktreeCostClasses } else { '' }))
if ($costError) { Add-Line ('| Cost-file note | {0} |' -f (Format-Cell $costError)) }
Add-Line ''
if ($planDigestMatchesWorktree) {
    Add-Line ('Chunk plannedSeconds is the sum of per-class seconds from scripts/lib/nightly-class-costs.json (platform {0}, one numeric seconds value on each of {1} classes). The plan costFileDigest matches this worktree copy of that file ({2} bytes). The blob of the same path at source commit {3} is a different file ({4}, {5} classes, platform {6}).' -f $worktreeCostPlatform, $worktreeCostClasses, $worktreeCostBytes, $SourceCommit, $costFileSha, $costClassCount, $costPlatform)
}
else {
    Add-Line ('Chunk plannedSeconds is the sum of per-class seconds from scripts/lib/nightly-class-costs.json. The plan costFileDigest does not match this worktree copy. The source-commit blob hash is {0} ({1} classes, platform {2}).' -f $costFileSha, $costClassCount, $costPlatform)
}
Add-Line ''
Add-Line '## Progress'
Add-Line ''
Add-Line ('| Item | Value |')
Add-Line ('| --- | --- |')
Add-Line ('| Plan files | {0} |' -f $planFiles.Count)
Add-Line ('| Planned chunks | {0} |' -f $planned.Count)
Add-Line ('| Planned seconds (all chunks) | {0} ({1}) |' -f (Format-Fixed $planPlannedSum 1), (Format-Span $planPlannedSum))
Add-Line ('| Timing files | {0} |' -f $timingFiles.Count)
Add-Line ('| Completed chunks | {0} |' -f $completed.Count)
Add-Line ('| Completed and in the plan | {0} |' -f $matched.Count)
Add-Line ('| Completed but not in the plan | {0} |' -f $unmatched.Count)
Add-Line ('| In flight (timing file, phase not complete) | {0} |' -f $inFlight.Count)
Add-Line ('| Remaining planned chunks | {0} |' -f $remaining.Count)
Add-Line ('| Remaining planned seconds | {0} ({1}) |' -f (Format-Fixed $remainingPlanned 1), (Format-Span $remainingPlanned))
Add-Line ('| Duplicate plan keys skipped | {0} |' -f $duplicatePlanKeys.Count)
Add-Line ('| Unreadable timing files | {0} |' -f $timingUnreadable.Count)
Add-Line ('| TRX reads that threw | {0} |' -f $trxUnreadable.Count)
Add-Line ''
Add-Line ("Done {0} of {1} planned chunks. Remaining planned seconds {2}." -f $matched.Count, $planned.Count, (Format-Fixed $remainingPlanned 1))
Add-Line ''
if ($inFlight.Count -gt 0) {
    Add-Line '### In flight'
    Add-Line ''
    Add-Line '| Chunk | Phase | Started (UTC) | Elapsed seconds | Planned seconds |'
    Add-Line '| --- | --- | --- | --- | --- |'
    foreach ($record in @($inFlight | Sort-Object Suite, Chunk)) {
        $elapsed = $null
        if ($null -ne $record.Started) { $elapsed = ($measuredAt - $record.Started).TotalSeconds }
        $planSeconds = $null
        if ($planByKey.ContainsKey($record.Key)) { $planSeconds = $planByKey[$record.Key].PlannedSeconds }
        $startedText = ''
        if ($null -ne $record.Started) { $startedText = $record.Started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', $invariant) }
        Add-Line ('| {0} | {1} | {2} | {3} | {4} |' -f (Format-Cell $record.Name), (Format-Cell $record.Phase), $startedText, (Format-Fixed $elapsed 1), (Format-Fixed $planSeconds 1))
    }
    Add-Line ''
}
if ($unmatched.Count -gt 0) {
    Add-Line '### Completed timings with no plan row'
    Add-Line ''
    Add-Line '| Chunk | Wall seconds |'
    Add-Line '| --- | --- |'
    foreach ($record in @($unmatched | Sort-Object Name)) {
        Add-Line ('| {0} | {1} |' -f (Format-Cell $record.Name), (Format-Fixed $record.Wall 1))
    }
    Add-Line ''
}

Add-Line '## Wall distribution (every completed chunk)'
Add-Line ''
Add-Line 'Wall is `runWallSeconds`.'
Add-Line ''
Add-Line '| n | sum seconds | min | p50 | p90 | p95 | max | mean |'
Add-Line '| --- | --- | --- | --- | --- | --- | --- | --- |'
Add-Line ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f `
    $wallArray.Length, `
    (Format-Fixed $wallSum 1), `
    (Format-Fixed (Get-Percentile $wallArray 0.0) 1), `
    (Format-Fixed (Get-Percentile $wallArray 0.50) 1), `
    (Format-Fixed (Get-Percentile $wallArray 0.90) 1), `
    (Format-Fixed (Get-Percentile $wallArray 0.95) 1), `
    (Format-Fixed (Get-Percentile $wallArray 1.0) 1), `
    (Format-Fixed (Get-Mean $wallArray) 1))
Add-Line ''
Add-Line ("Sum of chunk walls is serial test time, {0}. It is not calendar time." -f (Format-Span $wallSum))
if ($null -ne $trxDeltaP50) {
    Add-Line ("Median (TRX finish-start minus runWallSeconds) is {0} seconds across completed chunks that had both." -f (Format-Fixed $trxDeltaP50 3))
}
if ($null -ne $sharedBuild -and $timingFiles.Count -gt 0 -and ($sharedBuildCount / $timingFiles.Count) -ge 0.5) {
    Add-Line ('buildSeconds is {0} on {1} of {2} timing files. The value repeats, so it is not added to wall.' -f $sharedBuild, $sharedBuildCount, $timingFiles.Count)
}
Add-Line ("TRX Counters on {0} completed chunks: executed {1}, passed {2}, failed {3}, notExecuted {4}. timing.json testCount differed from Counters.executed on {5} chunks." -f $counterChunks, $executedSum, $passedSum, $failedSum, $notExecutedSum, $testCountMismatch)
Add-Line ''

Add-Line '### Fifteen slowest completed chunks'
Add-Line ''
Add-Line '| Chunk | Wall seconds | Planned seconds | Tests executed | Ratio | Intra-chunk test p50 | Intra-chunk test p95 |'
Add-Line '| --- | --- | --- | --- | --- | --- | --- |'
foreach ($record in $slowest) {
    $planSeconds = $null
    $ratio = $null
    $tests = $record.Executed
    if ($null -eq $tests) { $tests = $record.TestCount }
    if ($planByKey.ContainsKey($record.Key)) {
        $planSeconds = $planByKey[$record.Key].PlannedSeconds
        if ($null -ne $record.Wall -and $null -ne $planSeconds -and $planSeconds -gt 0) { $ratio = $record.Wall / $planSeconds }
    }
    Add-Line ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f `
        (Format-Cell $record.Name), (Format-Fixed $record.Wall 1), (Format-Fixed $planSeconds 1), `
        $(if ($null -ne $tests) { $tests } else { '' }), (Format-Fixed $ratio 3), `
        (Format-Fixed $record.IntraP50 3), (Format-Fixed $record.IntraP95 3))
}
Add-Line ''

Add-Line '## Plan error'
Add-Line ''
Add-Line ("Ratios use {0} completed planned chunks with plannedSeconds > 0. Excluded: {1} with plannedSeconds <= 0, {2} with no wall." -f $ratioArray.Length, $zeroPlanned, $missingWall)
Add-Line ''
Add-Line '| Statistic | Unweighted per chunk | Weighted by plannedSeconds |'
Add-Line '| --- | --- | --- |'
Add-Line ('| Mean actual/planned | {0} | {1} |' -f (Format-Fixed $meanRatio 3), (Format-Fixed $weightedRatio 3))
Add-Line ('| p50 ratio | {0} | {1} |' -f (Format-Fixed $ratioP50 3), (Format-Fixed $weightedP50 3))
Add-Line ('| p90 ratio | {0} |  |' -f (Format-Fixed $ratioP90 3))
Add-Line ('| p95 ratio | {0} | {1} |' -f (Format-Fixed $ratioP95 3), (Format-Fixed $weightedP95 3))
Add-Line ''
Add-Line 'The weighted ratio is the scale that turns the sum of planned seconds into the sum of observed wall. The unweighted mean is the figure the naive ETA uses. Each chunk has one vote, so a short profile chunk moves the mean as much as a 900-second group.'
Add-Line ''
Add-Line '### Short chunks and the unweighted mean'
Add-Line ''
Add-Line 'Short means plannedSeconds below 60. Long means 60 or more. Both sets are completed planned chunks with a ratio.'
Add-Line ''
Add-Line '| Set | Chunks | Mean ratio | Sum planned s | Sum wall s |'
Add-Line '| --- | --- | --- | --- | --- |'
Add-Line ('| Planned under 60 s | {0} | {1} | {2} | {3} |' -f $shortCount, (Format-Fixed $shortMean 3), (Format-Fixed $shortPlan 1), (Format-Fixed $shortWall 1))
Add-Line ('| Planned 60 s or more | {0} | {1} | {2} | {3} |' -f $longCount, (Format-Fixed $longMean 3), (Format-Fixed $longPlan 1), (Format-Fixed $longWall 1))
Add-Line ''
if ($null -ne $shortMeanShare -and $null -ne $meanRatio -and $meanRatio -ne 0) {
    Add-Line ('The short set contributes {0} of the unweighted mean {1} (its ratios summed, then divided by every ratio chunk).' -f (Format-Fixed $shortMeanShare 3), (Format-Fixed $meanRatio 3))
    Add-Line ''
}
Add-Line '### Per suite'
Add-Line ''
Add-Line '| Suite | Plan chunks | Done | Left | Sum planned s | Sum remaining s | Sum wall s | Mean ratio | p50 ratio | p95 ratio | Weighted ratio |'
Add-Line '| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |'
foreach ($row in $suiteRows) {
    Add-Line ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |' -f `
        (Format-Cell $row.Suite), $row.PlannedChunks, $row.Completed, $row.Remaining, `
        (Format-Fixed $row.SumPlanned 1), (Format-Fixed $row.SumRemaining 1), (Format-Fixed $row.SumWall 1), `
        (Format-Fixed $row.MeanRatio 3), (Format-Fixed $row.P50Ratio 3), (Format-Fixed $row.P95Ratio 3), (Format-Fixed $row.WeightedRatio 3))
}
Add-Line ''
Add-Line '### Ten largest under-estimates (actual above plan)'
Add-Line ''
Add-Line '| Chunk | Wall seconds | Planned seconds | Actual minus planned | Ratio | Tests executed |'
Add-Line '| --- | --- | --- | --- | --- | --- |'
foreach ($row in $under) {
    Add-Line ('| {0} | {1} | {2} | {3} | {4} | {5} |' -f `
        (Format-Cell $row.Name), (Format-Fixed $row.Wall 1), (Format-Fixed $row.Planned 1), `
        (Format-Fixed $row.ErrorSeconds 1), (Format-Fixed $row.Ratio 3), $(if ($null -ne $row.Tests) { $row.Tests } else { '' }))
}
Add-Line ''
Add-Line '### Ten largest over-estimates (plan above actual)'
Add-Line ''
Add-Line '| Chunk | Wall seconds | Planned seconds | Planned minus actual | Ratio | Tests executed |'
Add-Line '| --- | --- | --- | --- | --- | --- |'
foreach ($row in $over) {
    $overBy = $null
    if ($null -ne $row.ErrorSeconds) { $overBy = -$row.ErrorSeconds }
    Add-Line ('| {0} | {1} | {2} | {3} | {4} | {5} |' -f `
        (Format-Cell $row.Name), (Format-Fixed $row.Wall 1), (Format-Fixed $row.Planned 1), `
        (Format-Fixed $overBy 1), (Format-Fixed $row.Ratio 3), $(if ($null -ne $row.Tests) { $row.Tests } else { '' }))
}
Add-Line ''

Add-Line '## Concurrency and ETA'
Add-Line ''
Add-Line ('| Item | Value |')
Add-Line ('| --- | --- |')
Add-Line ('| Completed intervals in the sweep | {0} |' -f $intervals)
Add-Line ('| Completed chunks with no usable interval | {0} |' -f $missingInterval)
Add-Line ('| In-flight intervals held open to the snapshot | {0} |' -f $inFlightOpen)
Add-Line ('| Peak chunks overlapping | {0} |' -f $peak)
Add-Line ('| Time-weighted mean overlap (includes idle gaps in the span) | {0} |' -f (Format-Fixed $meanOverlap 3))
Add-Line ('| Time-weighted mean overlap while at least one chunk ran | {0} |' -f (Format-Fixed $busyMean 3))
Add-Line ('| Observed concurrency (mode while busy) | {0} |' -f $(if ($null -ne $mode) { $mode } else { '' }))
Add-Line ('| Share of busy time at that mode | {0} |' -f $(if ($null -ne $modeShare) { (Format-Fixed ($modeShare * 100) 1) + '%' } else { '' }))
Add-Line ('| Span of the sweep | {0} |' -f (Format-Span $span))
Add-Line ''
Add-Line ("Observed concurrency is {0}: that many chunks overlapped for {1} of the time at least one chunk was running. Peak overlap was {2}. The sweep span is {3} and the sum of completed walls is {4}." -f `
    $(if ($null -ne $observedConcurrency) { $observedConcurrency } else { 'unknown' }), `
    $(if ($null -ne $modeShare) { (Format-Fixed ($modeShare * 100) 1) + '%' } else { 'n/a' }), `
    $peak, (Format-Span $span), (Format-Span $wallSum))
Add-Line ''
Add-Line ('| Estimate | Scale | Seconds from snapshot | Clock | Finish (UTC) |')
Add-Line ('| --- | --- | --- | --- | --- |')
function Add-EtaRow {
    param([string]$Label, $Scale, $Eta)
    $finish = ''
    if ($null -ne $Eta) { $finish = $measuredAt.AddSeconds([double]$Eta).ToString('yyyy-MM-ddTHH:mm:ssZ', $invariant) }
    Add-Line ('| {0} | {1} | {2} | {3} | {4} |' -f $Label, (Format-Fixed $Scale 3), (Format-Fixed $Eta 1), $(if ($null -ne $Eta) { Format-Span ([double]$Eta) } else { '' }), $finish)
}
Add-EtaRow 'Naive (mean ratio / observed concurrency)' $meanRatio $etaNaive
Add-EtaRow 'p50 ratio / observed concurrency' $ratioP50 $etaP50
Add-EtaRow 'p95 ratio / observed concurrency' $ratioP95 $etaP95
Add-EtaRow 'Weighted ratio / observed concurrency' $weightedRatio $etaWeighted
Add-EtaRow 'Weighted p50 / observed concurrency' $weightedP50 $etaWeightedP50
Add-EtaRow 'Weighted p95 / observed concurrency' $weightedP95 $etaWeightedP95
Add-EtaRow 'Mean ratio / busy-time mean overlap' $meanRatio $etaMeanOverlap
Add-EtaRow 'Per-suite weighted ratios / observed concurrency' $suiteScale $etaSuite
Add-Line ''
Add-Line 'The naive row is remaining planned seconds times the unweighted mean ratio, divided by the observed concurrency. The p50 and p95 rows swap that mean for the unweighted percentile of the per-chunk ratio distribution. Those three rows are the brief formula. The weighted rows replace the scale with sum(wall) / sum(plannedSeconds), which reproduces the time already spent. The per-suite row scales each suite remainder by that suite weighted ratio (the global weighted ratio when the suite has no completed chunk) and divides by the same concurrency. In-flight planned seconds stay fully inside the remainder, so none of these clocks credit time already elapsed on an open chunk.'
Add-Line ''
if ($timingUnreadable.Count -gt 0) {
    Add-Line '## Unreadable timing files'
    Add-Line ''
    foreach ($item in $timingUnreadable) { Add-Line ('- {0}' -f (Format-Cell $item)) }
    Add-Line ''
}
if ($trxUnreadable.Count -gt 0) {
    Add-Line '## TRX reads that threw'
    Add-Line ''
    foreach ($item in $trxUnreadable) { Add-Line ('- {0}' -f (Format-Cell $item)) }
    Add-Line ''
}
Add-Line '## Reproduce'
Add-Line ''
Add-Line '```powershell'
Add-Line 'pwsh -NoProfile -File scripts/measure-run-6591e378-timings.ps1'
Add-Line '```'
Add-Line ''
Add-Line 'The script only reads the run directory and the timing directory. It writes this report and nothing under the qualification logs.'
Add-Line ''

$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines($outputFull, $lines.ToArray(), $utf8)

Write-Output ('measuredAt=' + $measuredAt.ToString('yyyy-MM-ddTHH:mm:ssZ', $invariant))
Write-Output ('planned=' + $planned.Count + ' completed=' + $completed.Count + ' matched=' + $matched.Count + ' remaining=' + $remaining.Count)
Write-Output ('remainingPlanned=' + (Format-Fixed $remainingPlanned 1) + ' wallSum=' + (Format-Fixed $wallSum 1))
Write-Output ('meanRatio=' + (Format-Fixed $meanRatio 3) + ' weighted=' + (Format-Fixed $weightedRatio 3) + ' p50=' + (Format-Fixed $ratioP50 3) + ' p95=' + (Format-Fixed $ratioP95 3))
Write-Output ('peak=' + $peak + ' mode=' + $observedConcurrency + ' busyMean=' + (Format-Fixed $busyMean 3) + ' modeShare=' + (Format-Fixed $(if ($null -ne $modeShare) { $modeShare * 100 } else { $null }) 1))
Write-Output ('etaNaive=' + (Format-Fixed $etaNaive 1) + ' etaP50=' + (Format-Fixed $etaP50 1) + ' etaP95=' + (Format-Fixed $etaP95 1) + ' etaWeighted=' + (Format-Fixed $etaWeighted 1))
Write-Output ('classes=' + $costClassCount + ' platform=' + $costPlatform + ' digestMatch=' + $costDigestMatch)
Write-Output ('report=' + $outputFull)
if ($costError) { Write-Output ('costError=' + $costError) }
