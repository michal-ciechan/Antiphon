#requires -Version 5.1
<#
.SYNOPSIS
    Report (default) or archive/delete stale Claude Code remote-control sessions via the
    code-sessions REST API. Classifies GET /v1/code/sessions against last_event_at age,
    optional disconnected-only narrowing, status/worker gates, and a join to live local
    ~/.claude/sessions/*.json bridgeSessionId values.

    Default is a dry run. Pass -Execute to mutate. Default -Action is Archive (reversible).
    -Action Delete is implemented but is not what a scheduled run should use.

    Recurring cleanup is a Windmill schedule on server2 (u/lndcobra/claude_session_cleanup),
    not a local Windows Scheduled Task - do not add one. scripts/install-cleanup-task.ps1 is
    the superseded local-task installer for a different job and must not gain a sibling.

    ASCII-only on purpose: must parse under both pwsh 7 and Windows PowerShell 5.1.

    Never prints, logs, or writes the OAuth access token. The JSON report is built from an
    allow-list of fields, never by echoing a raw API response. This script never refreshes
    the CLI token; stale/missing credentials exit 2.

.PARAMETER OlderThanDays
    C2 clock. A session is old when last_event_at is strictly older than this many days.

.PARAMETER Action
    Archive (POST .../archive) or Delete (DELETE .../{id}). Default Archive.

.PARAMETER Execute
    Absent => report only. THE DEFAULT IS A DRY RUN.

.PARAMETER MaxPerRun
    Refuse (exit 4) rather than truncate when the candidate set is larger than this.

.PARAMETER SkipUnread
    Off by default. When set, unread rows are not candidates.

.PARAMETER RequireDisconnected
    C3 narrowing. Default $true (safest unparameterized invocation). Pass
    -RequireDisconnected:$false to also consider connected-but-ancient rows; C6 still
    excludes any session that is the bridgeSessionId of a live local claude/node pid.

.PARAMETER ReportPath
    Directory for the JSON report. Default <repo>/logs/claude-session-cleanup.

.PARAMETER InputJson
    Classify a saved GET /v1/code/sessions body instead of calling out. Forces -Execute
    off. Used by tests and the measured-split demonstration.

.PARAMETER HttpShim
    Injectable HTTP surface; defaults to Invoke-RestMethod. Tests record calls through this.
    Scriptblock signature: param($Method, $Uri, $Headers, $Body).

.PARAMETER LiveBridgesJson
    Optional C6 input: a JSON array of objects with bridgeSessionId (the committed
    scripts/fixtures/claude-sessions-live-bridges.json shape). When set, those ids are
    treated as live exclusions and ~/.claude/sessions is not enumerated.

.PARAMETER PassThru
    Return a result object instead of exiting. For in-process tests that inject -HttpShim.
#>
[CmdletBinding()]
param(
    [int]$OlderThanDays = 7,
    [ValidateSet('Archive', 'Delete')]
    [string]$Action = 'Archive',
    [switch]$Execute,
    [int]$MaxPerRun = 10,
    [switch]$SkipUnread,
    [bool]$RequireDisconnected = $true,
    [string]$ReportPath = '',
    [string]$InputJson = '',
    [scriptblock]$HttpShim,
    [string]$LiveBridgesJson = '',
    [string]$SessionsDir = '',
    [string]$CredentialsPath = '',
    [string]$Now = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch {
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$listLimit = 100
$apiBase = 'https://api.anthropic.com'
$anthropicVersion = '2023-06-01'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

if (-not $ReportPath) {
    $ReportPath = Join-Path (Join-Path $repoRoot 'logs') 'claude-session-cleanup'
}
if (-not $SessionsDir) {
    $SessionsDir = Join-Path $env:USERPROFILE (Join-Path '.claude' 'sessions')
}
if (-not $CredentialsPath) {
    $CredentialsPath = Join-Path $env:USERPROFILE (Join-Path '.claude' '.credentials.json')
}

# InputJson is a saved list: never mutate from it, and never hit the network for the list.
if ($InputJson) {
    if ($Execute) {
        Write-Host 'InputJson is set; -Execute is ignored (dry run only).'
    }
    $Execute = $false
}

$exitCode = 0
$reportFile = $null
$candidates = @()
$skipped = @()
$mutations = @()
$listCount = 0
$truncated = $false
$liveBridgeCount = 0
$accessToken = $null
$nowUtc = [datetime]::UtcNow

# --- helpers ----------------------------------------------------------------

function ConvertTo-UtcDate {
    param([object]$Value)
    if ($null -eq $Value -or $Value -eq '') { return $null }
    # pwsh 7 ConvertFrom-Json turns ISO timestamps into DateTime; never stringify
    # those through the current culture (UK dd/MM vs US MM/dd swapped Aug 12 into
    # Dec 8 and made day-13+ fail to parse).
    if ($Value -is [datetimeoffset]) {
        return ([datetimeoffset]$Value).UtcDateTime
    }
    if ($Value -is [datetime]) {
        $dt = [datetime]$Value
        if ($dt.Kind -eq [DateTimeKind]::Utc) { return $dt }
        if ($dt.Kind -eq [DateTimeKind]::Local) { return $dt.ToUniversalTime() }
        return [datetime]::SpecifyKind($dt, 'Utc')
    }
    try {
        return [datetimeoffset]::Parse(
            [string]$Value,
            $invariant,
            [System.Globalization.DateTimeStyles]::RoundtripKind
        ).UtcDateTime
    } catch {
        return $null
    }
}

function Format-UtcStamp {
    param([object]$Value)
    $utc = ConvertTo-UtcDate $Value
    if ($null -eq $utc) { return [string]$Value }
    return ([datetime]$utc).ToString('yyyy-MM-ddTHH:mm:ssZ', $invariant)
}

function Format-AgeDays {
    param([double]$Days)
    return $Days.ToString('0.0', $invariant)
}

function ConvertTo-SessionSuffix {
    param([string]$Id)
    if ([string]::IsNullOrEmpty($Id)) { return '' }
    if ($Id.Length -gt 4 -and $Id.StartsWith('cse_')) { return $Id.Substring(4) }
    if ($Id.Length -gt 8 -and $Id.StartsWith('session_')) { return $Id.Substring(8) }
    return $Id
}

function ConvertTo-UnixMs {
    param([datetime]$Utc)
    $epoch = [datetime]::SpecifyKind([datetime]'1970-01-01', 'Utc')
    return [int64]($Utc - $epoch).TotalMilliseconds
}

function Get-ExpiresAtUtc {
    param([object]$ExpiresAt)
    if ($null -eq $ExpiresAt -or $ExpiresAt -eq '') { return $null }
    $n = 0L
    if ([int64]::TryParse([string]$ExpiresAt, [ref]$n)) {
        if ($n -gt 1000000000000) {
            $epoch = [datetime]::SpecifyKind([datetime]'1970-01-01', 'Utc')
            return $epoch.AddMilliseconds($n)
        }
        if ($n -gt 1000000000) {
            $epoch = [datetime]::SpecifyKind([datetime]'1970-01-01', 'Utc')
            return $epoch.AddSeconds($n)
        }
    }
    return (ConvertTo-UtcDate $ExpiresAt)
}

function Get-AllowlistedRow {
    param($Row, [double]$AgeDays, [string]$Reason, [bool]$IsCandidate)
    $tags = @()
    if ($null -ne $Row.tags) { $tags = @($Row.tags | ForEach-Object { [string]$_ }) }
    $unread = $false
    if ($null -ne $Row.unread) { $unread = [bool]$Row.unread }
    return [pscustomobject]@{
        id                 = [string]$Row.id
        title              = [string]$Row.title
        status             = [string]$Row.status
        connection_status  = [string]$Row.connection_status
        status_bucket      = [string]$Row.status_bucket
        worker_status      = [string]$Row.worker_status
        created_at         = Format-UtcStamp $Row.created_at
        last_event_at      = Format-UtcStamp $Row.last_event_at
        unread             = $unread
        tags               = $tags
        ageDays            = [math]::Round($AgeDays, 3)
        reason             = $Reason
        candidate          = $IsCandidate
    }
}

function Get-HttpErrorInfo {
    param($ErrorRecord)
    $status = 0
    $body = ''
    if ($null -eq $ErrorRecord) {
        return @{ StatusCode = 0; Body = '' }
    }
    if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
        $body = [string]$ErrorRecord.ErrorDetails.Message
    }
    $ex = $ErrorRecord.Exception
    $resp = $null
    if ($ex) { $resp = $ex.Response }
    if ($resp) {
        try { $status = [int]$resp.StatusCode } catch { }
        if (-not $body) {
            try {
                $stream = $resp.GetResponseStream()
                if ($stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                    $reader.Close()
                }
            } catch { }
        }
    }
    if ($status -eq 0 -and $ex -and $ex.Message -match '(\d{3})') {
        $status = [int]$Matches[1]
    }
    if (-not $body -and $ex) { $body = [string]$ex.Message }
    return @{ StatusCode = $status; Body = $body }
}

function Test-AlreadyGone {
    param($Info)
    if ($Info.StatusCode -eq 404) { return $true }
    if ($Info.Body -and $Info.Body -match 'not_found_error') { return $true }
    return $false
}

function Invoke-ClaudeApi {
    param(
        [string]$Method,
        [string]$Uri,
        [hashtable]$Headers,
        [string]$Body
    )
    if ($HttpShim) {
        return & $HttpShim -Method $Method -Uri $Uri -Headers $Headers -Body $Body
    }
    $params = @{
        Method  = $Method
        Uri     = $Uri
        Headers = $Headers
    }
    if ($null -ne $Body -and $Body -ne '') {
        $params.Body = $Body
        $params.ContentType = 'application/json'
    }
    return Invoke-RestMethod @params
}

function Get-LiveBridgeSuffixes {
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $items = @()
    if ($LiveBridgesJson) {
        if (-not (Test-Path -LiteralPath $LiveBridgesJson)) {
            throw "LiveBridgesJson not found: $LiveBridgesJson"
        }
        $raw = Get-Content -LiteralPath $LiveBridgesJson -Raw -Encoding UTF8 | ConvertFrom-Json
        $items = @()
        if ($null -eq $raw) {
            $items = @()
        } elseif ($raw -isnot [System.Array]) {
            $names = @($raw.PSObject.Properties.Name)
            if ($names -contains 'bridges') {
                $items = @($raw.bridges)
            } else {
                $items = @($raw)
            }
        } else {
            $items = @($raw)
        }
        foreach ($item in $items) {
            if ($null -eq $item) { continue }
            $bridgeId = [string]$item.bridgeSessionId
            if (-not $bridgeId) { $bridgeId = [string]$item }
            $suffix = ConvertTo-SessionSuffix $bridgeId
            if ($suffix) { [void]$set.Add($suffix) }
        }
        # Unary comma: an empty HashSet enumerates to nothing and becomes $null.
        return ,$set
    }

    if (-not (Test-Path -LiteralPath $SessionsDir)) {
        return ,$set
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $SessionsDir -Filter '*.json' -ErrorAction SilentlyContinue)) {
        $session = $null
        try {
            $session = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch {
            continue
        }
        $bridgeId = [string]$session.bridgeSessionId
        if (-not $bridgeId) { continue }
        $pidVal = 0
        if ($session.pid) {
            [void][int]::TryParse([string]$session.pid, [ref]$pidVal)
        }
        if ($pidVal -le 0) {
            [void][int]::TryParse($file.BaseName, [ref]$pidVal)
        }
        if ($pidVal -le 0) { continue }
        $proc = Get-Process -Id $pidVal -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        $name = [string]$proc.ProcessName
        if (-not ($name -like 'claude*' -or $name -like 'node*')) { continue }
        $suffix = ConvertTo-SessionSuffix $bridgeId
        if ($suffix) { [void]$set.Add($suffix) }
    }
    return ,$set
}

function Write-ClassificationTable {
    param($CandidateRows, $SkippedRows)
    Write-Host ('=== CANDIDATES ({0}) ===' -f @($CandidateRows).Count)
    foreach ($row in @($CandidateRows)) {
        $age = Format-AgeDays $row.ageDays
        Write-Host ('{0}  {1,-12} {2,-13} {3,-8} age={4}d  {5}' -f `
            $row.id, $row.connection_status, $row.status_bucket, $row.worker_status, $age, $row.title)
    }
    Write-Host ''
    Write-Host ('=== SKIPPED ({0}) ===' -f @($SkippedRows).Count)
    foreach ($row in @($SkippedRows)) {
        $age = Format-AgeDays $row.ageDays
        Write-Host ('{0}  {1,-12} {2,-13} {3,-8} age={4}d  <- {5}' -f `
            $row.id, $row.connection_status, $row.status_bucket, $row.worker_status, $age, $row.reason)
    }
}

function Write-ReportFile {
    param(
        [string]$Path,
        [int]$Code
    )
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    # Allow-list only. Never dump credentials, headers, or a raw API payload.
    $payload = [pscustomobject]@{
        generatedAt          = [datetime]::UtcNow.ToString('o')
        now                  = $nowUtc.ToString('o')
        olderThanDays        = $OlderThanDays
        action               = $Action
        execute              = [bool]$Execute
        maxPerRun            = $MaxPerRun
        skipUnread           = [bool]$SkipUnread
        requireDisconnected  = [bool]$RequireDisconnected
        inputJson            = [bool]$InputJson
        listCount            = $listCount
        candidateCount       = @($candidates).Count
        skipCount            = @($skipped).Count
        truncated            = [bool]$truncated
        liveBridgeCount      = $liveBridgeCount
        candidates           = @($candidates)
        skipped              = @($skipped)
        mutations            = @($mutations)
        exitCode             = $Code
    }
    $json = $payload | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function New-PassThruResult {
    param([int]$Code, [string]$Path)
    return [pscustomobject]@{
        ExitCode       = $Code
        ReportPath     = $Path
        CandidateCount = @($candidates).Count
        SkipCount      = @($skipped).Count
        CandidateIds   = @($candidates | ForEach-Object { $_.id })
        SkipIds        = @($skipped | ForEach-Object { $_.id })
        Truncated      = [bool]$truncated
        Mutations      = @($mutations)
    }
}

function Complete-Run {
    param([int]$Code)
    if ($reportFile) {
        try { Write-ReportFile -Path $reportFile -Code $Code } catch {
            Write-Host ('warning: failed to write report: ' + $_.Exception.Message)
        }
    }
    $result = New-PassThruResult -Code $Code -Path $reportFile
    if ($PassThru) {
        return $result
    }
    exit $Code
}

# --- clock ------------------------------------------------------------------

if ($Now) {
    $parsedNow = ConvertTo-UtcDate $Now
    if ($null -eq $parsedNow) {
        Write-Host "Could not parse -Now '$Now' as a timestamp."
        $exitCode = 1
        if ($PassThru) { return (New-PassThruResult -Code $exitCode -Path $null) }
        exit $exitCode
    }
    $nowUtc = $parsedNow
}

$stamp = [datetime]::UtcNow.ToString('yyyy-MM-dd-HHmmss')
if (-not (Test-Path -LiteralPath $ReportPath)) {
    New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
}
$reportFile = Join-Path $ReportPath ($stamp + '.json')

# --- 1. credentials (skipped when classifying a saved list) -----------------

if (-not $InputJson) {
    if (-not (Test-Path -LiteralPath $CredentialsPath)) {
        Write-Host 'CLI credentials missing - run/refresh Claude Code on this machine.'
        $exitCode = 2
        return (Complete-Run $exitCode)
    }
    $credObj = $null
    try {
        $credObj = Get-Content -LiteralPath $CredentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Host 'CLI credentials unreadable - run/refresh Claude Code on this machine.'
        $exitCode = 2
        return (Complete-Run $exitCode)
    }
    $oauth = $credObj.claudeAiOauth
    if ($null -eq $oauth -or [string]::IsNullOrEmpty([string]$oauth.accessToken)) {
        Write-Host 'CLI credentials missing - run/refresh Claude Code on this machine.'
        $exitCode = 2
        return (Complete-Run $exitCode)
    }
    $expiresUtc = Get-ExpiresAtUtc $oauth.expiresAt
    if ($null -eq $expiresUtc -or $expiresUtc -lt $nowUtc.AddSeconds(60)) {
        Write-Host 'CLI credentials stale - run/refresh Claude Code on this machine.'
        $exitCode = 2
        return (Complete-Run $exitCode)
    }
    # Held in memory only. Never written, never printed, never passed on a command line.
    $accessToken = [string]$oauth.accessToken
}

# --- 2. list ----------------------------------------------------------------

$listData = @()
if ($InputJson) {
    if (-not (Test-Path -LiteralPath $InputJson)) {
        Write-Host "InputJson not found: $InputJson"
        $exitCode = 1
        return (Complete-Run $exitCode)
    }
    $listBody = Get-Content -LiteralPath $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($listBody.PSObject.Properties.Name -contains 'data') {
        $listData = @($listBody.data)
    } else {
        $listData = @($listBody)
    }
} else {
    $headers = @{
        'Authorization'    = "Bearer $accessToken"
        'anthropic-version' = $anthropicVersion
        'Accept'           = 'application/json'
    }
    $listUri = $apiBase + '/v1/code/sessions?statuses=active&statuses=paused&limit=' + $listLimit
    try {
        $listBody = Invoke-ClaudeApi -Method 'GET' -Uri $listUri -Headers $headers -Body $null
    } catch {
        $info = Get-HttpErrorInfo $_
        Write-Host ('GET /v1/code/sessions failed (status {0}).' -f $info.StatusCode)
        $exitCode = 1
        return (Complete-Run $exitCode)
    }
    if ($null -eq $listBody) {
        $listData = @()
    } elseif ($listBody.PSObject.Properties.Name -contains 'data') {
        $listData = @($listBody.data)
    } else {
        $listData = @($listBody)
    }
}

$listCount = @($listData).Count
if ($listCount -ge $listLimit) {
    Write-Host ("WARNING: session list is truncated (data.Count=$listCount >= limit=$listLimit). Refusing to sweep a partial view.")
    $truncated = $true
    $exitCode = 3
    return (Complete-Run $exitCode)
}

# --- 3. local exclusions (C6) -----------------------------------------------

$liveSuffixes = Get-LiveBridgeSuffixes
$liveBridgeCount = $liveSuffixes.Count

# --- 4. classify ------------------------------------------------------------

$candidateRows = New-Object System.Collections.ArrayList
$skippedRows = New-Object System.Collections.ArrayList

foreach ($row in $listData) {
    if ($null -eq $row) { continue }
    $id = [string]$row.id
    $status = [string]$row.status
    $conn = [string]$row.connection_status
    $bucket = [string]$row.status_bucket
    $worker = [string]$row.worker_status
    $lastEvent = ConvertTo-UtcDate $row.last_event_at
    $ageDays = 0.0
    if ($null -ne $lastEvent) {
        $ageDays = ($nowUtc - $lastEvent).TotalDays
    }
    $reasons = New-Object System.Collections.ArrayList

    # C1: only sidebar-active rows.
    if ($status -ine 'active') {
        [void]$reasons.Add("status=$status")
    }
    # C2: last_event_at older than -OlderThanDays.
    if ($null -eq $lastEvent) {
        [void]$reasons.Add('no last_event_at')
    } elseif ($ageDays -le $OlderThanDays) {
        [void]$reasons.Add('fresh')
    }
    # C3: narrowing only; never adds candidates.
    if ($RequireDisconnected -and $conn -ine 'disconnected') {
        [void]$reasons.Add('connected')
    }
    # C4
    if ($bucket -ieq 'working' -or $bucket -ieq 'blocked') {
        [void]$reasons.Add("bucket=$bucket")
    }
    # C5
    if ($worker -ieq 'running' -or $worker -ieq 'requires_action') {
        [void]$reasons.Add("worker=$worker")
    }
    # C6: live local bridge target.
    $suffix = ConvertTo-SessionSuffix $id
    if ($suffix -and $liveSuffixes.Contains($suffix)) {
        [void]$reasons.Add('LOCAL-LIVE')
    }
    # Optional unread skip (default off).
    $unread = $false
    if ($null -ne $row.unread) { $unread = [bool]$row.unread }
    if ($SkipUnread -and $unread) {
        [void]$reasons.Add('unread')
    }

    $isCandidate = ($reasons.Count -eq 0)
    $reasonText = ($reasons -join ', ')
    $entry = Get-AllowlistedRow -Row $row -AgeDays $ageDays -Reason $reasonText -IsCandidate $isCandidate
    if ($isCandidate) {
        [void]$candidateRows.Add($entry)
    } else {
        [void]$skippedRows.Add($entry)
    }
}

$candidates = @($candidateRows | Sort-Object last_event_at -Descending)
$skipped = @($skippedRows | Sort-Object last_event_at -Descending)

Write-ClassificationTable -CandidateRows $candidates -SkippedRows $skipped
Write-Host ''
Write-Host ('candidates={0} skipped={1} liveBridges={2} execute={3} action={4} requireDisconnected={5}' -f `
    $candidates.Count, $skipped.Count, $liveBridgeCount, [bool]$Execute, $Action, $RequireDisconnected)

# --- 6. act (only under -Execute) -------------------------------------------

if ($Execute) {
    if ($candidates.Count -gt $MaxPerRun) {
        Write-Host ("Refusing to $Action $($candidates.Count) candidates; -MaxPerRun is $MaxPerRun. The cap refuses, it does not truncate.")
        $exitCode = 4
        return (Complete-Run $exitCode)
    }

    $headers = @{
        'Authorization'     = "Bearer $accessToken"
        'anthropic-version' = $anthropicVersion
        'Accept'            = 'application/json'
    }
    $failCount = 0
    foreach ($row in $candidates) {
        $id = $row.id
        $uri = $null
        $method = $null
        if ($Action -eq 'Delete') {
            $method = 'DELETE'
            $uri = $apiBase + '/v1/code/sessions/' + $id
        } else {
            $method = 'POST'
            $uri = $apiBase + '/v1/code/sessions/' + $id + '/archive'
        }
        $ok = $false
        $alreadyGone = $false
        $statusCode = 0
        $errText = ''
        try {
            $null = Invoke-ClaudeApi -Method $method -Uri $uri -Headers $headers -Body $null
            $ok = $true
            $statusCode = 200
        } catch {
            $info = Get-HttpErrorInfo $_
            $statusCode = $info.StatusCode
            $errText = $info.Body
            if (Test-AlreadyGone $info) {
                $ok = $true
                $alreadyGone = $true
            }
        }
        $mutations += [pscustomobject]@{
            id          = $id
            action      = $Action
            method      = $method
            ok          = $ok
            alreadyGone = $alreadyGone
            statusCode  = $statusCode
        }
        if ($ok) {
            if ($alreadyGone) {
                Write-Host ("$Action $id already gone (404 not_found_error) - treating as success")
            } else {
                Write-Host ("$Action $id ok")
            }
        } else {
            $failCount++
            $snippet = $errText
            if ($snippet.Length -gt 180) { $snippet = $snippet.Substring(0, 180) }
            Write-Host ("$Action $id FAILED status=$statusCode $snippet")
        }
    }
    if ($failCount -gt 0) {
        Write-Host ("$failCount mutation(s) failed.")
        $exitCode = 5
    }
}

return (Complete-Run $exitCode)
