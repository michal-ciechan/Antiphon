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
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060001Z","exit":0,"bytes":1,"mode":"plain"}' -Encoding ASCII
        Assert-True (Test-SnapshotStatusFresh -Path $path -NowUtc $now -MaxAgeHours 26) '25h59m success is fresh' 'stale'
        Set-Content -LiteralPath $path -Value '{"utc":"20261109T060000Z","exit":0,"bytes":1,"mode":"plain"}' -Encoding ASCII
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
    $readmePath = Join-Path $here 'windmill\README.md'
    $readmeText = ''
    if (Test-Path -LiteralPath $readmePath) { $readmeText = [System.IO.File]::ReadAllText($readmePath) }
    Assert-True ($readmeText.Contains('Write SOURCE-SHA')) 'readme says to write SOURCE-SHA' 'missing'
    Assert-True ($readmeText.Contains('Delete SOURCE-SHA.txt')) 'readme says to delete SOURCE-SHA.txt' 'missing'
    Assert-True ($readmeText.Contains('do not write SOURCE-SHA.txt')) 'readme says to stop writing SOURCE-SHA.txt' 'missing'
    Assert-True ($text.Contains("Join-Path `$PSScriptRoot 'SOURCE-SHA'")) 'resolver reads SOURCE-SHA' 'missing'
    Assert-True ($text.IndexOf('SOURCE-SHA.txt') -lt 0) 'resolver does not name SOURCE-SHA.txt' 'named'
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
        $staleTxt = Join-Path $root 'SOURCE-SHA.txt'
        Set-Content -LiteralPath $staleTxt -Value 'd6f4584376cd3fa7fca1b86fb9ded06eb9b09838' -Encoding ASCII
        Set-Content -LiteralPath $pin -Value $lateHash -Encoding ASCII
        $withStale = Resolve-PlainSnapshotScript -Candidates @($late) -PinFile $pin
        Assert-True ([string]$withStale -eq $late) 'stale SOURCE-SHA.txt does not replace SOURCE-SHA' ([string]$withStale)
        Remove-Item -LiteralPath $pin -Force
        $txtOnly = Resolve-PlainSnapshotScript -Candidates @($late) -PinFile $pin
        Assert-True ([string]$txtOnly -eq '') 'a pin in SOURCE-SHA.txt is not read' ([string]$txtOnly)
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


# Backup retarget S2 (plan 2026-11-18-postgres-backup-retarget-plan.md): the
# one-word snapshot-target file, Set-SnapshotTarget (W-3 flip, W-5 rollback)
# and the src= status line. The job runs with the child, the script resolver
# and the Telegram sender replaced by recorders.
function New-InvokeRoot {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1831-target-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $here 'windmill\invoke-postgres-snapshot.ps1') -Destination (Join-Path $root 'invoke-postgres-snapshot.ps1')
    Copy-Item -LiteralPath (Join-Path $here 'windmill\job-status.ps1') -Destination (Join-Path $root 'job-status.ps1')
    New-Item -ItemType Directory -Path (Join-Path $root 'job-status') -Force | Out-Null
    return $root
}

function Write-TargetFileBytes {
    param([string]$Root, [string]$Text)
    [System.IO.File]::WriteAllBytes((Join-Path $Root 'snapshot-target'), [System.Text.Encoding]::ASCII.GetBytes($Text))
}

function Get-TargetFileHex {
    param([string]$Root)
    $path = Join-Path $Root 'snapshot-target'
    if (-not (Test-Path -LiteralPath $path)) { return 'ABSENT' }
    return ([System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($path)))
}

function Split-TargetOutput {
    param([object[]]$Items)
    $info = New-Object System.Collections.Generic.List[string]
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Items)) {
        if ($item -is [System.Management.Automation.InformationRecord]) { [void]$info.Add([string]$item.MessageData) }
        else { [void]$values.Add([string]$item) }
    }
    return @{ Info = ($info -join "`n"); Values = $values.ToArray() }
}

function Test-TargetFileValues {
    $root = New-InvokeRoot
    try {
        . (Join-Path $root 'invoke-postgres-snapshot.ps1')
        if (-not (Get-Command Get-SnapshotTarget -ErrorAction SilentlyContinue)) {
            Write-Fail 'TargetFileValues reader exists' 'Get-SnapshotTarget missing'
            return
        }
        $path = Join-Path $root 'snapshot-target'
        Assert-True (-not (Test-Path -LiteralPath $path)) 'TargetFileValues file starts absent' $path
        Assert-True ((Get-SnapshotTarget -Dir $root) -eq '') 'TargetFileValues missing file is invalid' 'accepted'
        foreach ($bad in @(@{ Name = 'empty'; Text = '' }, @{ Name = 'cased with space'; Text = 'Server2 ' }, @{ Name = 'unknown'; Text = 'server3' },
                @{ Name = 'two lines'; Text = "server2`r`ndesktop`r`n" }, @{ Name = 'blank'; Text = "  `r`n" }, @{ Name = 'two newlines'; Text = "desktop`n`n" })) {
            Write-TargetFileBytes $root ([string]$bad.Text)
            $got = Get-SnapshotTarget -Dir $root
            Assert-True ($got -eq '') ('TargetFileValues ' + [string]$bad.Name + ' is invalid') ("got=$got")
        }
        foreach ($good in @(@{ Text = 'desktop'; Want = 'desktop' }, @{ Text = "desktop`r`n"; Want = 'desktop' }, @{ Text = 'server2'; Want = 'server2' }, @{ Text = "server2`r`n"; Want = 'server2' }, @{ Text = "server2`n"; Want = 'server2' })) {
            Write-TargetFileBytes $root ([string]$good.Text)
            $got = Get-SnapshotTarget -Dir $root
            Assert-True ($got -ceq [string]$good.Want) ('TargetFileValues accepts ' + ([string]$good.Text).Replace("`r", '\r').Replace("`n", '\n')) ("got=$got")
        }
        Write-TargetFileBytes $root 'server3'
        $script:TargetChildCalls = New-Object System.Collections.Generic.List[string]
        $script:TargetSent = New-Object System.Collections.Generic.List[string]
        function Invoke-PlainSnapshotChild { param([string]$Snapshot, [string[]]$ArgumentList) [void]$script:TargetChildCalls.Add(($ArgumentList -join ' ')); return @{ Exit = 0; Out = ''; Err = '' } }
        function Send-JobStatusLine { param([string]$Line, [string]$ChatId = '', [string]$SenderRoot = '') [void]$script:TargetSent.Add($Line); return 0 }
        function Resolve-PlainSnapshotScript { param([string[]]$Candidates, [string]$PinFile) return (Join-Path $root 'postgres-snapshot.ps1') }
        $jobRun = Invoke-PlainSnapshotJob -RunnerDir $root -RecordDir (Join-Path $root 'job-status') -StatusPath (Join-Path $root 'snapshot-status.json')
        $text = @($jobRun.Lines) -join "`n"
        Assert-True ($text -match 'reason=target-invalid') 'TargetFileValues wrapper reports target-invalid' $text
        Assert-True ([int]$jobRun.Exit -eq 9) 'TargetFileValues wrapper exits 9' ("exit=$($jobRun.Exit)")
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-TargetInvalidSendsFail {
    $root = New-InvokeRoot
    try {
        . (Join-Path $root 'invoke-postgres-snapshot.ps1')
        if (-not (Get-Command Invoke-PlainSnapshotJob -ErrorAction SilentlyContinue)) {
            Write-Fail 'TargetInvalidSendsFail job exists' 'Invoke-PlainSnapshotJob missing'
            return
        }
        foreach ($variant in @(@{ Name = 'missing'; Text = $null }, @{ Name = 'bad word'; Text = 'Server2' })) {
            Remove-Item -LiteralPath (Join-Path $root 'snapshot-target') -Force -ErrorAction SilentlyContinue
            if ($null -ne $variant.Text) { Write-TargetFileBytes $root ([string]$variant.Text) }
            $script:TargetChildCalls = New-Object System.Collections.Generic.List[string]
            $script:TargetSent = New-Object System.Collections.Generic.List[string]
            function Invoke-PlainSnapshotChild { param([string]$Snapshot, [string[]]$ArgumentList) [void]$script:TargetChildCalls.Add(($ArgumentList -join ' ')); return @{ Exit = 0; Out = ''; Err = '' } }
            function Send-JobStatusLine { param([string]$Line, [string]$ChatId = '', [string]$SenderRoot = '') [void]$script:TargetSent.Add($Line); return 0 }
            function Resolve-PlainSnapshotScript { param([string[]]$Candidates, [string]$PinFile) return (Join-Path $root 'postgres-snapshot.ps1') }
            $jobRun = Invoke-PlainSnapshotJob -RunnerDir $root -RecordDir (Join-Path $root 'job-status') -StatusPath (Join-Path $root 'snapshot-status.json')
            $name = 'TargetInvalidSendsFail ' + [string]$variant.Name
            Assert-True ($script:TargetChildCalls.Count -eq 0) "$name starts no child" ($script:TargetChildCalls -join ' | ')
            Assert-True ($script:TargetSent.Count -eq 1) "$name sends once" ("sent=$($script:TargetSent.Count)")
            $line = ''
            if ($script:TargetSent.Count -ge 1) { $line = [string]$script:TargetSent[0] }
            Assert-True ($line -ceq 'DB backup: FAIL exit 9 target-invalid src=none') "$name line" $line
            Assert-True ([int]$jobRun.Exit -eq 9) "$name exits 9" ("exit=$($jobRun.Exit)")
            $record = Join-Path (Join-Path $root 'job-status') 'db-backup.json'
            $json = ''
            if (Test-Path -LiteralPath $record) { $json = [System.IO.File]::ReadAllText($record) }
            Assert-True ($json -match '"detail":"exit 9 target-invalid src=none"') "$name records the failure" $json
        }
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-OkLineNamesSource {
    if (-not (Import-JobStatus)) { return }
    $cmd = Get-Command New-PlainBackupCloudRecord -ErrorAction SilentlyContinue
    if (-not $cmd -or -not $cmd.Parameters.ContainsKey('Target')) {
        Write-Fail 'OkLineNamesSource -Target' 'New-PlainBackupCloudRecord -Target missing'
        return
    }
    $server2 = New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive 'ok' -Target 'server2'
    Assert-True ((Format-JobStatusLine $server2) -ceq 'DB backup: OK 0.7 GB +cloud src=server2') 'OkLineNamesSource server2 ok line' (Format-JobStatusLine $server2)
    $desktop = New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive 'ok' -Target 'desktop'
    Assert-True ((Format-JobStatusLine $desktop) -ceq 'DB backup: OK 0.7 GB +cloud src=desktop') 'OkLineNamesSource desktop ok line' (Format-JobStatusLine $desktop)
    $warn = New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive 'missing' -Target 'server2'
    Assert-True ((Format-JobStatusLine $warn) -ceq 'DB backup: WARN 0.7 GB no cloud copy src=server2') 'OkLineNamesSource warn line' (Format-JobStatusLine $warn)
    $plain = New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive 'ok'
    Assert-True ((Format-JobStatusLine $plain) -ceq 'DB backup: OK 0.7 GB +cloud') 'OkLineNamesSource without -Target is unchanged' (Format-JobStatusLine $plain)
    $bad = New-PlainBackupCloudRecord -Bytes 750000000 -OneDrive 'ok' -Target 'server 2'
    Assert-True ($null -eq $bad) 'OkLineNamesSource refuses a bad target token' 'accepted'
}

function Test-FailLineCarriesReason {
    $root = New-InvokeRoot
    try {
        . (Join-Path $root 'invoke-postgres-snapshot.ps1')
        if (-not (Get-Command Get-PlainSnapshotFailDetail -ErrorAction SilentlyContinue)) {
            Write-Fail 'FailLineCarriesReason parser exists' 'Get-PlainSnapshotFailDetail missing'
            return
        }
        $cases = @(
            @{ Name = 'dump-failed'; Exit = 4; Out = "PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none`r`nSNAPSHOT fail exit=4 reason=dump-failed`r`n"; Want = 'exit 4 dump-failed src=server2' },
            @{ Name = 'detail after reason'; Exit = 9; Out = 'SNAPSHOT fail exit=9 reason=drill-refused guard=database'; Want = 'exit 9 drill-refused src=server2' },
            @{ Name = 'reason with a space'; Exit = 4; Out = 'SNAPSHOT fail exit=4 reason=dump failed'; Want = 'exit 4 src=server2' },
            @{ Name = 'reason with ='; Exit = 4; Out = 'SNAPSHOT fail exit=4 reason=dump=failed'; Want = 'exit 4 src=server2' },
            @{ Name = 'other exit'; Exit = 4; Out = 'SNAPSHOT fail exit=5 reason=verify-failed'; Want = 'exit 4 src=server2' },
            @{ Name = 'no line'; Exit = 4; Out = 'something else'; Want = 'exit 4 src=server2' }
        )
        foreach ($case in $cases) {
            $got = Get-PlainSnapshotFailDetail -Code ([int]$case.Exit) -Text ([string]$case.Out) -Target 'server2'
            Assert-True ($got -ceq [string]$case.Want) ('FailLineCarriesReason ' + [string]$case.Name) ("got=$got")
        }
        Write-TargetFileBytes $root 'server2'
        $script:TargetChildCalls = New-Object System.Collections.Generic.List[string]
        $script:TargetSent = New-Object System.Collections.Generic.List[string]
        function Invoke-PlainSnapshotChild { param([string]$Snapshot, [string[]]$ArgumentList) [void]$script:TargetChildCalls.Add(($ArgumentList -join ' ')); return @{ Exit = 4; Out = "SNAPSHOT fail exit=4 reason=dump-failed`r`n"; Err = '' } }
        function Send-JobStatusLine { param([string]$Line, [string]$ChatId = '', [string]$SenderRoot = '') [void]$script:TargetSent.Add($Line); return 0 }
        function Resolve-PlainSnapshotScript { param([string[]]$Candidates, [string]$PinFile) return (Join-Path $root 'postgres-snapshot.ps1') }
        $jobRun = Invoke-PlainSnapshotJob -RunnerDir $root -RecordDir (Join-Path $root 'job-status') -StatusPath (Join-Path $root 'snapshot-status.json')
        $line = ''
        if ($script:TargetSent.Count -ge 1) { $line = [string]$script:TargetSent[0] }
        Assert-True ($line -ceq 'DB backup: FAIL exit 4 dump-failed src=server2') 'FailLineCarriesReason wrapper line' $line
        Assert-True ([int]$jobRun.Exit -eq 4) 'FailLineCarriesReason wrapper exits 4' ("exit=$($jobRun.Exit)")
        Assert-True ($script:TargetChildCalls.Count -eq 1 -and $script:TargetChildCalls[0] -match '-Target server2$') 'FailLineCarriesReason child gets -Target server2' ($script:TargetChildCalls -join ' | ')
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-TargetRollbackRestoresDesktop {
    $root = New-InvokeRoot
    try {
        . (Join-Path $root 'invoke-postgres-snapshot.ps1')
        if (-not (Get-Command Set-SnapshotTarget -ErrorAction SilentlyContinue)) {
            Write-Fail 'TargetRollbackRestoresDesktop helper exists' 'Set-SnapshotTarget missing'
            return
        }
        $desktopHex = [System.BitConverter]::ToString([System.Text.Encoding]::ASCII.GetBytes('desktop'))
        $flip = Split-TargetOutput @(Set-SnapshotTarget server2 6>&1)
        Assert-True ($flip.Info -match 'SNAPSHOT TARGET server2') 'TargetRollbackRestoresDesktop flip prints the word' $flip.Info
        Assert-True (@($flip.Values).Count -eq 1 -and $flip.Values[0] -ceq 'server2') 'TargetRollbackRestoresDesktop flip returns server2' (@($flip.Values) -join ',')
        Assert-True ((Get-SnapshotTarget) -ceq 'server2') 'TargetRollbackRestoresDesktop flip lands next to the runner copy' (Get-TargetFileHex $root)
        $back = Split-TargetOutput @(Set-SnapshotTarget desktop 6>&1)
        Assert-True ($back.Info -match 'SNAPSHOT TARGET desktop') 'TargetRollbackRestoresDesktop rollback prints the word' $back.Info
        Assert-True (@($back.Values).Count -eq 1 -and $back.Values[0] -ceq 'desktop') 'TargetRollbackRestoresDesktop rollback returns desktop' (@($back.Values) -join ',')
        Assert-True ((Get-TargetFileHex $root) -ceq $desktopHex) 'TargetRollbackRestoresDesktop file bytes are exactly desktop' (Get-TargetFileHex $root)
        Assert-True ((Get-SnapshotTarget) -ceq 'desktop') 'TargetRollbackRestoresDesktop reader returns desktop' (Get-TargetFileHex $root)
        $again = Split-TargetOutput @(Set-SnapshotTarget desktop 6>&1)
        Assert-True ((Get-TargetFileHex $root) -ceq $desktopHex) 'TargetRollbackRestoresDesktop second rollback keeps the bytes' (Get-TargetFileHex $root)
        Assert-True ($again.Info -ceq $back.Info -and (@($again.Values) -join ',') -ceq (@($back.Values) -join ',')) 'TargetRollbackRestoresDesktop second rollback has the same output' ($again.Info + ' / ' + (@($again.Values) -join ','))
        foreach ($bad in @('Desktop', 'server3', '')) {
            $refused = Split-TargetOutput @(Set-SnapshotTarget $bad 6>&1)
            Assert-True (@($refused.Values).Count -eq 1 -and $refused.Values[0] -ceq '') ("TargetRollbackRestoresDesktop refuses '" + $bad + "'") (@($refused.Values) -join ',')
            Assert-True ($refused.Info -notmatch 'SNAPSHOT TARGET (desktop|server2)') ("TargetRollbackRestoresDesktop '" + $bad + "' prints no word") $refused.Info
            Assert-True ((Get-TargetFileHex $root) -ceq $desktopHex) ("TargetRollbackRestoresDesktop '" + $bad + "' leaves the bytes") (Get-TargetFileHex $root)
        }
        [void](Set-SnapshotTarget server2 6>$null)
        [void](Set-SnapshotTarget desktop 6>$null)
        $script:TargetChildCalls = New-Object System.Collections.Generic.List[string]
        $script:TargetSent = New-Object System.Collections.Generic.List[string]
        function Invoke-PlainSnapshotChild { param([string]$Snapshot, [string[]]$ArgumentList) [void]$script:TargetChildCalls.Add(($ArgumentList -join ' ')); return @{ Exit = 0; Out = 'SNAPSHOT ok exit=0 reason=ok'; Err = '' } }
        function Send-JobStatusLine { param([string]$Line, [string]$ChatId = '', [string]$SenderRoot = '') [void]$script:TargetSent.Add($Line); return 0 }
        function Resolve-PlainSnapshotScript { param([string[]]$Candidates, [string]$PinFile) return (Join-Path $root 'postgres-snapshot.ps1') }
        $statusPath = Join-Path $root 'snapshot-status.json'
        Set-Content -LiteralPath $statusPath -Value '{"utc":"20261118T075000Z","exit":0,"bytes":750000000,"mode":"plain","oneDrive":"ok","target":"desktop","serverVersion":"16.14"}' -Encoding ASCII
        $jobRun = Invoke-PlainSnapshotJob -RunnerDir $root -RecordDir (Join-Path $root 'job-status') -StatusPath $statusPath
        $args0 = ''
        if ($script:TargetChildCalls.Count -ge 1) { $args0 = [string]$script:TargetChildCalls[0] }
        Assert-True ($script:TargetChildCalls.Count -eq 1) 'TargetRollbackRestoresDesktop job runs one child' ("calls=$($script:TargetChildCalls.Count)")
        Assert-True ($args0 -match '(^| )-Target desktop$') 'TargetRollbackRestoresDesktop child gets -Target desktop' $args0
        Assert-True ($args0 -notmatch 'server2') 'TargetRollbackRestoresDesktop child never names server2' $args0
        Assert-True ($args0 -match '-Plain -Execute -OneDriveCopy') 'TargetRollbackRestoresDesktop child keeps the plain arguments' $args0
        $line = ''
        if ($script:TargetSent.Count -ge 1) { $line = [string]$script:TargetSent[0] }
        Assert-True ($line -ceq 'DB backup: OK 0.7 GB +cloud src=desktop') 'TargetRollbackRestoresDesktop ok line names desktop' $line
        Assert-True ([int]$jobRun.Exit -eq 0) 'TargetRollbackRestoresDesktop job exits 0' ("exit=$($jobRun.Exit)")
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$all = @('RenderLines', 'RecordFile', 'StaleWindow', 'ScheduleFiles', 'RegisterPreview', 'CloudSuffix', 'RunnerSourceSha', 'HelperAscii',
    'TargetFileValues', 'TargetInvalidSendsFail', 'OkLineNamesSource', 'FailLineCarriesReason', 'TargetRollbackRestoresDesktop')
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
