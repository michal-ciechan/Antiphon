# CARD-1313 timing journal and report. Schema antiphon-test-timing-v1.
# Persistence failure is not a test result. Missing configuration writes nothing.

function Resolve-TestTimingRoot {
    param([string]$Explicit, [string]$Repository, [switch]$Hosted, [string]$Checkout, [string]$Output, [string]$Cleanup, [switch]$IncludeGitCommon)
    $chosen = $Explicit
    if (-not $chosen) { $chosen = $env:ANTIPHON_TEST_TIMING_ROOT }
    if (-not $chosen -and $IncludeGitCommon) {
        $gitArgs = @('rev-parse', '--git-common-dir')
        if ($Repository) { $gitArgs = @('-C', $Repository) + $gitArgs }
        $git = & git @gitArgs 2>$null
        if ($LASTEXITCODE -eq 0 -and $git) {
            if (-not [IO.Path]::IsPathRooted($git)) {
                $base = if ($Repository) { $Repository } else { (Get-Location).Path }
                $git = Join-Path $base $git
            }
            $chosen = Join-Path $git 'antiphon/test-timings'
        }
    }
    if (-not $chosen) { return $null }
    $full = [IO.Path]::GetFullPath($chosen)
    if ($Hosted) {
        foreach ($forbidden in @($Checkout, $Output, $Cleanup)) {
            if (-not $forbidden) { continue }
            $parent = [IO.Path]::GetFullPath($forbidden).TrimEnd('\', '/')
            if ($full.Equals($parent, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'root-outside-custody'
            }
        }
    }
    return $full
}

function Test-TestTimingComponent {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -eq '.' -or $Value -eq '..' -or $Value.Contains('..') -or $Value.Contains('/') -or $Value.Contains('\') -or [IO.Path]::IsPathRooted($Value)) {
        throw 'path-component-refused'
    }
}

function Write-TestTimingEvent {
    param([string]$Root, $Event)
    if (-not $Root) { return }
    Test-TestTimingComponent $Event.runId
    Test-TestTimingComponent ([string]$Event.attempt)
    Test-TestTimingComponent $Event.eventId
    $date = ([DateTimeOffset]$Event.startedAt).UtcDateTime.ToString('yyyy-MM-dd')
    $dir = Join-Path $Root (Join-Path 'runs' (Join-Path $date (Join-Path $Event.runId ([string]$Event.attempt))))
    [void][IO.Directory]::CreateDirectory($dir)
    $path = Join-Path $dir 'events.jsonl'
    $mutexName = 'antiphon-timing-' + ([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($dir))).Replace('-', '').Substring(0, 32))
    $mutex = [Threading.Mutex]::new($false, $mutexName)
    [void]$mutex.WaitOne()
    try {
        $existing = @()
        if (Test-Path -LiteralPath $path) {
            foreach ($line in [IO.File]::ReadAllLines($path)) {
                if ($line) { $existing += ($line | ConvertFrom-Json) }
            }
        }
        $match = $existing | Where-Object { $_.eventId -ceq $Event.eventId } | Select-Object -First 1
        $payload = ($Event | ConvertTo-Json -Compress -Depth 8)
        if ($match) {
            $matchPayload = ($match | ConvertTo-Json -Compress -Depth 8)
            if ($matchPayload -cne $payload) { throw 'event-conflict' }
            return
        }
        $owned = $existing | Where-Object { $_.eventKind -ceq 'set-completed' -and $_.setId -ceq $Event.setId -and $_.setKind -ceq $Event.setKind -and $_.producer -cne $Event.producer } | Select-Object -First 1
        if ($Event.eventKind -ceq 'set-completed' -and $owned) { return }
        [IO.File]::AppendAllText($path, $payload + "`n")
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
}

function Read-TestTimingEvents {
    param([string[]]$Roots)
    $events = @(); $warnings = @(); $corrupt = 0
    $seen = @{}
    foreach ($root in @($Roots)) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $root -Filter events.jsonl -Recurse -File | Sort-Object FullName)) {
            $text = [IO.File]::ReadAllText($file.FullName)
            if (-not $text) { continue }
            $torn = -not $text.EndsWith("`n")
            $lines = $text -split "`n"
            $last = $lines.Length - 1
            for ($i = 0; $i -lt $last; $i++) {
                if (-not $lines[$i]) { continue }
                try { $item = $lines[$i] | ConvertFrom-Json } catch { $corrupt++; continue }
                $id = [string]$item.eventId
                if (-not [string]::IsNullOrEmpty($id)) {
                    if ($seen.ContainsKey($id)) { continue }
                    $seen[$id] = $true
                }
                $events += $item
            }
            if ($torn -and $lines[$last]) { $warnings += 'torn-tail' }
        }
    }
    return [pscustomobject]@{ events = @($events); warnings = @($warnings); corruptCount = $corrupt }
}

function Get-TestTimingMedian {
    param([double[]]$Values)
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[[int](($sorted.Count - 1) / 2)] }
    $mid = [int]($sorted.Count / 2)
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Get-TestTimingNearestRank {
    param([double[]]$Values, [double]$Percent)
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $rank = [int][Math]::Ceiling($Percent * $sorted.Count)
    if ($rank -lt 1) { $rank = 1 }
    if ($rank -gt $sorted.Count) { $rank = $sorted.Count }
    return [double]$sorted[$rank - 1]
}

function Get-TestTimingReport {
    param([string[]]$Roots, [string]$GroupBy = 'class')
    $read = Read-TestTimingEvents -Roots $Roots
    $metric = if ($GroupBy -eq 'roster') { 'host-wall' } else { 'inclusive-case-window' }
    $wanted = switch ($GroupBy) {
        'roster' { @('roster', 'run', 'chunk') }
        'category' { @('category') }
        default { @('class', 'checkpoint-row', 'category') }
    }
    $groups = @()
    $buckets = @{}
    foreach ($ev in @($read.events)) {
        if ($ev.eventKind -cne 'set-completed') { continue }
        if ($wanted -notcontains [string]$ev.setKind) { continue }
        $key = (@($ev.repositoryIdentity, $ev.setId, $ev.membershipDigest, $ev.measurementKind, $ev.os, $ev.architecture, $ev.runtimeVersion, $ev.concurrency, $ev.profile, $ev.hostId) -join '|')
        if (-not $buckets.ContainsKey($key)) { $buckets[$key] = @() }
        $buckets[$key] += $ev
    }
    foreach ($key in ($buckets.Keys | Sort-Object)) {
        $rows = @($buckets[$key])
        $success = @($rows | Where-Object { $_.outcome -ceq 'passed' -and $_.purpose -cne 'retry' })
        $values = @($success | ForEach-Object {
            $metricValue = if ($GroupBy -eq 'roster') { $_.wallSeconds } elseif ($null -ne $_.caseWindowSeconds) { $_.caseWindowSeconds } else { $_.wallSeconds }
            if ($null -ne $metricValue) { [double]$metricValue }
        })
        $ordered = @($success | Sort-Object -Property @{ Expression = { [datetimeoffset]$_.startedAt } }, @{ Expression = { $_.sequence } })
        $insufficient = $ordered.Count -lt 10
        $prior = $null; $latestFive = $null; $change = $null; $percent = $null
        if (-not $insufficient) {
            $window = @($ordered | Select-Object -Last 10)
            $priorValues = @($window | Select-Object -First 5 | ForEach-Object { if ($GroupBy -eq 'roster') { [double]$_.wallSeconds } elseif ($null -ne $_.caseWindowSeconds) { [double]$_.caseWindowSeconds } else { [double]$_.wallSeconds } })
            $latestValues = @($window | Select-Object -Last 5 | ForEach-Object { if ($GroupBy -eq 'roster') { [double]$_.wallSeconds } elseif ($null -ne $_.caseWindowSeconds) { [double]$_.caseWindowSeconds } else { [double]$_.wallSeconds } })
            $prior = Get-TestTimingMedian $priorValues
            $latestFive = Get-TestTimingMedian $latestValues
            $change = $latestFive - $prior
            if ($prior -eq 0) { $percent = $null } else { $percent = 100 * $change / $prior }
        }
        $buildSeen = @{}; $buildCost = 0.0; $wallCost = 0.0
        foreach ($row in $rows) {
            if ($null -ne $row.wallSeconds) { $wallCost += [double]$row.wallSeconds }
            if ($row.producingBuildId -and -not $buildSeen.ContainsKey([string]$row.producingBuildId)) {
                $buildSeen[[string]$row.producingBuildId] = $true
                if ($null -ne $row.buildSeconds) { $buildCost += [double]$row.buildSeconds }
            }
        }
        $sample = $rows | Select-Object -First 1
        $groups += [pscustomobject]@{
            setId = [string]$sample.setId
            setKind = [string]$sample.setKind
            membershipDigest = [string]$sample.membershipDigest
            hostId = [string]$sample.hostId
            os = [string]$sample.os
            runtimeVersion = [string]$sample.runtimeVersion
            concurrency = $sample.concurrency
            metric = $metric
            sampleCount = $values.Count
            median = (Get-TestTimingMedian $values)
            p90 = (Get-TestTimingNearestRank $values 0.9)
            latest = $(if ($ordered.Count) { if ($GroupBy -eq 'roster') { [double]$ordered[-1].wallSeconds } elseif ($null -ne $ordered[-1].caseWindowSeconds) { [double]$ordered[-1].caseWindowSeconds } else { [double]$ordered[-1].wallSeconds } } else { $null })
            priorFiveMedian = $prior
            latestFiveMedian = $latestFive
            absoluteChange = $change
            percentageChange = $percent
            insufficientHistory = [bool]$insufficient
            provisional = [bool]($values.Count -lt 5)
            totalAttemptedCost = ($wallCost + $buildCost)
            missingCount = @($rows | Where-Object { $_.unavailableReason }).Count
        }
    }
    return [pscustomobject]@{ schemaVersion = 'antiphon-test-timing-report-v1'; corruptCount = $read.corruptCount; warnings = @($read.warnings); groups = @($groups) }
}

function Add-TestTimingEstimates {
    param($Selection, $Report)
    if ($Selection -isnot [System.Collections.IDictionary]) {
        $map = [ordered]@{}
        foreach ($property in $Selection.PSObject.Properties) { $map[$property.Name] = $property.Value }
        $Selection = $map
    }
    $estimates = @()
    foreach ($set in @($Selection.selectedSets)) {
        $id = [string]$set.id
        $group = @($Report.groups) | Where-Object { $_.setId -ceq $id } | Select-Object -First 1
        if ($null -eq $group -or $null -eq $group.median) {
            $estimates += [ordered]@{ setId = $id; median = $null; p90 = $null; sampleCount = 0; provisional = $true; unbudgeted = $true; last = $null }
        } else {
            $estimates += [ordered]@{ setId = $id; median = $group.median; p90 = $group.p90; sampleCount = $group.sampleCount; provisional = [bool]$group.provisional; unbudgeted = $false; last = $group.latest }
        }
    }
    $copy = [ordered]@{}
    foreach ($key in @($Selection.Keys)) {
        if ($key -cne 'estimates' -and $key -cne 'budgetObservations') { $copy[$key] = $Selection[$key] }
    }
    $copy.estimates = @($estimates)
    $copy.budgetObservations = @('budget-observed')
    $copy.selectedSets = @($Selection.selectedSets)
    return $copy
}
