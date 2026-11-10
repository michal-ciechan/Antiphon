#requires -Version 5.1
<#
.SYNOPSIS
    One-line job status record for a later 08:00 digest.

    Fields are job, icon, and status (ok, warn, fail, stale), plus an optional
    detail. Format-JobStatusLine renders one short line such as
    "DB backup: OK 0.6 GB" or "DB backup: FAIL exit 3".
    -Send posts that line once, through scripts/tg-send.cs on the messaging
    bus. Tests dot-source this file and do not pass -Send.
    ASCII-only.
#>
[CmdletBinding()]
param(
    [string]$Job = '',
    [string]$Icon = '',
    [string]$Status = '',
    [string]$Detail = '',
    [string]$RecordPath = '',
    [switch]$Send,
    [string]$ChatId = '8738110514',
    [string]$SenderRoot = 'C:\src\ClaudeBot',
    [switch]$StaleCheck,
    [string]$StatusFile = '',
    [string]$DumpDir = '',
    [int]$MaxAgeHours = 26
)

$script:JobStatusAllowed = @('ok', 'warn', 'fail', 'stale')
$script:JobStatusLabels = @{ 'db-backup' = 'DB backup' }

function New-JobStatusRecord {
    param([string]$Job, [string]$Icon, [string]$Status, [string]$Detail)
    if ($script:JobStatusAllowed -notcontains $Status) { return $null }
    if ($Job -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return $null }
    if ($Icon -notmatch '^[A-Za-z0-9]{1,8}$') { return $null }
    if ($Detail -match '[\r\n"\\]' -or $Detail.Length -gt 120) { return $null }
    if ($Detail -match '(?i)password\s*=|postgres://|Host\s*=') { return $null }
    $record = [ordered]@{ job = $Job; icon = $Icon; status = $Status }
    if (-not [string]::IsNullOrWhiteSpace($Detail)) { $record.detail = $Detail }
    return [pscustomobject]$record
}

function Format-JobStatusLine {
    param($Record)
    $label = [string]$Record.job
    if ($script:JobStatusLabels.ContainsKey($label)) { $label = [string]$script:JobStatusLabels[$label] }
    $line = $label + ': ' + [string]$Record.icon
    $names = @($Record.PSObject.Properties.Name)
    if ($names -contains 'detail' -and -not [string]::IsNullOrWhiteSpace([string]$Record.detail)) {
        $line = $line + ' ' + [string]$Record.detail
    }
    return $line
}

function ConvertTo-JobStatusJson {
    param($Record)
    $json = '{"job":"' + $Record.job + '","icon":"' + $Record.icon + '","status":"' + $Record.status + '"'
    $names = @($Record.PSObject.Properties.Name)
    if ($names -contains 'detail') { $json = $json + ',"detail":"' + [string]$Record.detail + '"' }
    return $json + '}'
}

function Write-JobStatusRecord {
    param($Record, [string]$Path)
    if ($null -eq $Record -or [string]::IsNullOrWhiteSpace($Path)) { return $false }
    $dir = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrWhiteSpace($dir) -or -not (Test-Path -LiteralPath $dir -PathType Container)) { return $false }
    $json = ConvertTo-JobStatusJson $Record
    if ($json -match '(?i)password\s*=|postgres://|Host\s*=') { return $false }
    $partial = $Path + '.partial'
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($partial, $json, $encoding)
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Move-Item -LiteralPath $partial -Destination $Path
    return (-not (Test-Path -LiteralPath $partial)) -and (Test-Path -LiteralPath $Path)
}

function Format-SnapshotSize {
    param([int64]$Bytes)
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    # This database dumps at about half a GiB. Show tenths of a GB from 100 MiB up.
    if ($Bytes -ge 100MB) { return [string]::Format($culture, '{0:0.0} GB', ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return [string]::Format($culture, '{0:0.0} MB', ($Bytes / 1MB)) }
    return [string]::Format($culture, '{0} B', $Bytes)
}

function New-PlainBackupCloudRecord {
    param([int64]$Bytes, [string]$OneDrive = '')
    $size = Format-SnapshotSize $Bytes
    if ($OneDrive -eq 'ok') {
        return New-JobStatusRecord -Job 'db-backup' -Icon 'OK' -Status 'ok' -Detail ($size + ' +cloud')
    }
    if ($OneDrive -eq 'missing' -or $OneDrive -eq 'failed') {
        return New-JobStatusRecord -Job 'db-backup' -Icon 'WARN' -Status 'warn' -Detail ($size + ' no cloud copy')
    }
    return New-JobStatusRecord -Job 'db-backup' -Icon 'OK' -Status 'ok' -Detail $size
}

function ConvertFrom-SnapshotUtcStamp {
    param([string]$Stamp)
    if ($Stamp -notmatch '^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$') { return $null }
    try {
        return [datetime]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4], [int]$Matches[5], [int]$Matches[6], [DateTimeKind]::Utc)
    } catch { return $null }
}

function Test-SnapshotStatusFresh {
    param([string]$Path, [datetime]$NowUtc, [int]$MaxAgeHours = 26)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try { $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $false }
    $exitCode = 1
    try { $exitCode = [int]$obj.exit } catch { return $false }
    if ($exitCode -ne 0) { return $false }
    $when = ConvertFrom-SnapshotUtcStamp ([string]$obj.utc)
    if ($null -eq $when) { return $false }
    if ($NowUtc.Kind -ne [DateTimeKind]::Utc) { $NowUtc = [datetime]::SpecifyKind($NowUtc, [DateTimeKind]::Utc) }
    $ageHours = ($NowUtc - $when).TotalHours
    if ($ageHours -ge $MaxAgeHours) { return $false }
    if ($ageHours -lt -1) { return $false }
    return $true
}

function Test-SnapshotDumpFresh {
    param([string]$Dir, [datetime]$NowUtc, [int]$MaxAgeHours = 26, [string]$NamePattern = '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$')
    if ([string]::IsNullOrWhiteSpace($Dir) -or -not (Test-Path -LiteralPath $Dir -PathType Container)) { return $false }
    if ($NowUtc.Kind -ne [DateTimeKind]::Utc) { $NowUtc = [datetime]::SpecifyKind($NowUtc, [DateTimeKind]::Utc) }
    $newest = $null
    foreach ($file in @(Get-ChildItem -LiteralPath $Dir -Filter 'antiphon-*.dump' -File -ErrorAction SilentlyContinue)) {
        if ($file.Name -notmatch $NamePattern) { continue }
        if ($null -eq $newest -or $file.LastWriteTimeUtc -gt $newest) { $newest = $file.LastWriteTimeUtc }
    }
    if ($null -eq $newest) { return $false }
    $ageHours = ($NowUtc - $newest).TotalHours
    if ($ageHours -ge $MaxAgeHours) { return $false }
    if ($ageHours -lt -1) { return $false }
    return $true
}

function Send-JobStatusLine {
    param(
        [string]$Line,
        [string]$ChatId = '8738110514',
        [string]$SenderRoot = 'C:\src\ClaudeBot'
    )
    if ($script:JobStatusSent) { return 2 }
    if ([string]::IsNullOrWhiteSpace($Line)) { return 1 }
    if ($Line -match '(?i)password\s*=|postgres://|Host\s*=') { return 1 }
    $script:JobStatusSent = $true
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('tg-job-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $file = Join-Path $dir 'msg.txt'
        $encoding = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($file, $Line, $encoding)
        $dotnet = $null
        $found = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $dotnet = $found.Source }
        if ([string]::IsNullOrWhiteSpace($dotnet)) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
        $sender = Join-Path $SenderRoot 'scripts\tg-send.cs'
        Push-Location $SenderRoot
        try {
            & $dotnet run $sender -- --to $ChatId --text-file $file
            if ($null -eq $LASTEXITCODE) { return 1 }
            return [int]$LASTEXITCODE
        } finally {
            Pop-Location
        }
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

$recordDir = 'C:\Users\lndco\antiphon-backups\job-status'
if ([string]::IsNullOrWhiteSpace($StatusFile)) { $StatusFile = 'C:\Users\lndco\antiphon-backups\snapshot-status.json' }
if ([string]::IsNullOrWhiteSpace($DumpDir)) { $DumpDir = 'C:\Users\lndco\antiphon-backups' }
$now = [datetime]::UtcNow
if ($StaleCheck) {
    $fresh = (Test-SnapshotStatusFresh -Path $StatusFile -NowUtc $now -MaxAgeHours $MaxAgeHours) -or (Test-SnapshotDumpFresh -Dir $DumpDir -NowUtc $now -MaxAgeHours $MaxAgeHours)
    if ($fresh) {
        Write-Output 'JOB STATUS fresh'
        exit 0
    }
    $stale = New-JobStatusRecord -Job 'db-backup' -Icon 'STALE' -Status 'stale'
    if (-not (Test-Path -LiteralPath $recordDir)) { New-Item -ItemType Directory -Path $recordDir | Out-Null }
    [void](Write-JobStatusRecord -Record $stale -Path (Join-Path $recordDir 'db-backup-stale.json'))
    $staleLine = Format-JobStatusLine $stale
    Write-Output $staleLine
    if ($Send) { exit (Send-JobStatusLine -Line $staleLine -ChatId $ChatId -SenderRoot $SenderRoot) }
    exit 0
}

$made = New-JobStatusRecord -Job $Job -Icon $Icon -Status $Status -Detail $Detail
if ($null -eq $made) { Write-Output 'JOB STATUS bad-record'; exit 2 }
if (-not [string]::IsNullOrWhiteSpace($RecordPath)) {
    $parent = [System.IO.Path]::GetDirectoryName($RecordPath)
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    [void](Write-JobStatusRecord -Record $made -Path $RecordPath)
}
$outLine = Format-JobStatusLine $made
Write-Output $outLine
if ($Send) { exit (Send-JobStatusLine -Line $outLine -ChatId $ChatId -SenderRoot $SenderRoot) }
exit 0
