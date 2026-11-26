#requires -Version 7.0
# Run from a checkout containing this file. The only output is a JSON file beside CandidatesRoot.
[CmdletBinding()]
param([string]$CandidatesRoot = 'C:\Antiphon\releases\candidates')

$ErrorActionPreference = 'Stop'
$pins = [ordered]@{
    b8cbfb24 = '2fd61b643ea4231fa732cd4969b5284d43b33f39'
    '1fe763a9' = '0cfc4ac1ffb41f5af159a0c1db423fe119950267'
}

function Read-JsonFile($File) {
    Get-Content -LiteralPath $File -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable -Depth 100
}

# Results often precede definitions. Retain only the fields needed for the join for one TRX.
# Reading through XmlReader avoids materializing stdout, attachments and per-case stack traces.
function Read-Trx($Path) {
    $definitions = @{}
    $rows = [Collections.Generic.List[object]]::new()
    $issues = [Collections.Generic.HashSet[string]]::new()
    $summaryOutcome = ''
    $declaredTotal = $null
    $unitId = ''
    $unitStorage = ''
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        while ($reader.Read()) {
            if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            switch ($reader.LocalName) {
                UnitTest {
                    $unitId = $reader.GetAttribute('id')
                    $unitStorage = $reader.GetAttribute('storage')
                }
                TestMethod {
                    $codeBase = $reader.GetAttribute('codeBase')
                    if (!$codeBase) { $codeBase = $unitStorage }
                    $assembly = (($codeBase -split '[/\\]')[-1] -replace '(?i)\.dll$', '')
                    $class = $reader.GetAttribute('className')
                    if ($unitId -and $assembly -and $class) {
                        $definitions[$unitId] = @($assembly, $class)
                    }
                }
                UnitTestResult {
                    $rows.Add(@($reader.GetAttribute('testId'), $reader.GetAttribute('outcome'), $reader.GetAttribute('duration')))
                }
                ResultSummary { $summaryOutcome = $reader.GetAttribute('outcome') }
                Counters { $declaredTotal = $reader.GetAttribute('total') }
            }
        }
    } finally { $reader.Dispose() }

    if (!$rows.Count) { [void]$issues.Add('empty-trx') }
    if ($summaryOutcome -notin 'Completed','Passed','Failed') { [void]$issues.Add('incomplete-trx-summary') }
    if ($null -eq $declaredTotal -or [long]$declaredTotal -ne $rows.Count) { [void]$issues.Add('trx-counter-mismatch') }
    return @{ definitions = $definitions; rows = $rows; issues = $issues }
}

function Add-ClassResults($Trx, $Classes, $Issues) {
    foreach ($row in $Trx.rows) {
        $id, $outcome, $duration = $row
        if (!$id -or !$Trx.definitions.ContainsKey($id)) {
            [void]$Issues.Add('unbound-test-id')
            continue
        }
        $assembly, $class = $Trx.definitions[$id]
        $key = "$assembly/$class"
        if (!$Classes.ContainsKey($key)) {
            $Classes[$key] = [ordered]@{ assembly = $assembly; class = $class; outcomes = [ordered]@{}
                totalSeconds = [decimal]0; maxSeconds = $null; untimed = 0 }
        }
        $stats = $Classes[$key]
        if (!$outcome) { $outcome = 'Unknown'; [void]$Issues.Add('missing-outcome') }
        $stats.outcomes[$outcome] = 1 + [int]$stats.outcomes[$outcome]
        $elapsed = [timespan]::Zero
        if (![timespan]::TryParse($duration, [Globalization.CultureInfo]::InvariantCulture, [ref]$elapsed) -or $elapsed.Ticks -lt 0) {
            $stats.untimed++
            continue
        }
        $seconds = [decimal]$elapsed.Ticks / [timespan]::TicksPerSecond
        $stats.totalSeconds += $seconds
        if ($null -eq $stats.maxSeconds -or $seconds -gt $stats.maxSeconds) { $stats.maxSeconds = $seconds }
    }
}

function Get-NativeGroup($Groups, $Suite, $Chunk) {
    if (!$Suite -or !$Chunk -or $Suite -in 'client','scripts') { return }
    $key = "$Suite-$Chunk"
    if (!$Groups.ContainsKey($key)) {
        $Groups[$key] = @{ expected = $null; issues = [Collections.Generic.HashSet[string]]::new() }
    }
    return $Groups[$key]
}

function Get-RunTiming($RunDir, $Prefix, $ExpectedSha) {
    $summaryFile = Join-Path $RunDir.FullName 'summary.json'
    $summary = if ([IO.File]::Exists($summaryFile)) { Read-JsonFile $summaryFile }
    $runId = $Prefix
    if ($summary) {
        $runId = [string]$summary.runId
        if (!$runId.StartsWith($Prefix, [StringComparison]::OrdinalIgnoreCase) -or $summary.sha -ne $ExpectedSha) {
            throw "Run or SHA mismatch in $summaryFile"
        }
    } else {
        $candidate = Read-JsonFile (Join-Path $RunDir.Parent.Parent.FullName 'candidate.json')
        if ($candidate.sha -ne $ExpectedSha) { throw "Candidate SHA mismatch for $Prefix" }
    }

    $groups = @{}
    $scheduleFile = Join-Path $RunDir.FullName 'dispatch-schedule.json'
    $schedule = if ([IO.File]::Exists($scheduleFile)) { Read-JsonFile $scheduleFile }
    if ($schedule) {
        $scheduleId = [string]$schedule.runId
        if (!$scheduleId.StartsWith($runId, [StringComparison]::OrdinalIgnoreCase)) { throw "Foreign schedule in $scheduleFile" }
        $runId = $scheduleId
        foreach ($entry in $schedule.entries) { $null = Get-NativeGroup $groups $entry.suite $entry.chunk }
    }
    # Each manifest can contain many UIDs. Discard them once its group counts have been read.
    $timingDirectories = if ($summary.timingRoot -and [IO.Directory]::Exists($summary.timingRoot)) {
        $summary.timingRoot
    } else {
        foreach ($parent in @($RunDir.FullName, $RunDir.Parent.FullName)) {
            (Get-ChildItem -LiteralPath $parent -Directory -Filter "timing-$Prefix*").FullName
        }
        $local = Join-Path $RunDir.FullName 'timing'
        if ([IO.Directory]::Exists($local)) { $local }
    }
    foreach ($directory in $timingDirectories) {
        if (!$directory) { continue }
        $manifests = Get-ChildItem -LiteralPath $directory -File -Filter 'groups-planned*.json' |
            Where-Object Name -ne 'groups-planned-index.json'
        foreach ($file in $manifests) {
            $manifest = Read-JsonFile $file.FullName
            $manifestId = [string]$manifest.runId
            if (!$manifestId.StartsWith($runId, [StringComparison]::OrdinalIgnoreCase) -or $manifest.sourceDigest -ne $ExpectedSha) {
                throw "Foreign planned groups in $($file.FullName)"
            }
            $runId = $manifestId
            foreach ($entry in $manifest.groups) {
                $group = Get-NativeGroup $groups $manifest.suite $entry.chunk
                if ($group -and $null -ne $entry.plannedTestIds) { $group.expected = $entry.plannedTestIds.Count }
            }
        }
    }
    foreach ($row in $summary.suites) {
        $group = Get-NativeGroup $groups $row.id $row.chunk
        if (!$group) { continue }
        if ($row.timedOut) { [void]$group.issues.Add('timeout') }
        if ($row.coverageComplete -eq $false) { [void]$group.issues.Add('incomplete-coverage') }
    }
    $timings = if ($summary.timingChunks) { $summary.timingChunks } else {
        Get-ChildItem -LiteralPath $RunDir.FullName -File -Filter '*.timing.json' | ForEach-Object { Read-JsonFile $_.FullName }
    }
    foreach ($timing in $timings) {
        $group = Get-NativeGroup $groups $timing.suite $timing.chunk
        if (!$group) { continue }
        if ($timing.trxMissing) { [void]$group.issues.Add('reported-missing-trx') }
        if (!$timing.completedAt) { [void]$group.issues.Add('no-completion-time') }
        if ($timing.why -match 'timeout|hang|launch-failed') { [void]$group.issues.Add('interrupted-launch') }
    }

    $classes = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $files = @(Get-ChildItem -LiteralPath $RunDir.FullName -File -Filter '*.trx' | Sort-Object Name)
    $present = @{}
    foreach ($file in $files) {
        $key = $file.BaseName
        $present[$key] = $true
        if (!$groups.ContainsKey($key)) {
            $groups[$key] = @{ expected = $null; issues = [Collections.Generic.HashSet[string]]::new() }
            [void]$groups[$key].issues.Add('unlisted-trx')
        }
        $group = $groups[$key]
        try { $trx = Read-Trx $file.FullName }
        catch [Xml.XmlException], [IO.IOException], [UnauthorizedAccessException] {
            [void]$group.issues.Add('unreadable-trx')
            continue
        }
        $group.issues.UnionWith($trx.issues)
        if ($null -ne $group.expected -and $group.expected -ne $trx.rows.Count) { [void]$group.issues.Add('planned-count-mismatch') }
        Add-ClassResults $trx $classes $group.issues
    }
    $incomplete = [ordered]@{}
    foreach ($key in ($groups.Keys | Sort-Object)) {
        if (!$present.ContainsKey($key)) { [void]$groups[$key].issues.Add('missing-trx') }
        if ($groups[$key].issues.Count) { $incomplete[$key] = @($groups[$key].issues | Sort-Object) }
    }
    [ordered]@{
        runId = $runId; sha = $ExpectedSha; summaryPresent = [bool]$summary
        outcome = $summary.outcome; coverageComplete = $summary.coverageComplete
        scheduleComplete = $schedule.preparationComplete; trxFiles = $files.Count
        knownNativeGroups = $groups.Count; incompleteGroups = $incomplete
        classes = @($classes.Values | Sort-Object { $_.assembly }, { $_.class })
    }
}

$root = Get-Item -LiteralPath $CandidatesRoot
$runs = foreach ($prefix in $pins.Keys) {
    $matchesForRun = @(Get-ChildItem -LiteralPath $root.FullName -Directory | ForEach-Object {
        $logs = Join-Path $_.FullName 'logs'
        if ([IO.Directory]::Exists($logs)) { Get-ChildItem -LiteralPath $logs -Directory -Filter "*-$prefix" }
    })
    if ($matchesForRun.Count -ne 1) { throw "Expected one log directory ending in -$prefix, found $($matchesForRun.Count)." }
    Get-RunTiming $matchesForRun[0] $prefix $pins[$prefix]
}
# Write outside the retained tree. Replace the directory entry atomically only after both reads succeed.
$output = Join-Path $root.Parent.FullName 'card-2082-windows-class-timing.json'
$temp = "$output.$([guid]::NewGuid().ToString('N')).tmp"
try {
    $json = [ordered]@{ schemaVersion = 1; runs = @($runs) } | ConvertTo-Json -Depth 10 -Compress
    [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temp, $output, $true)
} finally {
    if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
}
Write-Output $output
