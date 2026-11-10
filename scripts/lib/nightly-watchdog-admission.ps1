#requires -Version 5.1
# CARD-1514 S3. Launch watchdog admission. Detection and refusal only.
# ASCII-only. Does not dot-source nightly-tests-impl.ps1 (that file loads this one).
# A missing or unmeasured calibration keeps the suite watchdog. Nothing here stops a process.

function Get-NightlyWatchdogField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-NightlyCeilTo60 {
    param([decimal]$Seconds)
    if ($Seconds -le 0) { return [double]$Seconds }
    $steps = [decimal]::Ceiling($Seconds / [decimal]60)
    return [double]($steps * [decimal]60)
}

function Get-NightlyNearestRank {
    param([int]$Count)
    if ($Count -le 0) { return 0 }
    $rank = [decimal]::Ceiling(([decimal]95 * [decimal]$Count) / [decimal]100)
    $index = [int]$rank
    if ($index -lt 1) { $index = 1 }
    if ($index -gt $Count) { $index = $Count }
    return $index
}

function Get-NightlyWatchdogErrorFactors {
    param($Samples, $PlannedSeconds)
    $planned = 0.0
    $plannedKnown = $false
    if ($null -ne $PlannedSeconds) {
        $parsedPlan = 0.0
        if ([double]::TryParse([string]$PlannedSeconds, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsedPlan)) {
            if (-not [double]::IsNaN($parsedPlan) -and -not [double]::IsInfinity($parsedPlan) -and $parsedPlan -gt 0) {
                $planned = $parsedPlan
                $plannedKnown = $true
            }
        }
    }
    $ratios = New-Object System.Collections.Generic.List[double]
    $failed = 0
    $censored = $false
    $complete = 0
    foreach ($sample in @($Samples)) {
        if ($null -eq $sample) { continue }
        $flag = Get-NightlyWatchdogField $sample 'Censored'
        if ($flag -eq $true) {
            $censored = $true
            continue
        }
        $wallText = Get-NightlyWatchdogField $sample 'Wall'
        $wall = 0.0
        $wallKnown = $false
        if ($null -ne $wallText) {
            $wallInvariant = $null
            if ($wallText -is [string]) {
                $wallInvariant = [string]$wallText
            } else {
                try { $wallInvariant = [System.Convert]::ToString($wallText, [Globalization.CultureInfo]::InvariantCulture) } catch { $wallInvariant = $null }
            }
            if ($null -ne $wallInvariant) {
                $wallKnown = [double]::TryParse($wallInvariant, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$wall)
            }
        }
        if (-not $wallKnown -or [double]::IsNaN($wall) -or [double]::IsInfinity($wall) -or $wall -le 0) {
            $censored = $true
            continue
        }
        $complete++
        $outcome = [string](Get-NightlyWatchdogField $sample 'Outcome')
        if ($outcome -ceq 'Failed') { $failed++ }
        if ($plannedKnown) { [void]$ratios.Add($wall / $planned) }
    }
    if (-not $plannedKnown) { $censored = $true }
    $e95 = 1.0
    $emax = 1.0
    if ($ratios.Count -gt 0) {
        $sorted = @($ratios | Sort-Object)
        $rank = Get-NightlyNearestRank -Count $sorted.Count
        $picked = [double]$sorted[$rank - 1]
        if ($picked -gt $e95) { $e95 = $picked }
        foreach ($ratio in $sorted) {
            if ([double]$ratio -gt $emax) { $emax = [double]$ratio }
        }
        if ($emax -lt $e95) { $emax = $e95 }
    }
    return [pscustomobject]@{
        N = $complete
        E95 = [double]$e95
        Emax = [double]$emax
        FailedKept = $failed
        Censored = [bool]$censored
    }
}

function Get-NightlyWatchdogSeconds {
    param([double]$E95, [double]$Emax, [double]$PlannedSeconds, [double]$MarginSeconds)
    $paced = ([decimal]2 * [decimal]$E95 * [decimal]$PlannedSeconds)
    $tailed = ([decimal]$Emax * [decimal]$PlannedSeconds) + [decimal]$MarginSeconds
    $raw = [decimal]600
    if ($paced -gt $raw) { $raw = $paced }
    if ($tailed -gt $raw) { $raw = $tailed }
    return (Get-NightlyCeilTo60 -Seconds $raw)
}

function Get-NightlySuiteWatchdogSeconds {
    param($PolicyObject, [string]$SuiteId)
    $fallbackMs = 3600000
    if ($SuiteId -eq 'e2e') { $fallbackMs = 7200000 }
    if ($null -eq $PolicyObject) { return ([double]$fallbackMs / 1000.0) }
    if (Get-Command Get-NightlyWatchdogMs -ErrorAction SilentlyContinue) {
        $ms = Get-NightlyWatchdogMs -PolicyObject $PolicyObject -Key $SuiteId -Fallback $fallbackMs
        return ([double]$ms / 1000.0)
    }
    return ([double]$fallbackMs / 1000.0)
}

function Get-NightlyLaunchEstimateCeilingSeconds {
    param([string]$SuiteId)
    if ($SuiteId -eq 'e2e') { return 1800.0 }
    return 2400.0
}

function Test-NightlyWatchdogStratumMatch {
    param($Launch, $Bound)
    if ($null -eq $Launch -or $null -eq $Bound) { return $false }
    $keys = @(
        'suite', 'uidDigest', 'costDigest', 'executionDigest', 'assemblyDigest',
        'os', 'runtime', 'host', 'loadBand', 'fixtureMode', 'concurrency'
    )
    foreach ($key in $keys) {
        $left = Get-NightlyWatchdogField $Launch $key
        $right = Get-NightlyWatchdogField $Bound $key
        if ($null -eq $left -or $null -eq $right) { return $false }
        if ([string]$left -cne [string]$right) { return $false }
    }
    return $true
}

function Test-NightlyFiniteNumber {
    param($Value)
    $parsed = 0.0
    if ($null -eq $Value) { return $false }
    if (-not [double]::TryParse([string]$Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) { return $false }
    if ([double]::IsNaN($parsed) -or [double]::IsInfinity($parsed)) { return $false }
    return $true
}

function New-NightlyWatchdogDecision {
    param([bool]$Admitted, [double]$WatchdogSeconds, [string]$Reason, [double]$E95, [double]$Emax, [int]$N)
    $milliseconds = [int][Math]::Round($WatchdogSeconds * 1000.0)
    return [pscustomobject]@{
        Admitted = [bool]$Admitted
        WatchdogSeconds = [double]$WatchdogSeconds
        WatchdogMilliseconds = $milliseconds
        Reason = [string]$Reason
        E95 = [double]$E95
        Emax = [double]$Emax
        N = [int]$N
    }
}

function Resolve-NightlyLaunchWatchdog {
    param(
        [string]$SuiteId,
        $PlannedSeconds,
        [string]$Basis = 'default',
        [string]$PolicyHash = '',
        $LaunchStratum,
        $Calibration,
        $PolicyObject
    )
    $suiteSeconds = Get-NightlySuiteWatchdogSeconds -PolicyObject $PolicyObject -SuiteId $SuiteId
    if ($null -eq $PlannedSeconds) {
        return (New-NightlyWatchdogDecision -Admitted $true -WatchdogSeconds $suiteSeconds -Reason 'unmeasured-cost' -E95 1 -Emax 1 -N 0)
    }
    $planned = 0.0
    $parsed = [double]::TryParse([string]$PlannedSeconds, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$planned)
    if (-not $parsed -or [double]::IsNaN($planned) -or [double]::IsInfinity($planned) -or $planned -le 0) {
        return (New-NightlyWatchdogDecision -Admitted $false -WatchdogSeconds $suiteSeconds -Reason 'watchdog-seconds-invalid' -E95 1 -Emax 1 -N 0)
    }
    $ceiling = Get-NightlyLaunchEstimateCeilingSeconds -SuiteId $SuiteId
    if ($planned -gt $ceiling) {
        return (New-NightlyWatchdogDecision -Admitted $false -WatchdogSeconds $suiteSeconds -Reason 'launch-estimate-ceiling' -E95 1 -Emax 1 -N 0)
    }
    $eligible = $false
    $e95 = 1.0
    $emax = 1.0
    $margin = 0.0
    $bound = 0.0
    $wmax = 0.0
    $n = 0
    if ($Basis -ceq 'measured' -and $null -ne $Calibration) {
        $censored = Get-NightlyWatchdogField $Calibration 'Censored'
        $nValue = Get-NightlyWatchdogField $Calibration 'N'
        $nParsed = 0
        $nKnown = [int]::TryParse([string]$nValue, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$nParsed)
        $boundHash = [string](Get-NightlyWatchdogField $Calibration 'BoundPolicyHash')
        $hashOk = (-not [string]::IsNullOrWhiteSpace($boundHash)) -and ($boundHash -ceq [string]$PolicyHash)
        $stratum = Get-NightlyWatchdogField $Calibration 'Stratum'
        $stratumOk = Test-NightlyWatchdogStratumMatch -Launch $LaunchStratum -Bound $stratum
        $e95Value = Get-NightlyWatchdogField $Calibration 'E95'
        $emaxValue = Get-NightlyWatchdogField $Calibration 'Emax'
        $marginValue = Get-NightlyWatchdogField $Calibration 'M'
        $boundValue = Get-NightlyWatchdogField $Calibration 'C'
        $wmaxValue = Get-NightlyWatchdogField $Calibration 'Wmax'
        $numbersOk = (Test-NightlyFiniteNumber $e95Value) -and (Test-NightlyFiniteNumber $emaxValue) -and (Test-NightlyFiniteNumber $marginValue) -and (Test-NightlyFiniteNumber $boundValue) -and (Test-NightlyFiniteNumber $wmaxValue)
        if ($numbersOk) {
            $e95 = [double]$e95Value
            $emax = [double]$emaxValue
            $margin = [double]$marginValue
            $bound = [double]$boundValue
            $wmax = [double]$wmaxValue
        }
        if ($censored -ne $true -and $nKnown -and $nParsed -ge 20 -and $hashOk -and $stratumOk -and $numbersOk -and $emax -ge $e95 -and $e95 -ge 1 -and $margin -ge 0 -and $bound -gt 0 -and $wmax -gt 0) {
            $eligible = $true
            $n = $nParsed
        }
    }
    if (-not $eligible) {
        if ($planned -gt $suiteSeconds) {
            return (New-NightlyWatchdogDecision -Admitted $false -WatchdogSeconds $suiteSeconds -Reason 'suite-watchdog-ceiling' -E95 $e95 -Emax $emax -N $n)
        }
        return (New-NightlyWatchdogDecision -Admitted $true -WatchdogSeconds $suiteSeconds -Reason 'unmeasured-cost' -E95 $e95 -Emax $emax -N $n)
    }
    if ($planned -gt $bound) {
        return (New-NightlyWatchdogDecision -Admitted $false -WatchdogSeconds $suiteSeconds -Reason 'launch-estimate-ceiling' -E95 $e95 -Emax $emax -N $n)
    }
    $watchdog = Get-NightlyWatchdogSeconds -E95 $e95 -Emax $emax -PlannedSeconds $planned -MarginSeconds $margin
    $atCeiling = Get-NightlyWatchdogSeconds -E95 $e95 -Emax $emax -PlannedSeconds $bound -MarginSeconds $margin
    if ($watchdog -gt $wmax -or $atCeiling -gt $wmax) {
        return (New-NightlyWatchdogDecision -Admitted $false -WatchdogSeconds $watchdog -Reason 'policy-max-mismatch' -E95 $e95 -Emax $emax -N $n)
    }
    return (New-NightlyWatchdogDecision -Admitted $true -WatchdogSeconds $watchdog -Reason 'measured' -E95 $e95 -Emax $emax -N $n)
}

function Test-NightlyWatchdogKilled {
    param([double]$ElapsedSeconds, [double]$WatchdogSeconds, [bool]$Terminal)
    if ($Terminal -and $ElapsedSeconds -lt $WatchdogSeconds) { return $false }
    return ($ElapsedSeconds -ge $WatchdogSeconds)
}

# Once total ran and request execute stop are on disk, a leftover tree is
# killed after this grace instead of the full launch watchdog.
# Unmeasured placeholder. Chunk logs record execution-stop-to-exit-seconds.
# Set this near 2x that p95 (docs/test-performance-standards.md). 60000 matches
# NightlyCompletionGrace.DefaultMilliseconds.
$script:NightlyCompletionGraceMilliseconds = 60000

function Get-NightlyHarnessHangField {
    param([switch]$CompletedButHung)
    if ($CompletedButHung) { return 'completed-but-hung' }
    return ''
}

function Get-NightlyWatchdogTimeoutDisposition {
    param([bool]$TrxPresent, [bool]$TerminalAccounting, [switch]$CompletedButHung)
    $reason = 'watchdog-timeout'
    if ($CompletedButHung) { $reason = 'completed-but-hung' }
    return [pscustomobject]@{
        CoverageComplete = $false
        TestsPassed = $false
        Incomplete = $true
        Reason = $reason
        Pass = $false
    }
}

function Read-NightlyTrxRunSummary {
    param([string]$Path)
    $missing = [pscustomobject]@{
        Present = $false
        Outcome = ''
        Total = -1
        Executed = -1
        Passed = -1
        Failed = -1
        NotExecuted = -1
    }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $missing }
    $doc = New-Object System.Xml.XmlDocument
    $stream = $null
    try {
        $doc.XmlResolver = $null
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $doc.Load($stream)
    } catch {
        return $missing
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
    $summary = $doc.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $summary) { return $missing }
    $counters = $summary.SelectSingleNode('./*[local-name()="Counters"]')
    if ($null -eq $counters) { return $missing }
    $style = [Globalization.NumberStyles]::Integer
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $total = 0
    $executed = 0
    $notExecuted = 0
    $passed = 0
    $failed = 0
    $totalOk = [int]::TryParse([string]$counters.GetAttribute('total'), $style, $culture, [ref]$total)
    $executedOk = [int]::TryParse([string]$counters.GetAttribute('executed'), $style, $culture, [ref]$executed)
    $notOk = [int]::TryParse([string]$counters.GetAttribute('notExecuted'), $style, $culture, [ref]$notExecuted)
    if (-not $totalOk -or -not $executedOk -or -not $notOk) { return $missing }
    [void][int]::TryParse([string]$counters.GetAttribute('passed'), $style, $culture, [ref]$passed)
    [void][int]::TryParse([string]$counters.GetAttribute('failed'), $style, $culture, [ref]$failed)
    return [pscustomobject]@{
        Present = $true
        Outcome = [string]$summary.GetAttribute('outcome')
        Total = $total
        Executed = $executed
        Passed = $passed
        Failed = $failed
        NotExecuted = $notExecuted
    }
}

function Read-NightlyTextTail {
    param([string]$Path, [int]$ByteCount = 16384)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $stream = $null
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $length = [int64]$stream.Length
        if ($length -le 0) { return '' }
        $take = $ByteCount
        if ($take -gt $length) { $take = [int]$length }
        if ($take -le 0) { return '' }
        [void]$stream.Seek($length - $take, [System.IO.SeekOrigin]::Begin)
        $buffer = New-Object byte[] $take
        $read = $stream.Read($buffer, 0, $take)
        if ($read -le 0) { return '' }
        return [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    } catch {
        return ''
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Test-NightlyExecutionFinished {
    param([string]$TrxPath, [string]$DiagnosticPath)
    $summary = Read-NightlyTrxRunSummary -Path $TrxPath
    if (-not $summary.Present) { return $false }
    if ($summary.Outcome -cne 'Completed') { return $false }
    if ($summary.Executed -ne $summary.Total) { return $false }
    if ($summary.NotExecuted -ne 0) { return $false }
    $tail = Read-NightlyTextTail -Path $DiagnosticPath
    if ([string]::IsNullOrEmpty($tail)) { return $false }
    if ($tail.IndexOf('request execute stop') -lt 0) { return $false }
    $match = [regex]::Match($tail, 'total ran:\s*(\d+)', [System.Text.RegularExpressions.RegexOptions]::RightToLeft)
    if (-not $match.Success) { return $false }
    $ran = 0
    $style = [Globalization.NumberStyles]::Integer
    $culture = [Globalization.CultureInfo]::InvariantCulture
    if (-not [int]::TryParse($match.Groups[1].Value, $style, $culture, [ref]$ran)) { return $false }
    return ($ran -eq $summary.Total)
}

function Format-NightlyHangChildren {
    param($Identities, $Images, [int]$RootPid = 0)
    $ids = @($Identities)
    $imgs = @($Images)
    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $ids.Count; $i++) {
        $text = [string]$ids[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $pidText = $text
        $colon = $text.IndexOf(':')
        if ($colon -gt 0) { $pidText = $text.Substring(0, $colon) }
        $image = ''
        if ($i -lt $imgs.Count -and $null -ne $imgs[$i]) { $image = [string]$imgs[$i] }
        [void]$parts.Add(('pid={0}/image={1}' -f $pidText, $image))
    }
    if ($parts.Count -eq 0) {
        if ($RootPid -gt 0) { return ('pid={0}/image=' -f $RootPid) }
        return ''
    }
    return ($parts -join ',')
}

function Resolve-NightlyCompletedHang {
    param([bool]$TimedOut, [bool]$Finished, [string]$SuiteId, [string]$Children)
    if ($TimedOut -and $Finished) {
        $childText = $Children
        if ([string]::IsNullOrWhiteSpace($childText)) { $childText = 'pid=0/image=' }
        return [pscustomobject]@{
            CompletedButHung = $true
            Reason = ('completed-but-hung {0} children={1}' -f $SuiteId, $childText)
        }
    }
    return [pscustomobject]@{
        CompletedButHung = $false
        Reason = 'watchdog-timeout'
    }
}

function Write-NightlyHangCapture {
    param([string]$Path, [string]$SuiteId, [string]$Children, [int]$RootPid = 0)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $lines = @(
        'completed-hang-capture',
        ('suite={0}' -f $SuiteId),
        ('children={0}' -f $Children),
        ('root pid={0}' -f $RootPid),
        'handles=not-enumerated'
    )
    [System.IO.File]::WriteAllText($Path, (($lines -join "`n") + "`n"))
}


