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

$script:JobStatusLoaded = $false
if (Test-Path -LiteralPath $helperPath) {
    . $helperPath
    $script:JobStatusLoaded = $true
}

function Import-JobStatus {
    if (-not $script:JobStatusLoaded) {
        Write-Fail 'job-status script exists' $helperPath
        return $false
    }
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
    Assert-True ((Format-SnapshotSize 1073741824) -eq '1.0 GB') 'size formats gibibytes' (Format-SnapshotSize 1073741824)
    Assert-True ((Format-SnapshotSize 1048576) -eq '1.0 MB') 'size formats mebibytes' (Format-SnapshotSize 1048576)
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
        $now = [datetime]::new(2026, 11, 10, 8, 0, 0, [DateTimeKind]::Utc)
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'missing status is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060001Z","exit":0,"bytes":1}' -Encoding ASCII
        Assert-True (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26) '25h59m success is fresh' 'stale'
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060000Z","exit":0,"bytes":1}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) '26h success is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T075000Z","exit":4,"bytes":0}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'failed status is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"not-a-time","exit":0}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'bad utc is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T075000Z","exit":0,"bytes":1,"mode":"age"}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'non-plain status is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T075000Z","exit":0,"bytes":1}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) 'status without mode=plain is stale' 'fresh'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T083000Z","exit":0,"bytes":1,"mode":"plain"}' -Encoding ASCII
        Assert-True (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26) '30m future plain status is fresh' 'stale'
        Set-Content -LiteralPath $path -Value '{"utc":"20261110T100000Z","exit":0,"bytes":1,"mode":"plain"}' -Encoding ASCII
        Assert-True (-not (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26)) '2h future plain status is stale' 'fresh'
        if (-not (Get-Command Test-SnapshotDumpFresh -ErrorAction SilentlyContinue)) {
            Write-Fail 'dump freshness exists' 'Test-SnapshotDumpFresh missing'
            return
        }
        Assert-True (-not (Test-SnapshotDumpFresh -Dir $dir -NowUtc $now -MaxAgeHours 26)) 'directory with no dump is stale' 'fresh'
        $cipher = Join-Path $dir 'antiphon-20261110T075000Z.dump.age'
        Set-Content -LiteralPath $cipher -Value 'cipher' -Encoding ASCII
        (Get-Item -LiteralPath $cipher).LastWriteTimeUtc = $now.AddHours(-1)
        Assert-True (-not (Test-SnapshotDumpFresh -Dir $dir -NowUtc $now -MaxAgeHours 26)) 'ciphertext does not count as a plain success' 'fresh'
        $oldDump = Join-Path $dir 'antiphon-20261108T000000Z.dump'
        Set-Content -LiteralPath $oldDump -Value 'old' -Encoding ASCII
        (Get-Item -LiteralPath $oldDump).LastWriteTimeUtc = $now.AddHours(-30)
        Assert-True (-not (Test-SnapshotDumpFresh -Dir $dir -NowUtc $now -MaxAgeHours 26)) '30h dump is stale' 'fresh'
        $newDump = Join-Path $dir 'antiphon-20261110T075000Z.dump'
        Set-Content -LiteralPath $newDump -Value 'new' -Encoding ASCII
        (Get-Item -LiteralPath $newDump).LastWriteTimeUtc = $now.AddHours(-1)
        Assert-True (Test-SnapshotDumpFresh -Dir $dir -NowUtc $now -MaxAgeHours 26) '1h dump is fresh' 'stale'
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

function Test-CloudSuffix {
    if (-not (Import-JobStatus)) { return }
    $readmePath = Join-Path $here 'windmill\README.md'
    $invokePath = Join-Path $here 'windmill\invoke-postgres-snapshot.ps1'
    $readmeText = ''
    $invokeText = ''
    if (Test-Path -LiteralPath $readmePath) { $readmeText = [System.IO.File]::ReadAllText($readmePath) }
    if (Test-Path -LiteralPath $invokePath) { $invokeText = [System.IO.File]::ReadAllText($invokePath) }
    Assert-True ($readmeText -match 'oneDrive=missing') 'readme names oneDrive=missing' 'missing'
    Assert-True ($readmeText -match '\+cloud') 'readme names the +cloud suffix' 'missing'
    Assert-True ($readmeText -match 'no cloud copy') 'readme names the no-cloud warning' 'missing'
    Assert-True ($readmeText -match 'unencrypted') 'readme says the cloud copy is unencrypted' 'missing'
    if ($invokeText -notmatch '-OneDriveCopy') {
        Write-Fail 'scheduled job names OneDriveCopy' 'flag missing'
        return
    }
    if (-not (Get-Command New-PlainBackupCloudRecord -ErrorAction SilentlyContinue)) {
        Write-Fail 'cloud record exists' 'New-PlainBackupCloudRecord missing'
        return
    }
    $ok = New-PlainBackupCloudRecord -Bytes 530395357 -OneDrive 'ok'
    Assert-True ((Format-JobStatusLine $ok) -eq 'DB backup: OK 0.5 GB +cloud') 'success line has +cloud' (Format-JobStatusLine $ok)
    $missing = New-PlainBackupCloudRecord -Bytes 530395357 -OneDrive 'missing'
    Assert-True ((Format-JobStatusLine $missing) -eq 'DB backup: WARN 0.5 GB no cloud copy') 'missing cloud line warns' (Format-JobStatusLine $missing)
    $failed = New-PlainBackupCloudRecord -Bytes 531543585 -OneDrive 'failed'
    Assert-True ((Format-JobStatusLine $failed) -eq 'DB backup: WARN 0.5 GB no cloud copy') 'failed cloud line warns' (Format-JobStatusLine $failed)
    $localOnly = New-PlainBackupCloudRecord -Bytes 530395357 -OneDrive ''
    Assert-True ((Format-JobStatusLine $localOnly) -eq 'DB backup: OK 0.5 GB') 'empty cloud state stays the local line' (Format-JobStatusLine $localOnly)
    Assert-True ([string]$ok.detail -notmatch '(?i)password\s*=|postgres://|Host\s*=') 'cloud line has no secret' ([string]$ok.detail)
}

function Test-RunnerSourceSha {
    $invoke = Join-Path $here 'windmill\invoke-postgres-snapshot.ps1'
    if (-not (Test-Path -LiteralPath $invoke)) { Write-Fail 'runner invoke script exists' $invoke; return }
    $text = [System.IO.File]::ReadAllText($invoke)
    if ($text.IndexOf('SOURCE-SHA') -lt 0 -or $text.IndexOf("InvocationName -eq '.'") -lt 0) {
        Write-Fail 'runner copy pins SOURCE-SHA' 'pin missing'
        return
    }
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1831-pin-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        Copy-Item -LiteralPath $invoke -Destination (Join-Path $root 'invoke-postgres-snapshot.ps1')
        Copy-Item -LiteralPath (Join-Path $here 'windmill\job-status.ps1') -Destination (Join-Path $root 'job-status.ps1')
        . (Join-Path $root 'invoke-postgres-snapshot.ps1')
        if (-not (Get-Command Resolve-PlainSnapshotScript -ErrorAction SilentlyContinue)) {
            Write-Fail 'runner resolver exists' 'missing'
            return
        }
        $marker = "[switch]`$Plain`r`n`$script:SnapshotPlainDecision = '2026-11-10'`r`n[switch]`$OneDriveCopy`r`n"
        $early = Join-Path $root 'early.ps1'
        $late = Join-Path $root 'postgres-snapshot.ps1'
        Set-Content -LiteralPath $early -Value ($marker + "Write-Output 'EARLY'`r`n") -Encoding ASCII
        Set-Content -LiteralPath $late -Value ($marker + "Write-Output 'RUNNER'`r`n") -Encoding ASCII
        $lateHash = (Get-FileHash -LiteralPath $late -Algorithm SHA256).Hash.ToLowerInvariant()
        $pin = Join-Path $root 'SOURCE-SHA'
        Set-Content -LiteralPath $pin -Value $lateHash -Encoding ASCII
        $chosen = Resolve-PlainSnapshotScript -Candidates @($early, $late) -PinFile $pin
        Assert-True ([string]$chosen -eq $late) 'runner pin skips an earlier copy with a different hash' ([string]$chosen)
        $bogus = 'ab' * 32
        Set-Content -LiteralPath $pin -Value $bogus -Encoding ASCII
        $refused = Resolve-PlainSnapshotScript -Candidates @($early, $late) -PinFile $pin
        Assert-True ([string]$refused -eq '') 'runner pin refuses when no copy matches SOURCE-SHA' ([string]$refused)
        $missing = Resolve-PlainSnapshotScript -Candidates @($late) -PinFile (Join-Path $root 'missing-pin')
        Assert-True ([string]$missing -eq '') 'runner pin refuses a missing SOURCE-SHA' ([string]$missing)
        Set-Content -LiteralPath $pin -Value $lateHash -Encoding ASCII
        $earlySame = Join-Path $root 'same-early.ps1'
        Copy-Item -LiteralPath $late -Destination $earlySame
        $first = Resolve-PlainSnapshotScript -Candidates @($earlySame, $late) -PinFile $pin
        Assert-True ([string]$first -eq $earlySame) 'runner pin keeps an earlier copy when the hash matches' ([string]$first)
        Assert-True ([string]$script:PlainSnapshotSourceSha -eq $lateHash) 'runner pin records the source sha' ([string]$script:PlainSnapshotSourceSha)
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
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

$all = @('RenderLines', 'RecordFile', 'StaleWindow', 'ScheduleFiles', 'RegisterPreview', 'CloudSuffix', 'RunnerSourceSha', 'HelperAscii')
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
