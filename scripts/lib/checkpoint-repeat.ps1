# CARD-0885 native repeat evidence reducer. ASCII-only.
function Get-CheckpointRepeatEvidence {
    param(
        [xml]$Document,
        [int]$Requested,
        [string]$Nonce,
        [int]$MinExecuted,
        [string[]]$Expect
    )
    if ($Requested -lt 2 -or [string]::IsNullOrWhiteSpace($Nonce)) { throw 'repeat_request_invalid' }
    $results = @($Document.SelectNodes('//*[local-name()="UnitTestResult"]'))
    if ($results.Count -eq 0) { throw 'repeat_results_missing' }
    $definitions = @{}
    foreach ($unit in @($Document.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]'))) {
        $method = $unit.SelectSingleNode('./*[local-name()="TestMethod"]')
        if ($null -ne $method) {
            $definitions[[string]$unit.GetAttribute('id')] = ('{0}.{1}' -f $method.GetAttribute('className'), $method.GetAttribute('name'))
        }
    }
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $nativeIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ordinals = @()
    for ($i = 0; $i -lt $Requested; $i++) {
        $ordinals += [pscustomobject]@{ ordinal = $i; executed = 0; passed = 0; failed = 0; skipped = 0; cases = [System.Collections.ArrayList]::new() }
    }
    $pidValue = 0
    foreach ($result in $results) {
        $executionId = [string]$result.GetAttribute('executionId')
        if (-not $executionId -or -not $ids.Add($executionId)) { throw 'duplicate_execution_id' }
        $outcome = [string]$result.GetAttribute('outcome')
        if ($outcome -cnotin @('Passed', 'Failed', 'Error', 'Timeout', 'Aborted')) { throw 'repeat_skipped_or_unknown_outcome' }
        $stdoutNode = $result.SelectSingleNode('./*[local-name()="Output"]/*[local-name()="StdOut"]')
        $stdout = if ($null -eq $stdoutNode) { '' } else { [string]$stdoutNode.InnerText }
        if ($stdout.Contains('C885_REPEAT_INVALID')) { throw 'repeat_receiver_invalid' }
        $startLines = @([regex]::Matches($stdout, '(?m)^C885_REPEAT_START (.+)\r?$'))
        $endLines = @([regex]::Matches($stdout, '(?m)^C885_REPEAT_END (.+)\r?$'))
        if ($startLines.Count -ne 1 -or $endLines.Count -ne 1) { throw 'repeat_marker_pair_missing_or_duplicate' }
        $start = $startLines[0].Groups[1].Value | ConvertFrom-Json
        $end = $endLines[0].Groups[1].Value | ConvertFrom-Json
        if (($start | ConvertTo-Json -Compress -Depth 8) -cne ($end | ConvertTo-Json -Compress -Depth 8)) { throw 'repeat_marker_pair_disagreement' }
        if ($start.Version -ne 1 -or $start.Requested -ne $Requested -or $start.Nonce -cne $Nonce -or
            $start.Ordinal -lt 0 -or $start.Ordinal -ge $Requested -or $start.HostPid -lt 1) { throw 'repeat_marker_identity_mismatch' }
        $match = [regex]::Match([string]$start.NativeId, '^(?<key>.+\.\d+\.\d+)\.(?<ordinal>\d+)(?<inherit>_inherited[1-9]\d*)?$')
        if (-not $match.Success -or [int]$match.Groups['ordinal'].Value -ne $start.Ordinal -or
            ($match.Groups['key'].Value + $match.Groups['inherit'].Value) -cne $start.CaseKey -or
            -not $nativeIds.Add([string]$start.NativeId)) { throw 'repeat_native_identity_invalid' }
        if ($pidValue -eq 0) { $pidValue = [int]$start.HostPid }
        elseif ($pidValue -ne [int]$start.HostPid) { throw 'repeat_multiple_hosts' }
        $testId = [string]$result.GetAttribute('testId')
        $name = if ($definitions.ContainsKey($testId)) { [string]$definitions[$testId] } else { [string]$result.GetAttribute('testName') }
        if (-not $name.Contains([string]$start.Method, [StringComparison]::Ordinal)) { throw 'repeat_name_mismatch' }
        $ordinal = $ordinals[[int]$start.Ordinal]
        [void]$ordinal.cases.Add([pscustomobject]@{ caseKey = [string]$start.CaseKey; nativeId = [string]$start.NativeId; name = $name; outcome = $outcome })
        $ordinal.executed++
        if ($outcome -ceq 'Passed') { $ordinal.passed++ } else { $ordinal.failed++ }
    }
    $roster = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $ordinals[0].cases) { [void]$roster.Add([string]$case.caseKey) }
    if ($roster.Count -eq 0) { throw 'repeat_empty_roster' }
    foreach ($ordinal in $ordinals) {
        $keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($case in $ordinal.cases) {
            if (-not $keys.Add([string]$case.caseKey)) { throw 'repeat_duplicate_case' }
        }
        if (-not $keys.SetEquals($roster)) { throw 'repeat_roster_mismatch' }
        if ($ordinal.executed -lt $MinExecuted) { throw 'repeat_min_executed' }
        foreach ($token in $Expect) {
            if ([string]::IsNullOrWhiteSpace($token)) { continue }
            $hit = @($ordinal.cases | Where-Object { $_.name.Contains($token, [StringComparison]::OrdinalIgnoreCase) })
            if ($hit.Count -eq 0) { throw 'repeat_expect_miss' }
        }
    }
    $passedOrdinals = @($ordinals | Where-Object { $_.failed -eq 0 -and $_.skipped -eq 0 }).Count
    return [pscustomobject]@{
        requested = $Requested; completed = $Requested; passed = $passedOrdinals
        hostInvocations = 1; hostPid = $pidValue; nonce = $Nonce
        repetitions = @($ordinals | ForEach-Object {
            [pscustomobject]@{ ordinal = $_.ordinal; executed = $_.executed; passed = $_.passed
                failed = $_.failed; skipped = $_.skipped; cases = @($_.cases) }
        })
    }
}

function Test-CheckpointRepeatSummary {
    param($Repeat, [int]$Executed, [int]$Passed, [int]$Failed, [int]$Skipped, [int]$ExpectedRepeat)
    if ($null -eq $Repeat -or $Repeat.requested -ne $ExpectedRepeat -or
        $Repeat.completed -ne $ExpectedRepeat -or $Repeat.passed -ne $ExpectedRepeat -or
        $Repeat.hostInvocations -ne 1 -or $Repeat.hostPid -lt 1 -or
        [string]::IsNullOrWhiteSpace([string]$Repeat.nonce) -or
        @($Repeat.repetitions).Count -ne $ExpectedRepeat) { return $false }
    $sumExecuted = 0; $sumPassed = 0; $sumFailed = 0; $sumSkipped = 0
    $first = $null
    for ($i = 0; $i -lt $ExpectedRepeat; $i++) {
        $ordinal = $Repeat.repetitions[$i]
        if ($ordinal.ordinal -ne $i -or $ordinal.failed -ne 0 -or $ordinal.skipped -ne 0 -or
            $ordinal.executed -ne @($ordinal.cases).Count -or $ordinal.passed -ne $ordinal.executed) { return $false }
        $keys = @($ordinal.cases | ForEach-Object { [string]$_.caseKey } | Sort-Object -CaseSensitive)
        if (@($keys | Select-Object -Unique).Count -ne $keys.Count) { return $false }
        if ($null -eq $first) { $first = $keys } elseif (($keys -join [char]0) -cne ($first -join [char]0)) { return $false }
        $sumExecuted += $ordinal.executed; $sumPassed += $ordinal.passed
        $sumFailed += $ordinal.failed; $sumSkipped += $ordinal.skipped
    }
    return $sumExecuted -eq $Executed -and $sumPassed -eq $Passed -and
        $sumFailed -eq $Failed -and $sumSkipped -eq $Skipped
}

function Get-CheckpointPhaseTimings {
    param([xml]$Document, [DateTimeOffset]$Launched, [DateTimeOffset]$Exited, [double]$HostElapsedSeconds,
        [double]$BuildSeconds, [double]$SlotWaitSeconds)
    $value = [pscustomobject]@{
        slotWaitSeconds = $SlotWaitSeconds; buildSeconds = $BuildSeconds
        startupSeconds = $null; testsWallSeconds = $null; teardownSeconds = $null
        testHostWallSeconds = $null; method = 'utc-trx-and-monotonic-host'; unavailableReason = $null
    }
    if ($null -eq $Document -or $HostElapsedSeconds -lt 0 -or $Exited -lt $Launched -or
        [Math]::Abs(($Exited - $Launched).TotalSeconds - $HostElapsedSeconds) -gt 2) {
        $value.unavailableReason = 'host_clock_discontinuity'; return $value
    }
    $results = @($Document.SelectNodes('//*[local-name()="UnitTestResult"]'))
    if ($results.Count -eq 0) { $value.unavailableReason = 'trx_timestamps_missing'; return $value }
    $starts = [System.Collections.Generic.List[DateTimeOffset]]::new()
    $ends = [System.Collections.Generic.List[DateTimeOffset]]::new()
    foreach ($result in $results) {
        $start = [DateTimeOffset]::MinValue; $end = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse($result.GetAttribute('startTime'), [ref]$start) -or
            -not [DateTimeOffset]::TryParse($result.GetAttribute('endTime'), [ref]$end)) {
            $value.unavailableReason = 'trx_timestamps_missing'; return $value
        }
        if ($end -lt $start) { $value.unavailableReason = 'trx_boundary_inconsistent'; return $value }
        $starts.Add($start); $ends.Add($end)
    }
    $first = @($starts | Sort-Object)[0]; $last = @($ends | Sort-Object)[-1]
    if ($first -lt $Launched -or $last -gt $Exited -or $first -gt $last) {
        $value.unavailableReason = 'trx_boundary_inconsistent'; return $value
    }
    $value.startupSeconds = ($first - $Launched).TotalSeconds
    $value.testsWallSeconds = ($last - $first).TotalSeconds
    $value.teardownSeconds = ($Exited - $last).TotalSeconds
    $value.testHostWallSeconds = $HostElapsedSeconds
    return $value
}
