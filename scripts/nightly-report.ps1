#requires -Version 5.1
<#
.SYNOPSIS
    Compose a nightly run card and file or update it on the Antiphon board.

    CARD-0124. Reads summary.json (and an optional previous summary for the
    new/still/fixed delta), then applies the one-open-nightly-card rule:
    red creates or updates; green auto-closes only an unassigned Backlog card.

    HTTP goes through Invoke-Antiphon, injectable via -HttpShim
    (param($Method, $Uri, $Headers, $Body)). No token is sent. A dead API
    writes card.md next to the summary and exits 3.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER Summary
    Path to this run's summary.json.

.PARAMETER PreviousSummary
    Optional previous run summary.json for the failure delta.

.PARAMETER HttpShim
    Injectable HTTP surface. Scriptblock signature: param($Method, $Uri, $Headers, $Body).

.PARAMETER DryRun
    Print the writes that would be sent and write card.md; no HTTP writes.

.PARAMETER Board
    Board name or guid. Default Antiphon.

.PARAMETER Api
    Antiphon API base. Default $env:ANTIPHON_API or http://localhost:17202.

.PARAMETER PassThru
    Return a result object instead of exiting. For in-process tests.
#>
param(
    [Parameter(Mandatory = $true)][string]$Summary,
    [string]$PreviousSummary = '',
    [scriptblock]$HttpShim,
    [switch]$DryRun,
    [string]$Board = 'Antiphon',
    [string]$Api = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$maxTitle = 300
$maxDescription = 20000
$script:didRetry = $false
$script:title = ''
$script:body = ''
$script:action = 'none'
$script:cardMdPath = $null
$script:exitCode = 0
$script:intendedWrites = @()

if ([string]::IsNullOrWhiteSpace($Api)) {
    $Api = $env:ANTIPHON_API
}
if ([string]::IsNullOrWhiteSpace($Api)) {
    $Api = 'http://localhost:17202'
}
$Api = $Api.TrimEnd('/')

function Write-ReportLine {
    param([string]$Message)
    Write-Host $Message
}

function ConvertTo-UtcDate {
    param([object]$Value)
    if ($null -eq $Value -or $Value -eq '') { return $null }
    if ($Value -is [datetimeoffset]) { return ([datetimeoffset]$Value).UtcDateTime }
    if ($Value -is [datetime]) {
        $dt = [datetime]$Value
        if ($dt.Kind -eq [DateTimeKind]::Utc) { return $dt }
        if ($dt.Kind -eq [DateTimeKind]::Local) { return $dt.ToUniversalTime() }
        return [datetime]::SpecifyKind($dt, 'Utc')
    }
    try {
        return [datetimeoffset]::Parse(
            [string]$Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind
        ).UtcDateTime
    } catch {
        return $null
    }
}

function Get-LondonStamp {
    param([datetime]$Utc)
    if ($Utc.Kind -eq [DateTimeKind]::Unspecified) {
        $Utc = [datetime]::SpecifyKind($Utc, [DateTimeKind]::Utc)
    } else {
        $Utc = $Utc.ToUniversalTime()
    }
    $tz = $null
    try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('GMT Standard Time') } catch { }
    if ($null -eq $tz) {
        try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Europe/London') } catch { }
    }
    if ($null -eq $tz) {
        return $Utc.ToString('yyyy-MM-dd HH:mm') + ' UTC'
    }
    $local = [TimeZoneInfo]::ConvertTimeFromUtc($Utc, $tz)
    return $local.ToString('yyyy-MM-dd HH:mm') + ' Europe/London'
}

function Format-Duration {
    param([double]$Seconds)
    $s = [int][math]::Round($Seconds)
    if ($s -lt 0) { $s = 0 }
    if ($s -lt 60) { return ('{0}s' -f $s) }
    $m = [int][math]::Floor($s / 60)
    $r = $s % 60
    if ($r -eq 0) { return ('{0}m' -f $m) }
    return ('{0}m{1}s' -f $m, $r)
}

function Get-FailedTests {
    param([string]$LogPath, [int]$Cap = 25)

    $items = @()
    if (-not (Test-Path -LiteralPath $LogPath)) {
        return @{ names = @(); details = @(); extra = 0 }
    }
    $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) {
        return @{ names = @(); details = @(); extra = 0 }
    }
    $text = [regex]::Replace($text, '[\x1B]\[[0-?]*[ -/]*[@-~]', '')
    $lines = $text -split '\r?\n'
    $times = [char]0xD7

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $name = $null
        $m = [regex]::Match($line, '^failed\s+(.+?)\s+\(')
        if ($m.Success) { $name = $m.Groups[1].Value.Trim() }
        if (-not $name) {
            $m = [regex]::Match($line, '^\s*FAIL\s+(\S+)\s+>\s+(.+?)\s*$')
            if ($m.Success) {
                $name = ($m.Groups[1].Value.Trim() + ' > ' + $m.Groups[2].Value.Trim())
            }
        }
        if (-not $name) {
            $m = [regex]::Match($line, ('^\s*[' + [regex]::Escape([string]$times) + 'x]\s+(.+?)\s*$'))
            if ($m.Success) {
                $name = $m.Groups[1].Value.Trim()
                $name = [regex]::Replace($name, '\s+\d+m?s$', '')
            }
        }
        if (-not $name) { continue }

        $detail = ''
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $next = $lines[$j].Trim()
            if ([string]::IsNullOrEmpty($next)) { continue }
            if ($next -match '^failed\s+') { break }
            if ($next -match '^\s*FAIL\s+') { break }
            $detail = $next
            if ($detail.Length -gt 300) { $detail = $detail.Substring(0, 300) }
            break
        }
        $items += [pscustomobject]@{ name = $name; detail = $detail }
    }

    $seen = @{}
    $unique = @()
    foreach ($it in $items) {
        if ($seen.ContainsKey($it.name)) { continue }
        $seen[$it.name] = $true
        $unique += $it
    }
    $extra = 0
    if ($unique.Count -gt $Cap) {
        $extra = $unique.Count - $Cap
        $unique = $unique[0..($Cap - 1)]
    }
    return @{
        names   = @($unique | ForEach-Object { $_.name })
        details = @($unique)
        extra   = [int]$extra
    }
}

function Get-SuiteFailed {
    param($Suite)
    $names = @()
    $details = @()
    $extra = 0
    if ($null -ne $Suite.failedTests) {
        foreach ($n in @($Suite.failedTests)) {
            if ($null -eq $n) { continue }
            if ($n -is [string]) {
                $names += [string]$n
            } elseif ($n.name) {
                $names += [string]$n.name
                if ($n.detail) {
                    $details += [pscustomobject]@{ name = [string]$n.name; detail = [string]$n.detail }
                }
            }
        }
    }
    if ($names.Count -eq 0 -and $Suite.log) {
        $parsed = Get-FailedTests -LogPath ([string]$Suite.log)
        $names = @($parsed.names)
        $details = @($parsed.details)
        $extra = [int]$parsed.extra
    } elseif ($Suite.failedDetails) {
        $details = @($Suite.failedDetails)
        if ($Suite.failedExtra) { $extra = [int]$Suite.failedExtra }
    } elseif ($Suite.failedExtra) {
        $extra = [int]$Suite.failedExtra
    }
    return @{ names = $names; details = $details; extra = $extra }
}

function ConvertTo-NightlyJson {
    param($Object)
    return ($Object | ConvertTo-Json -Depth 8 -Compress)
}

function Invoke-Antiphon {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Body = $null
    )
    $headers = @{}
    if ($HttpShim) {
        return & $HttpShim -Method $Method -Uri $Uri -Headers $headers -Body $Body
    }
    $invokeOnce = {
        param($Method, $Uri, $Body)
        $params = @{
            Method  = $Method
            Uri     = $Uri
            Headers = @{}
        }
        if ($null -ne $Body -and $Body -ne '') {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $params.Body = $bytes
            $params.ContentType = 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod @params
    }
    try {
        $result = & $invokeOnce $Method $Uri $Body
    } catch {
        if ($script:didRetry) { throw }
        $script:didRetry = $true
        Write-ReportLine 'API failed; retrying once after 60s.'
        Start-Sleep -Seconds 60
        $result = & $invokeOnce $Method $Uri $Body
    }
    # A JSON array returned from a nested scriptblock arrives as one Object[].
    # Streaming the elements so @() at the caller collects boards/columns correctly.
    if ($result -is [System.Array]) {
        foreach ($item in $result) { $item }
    } else {
        $result
    }
}

function ConvertTo-FlatArray {
    param($Value)
    $out = @()
    if ($null -eq $Value) { return $out }
    if ($Value -is [System.Array]) {
        foreach ($v in $Value) {
            if ($v -is [System.Array]) {
                foreach ($inner in $v) { $out += $inner }
            } else {
                $out += $v
            }
        }
        return $out
    }
    return @($Value)
}

function Write-CardMdFile {
    param([string]$Path, [string]$Title, [string]$Body)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $text = $Title + "`r`n`r`n" + $Body
    Set-Content -LiteralPath $Path -Value $text -Encoding UTF8
}

function Limit-Description {
    param([string]$Text, [int]$Max = 20000)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    if ($Text.Length -le $Max) { return $Text }
    $parts = [regex]::Split($Text, '(?m)(?=^## Run )')
    while ($Text.Length -gt $Max -and $parts.Count -gt 1) {
        $parts = @($parts[0..($parts.Count - 2)])
        $Text = ($parts -join '')
    }
    if ($Text.Length -gt $Max) {
        $Text = $Text.Substring(0, $Max)
    }
    return $Text
}

function Test-CardHasLabel {
    param($Card, [string]$Label)
    foreach ($l in @($Card.labels)) {
        if ([string]$l -eq $Label) { return $true }
    }
    return $false
}

function Test-CardOpen {
    param($Card)
    $status = [string]$Card.status
    return @('Backlog', 'InProgress', 'Review', 'NeedsDecision') -contains $status
}

function Get-CardToken {
    param($Card)
    if ($null -eq $Card) { return $null }
    if ($Card.concurrencyToken) { return [string]$Card.concurrencyToken }
    if ($Card.card -and $Card.card.concurrencyToken) { return [string]$Card.card.concurrencyToken }
    return $null
}

function Get-CardObject {
    param($Response)
    if ($null -eq $Response) { return $null }
    if ($Response.card) { return $Response.card }
    return $Response
}

function Get-FailedNamesFromSummary {
    param($SummaryObject)
    $names = @()
    foreach ($suite in @($SummaryObject.suites)) {
        $failed = Get-SuiteFailed -Suite $suite
        foreach ($n in @($failed.names)) { $names += $n }
    }
    return $names
}

function Format-CountLine {
    param($Suite)
    if ($Suite.skipped) { return 'skipped: build failed' }
    if ($Suite.timedOut) { return 'TIMEOUT - see log' }
    if ($Suite.countsParsed) {
        $failedN = if ($null -ne $Suite.failed) { $Suite.failed } else { 0 }
        $passedN = if ($null -ne $Suite.passed) { $Suite.passed } else { 0 }
        $skippedN = 0
        if ($null -ne $Suite.skippedCount) { $skippedN = $Suite.skippedCount }
        elseif ($null -ne $Suite.skipped -and $Suite.skipped -is [int]) { $skippedN = $Suite.skipped }
        $dur = Format-Duration $Suite.durationSeconds
        if ($Suite.exitCode -ne 0) {
            return ('{0} failed / {1} passed / {2} skipped - {3}' -f $failedN, $passedN, $skippedN, $dur)
        }
        return ('{0} passed - {1}' -f $passedN, $dur)
    }
    $code = $Suite.exitCode
    if ($null -eq $code) { $code = '?' }
    return ('exit {0}, counts unparsed - see log' -f $code)
}

function New-RunSection {
    param($SummaryObject, $PreviousObject)

    $started = ConvertTo-UtcDate $SummaryObject.startedAt
    if ($null -eq $started) { $started = [datetime]::UtcNow }
    $stamp = Get-LondonStamp -Utc $started
    $succeeded = [bool]$SummaryObject.succeeded
    $outcome = [string]$SummaryObject.outcome
    if ([string]::IsNullOrWhiteSpace($outcome)) {
        $outcome = if ($succeeded) { 'green' } else { 'TESTS' }
    }
    $verdict = if ($succeeded) { 'GREEN' } else { ('RED ({0})' -f $outcome) }
    $dur = Format-Duration $SummaryObject.durationSeconds
    $sha = [string]$SummaryObject.sha
    if ([string]::IsNullOrWhiteSpace($sha)) { $sha = 'unknown' }
    $gitRef = [string]$SummaryObject.gitRef
    if ([string]::IsNullOrWhiteSpace($gitRef)) { $gitRef = 'origin/master' }
    $clone = [string]$SummaryObject.clone
    if ([string]::IsNullOrWhiteSpace($clone)) { $clone = 'C:\Antiphon\nightly\checkout' }
    $logDir = [string]$SummaryObject.logDir
    $concurrent = 0
    if ($SummaryObject.preflight -and $SummaryObject.preflight.concurrent) {
        $concurrent = [int]$SummaryObject.preflight.concurrent.total
    }
    $dockerVer = ''
    if ($SummaryObject.preflight -and $SummaryObject.preflight.docker -and $SummaryObject.preflight.docker.version) {
        $dockerVer = [string]$SummaryObject.preflight.docker.version
    }
    $dockerBit = if ($dockerVer) { ('Docker {0}' -f $dockerVer) } else { 'Docker unknown' }

    $lines = @()
    $lines += ('## Run {0} -- {1} -- {2} -- {3} {4}' -f $stamp, $verdict, $dur, $gitRef, $sha)
    $lines += ('Concurrent test processes at start: {0} | {1} | clone {2}' -f $concurrent, $dockerBit, $clone)
    $lines += ''
    $lines += '| Step | Result | Detail |'
    $lines += '| --- | --- | --- |'
    foreach ($b in @($SummaryObject.builds)) {
        $result = [string]$b.result
        if ([string]::IsNullOrWhiteSpace($result)) {
            $result = if ($b.exitCode -eq 0) { 'pass' } else { 'FAIL' }
        }
        $detail = [string]$b.detail
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = Format-Duration $b.durationSeconds }
        $lines += ('| {0} | {1} | {2} |' -f [string]$b.name, $result, $detail)
    }
    foreach ($s in @($SummaryObject.suites)) {
        $result = [string]$s.result
        if ([string]::IsNullOrWhiteSpace($result)) {
            if ($s.skipped) { $result = 'skipped' }
            elseif ($s.exitCode -eq 0) { $result = 'pass' }
            else { $result = 'FAIL' }
        }
        $lines += ('| {0} | {1} | {2} |' -f [string]$s.name, $result, (Format-CountLine $s))
    }

    $currNames = @(Get-FailedNamesFromSummary $SummaryObject)
    $prevNames = @()
    if ($null -ne $PreviousObject) {
        $prevNames = @(Get-FailedNamesFromSummary $PreviousObject)
    }
    $currSet = @{}
    foreach ($n in $currNames) { $currSet[$n] = $true }
    $prevSet = @{}
    foreach ($n in $prevNames) { $prevSet[$n] = $true }
    $newNames = @($currNames | Where-Object { -not $prevSet.ContainsKey($_) })
    $stillNames = @($currNames | Where-Object { $prevSet.ContainsKey($_) })
    $fixedNames = @($prevNames | Where-Object { -not $currSet.ContainsKey($_) })

    $detailByName = @{}
    foreach ($s in @($SummaryObject.suites)) {
        $failed = Get-SuiteFailed -Suite $s
        foreach ($d in @($failed.details)) {
            $detailByName[[string]$d.name] = [string]$d.detail
        }
    }

    if (-not $succeeded) {
        $lines += ''
        $lines += ('### New since last run ({0})' -f $newNames.Count)
        if ($newNames.Count -eq 0) {
            $lines += '- none'
        } else {
            foreach ($n in $newNames) {
                $det = ''
                if ($detailByName.ContainsKey($n)) { $det = $detailByName[$n] }
                if ($det) { $lines += ('- {0}  {1}' -f $n, $det) }
                else { $lines += ('- {0}' -f $n) }
            }
        }
        $lines += ('### Still failing ({0})  |  ### Fixed since last run ({1})' -f $stillNames.Count, $fixedNames.Count)
        foreach ($n in $stillNames) { $lines += ('- still: {0}' -f $n) }
        foreach ($n in $fixedNames) { $lines += ('- fixed: {0}' -f $n) }
    }

    $lines += ''
    if ($logDir) {
        $lines += ('Logs: {0} (antiphon-tests.log, agents-pty-tests.log, client-tests.log, build.log, summary.json)' -f $logDir)
    }
    $rerun = $null
    foreach ($s in @($SummaryObject.suites)) {
        $failed = Get-SuiteFailed -Suite $s
        if ($failed.names.Count -gt 0) {
            $first = [string]$failed.names[0]
            if ($s.id -eq 'client' -or [string]$s.name -eq 'client') {
                $file = $first
                if ($file -match '^(\S+)\s+>') { $file = $Matches[1] }
                $rerun = ('pwsh -File scripts/test-client.ps1 {0}' -f $file)
            } else {
                $exe = 'tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.exe'
                if ($s.id -eq 'agents-pty') {
                    $exe = 'tests/Antiphon.Agents.Pty.Tests/bin/Debug/net9.0/Antiphon.Agents.Pty.Tests.exe'
                }
                $rerun = ('{0} --treenode-filter "/*/*/*/{1}"' -f $exe, $first)
            }
            break
        }
    }
    if ($rerun) {
        $lines += ('Re-run one: {0}' -f $rerun)
    }
    return ($lines -join "`n")
}

function New-NightlyTitle {
    param($SummaryObject, [datetime]$Utc)
    $date = 'unknown'
    $tz = $null
    try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('GMT Standard Time') } catch { }
    if ($null -eq $tz) {
        try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Europe/London') } catch { }
    }
    if ($null -ne $tz) {
        $date = [TimeZoneInfo]::ConvertTimeFromUtc($Utc.ToUniversalTime(), $tz).ToString('yyyy-MM-dd')
    } else {
        $date = $Utc.ToUniversalTime().ToString('yyyy-MM-dd')
    }
    $outcome = [string]$SummaryObject.outcome
    if ($outcome -eq 'BUILD') {
        $title = ('Nightly red {0}: BUILD FAILED' -f $date)
        if ($title.Length -gt $maxTitle) { $title = $title.Substring(0, $maxTitle) }
        return $title
    }
    $bits = @()
    foreach ($s in @($SummaryObject.suites)) {
        $nFail = 0
        if ($null -ne $s.failed) { $nFail = [int]$s.failed }
        elseif ($s.failedTests) { $nFail = @($s.failedTests).Count }
        if ($nFail -gt 0) {
            $bits += ('{0} {1} failed' -f [string]$s.name, $nFail)
        } elseif ($s.timedOut) {
            $bits += ('{0} TIMEOUT' -f [string]$s.name)
        } elseif (-not $s.skipped -and $s.exitCode -ne 0) {
            $bits += ('{0} exit {1}' -f [string]$s.name, $s.exitCode)
        }
    }
    foreach ($b in @($SummaryObject.builds)) {
        if ($b.result -eq 'FAIL' -or ($null -ne $b.exitCode -and $b.exitCode -ne 0 -and -not $b.skipped)) {
            $bits += ('{0} failed' -f [string]$b.name)
        }
    }
    $rest = if ($bits.Count -gt 0) { $bits -join ', ' } else { $outcome }
    if ([string]::IsNullOrWhiteSpace($rest)) { $rest = 'failed' }
    $title = ('Nightly red {0}: {1}' -f $date, $rest)
    if ($title.Length -gt $maxTitle) { $title = $title.Substring(0, $maxTitle) }
    return $title
}

function Finish-Report {
    if ($PassThru) {
        return [pscustomobject]@{
            ExitCode   = $script:exitCode
            Action     = $script:action
            Title      = $script:title
            Body       = $script:body
            CardMdPath = $script:cardMdPath
            Writes     = $script:intendedWrites
        }
    }
    exit $script:exitCode
}

if (-not (Test-Path -LiteralPath $Summary)) {
    Write-Error ("Summary '{0}' does not exist." -f $Summary)
    $script:exitCode = 2
    return Finish-Report
}

$summaryObject = Get-Content -LiteralPath $Summary -Raw -Encoding UTF8 | ConvertFrom-Json
$previousObject = $null
if (-not [string]::IsNullOrWhiteSpace($PreviousSummary) -and (Test-Path -LiteralPath $PreviousSummary)) {
    $previousObject = Get-Content -LiteralPath $PreviousSummary -Raw -Encoding UTF8 | ConvertFrom-Json
}

function ConvertTo-NightlyCanonicalPathSafe {
    param([string]$Path)
    try {
        return [System.IO.Path]::GetFullPath($Path)
    } catch {
        return $Path
    }
}

$logDir = [string]$summaryObject.logDir
if ([string]::IsNullOrWhiteSpace($logDir)) {
    $logDir = Split-Path -Parent (ConvertTo-NightlyCanonicalPathSafe $Summary)
}
if ([string]::IsNullOrWhiteSpace($logDir)) {
    $logDir = Split-Path -Parent $Summary
}
$script:cardMdPath = Join-Path $logDir 'card.md'

$started = ConvertTo-UtcDate $summaryObject.startedAt
if ($null -eq $started) { $started = [datetime]::UtcNow }
$succeeded = [bool]$summaryObject.succeeded
$outcome = [string]$summaryObject.outcome
if ([string]::IsNullOrWhiteSpace($outcome)) {
    $outcome = if ($succeeded) { 'green' } else { 'TESTS' }
}
$sha = [string]$summaryObject.sha
if ([string]::IsNullOrWhiteSpace($sha)) { $sha = 'unknown' }
$dateLondon = Get-LondonStamp -Utc $started
$dateOnly = $dateLondon.Substring(0, 10)

$script:body = New-RunSection -SummaryObject $summaryObject -PreviousObject $previousObject
$script:title = if ($succeeded) { ('Nightly green {0}' -f $dateOnly) } else { New-NightlyTitle -SummaryObject $summaryObject -Utc $started }

$importance = 'Normal'
$classLabel = 'tests'
if ($outcome -eq 'BUILD') {
    $importance = 'High'
    $classLabel = 'build'
}
$labels = @('nightly', $classLabel)

$failCounts = @()
foreach ($s in @($summaryObject.suites)) {
    $n = 0
    if ($null -ne $s.failed) { $n = [int]$s.failed }
    elseif ($s.failedTests) { $n = @($s.failedTests).Count }
    if ($n -gt 0) { $failCounts += ('{0} {1} failed' -f [string]$s.name, $n) }
}
$countPhrase = if ($failCounts.Count -gt 0) { $failCounts -join ', ' } else { $outcome }

try {
    $boards = ConvertTo-FlatArray (Invoke-Antiphon -Method GET -Uri ($Api + '/api/boards'))
    $boardRow = $null
    $parsed = [guid]::Empty
    if ([guid]::TryParse($Board, [ref]$parsed)) {
        $boardRow = @($boards | Where-Object { [string]$_.id -eq $Board }) | Select-Object -First 1
    }
    if ($null -eq $boardRow) {
        $boardRow = @($boards | Where-Object { [string]$_.name -eq $Board }) | Select-Object -First 1
    }
    if ($null -eq $boardRow) {
        $names = @($boards | ForEach-Object { [string]$_.name }) -join ', '
        throw ("No board named '{0}'. Known: {1}" -f $Board, $names)
    }
    $boardId = [string]$boardRow.id

    $statuses = @('Backlog', 'InProgress', 'Review', 'NeedsDecision', 'Done')
    $openNightly = @()
    $doneNightly = @()
    foreach ($st in $statuses) {
        $uri = '{0}/api/cards?boardId={1}&status={2}' -f $Api, $boardId, $st
        $page = Invoke-Antiphon -Method GET -Uri $uri
        $cards = @()
        if ($page -and $null -ne $page.cards) { $cards = ConvertTo-FlatArray $page.cards }
        elseif ($page -is [System.Array]) { $cards = ConvertTo-FlatArray $page }
        $truncated = $false
        if ($page -and $page.truncated) { $truncated = [bool]$page.truncated }
        $matched = @($cards | Where-Object {
                (Test-CardHasLabel -Card $_ -Label 'nightly') -and ($null -eq $_.archivedAt -or $_.archivedAt -eq '')
            })
        if ($truncated -and $matched.Count -eq 0) {
            Write-ReportLine ('truncated=true on status {0} with no nightly match; treating as none' -f $st)
        }
        if ($st -eq 'Done') { $doneNightly += $matched }
        else { $openNightly += $matched }
    }

    $columns = ConvertTo-FlatArray (Invoke-Antiphon -Method GET -Uri ('{0}/api/boards/{1}/columns' -f $Api, $boardId))
    $terminal = @($columns | Where-Object { $_.isTerminal } | Sort-Object columnOrder) | Select-Object -First 1

    if ($succeeded) {
        if ($openNightly.Count -eq 0) {
            $script:action = 'none'
            Write-ReportLine 'green; no open nightly card; nothing to file.'
            return Finish-Report
        }
        $card = $openNightly | Sort-Object { ConvertTo-UtcDate $_.updatedAt } -Descending | Select-Object -First 1
        $status = [string]$card.status
        $assigned = $card.assignedAgentId
        $owner = $card.ownerSessionId
        $unassigned = ($null -eq $assigned -or $assigned -eq '') -and ($null -eq $owner -or $owner -eq '')
        $discussionBody = ('green on {0} at {1}; not closing because the card is {2}/{3}' -f $dateOnly, $sha, $status, $(if ($assigned) { $assigned } else { 'unassigned' }))
        if ($status -eq 'Backlog' -and $unassigned -and $null -ne $terminal) {
            $reason = ('[nightly auto-close] green on {0} at {1} - reopen if this was a flake you want tracked' -f $dateOnly, $sha)
            $moveBody = ConvertTo-NightlyJson @{
                boardColumnId    = [string]$terminal.id
                concurrencyToken = Get-CardToken $card
                reason           = $reason
            }
            $script:intendedWrites += [pscustomobject]@{ Method = 'PATCH'; Uri = ('{0}/api/cards/{1}' -f $Api, $card.id); Body = $moveBody }
            if ($DryRun) {
                Write-ReportLine ('DRYRUN PATCH /api/cards/{0} close: {1}' -f $card.id, $reason)
                Write-CardMdFile -Path $script:cardMdPath -Title $script:title -Body $script:body
                $script:action = 'dry-run'
                return Finish-Report
            }
            $null = Invoke-Antiphon -Method PATCH -Uri ('{0}/api/cards/{1}' -f $Api, $card.id) -Body $moveBody
            $script:action = 'closed'
            Write-ReportLine ('closed {0} ({1})' -f $card.identifier, $reason)
            return Finish-Report
        }
        $discBody = ConvertTo-NightlyJson @{ body = $discussionBody; author = 'nightly' }
        $script:intendedWrites += [pscustomobject]@{ Method = 'POST'; Uri = ('{0}/api/cards/{1}/discussion' -f $Api, $card.id); Body = $discBody }
        if ($DryRun) {
            Write-ReportLine ('DRYRUN POST /api/cards/{0}/discussion: {1}' -f $card.id, $discussionBody)
            Write-CardMdFile -Path $script:cardMdPath -Title $script:title -Body $script:body
            $script:action = 'dry-run'
            return Finish-Report
        }
        $null = Invoke-Antiphon -Method POST -Uri ('{0}/api/cards/{1}/discussion' -f $Api, $card.id) -Body $discBody
        $script:action = 'discussion'
        Write-ReportLine ('discussion on {0}: {1}' -f $card.identifier, $discussionBody)
        return Finish-Report
    }

    # Red path.
    $target = $null
    $reopened = $false
    if ($openNightly.Count -gt 0) {
        $target = $openNightly | Sort-Object { ConvertTo-UtcDate $_.updatedAt } -Descending | Select-Object -First 1
    } else {
        $cutoff = [datetime]::UtcNow.AddDays(-7)
        $reopenable = @($doneNightly | Where-Object {
                ([string]$_.terminalReason).StartsWith('[nightly auto-close]') -and
                ($null -ne (ConvertTo-UtcDate $_.updatedAt)) -and
                ((ConvertTo-UtcDate $_.updatedAt) -ge $cutoff)
            } | Sort-Object { ConvertTo-UtcDate $_.updatedAt } -Descending)
        if ($reopenable.Count -gt 0) {
            $target = $reopenable[0]
            $reopenReason = ('red again on {0}' -f $dateOnly)
            $reopenBody = ConvertTo-NightlyJson @{
                concurrencyToken = Get-CardToken $target
                reason           = $reopenReason
            }
            $script:intendedWrites += [pscustomobject]@{ Method = 'POST'; Uri = ('{0}/api/cards/{1}/reopen' -f $Api, $target.id); Body = $reopenBody }
            if (-not $DryRun) {
                $reopenResult = Invoke-Antiphon -Method POST -Uri ('{0}/api/cards/{1}/reopen' -f $Api, $target.id) -Body $reopenBody
                $target = Get-CardObject $reopenResult
                $reopened = $true
                Write-ReportLine ('reopened {0}' -f $target.identifier)
            } else {
                $reopened = $true
            }
        }
    }

    $existingDescription = ''
    if ($null -ne $target -and $target.description) { $existingDescription = [string]$target.description }
    $combined = $script:body
    if (-not [string]::IsNullOrWhiteSpace($existingDescription)) {
        $combined = $script:body + "`n`n" + $existingDescription
    }
    $combined = Limit-Description -Text $combined -Max $maxDescription
    $script:body = $combined

    if ($null -eq $target) {
        $createObj = @{
            title       = $script:title
            description = $script:body
            importance  = $importance
            urgency     = 'Normal'
            labels      = @('nightly', $classLabel)
        }
        $createBody = ConvertTo-NightlyJson $createObj
        $script:intendedWrites += [pscustomobject]@{ Method = 'POST'; Uri = ('{0}/api/boards/{1}/cards' -f $Api, $boardId); Body = $createBody }
        if ($DryRun) {
            Write-ReportLine ('DRYRUN POST /api/boards/{0}/cards title={1}' -f $boardId, $script:title)
            Write-CardMdFile -Path $script:cardMdPath -Title $script:title -Body $script:body
            $script:action = 'dry-run'
            return Finish-Report
        }
        $created = Invoke-Antiphon -Method POST -Uri ('{0}/api/boards/{1}/cards' -f $Api, $boardId) -Body $createBody
        $script:action = 'created'
        Write-ReportLine ('created {0} {1}' -f $created.identifier, $script:title)
        return Finish-Report
    }

    $mergedLabels = @()
    foreach ($l in @($target.labels)) {
        $s = [string]$l
        if ($s -and ($mergedLabels -notcontains $s)) { $mergedLabels += $s }
    }
    foreach ($l in @('nightly', $classLabel)) {
        if ($mergedLabels -notcontains $l) { $mergedLabels += $l }
    }
    if ($mergedLabels.Count -lt 2) { $mergedLabels += 'nightly' }
    $patchObj = @{
        concurrencyToken = Get-CardToken $target
        reason           = ('nightly red on {0}' -f $dateOnly)
        title            = $script:title
        description      = $script:body
        labels           = $mergedLabels
        editedBy         = 'nightly'
    }
    if ($importance -eq 'High') { $patchObj['importance'] = 'High' }
    $patchBody = ConvertTo-NightlyJson $patchObj
    $discText = ('still red on {0}: {1}' -f $dateOnly, $countPhrase)
    $discBody = ConvertTo-NightlyJson @{ body = $discText; author = 'nightly' }
    $script:intendedWrites += [pscustomobject]@{ Method = 'PATCH'; Uri = ('{0}/api/cards/{1}/content' -f $Api, $target.id); Body = $patchBody }
    $script:intendedWrites += [pscustomobject]@{ Method = 'POST'; Uri = ('{0}/api/cards/{1}/discussion' -f $Api, $target.id); Body = $discBody }
    if ($DryRun) {
        Write-ReportLine ('DRYRUN PATCH /api/cards/{0}/content + discussion' -f $target.id)
        Write-CardMdFile -Path $script:cardMdPath -Title $script:title -Body $script:body
        $script:action = 'dry-run'
        return Finish-Report
    }
    $null = Invoke-Antiphon -Method PATCH -Uri ('{0}/api/cards/{1}/content' -f $Api, $target.id) -Body $patchBody
    $null = Invoke-Antiphon -Method POST -Uri ('{0}/api/cards/{1}/discussion' -f $Api, $target.id) -Body $discBody
    $script:action = if ($reopened) { 'reopened' } else { 'updated' }
    Write-ReportLine ('{0} {1}' -f $script:action, $target.identifier)
    return Finish-Report
} catch {
    $script:exitCode = 3
    $script:action = 'reporting-failed'
    Write-ReportLine ('REPORTING: could not file card, body at {0}' -f $script:cardMdPath)
    Write-ReportLine $_.Exception.Message
    try {
        Write-CardMdFile -Path $script:cardMdPath -Title $script:title -Body $script:body
    } catch {
        Write-ReportLine ('could not write card.md: {0}' -f $_.Exception.Message)
    }
    return Finish-Report
}
