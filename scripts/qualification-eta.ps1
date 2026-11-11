#requires -Version 5.1
# CARD-1842 S4. Read-only ETA. Headline scale is sum(wall)/sum(planned), not the unweighted mean.
param(
    [Parameter(Mandatory = $true)][string]$TimingRoot,
    [string]$NowUtc,
    [string]$ClassCostPath
)
$ErrorActionPreference = 'Stop'

$styles = [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal
if ([string]::IsNullOrWhiteSpace($NowUtc)) {
    $now = [datetime]::UtcNow
} else {
    $now = [datetime]::Parse($NowUtc, [Globalization.CultureInfo]::InvariantCulture, $styles)
}

function Format-EtaNumber([double]$Value) {
    $rounded = [Math]::Round($Value, 3)
    $whole = [Math]::Round($rounded, 0)
    if ([Math]::Abs($rounded - $whole) -lt 0.0000001) {
        return ([long]$whole).ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    return $rounded.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
}
function Format-EtaUtc([datetime]$Value) {
    return $Value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
}
function Test-SettledPhase([string]$Phase) {
    foreach ($name in @('complete', 'timeout', 'cancelled', 'teardown')) {
        if ($Phase -ceq $name) { return $true }
    }
    return $false
}
function Convert-EtaJson([string]$Path) {
    return ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}
function Get-EtaDouble($Value) {
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $number = 0.0
    if (-not [double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) { return $null }
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { return $null }
    return $number
}

$rootInfo = Get-Item -LiteralPath $TimingRoot
if (-not $rootInfo.PSIsContainer) { throw 'timing-root-missing' }
$root = $rootInfo.FullName

$script:CostTable = $null
$script:CostOpened = $false
function Get-CostTable {
    if ($script:CostOpened) { return $script:CostTable }
    $script:CostOpened = $true
    if ([string]::IsNullOrWhiteSpace($ClassCostPath)) { return $null }
    $doc = Convert-EtaJson $ClassCostPath
    if ([string]$doc.format -cne 'antiphon-nightly-class-costs-v1') { throw 'class-cost-format' }
    $table = New-Object 'System.Collections.Generic.Dictionary[string,double]' ([StringComparer]::Ordinal)
    foreach ($row in @($doc.classes.PSObject.Properties)) {
        $seconds = Get-EtaDouble $row.Value.seconds
        if ($null -eq $seconds) { continue }
        $table[[string]$row.Name] = [double]$seconds
    }
    $script:CostTable = $table
    return $table
}
function Get-ClassCostPrice($Classes) {
    $names = @($Classes | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($names.Count -eq 0) { return $null }
    $table = Get-CostTable
    if ($null -eq $table) { return $null }
    $sum = 0.0
    foreach ($name in $names) {
        $key = [string]$name
        if (-not $table.ContainsKey($key)) { return $null }
        $sum += [double]$table[$key]
    }
    return $sum
}

$chunks = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
function Get-Chunk([string]$Suite, [string]$ChunkId) {
    $key = $Suite + "`n" + $ChunkId
    if (-not $chunks.ContainsKey($key)) {
        $chunks[$key] = [pscustomobject]@{
            Suite = $Suite
            Chunk = $ChunkId
            Seconds = $null
            Priced = $false
            Phase = ''
            StartedAt = ''
            CompletedAt = ''
            Wall = $null
            HasTiming = $false
        }
    }
    return $chunks[$key]
}

foreach ($file in @($rootInfo.GetFiles())) {
    $name = $file.Name
    if ($name -ceq 'groups-planned-index.json') { continue }
    if (-not $name.StartsWith('groups-planned-', [StringComparison]::Ordinal)) { continue }
    if (-not $name.EndsWith('.json', [StringComparison]::Ordinal)) { continue }
    $plan = Convert-EtaJson $file.FullName
    $suite = [string]$plan.suite
    if ([string]::IsNullOrWhiteSpace($suite)) { throw ('plan-suite ' + $name) }
    foreach ($group in @($plan.groups)) {
        if ($null -eq $group) { continue }
        $chunkId = [string]$group.chunk
        if ([string]::IsNullOrWhiteSpace($chunkId)) { throw ('plan-chunk ' + $name) }
        $chunk = Get-Chunk $suite $chunkId
        if ($chunk.Priced -or $null -ne $chunk.Seconds) { continue }
        $hasPlanned = $group.PSObject.Properties.Name -contains 'plannedSeconds'
        $planned = $null
        if ($hasPlanned) { $planned = Get-EtaDouble $group.plannedSeconds }
        if ($null -ne $planned) {
            if ([double]$planned -lt 0) { continue }
            $chunk.Seconds = [double]$planned
            $chunk.Priced = $true
            continue
        }
        if ($hasPlanned -and $null -ne $group.plannedSeconds -and -not [string]::IsNullOrWhiteSpace([string]$group.plannedSeconds)) {
            continue
        }
        $classPrice = $null
        if ($group.PSObject.Properties.Name -contains 'classes') { $classPrice = Get-ClassCostPrice $group.classes }
        if ($null -ne $classPrice) {
            $chunk.Seconds = [double]$classPrice
            $chunk.Priced = $true
        }
    }
}

foreach ($file in @($rootInfo.GetFiles('*.timing.json'))) {
    $timing = Convert-EtaJson $file.FullName
    $suite = [string]$timing.suite
    $chunkId = [string]$timing.chunk
    if ([string]::IsNullOrWhiteSpace($suite) -or [string]::IsNullOrWhiteSpace($chunkId)) { throw ('timing-identity ' + $file.Name) }
    $chunk = Get-Chunk $suite $chunkId
    if ($chunk.HasTiming) { throw ('duplicate-timing ' + $file.Name) }
    $chunk.HasTiming = $true
    if ($timing.PSObject.Properties.Name -contains 'lastPhase') { $chunk.Phase = [string]$timing.lastPhase }
    if ($timing.PSObject.Properties.Name -contains 'startedAt' -and $null -ne $timing.startedAt) { $chunk.StartedAt = [string]$timing.startedAt }
    if ($timing.PSObject.Properties.Name -contains 'completedAt' -and $null -ne $timing.completedAt) { $chunk.CompletedAt = [string]$timing.completedAt }
    if ($timing.PSObject.Properties.Name -contains 'runWallSeconds') { $chunk.Wall = Get-EtaDouble $timing.runWallSeconds }
}

$activeSuite = ''
$activeChunk = ''
$activePath = Join-Path $root 'active-chunk.json'
if ([System.IO.File]::Exists($activePath)) {
    $active = Convert-EtaJson $activePath
    if ([string]$active.state -ceq 'Active') {
        $activeSuite = [string]$active.suite
        $activeChunk = [string]$active.chunk
    }
}

$outstanding = 0
$running = 0
$completed = 0
$unpriced = 0
$remainder = 0.0
$sumWall = 0.0
$sumPlanned = 0.0
$ratios = New-Object System.Collections.Generic.List[double]

foreach ($chunk in @($chunks.Values)) {
    $settled = (-not [string]::IsNullOrWhiteSpace($chunk.CompletedAt)) -or (Test-SettledPhase $chunk.Phase)
    $state = 'outstanding'
    if ($settled) { $state = 'completed' }
    elseif ($chunk.Suite -ceq $activeSuite -and $chunk.Chunk -ceq $activeChunk -and -not [string]::IsNullOrWhiteSpace($activeChunk)) { $state = 'running' }

    if ($state -ceq 'completed') {
        $completed++
        if ($chunk.Priced -and [double]$chunk.Seconds -gt 0 -and $null -ne $chunk.Wall) {
            $wall = [double]$chunk.Wall
            $planned = [double]$chunk.Seconds
            $ratios.Add($wall / $planned)
            $sumWall += $wall
            $sumPlanned += $planned
        }
        continue
    }

    if (-not $chunk.Priced) {
        $unpriced++
        if ($state -ceq 'running') { $running++ } else { $outstanding++ }
        continue
    }

    $price = [double]$chunk.Seconds
    if ($state -ceq 'running') {
        $running++
        $elapsed = $null
        if (-not [string]::IsNullOrWhiteSpace($chunk.StartedAt)) {
            $started = [datetime]::MinValue
            if ([datetime]::TryParse($chunk.StartedAt, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$started)) {
                $elapsed = ($now - $started.ToUniversalTime()).TotalSeconds
            }
        }
        if ($null -eq $elapsed) { $remainder += $price }
        else { $remainder += [Math]::Max(0.0, $price - [double]$elapsed) }
        continue
    }

    $outstanding++
    $remainder += $price
}

$ratioCount = $ratios.Count
$sorted = $ratios.ToArray()
[Array]::Sort($sorted)
$scale = 1.0
if ($ratioCount -gt 0 -and $sumPlanned -gt 0) { $scale = $sumWall / $sumPlanned }
$point = $remainder * $scale
$p50Seconds = $remainder
$p95Text = 'unavailable'
if ($ratioCount -ge 8) {
    $p50Index = [int][Math]::Ceiling([decimal]0.50 * [decimal]$ratioCount) - 1
    $p95Index = [int][Math]::Ceiling([decimal]0.95 * [decimal]$ratioCount) - 1
    $p50Seconds = $remainder * [double]$sorted[$p50Index]
    $p95Text = Format-EtaUtc $now.AddSeconds($remainder * [double]$sorted[$p95Index])
}

Write-Output ('ETA outstanding=' + $outstanding + ' running=' + $running + ' completed=' + $completed + ' unpriced=' + $unpriced + ' pointSeconds=' + (Format-EtaNumber $point) + ' p50=' + (Format-EtaUtc $now.AddSeconds($p50Seconds)) + ' p95=' + $p95Text)
