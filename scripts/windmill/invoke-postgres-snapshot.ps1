#requires -Version 5.1
<#
.SYNOPSIS
    Desktop entry for the daily plain snapshot. Runs the dump, then posts one status line.
    ASCII-only. No secrets on stdout.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'job-status.ps1')

function Resolve-PlainSnapshotScript {
    param([string[]]$Candidates, [string]$PinFile)
    # The banner concatenates the date at runtime, so the source does not
    # contain the literal operator-decision=2026-11-10. These literals do.
    # SOURCE-SHA is the sha256 of the postgres-snapshot.ps1 this runner copy
    # is allowed to execute. An earlier checkout is skipped when its bytes differ.
    $switch = '[switch]$Plain'
    $decision = 'SnapshotPlainDecision'
    $onedrive = '[switch]$OneDriveCopy'
    $script:PlainSnapshotSourceSha = ''
    if ([string]::IsNullOrWhiteSpace($PinFile)) {
        $PinFile = Join-Path $PSScriptRoot 'SOURCE-SHA'
    }
    if (-not (Test-Path -LiteralPath $PinFile -PathType Leaf)) { return '' }
    $pinned = ([System.IO.File]::ReadAllText($PinFile)).Trim().ToLowerInvariant()
    if ($pinned -notmatch '^[0-9a-f]{64}$') { return '' }
    if ($null -eq $Candidates -or @($Candidates).Count -eq 0) {
        $Candidates = @(
            'C:\src\Antiphon\scripts\postgres-snapshot.ps1',
            (Join-Path $PSScriptRoot 'postgres-snapshot.ps1'),
            'C:\Antiphon\worktrees\card-task-300d724e\scripts\postgres-snapshot.ps1'
        )
    }
    foreach ($path in @($Candidates)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $text = [System.IO.File]::ReadAllText($path)
        if (-not ($text.Contains($switch) -and $text.Contains($decision) -and $text.Contains('2026-11-10') -and $text.Contains($onedrive))) { continue }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $pinned) { continue }
        $script:PlainSnapshotSourceSha = $hash
        return $path
    }
    return ''
}

function Write-SafeSnapshotText {
    param([string]$Text, [int]$Max = 80)
    if ([string]::IsNullOrWhiteSpace($Text)) { return }
    $count = 0
    foreach ($line in @($Text -split "`r?`n")) {
        if ($line -match '(?i)password\s*=|postgres://|Bearer |token=') { continue }
        Write-Output $line
        $count++
        if ($count -ge $Max) { break }
    }
}

function Invoke-PlainSnapshotChild {
    param([string]$Snapshot)
    $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $stamp = [guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ('snapshot-out-' + $stamp + '.txt')
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ('snapshot-err-' + $stamp + '.txt')
    try {
        # Local\antiphon is writable by another account, so the default staging
        # parent fails CARD-1413. This profile directory's parents pass.
        $staging = 'C:\Users\lndco\antiphon-snapshot-staging'
        $proc = Start-Process -FilePath $exe -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Snapshot, '-Plain', '-Execute', '-OneDriveCopy',
            '-StagingPath', $staging
        ) -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $stdout = ''
        $stderr = ''
        if (Test-Path -LiteralPath $stdoutPath) { $stdout = [System.IO.File]::ReadAllText($stdoutPath) }
        if (Test-Path -LiteralPath $stderrPath) { $stderr = [System.IO.File]::ReadAllText($stderrPath) }
        return @{ Exit = [int]$proc.ExitCode; Out = $stdout; Err = $stderr }
    } finally {
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

$snapshot = Resolve-PlainSnapshotScript
$recordDir = 'C:\Users\lndco\antiphon-backups\job-status'
if (-not (Test-Path -LiteralPath $recordDir)) { New-Item -ItemType Directory -Path $recordDir | Out-Null }
$recordPath = Join-Path $recordDir 'db-backup.json'
$code = 1
if ([string]::IsNullOrWhiteSpace($snapshot)) {
    Write-Output 'SNAPSHOT fail exit=1 reason=missing-script'
} else {
    Write-Output ('snapshot-script=' + $snapshot)
    if (-not [string]::IsNullOrWhiteSpace($script:PlainSnapshotSourceSha)) {
        Write-Output ('source-sha=' + $script:PlainSnapshotSourceSha)
    }
    $ran = Invoke-PlainSnapshotChild $snapshot
    $code = [int]$ran.Exit
    Write-SafeSnapshotText $ran.Out
    if ($code -ne 0) { Write-SafeSnapshotText $ran.Err 40 }
}

$detail = 'exit ' + $code
$icon = 'FAIL'
$statusName = 'fail'
$record = $null
if ($code -eq 0) {
    $statusPath = 'C:\Users\lndco\antiphon-backups\snapshot-status.json'
    $bytes = -1L
    $obj = $null
    if (Test-Path -LiteralPath $statusPath) {
        try {
            $obj = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([int]$obj.exit -eq 0 -and [string]$obj.mode -eq 'plain') { $bytes = [int64]$obj.bytes }
        } catch { $bytes = -1L }
    }
    if ($bytes -ge 0) {
        $oneDrive = ''
        if ($null -ne $obj) {
            $names = @($obj.PSObject.Properties.Name)
            if ($names -contains 'oneDrive') { $oneDrive = [string]$obj.oneDrive }
        }
        $record = New-PlainBackupCloudRecord -Bytes $bytes -OneDrive $oneDrive
    }
}
if ($null -eq $record) {
    $record = New-JobStatusRecord -Job 'db-backup' -Icon $icon -Status $statusName -Detail $detail
}
if ($null -eq $record) { Write-Output 'JOB STATUS bad-record'; exit 1 }
[void](Write-JobStatusRecord -Record $record -Path $recordPath)
$line = Format-JobStatusLine $record
Write-Output $line
$sent = Send-JobStatusLine -Line $line
Write-Output ('telegram=' + $sent)
if ($sent -ne 0 -and $code -eq 0) { exit $sent }
exit $code
