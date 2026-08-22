#requires -Version 5.1
<#
.SYNOPSIS
    Fixture/shim tests T1-T12 for scripts/cleanup-claude-sessions.ps1.

    All HTTP is either skipped (-InputJson) or injected (-HttpShim). This file never
    makes a live HTTPS call and never passes -Execute at the real API.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

$here = $PSScriptRoot
$cleanup = Join-Path $here 'cleanup-claude-sessions.ps1'
$fixture = Join-Path $here (Join-Path 'fixtures' 'claude-sessions-2026-08-22.json')
$bridges = Join-Path $here (Join-Path 'fixtures' 'claude-sessions-live-bridges.json')
$nowPin = '2026-08-22T14:14:00Z'
$leakToken = 'tok_T12_LEAKTEST_9f3a1c7e_NOT_A_REAL_SECRET'

$expectedCandidates = @(
    'cse_01A1bGtpG1reU5iCwWMwkwyj',
    'cse_01LtSACyrcManQGiQPbjRynd',
    'cse_01Ht9GkXxoRJdax9a3w5KTzx',
    'cse_012tkQGM6m8ZJf86yTmguEsb',
    'cse_019hZwrMDNbF142zVeiAMsZ7',
    'cse_01DWKb4PB87hjqG67iKxxYaP',
    'cse_01AYbKg5uH3Mk635MmvFekcq',
    'cse_01CwamtCHtqhpxnLkvYvuCTs',
    'cse_01C5ZCwnjajAL6U18ioBHMoe'
)
$c6Ids = @(
    'cse_01VPMZ8VJbzp4D5EPA4v7Wxq',
    'cse_01U7SPMfVBcuuFmPq8sgQHPW'
)

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-Eq {
    param($Actual, $Expected, [string]$Name)
    if ($Actual -eq $Expected) { Write-Pass $Name }
    else { Write-Fail $Name "expected=$Expected actual=$Actual" }
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

function New-TestDir {
    $dir = Join-Path $env:TEMP ('claude-cleanup-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function New-TestCreds {
    param(
        [string]$Dir,
        [int64]$ExpiresAtMs,
        [string]$Token = 'TEST_TOKEN_PLACEHOLDER_NOT_REAL'
    )
    $path = Join-Path $Dir 'credentials.json'
    $hash = @{
        claudeAiOauth = @{
            accessToken           = $Token
            refreshToken          = 'refresh_unused_not_real'
            expiresAt             = $ExpiresAtMs
            refreshTokenExpiresAt = $ExpiresAtMs + 86400000
            subscriptionType      = 'pro'
        }
    }
    ($hash | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Get-PinnedExpiryMs {
    param([int]$OffsetSeconds)
    $dto = [datetimeoffset]::Parse($nowPin)
    $epoch = [datetime]::SpecifyKind([datetime]'1970-01-01', 'Utc')
    $ms = [int64]($dto.UtcDateTime - $epoch).TotalMilliseconds
    return $ms + ([int64]$OffsetSeconds * 1000)
}

function Read-FixtureData {
    $parsed = Get-Content -LiteralPath $fixture -Raw -Encoding UTF8 | ConvertFrom-Json
    return @($parsed.data)
}

function Save-ListJson {
    param([string]$Path, $Rows)
    $body = [pscustomobject]@{ data = @($Rows); resume_token = 'synthetic-not-a-secret' }
    ($body | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-Row {
    param(
        [string]$Id,
        [string]$Title = 'synthetic',
        [string]$Status = 'active',
        [string]$Connection = 'disconnected',
        [string]$Bucket = 'review_ready',
        [string]$Worker = 'idle',
        [string]$LastEvent = '2026-08-01T00:00:00Z',
        [bool]$Unread = $false
    )
    return [pscustomobject]@{
        id                 = $Id
        title              = $Title
        status             = $Status
        connection_status  = $Connection
        status_bucket      = $Bucket
        worker_status      = $Worker
        created_at         = '2026-07-01T00:00:00Z'
        last_event_at      = $LastEvent
        unread             = $Unread
        tags               = @('remote-control-repl')
    }
}

function Invoke-Cleanup {
    param([hashtable]$Params)
    $Params['PassThru'] = $true
    return & $cleanup @Params
}

function Get-MutatingCalls {
    param($Calls)
    return @($Calls | Where-Object {
        $_.Method -eq 'POST' -or $_.Method -eq 'DELETE' -or $_.Method -eq 'PUT' -or $_.Method -eq 'PATCH'
    })
}

if (-not (Test-Path -LiteralPath $cleanup)) { throw "missing $cleanup" }
if (-not (Test-Path -LiteralPath $fixture)) { throw "missing $fixture" }
if (-not (Test-Path -LiteralPath $bridges)) { throw "missing $bridges" }

# --- T1 ---------------------------------------------------------------------
$t1Dir = New-TestDir
$t1 = Invoke-Cleanup @{
    InputJson         = $fixture
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t1Dir
}
Assert-Eq $t1.ExitCode 0 'T1 exit 0'
Assert-Eq $t1.CandidateCount 9 'T1 candidate count 9'
Assert-Eq $t1.SkipCount 9 'T1 skip count 9'
$t1Missing = @($expectedCandidates | Where-Object { $t1.CandidateIds -notcontains $_ })
Assert-True ($t1Missing.Count -eq 0) 'T1 candidate ids match section 2.2' ($t1Missing -join ',')
$t1Extra = @($t1.CandidateIds | Where-Object { $expectedCandidates -notcontains $_ })
Assert-True ($t1Extra.Count -eq 0) 'T1 no extra candidates' ($t1Extra -join ',')

# --- T2 (C6 load-bearing: RequireDisconnected off, still skipped) -----------
$t2Dir = New-TestDir
$t2 = Invoke-Cleanup @{
    InputJson             = $fixture
    LiveBridgesJson       = $bridges
    Now                   = $nowPin
    ReportPath            = $t2Dir
    RequireDisconnected   = $false
}
Assert-Eq $t2.ExitCode 0 'T2 exit 0'
Assert-Eq $t2.CandidateCount 9 'T2 still 9 candidates with RequireDisconnected false'
$t2StillSkipped = @($c6Ids | Where-Object { $t2.SkipIds -contains $_ })
Assert-True ($t2StillSkipped.Count -eq 2) 'T2 C6 skips cse_01VPMZ8 and cse_01U7SPM' ($t2.SkipIds -join ',')
$t2Report = Get-ChildItem -LiteralPath $t2Dir -Filter '*.json' | Select-Object -First 1
$t2Json = Get-Content -LiteralPath $t2Report.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$t2Reasons = @($t2Json.skipped | Where-Object { $c6Ids -contains $_.id } | ForEach-Object { $_.reason })
$t2Local = @($t2Reasons | Where-Object { $_ -match 'LOCAL-LIVE' })
Assert-True ($t2Local.Count -eq 2) 'T2 skip reason is LOCAL-LIVE' ($t2Reasons -join ' | ')

# --- T3 ---------------------------------------------------------------------
Assert-True ($t1.SkipIds -contains 'cse_0127JHMVHrqYtq7qW23b9osH') 'T3 cse_0127JHMV skipped (fresh disconnected)'
$t1Report = Get-ChildItem -LiteralPath $t1Dir -Filter '*.json' | Select-Object -First 1
$t1Json = Get-Content -LiteralPath $t1Report.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$t3Row = @($t1Json.skipped | Where-Object { $_.id -eq 'cse_0127JHMVHrqYtq7qW23b9osH' }) | Select-Object -First 1
Assert-True ($null -ne $t3Row -and $t3Row.reason -match 'fresh') 'T3 reason is fresh' ([string]$t3Row.reason)

# --- T4 ---------------------------------------------------------------------
$t4Dir = New-TestDir
$t4Path = Join-Path $t4Dir 'input.json'
$t4Rows = @(Read-FixtureData) + (New-Row -Id 'cse_01SYNTHWORKING00000000001' -Title 'synthetic-working' -Bucket 'working')
Save-ListJson -Path $t4Path -Rows $t4Rows
$t4 = Invoke-Cleanup @{
    InputJson       = $t4Path
    LiveBridgesJson = $bridges
    Now             = $nowPin
    ReportPath      = $t4Dir
}
Assert-True ($t4.SkipIds -contains 'cse_01SYNTHWORKING00000000001') 'T4 working bucket skipped even when old+disconnected' ($t4.CandidateIds -join ',')
Assert-True ($t4.CandidateIds -notcontains 'cse_01SYNTHWORKING00000000001') 'T4 working row is not a candidate'

# --- T5 ---------------------------------------------------------------------
$t5Dir = New-TestDir
$t5Path = Join-Path $t5Dir 'input.json'
$t5Rows = @(Read-FixtureData) + (New-Row -Id 'cse_01SYNTHARCHIVED0000000001' -Title 'synthetic-archived' -Status 'archived')
Save-ListJson -Path $t5Path -Rows $t5Rows
$t5 = Invoke-Cleanup @{
    InputJson       = $t5Path
    LiveBridgesJson = $bridges
    Now             = $nowPin
    ReportPath      = $t5Dir
}
Assert-True ($t5.CandidateIds -notcontains 'cse_01SYNTHARCHIVED0000000001') 'T5 archived row is not a candidate'
Assert-True ($t5.SkipIds -contains 'cse_01SYNTHARCHIVED0000000001') 'T5 archived row is skipped'

# --- T6 ---------------------------------------------------------------------
$t6Dir = New-TestDir
$t6Calls = New-Object System.Collections.ArrayList
$t6Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t6Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    return @{ data = @() }
}.GetNewClosure()
$t6 = Invoke-Cleanup @{
    InputJson       = $fixture
    LiveBridgesJson = $bridges
    Now             = $nowPin
    ReportPath      = $t6Dir
    HttpShim        = $t6Shim
}
Assert-Eq $t6.ExitCode 0 'T6 dry-run exit 0'
Assert-Eq $t6Calls.Count 0 'T6 HttpShim records 0 requests (dry run / InputJson)'
Assert-Eq @(Get-MutatingCalls $t6Calls).Count 0 'T6 zero mutating calls'
Assert-Eq $t6.Mutations.Count 0 'T6 PassThru mutations empty'

# --- T7 ---------------------------------------------------------------------
$t7Dir = New-TestDir
$t7Rows = @(Read-FixtureData) +
    (New-Row -Id 'cse_01SYNTHCANDIDATE000000001' -Title 'extra-1') +
    (New-Row -Id 'cse_01SYNTHCANDIDATE000000002' -Title 'extra-2')
$t7List = [pscustomobject]@{ data = $t7Rows }
$t7Calls = New-Object System.Collections.ArrayList
$t7Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t7Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t7List }
    return @{ ok = $true }
}.GetNewClosure()
$t7Creds = New-TestCreds -Dir $t7Dir -ExpiresAtMs (Get-PinnedExpiryMs 86400)
$t7 = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t7Dir
    CredentialsPath   = $t7Creds
    HttpShim          = $t7Shim
    Execute           = $true
    MaxPerRun         = 10
    Action            = 'Archive'
}
Assert-Eq $t7.ExitCode 4 'T7 exit 4 when 11 candidates exceed MaxPerRun 10'
Assert-Eq $t7.CandidateCount 11 'T7 classified 11 candidates'
Assert-Eq @(Get-MutatingCalls $t7Calls).Count 0 'T7 zero mutating calls (cap refuses, does not truncate)'

# --- T8 ---------------------------------------------------------------------
$t8Dir = New-TestDir
$t8List = Get-Content -LiteralPath $fixture -Raw -Encoding UTF8 | ConvertFrom-Json
$t8Calls = New-Object System.Collections.ArrayList
$t8Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t8Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t8List }
    return @{ ok = $true }
}.GetNewClosure()
$t8Creds = New-TestCreds -Dir $t8Dir -ExpiresAtMs (Get-PinnedExpiryMs 86400)
$t8 = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t8Dir
    CredentialsPath   = $t8Creds
    HttpShim          = $t8Shim
    Execute           = $true
    Action            = 'Archive'
}
Assert-Eq $t8.ExitCode 0 'T8 archive exit 0'
$t8Posts = @($t8Calls | Where-Object { $_.Method -eq 'POST' })
$t8Deletes = @($t8Calls | Where-Object { $_.Method -eq 'DELETE' })
Assert-Eq $t8Posts.Count 9 'T8 exactly 9 POST archive calls'
Assert-Eq $t8Deletes.Count 0 'T8 never DELETE when Action=Archive'
$t8BadUri = @($t8Posts | Where-Object { $_.Uri -notmatch '/v1/code/sessions/cse_[^/]+/archive$' })
Assert-True ($t8BadUri.Count -eq 0) 'T8 POST uri is /v1/code/sessions/{id}/archive' ($t8Posts[0].Uri)
$t8IdsFromUri = @($t8Posts | ForEach-Object {
    if ($_.Uri -match '/sessions/(cse_[^/]+)/archive') { $Matches[1] }
})
$t8Missing = @($expectedCandidates | Where-Object { $t8IdsFromUri -notcontains $_ })
Assert-True ($t8Missing.Count -eq 0) 'T8 one POST per candidate' ($t8Missing -join ',')

# Delete path: DELETE /{id}, never archive POST
$t8dDir = New-TestDir
$t8dCalls = New-Object System.Collections.ArrayList
$t8dShim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t8dCalls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t8List }
    return @{ ok = $true }
}.GetNewClosure()
$t8dCreds = New-TestCreds -Dir $t8dDir -ExpiresAtMs (Get-PinnedExpiryMs 86400)
$t8d = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t8dDir
    CredentialsPath   = $t8dCreds
    HttpShim          = $t8dShim
    Execute           = $true
    Action            = 'Delete'
}
$t8dDeletes = @($t8dCalls | Where-Object { $_.Method -eq 'DELETE' })
$t8dPosts = @($t8dCalls | Where-Object { $_.Method -eq 'POST' })
Assert-Eq $t8d.ExitCode 0 'T8 delete path exit 0'
Assert-Eq $t8dDeletes.Count 9 'T8 Delete issues exactly 9 DELETE calls'
Assert-Eq $t8dPosts.Count 0 'T8 Delete never POSTs archive'
$t8dBad = @($t8dDeletes | Where-Object { $_.Uri -notmatch '/v1/code/sessions/cse_[^/]+$' -or $_.Uri -match '/archive' })
Assert-True ($t8dBad.Count -eq 0) 'T8 DELETE uri is /v1/code/sessions/{id}'

# --- T9 ---------------------------------------------------------------------
$t9Dir = New-TestDir
$t9List = Get-Content -LiteralPath $fixture -Raw -Encoding UTF8 | ConvertFrom-Json
$t9Calls = New-Object System.Collections.ArrayList
$t9GoneId = 'cse_01A1bGtpG1reU5iCwWMwkwyj'
$t9Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t9Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t9List }
    if ($Uri -like ('*' + $t9GoneId + '*')) {
        throw (New-Object System.Exception ('404 {"type":"not_found_error","resource_type":"session"}'))
    }
    return @{ ok = $true }
}.GetNewClosure()
$t9Creds = New-TestCreds -Dir $t9Dir -ExpiresAtMs (Get-PinnedExpiryMs 86400)
$t9 = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t9Dir
    CredentialsPath   = $t9Creds
    HttpShim          = $t9Shim
    Execute           = $true
    Action            = 'Archive'
}
Assert-Eq $t9.ExitCode 0 'T9 404 not_found_error treated as success (exit 0)'
Assert-Eq @(Get-MutatingCalls $t9Calls).Count 9 'T9 still attempted every candidate after a 404'
$t9GoneMut = @($t9.Mutations | Where-Object { $_.id -eq $t9GoneId }) | Select-Object -First 1
Assert-True ($null -ne $t9GoneMut -and [bool]$t9GoneMut.ok -and [bool]$t9GoneMut.alreadyGone) 'T9 404 marked alreadyGone'

# Exit 5: a non-404 failure does not abort the rest.
$t9eDir = New-TestDir
$t9eCalls = New-Object System.Collections.ArrayList
$t9eFailId = 'cse_01LtSACyrcManQGiQPbjRynd'
$t9eShim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t9eCalls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t9List }
    if ($Uri -like ('*' + $t9eFailId + '*')) {
        throw (New-Object System.Exception ('500 {"type":"api_error"}'))
    }
    return @{ ok = $true }
}.GetNewClosure()
$t9eCreds = New-TestCreds -Dir $t9eDir -ExpiresAtMs (Get-PinnedExpiryMs 86400)
$t9e = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t9eDir
    CredentialsPath   = $t9eCreds
    HttpShim          = $t9eShim
    Execute           = $true
    Action            = 'Archive'
}
Assert-Eq $t9e.ExitCode 5 'T9b non-404 failure yields exit 5'
Assert-Eq @(Get-MutatingCalls $t9eCalls).Count 9 'T9b remaining candidates still attempted after a 500'

# --- T10 --------------------------------------------------------------------
$t10Dir = New-TestDir
$t10Calls = New-Object System.Collections.ArrayList
$t10Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t10Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    return @{ data = @() }
}.GetNewClosure()
$t10Creds = New-TestCreds -Dir $t10Dir -ExpiresAtMs (Get-PinnedExpiryMs -300)
$t10 = Invoke-Cleanup @{
    Now             = $nowPin
    ReportPath      = $t10Dir
    CredentialsPath = $t10Creds
    HttpShim        = $t10Shim
}
Assert-Eq $t10.ExitCode 2 'T10 expired expiresAt => exit 2'
Assert-Eq $t10Calls.Count 0 'T10 zero HTTP calls'

# --- T11 --------------------------------------------------------------------
$t11Dir = New-TestDir
$t11Path = Join-Path $t11Dir 'input.json'
$t11Rows = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt 100; $i++) {
    $n = $i.ToString('000')
    [void]$t11Rows.Add((New-Row -Id ('cse_01TRUNCATED00000000000' + $n) -Title ('truncated-' + $n)))
}
Save-ListJson -Path $t11Path -Rows $t11Rows
$t11Calls = New-Object System.Collections.ArrayList
$t11Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t11Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    return @{ data = @() }
}.GetNewClosure()
$t11 = Invoke-Cleanup @{
    InputJson       = $t11Path
    LiveBridgesJson = $bridges
    Now             = $nowPin
    ReportPath      = $t11Dir
    HttpShim        = $t11Shim
}
Assert-Eq $t11.ExitCode 3 'T11 data.Count >= limit => exit 3'
Assert-Eq $t11Calls.Count 0 'T11 zero HTTP calls (InputJson truncated before mutate)'
Assert-True ([bool]$t11.Truncated) 'T11 truncated flag set'
Assert-Eq @(Get-MutatingCalls $t11Calls).Count 0 'T11 zero mutating calls'

# --- T12 --------------------------------------------------------------------
$t12Dir = New-TestDir
$t12List = Get-Content -LiteralPath $fixture -Raw -Encoding UTF8 | ConvertFrom-Json
$t12Calls = New-Object System.Collections.ArrayList
$t12Shim = {
    param($Method, $Uri, $Headers, $Body)
    [void]$t12Calls.Add(@{ Method = $Method; Uri = [string]$Uri })
    if ($Method -eq 'GET') { return $t12List }
    return @{ ok = $true }
}.GetNewClosure()
$t12Creds = New-TestCreds -Dir $t12Dir -ExpiresAtMs (Get-PinnedExpiryMs 86400) -Token $leakToken
$t12 = Invoke-Cleanup @{
    LiveBridgesJson   = $bridges
    Now               = $nowPin
    ReportPath        = $t12Dir
    CredentialsPath   = $t12Creds
    HttpShim          = $t12Shim
}
Assert-Eq $t12.ExitCode 0 'T12 report-only with live token in memory exits 0'
$t12Files = @(Get-ChildItem -LiteralPath $t12Dir -Filter '*.json' | Where-Object { $_.Name -ne 'credentials.json' })
$t12Leaks = @()
foreach ($f in $t12Files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    if ($text -and $text.Contains($leakToken)) { $t12Leaks += $f.Name }
}
Assert-True ($t12Leaks.Count -eq 0) 'T12 report JSON contains no substring of the access token' ($t12Leaks -join ',')
# Also the credentials file itself is expected to contain it; that file is the input, not the report.
Assert-True ($t12Files.Count -ge 1) 'T12 wrote a report file'

Write-Host ''
Write-Host ('T1-T12: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'CLEANUP SESSION TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'CLEANUP SESSION TESTS EXIT CODE: 0  (PASS)'
exit 0
