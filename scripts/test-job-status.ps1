#requires -Version 5.1
<#
.SYNOPSIS
    Witness for the shared Windmill job-status line and the plain snapshot schedule.

    Pure functions and local JSON only. No Telegram, no Windmill, no Docker.
    ASCII-only.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$helperPath = Join-Path $here 'windmill\job-status.ps1'
$schemaPath = Join-Path $here 'windmill\job-status.schema.json'
$scriptJson = Join-Path $here 'windmill\antiphon-postgres-snapshot.json'
$scheduleJson = Join-Path $here 'windmill\antiphon-postgres-snapshot.schedule.json'
$staleJson = Join-Path $here 'windmill\antiphon-postgres-snapshot-stale.json'
$staleScheduleJson = Join-Path $here 'windmill\antiphon-postgres-snapshot-stale.schedule.json'
$registerPath = Join-Path $here 'register-postgres-snapshot.ps1'
$script:passed = 0
$script:failed = 0
$script:failures = New-Object System.Collections.Generic.List[string]

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures.Add("$Name : $Detail")
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

function Import-JobStatus {
    if (-not (Test-Path -LiteralPath $helperPath)) {
        Write-Fail 'job-status script exists' $helperPath
        return $false
    }
    . $helperPath
    return $true
}

function Test-RenderLines {
    if (-not (Import-JobStatus)) { return }
    if (-not (Get-Command Format-JobStatusLine -ErrorAction SilentlyContinue)) {
        Write-Fail 'renderer exists' 'Format-JobStatusLine missing'
        return
    }
    $ok = New-JobStatusRecord -Job 'db-backup' -Icon 'OK' -Status 'ok' -Detail '0.6 GB'
    Assert-True ($null -ne $ok) 'ok record is accepted' 'null'
    Assert-True ((Format-JobStatusLine $ok) -eq 'DB backup: OK 0.6 GB') 'ok line is short' (Format-JobStatusLine $ok)
    $fail = New-JobStatusRecord -Job 'db-backup' -Icon 'FAIL' -Status 'fail' -Detail 'exit 3'
    Assert-True ((Format-JobStatusLine $fail) -eq 'DB backup: FAIL exit 3') 'fail line is loud and short' (Format-JobStatusLine $fail)
    $stale = New-JobStatusRecord -Job 'db-backup' -Icon 'STALE' -Status 'stale'
    Assert-True ((Format-JobStatusLine $stale) -eq 'DB backup: STALE') 'stale line has no detail' (Format-JobStatusLine $stale)
    Assert-True ($null -eq (New-JobStatusRecord -Job 'db-backup' -Icon 'OK' -Status 'nope')) 'bad status is refused' 'accepted'
    Assert-True ($null -eq (New-JobStatusRecord -Job 'db-backup' -Icon 'OK' -Status 'ok' -Detail ("password=secret"))) 'secret detail is refused' 'accepted'
    $json = ConvertTo-JobStatusJson $ok
    Assert-True ($json -eq '{"job":"db-backup","icon":"OK","status":"ok","detail":"0.6 GB"}') 'record has the three fields plus detail' $json
    $bare = ConvertTo-JobStatusJson $stale
    Assert-True ($bare -eq '{"job":"db-backup","icon":"STALE","status":"stale"}') 'detail is omitted when absent' $bare
    Assert-True ($json -notmatch '(?i)password\s*=|postgres://|Host\s*=') 'record json has no secret' $json
}

function Test-RecordFile {
    if (-not (Import-JobStatus)) { return }
    if (-not (Get-Command Write-JobStatusRecord -ErrorAction SilentlyContinue)) {
        Write-Fail 'record writer exists' 'Write-JobStatusRecord missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('job-status-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $record = New-JobStatusRecord -Job 'db-backup' -Icon 'FAIL' -Status 'fail' -Detail 'exit 3'
        $path = Join-Path $dir 'db-backup.json'
        $wrote = Write-JobStatusRecord -Record $record -Path $path
        Assert-True ($wrote -eq $true) 'record file writes' "wrote=$wrote"
        Assert-True (-not (Test-Path -LiteralPath ($path + '.partial'))) 'record file leaves no partial' $path
        $text = [System.IO.File]::ReadAllText($path).Trim()
        Assert-True ($text -eq '{"job":"db-backup","icon":"FAIL","status":"fail","detail":"exit 3"}') 'record file is one line' $text
        Assert-True ($text -notmatch '[\r\n]') 'record file has no newline inside' $text
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-StaleWindow {
    if (-not (Import-JobStatus)) { return }
    if (-not (Get-Command Test-SnapshotStatusFresh -ErrorAction SilentlyContinue)) {
        Write-Fail 'stale function exists' 'Test-SnapshotStatusFresh missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('job-stale-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $path = Join-Path $dir 'snapshot-status.json'
        $now = [datetime]::SpecifyKind([datetime]::ParseExact('20261110T080000Z', 'yyyyMMddTHHmmssZ', $null), [DateTimeKind]::Utc)
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'missing status is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060001Z","exit":0,"bytes":1}' -Encoding ASCII
        Assert-True (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26) '25h59m success is fresh' 'stale'
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060000Z","exit":0,"bytes":1}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) '26h success is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T075000Z","exit":4,"bytes":0}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'failed status is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"not-a-time","exit":0}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'bad utc is stale' 'fresh'
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-ScheduleFiles {
    foreach ($needed in @($scriptJson, $scheduleJson, $staleJson, $staleScheduleJson, $schemaPath)) {
        if (-not (Test-Path -LiteralPath $needed)) {
            Write-Fail 'schedule file exists' $needed
            return
        }
    }
    $scriptDef = Get-Content -LiteralPath $scriptJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $scheduleDef = Get-Content -LiteralPath $scheduleJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $staleDef = Get-Content -LiteralPath $staleJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $staleSchedule = Get-Content -LiteralPath $staleScheduleJson -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ([string]$scriptDef.path -eq 'u/lndcobra/antiphon_postgres_snapshot') 'script path' $scriptDef.path
    Assert-True ([string]$scriptDef.tag -eq 'desktop') 'script tag is desktop' $scriptDef.tag
    Assert-True ([string]$scriptDef.language -eq 'bash') 'script language is bash' $scriptDef.language
    Assert-True ([string]$scriptDef.content -match 'antiphon-snapshot-runner') 'script calls the runner' $scriptDef.content
    Assert-True ([bool]$scheduleDef.enabled) 'backup schedule is enabled' ([string]$scheduleDef.enabled)
    Assert-True ([string]$scheduleDef.timezone -eq 'Europe/London') 'backup timezone' $scheduleDef.timezone
    Assert-True ([string]$scheduleDef.schedule -eq '0 50 7 * * *') 'backup runs at 07:50' $scheduleDef.schedule
    Assert-True ([string]$scheduleDef.script_path -eq 'u/lndcobra/antiphon_postgres_snapshot') 'backup schedule path' $scheduleDef.script_path
    Assert-True ([string]$scheduleDef.path -eq 'u/lndcobra/antiphon_postgres_snapshot') 'backup schedule id' $scheduleDef.path
    Assert-True ([bool]$staleSchedule.enabled) 'stale schedule is enabled' ([string]$staleSchedule.enabled)
    Assert-True ([string]$staleSchedule.timezone -eq 'Europe/London') 'stale timezone' $staleSchedule.timezone
    Assert-True ([string]$staleSchedule.schedule -eq '0 0 8 * * *') 'stale check runs at 08:00' $staleSchedule.schedule
    Assert-True ([string]$staleSchedule.script_path -eq 'u/lndcobra/antiphon_postgres_snapshot_stale') 'stale script path' $staleSchedule.script_path
    Assert-True ([string]$staleDef.path -eq 'u/lndcobra/antiphon_postgres_snapshot_stale') 'stale script id' $staleDef.path
    Assert-True ([string]$staleDef.tag -eq 'desktop') 'stale tag is desktop' $staleDef.tag
    $blob = [System.IO.File]::ReadAllText($scriptJson) + [System.IO.File]::ReadAllText($staleJson)
    Assert-True ($blob -notmatch '(?i)password\s*=|token|postgres://') 'windmill json has no secret' 'matched'
    $schema = Get-Content -LiteralPath $schemaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $statusEnum = @($schema.properties.status.enum)
    Assert-True ($statusEnum -contains 'ok' -and $statusEnum -contains 'warn' -and $statusEnum -contains 'fail' -and $statusEnum -contains 'stale') 'schema status enum' ($statusEnum -join ',')
    Assert-True (@($schema.required) -contains 'job' -and @($schema.required) -contains 'icon' -and @($schema.required) -contains 'status') 'schema requires the three fields' ((@($schema.required)) -join ',')
}

function Test-RegisterPreview {
    if (-not (Test-Path -LiteralPath $registerPath)) {
        Write-Fail 'register script exists' $registerPath
        return
    }
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $registerPath, '-EnableSchedule')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    Assert-True ($proc.ExitCode -ne 0) 'EnableSchedule without Apply refuses' "exit=$($proc.ExitCode) $out $err"
    $psi2 = [System.Diagnostics.ProcessStartInfo]::new()
    $psi2.FileName = $pwsh
    $psi2.UseShellExecute = $false
    $psi2.RedirectStandardOutput = $true
    $psi2.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $registerPath)) {
        [void]$psi2.ArgumentList.Add($arg)
    }
    $proc2 = [System.Diagnostics.Process]::Start($psi2)
    $out2 = $proc2.StandardOutput.ReadToEnd()
    $err2 = $proc2.StandardError.ReadToEnd()
    $proc2.WaitForExit()
    Assert-True ($proc2.ExitCode -eq 0) 'preview exits 0' "exit=$($proc2.ExitCode) $out2 $err2"
    Assert-True ($out2 -match 'u/lndcobra/antiphon_postgres_snapshot') 'preview names the path' $out2
    Assert-True ($out2 -match '0 50 7 \* \* \*') 'preview names 07:50' $out2
    Assert-True ($out2 -match 'Europe/London') 'preview names the timezone' $out2
    Assert-True ($out2 -match 'enabled=true') 'preview shows the payload enabled' $out2
    Assert-True ($out2 -notmatch '(?i)bearer |token=') 'preview prints no token' $out2
}

function Test-HelperAscii {
    foreach ($needed in @($helperPath, $schemaPath, $registerPath)) {
        if (-not (Test-Path -LiteralPath $needed)) {
            Write-Fail 'ascii file exists' $needed
            return
        }
        $bytes = [System.IO.File]::ReadAllBytes($needed)
        $high = @($bytes | Where-Object { $_ -gt 127 })
        Assert-True ($high.Count -eq 0) ("ascii " + [System.IO.Path]::GetFileName($needed)) "non-ascii=$($high.Count)"
    }
}

$all = @('RenderLines', 'RecordFile', 'StaleWindow', 'ScheduleFiles', 'RegisterPreview', 'HelperAscii')
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Host "unknown case $Case"; exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ("job-status: {0} passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'JOB STATUS TESTS EXIT CODE: 1'
    exit 1
}
Write-Host 'JOB STATUS TESTS EXIT CODE: 0'
exit 0
