#requires -Version 5.1
# CARD-1514 S3. The launch watchdog admission itself is compiled (Antiphon.Nightly.NightlyWatchdog); this file keeps
# the TRX run summary, the hang capture and the completion grace. Detection and refusal only.
# ASCII-only. Does not dot-source nightly-tests-impl.ps1 (that file loads this one).
# A missing or unmeasured calibration keeps the suite watchdog. Nothing here stops a process.

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


