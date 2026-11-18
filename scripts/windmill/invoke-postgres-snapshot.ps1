#requires -Version 5.1
<#
.SYNOPSIS
    Desktop entry for the daily plain snapshot. Runs the dump, then posts one status line.

    The source database is the one word in snapshot-target next to this
    runner copy: desktop or server2 (backup retarget plan 2026-11-18). A
    missing file, an empty file or any other value fails closed with
    reason=target-invalid and still posts the FAIL line. Flip and roll
    back only through Set-SnapshotTarget, dot-sourced from the runner copy:
    powershell -NoProfile -Command ". C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot.ps1; Set-SnapshotTarget desktop"
    It must print SNAPSHOT TARGET desktop.
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

function Get-SnapshotTarget {
    param([string]$Dir = '')
    if ([string]::IsNullOrWhiteSpace($Dir)) { $Dir = $PSScriptRoot }
    $path = Join-Path $Dir 'snapshot-target'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
    $text = [System.IO.File]::ReadAllText($path)
    # One word, case-sensitive, with at most one trailing line break.
    if ($text -cmatch '^(desktop|server2)(\r?\n)?\z') { return $Matches[1] }
    return ''
}

function Set-SnapshotTarget {
    param([string]$Word, [string]$Dir = '')
    if ([string]::IsNullOrWhiteSpace($Dir)) { $Dir = $PSScriptRoot }
    if (-not ($Word -ceq 'desktop' -or $Word -ceq 'server2')) {
        Write-Host 'SNAPSHOT TARGET refused (desktop or server2)'
        return ''
    }
    if (-not (Test-Path -LiteralPath $Dir -PathType Container)) {
        Write-Host 'SNAPSHOT TARGET refused (runner directory missing)'
        return ''
    }
    $path = Join-Path $Dir 'snapshot-target'
    # ASCII has no BOM. The word is written without a line break.
    [System.IO.File]::WriteAllText($path, $Word, (New-Object System.Text.ASCIIEncoding))
    $read = Get-SnapshotTarget -Dir $Dir
    if (-not ($read -ceq $Word)) {
        Write-Host 'SNAPSHOT TARGET mismatch'
        return ''
    }
    Write-Host ('SNAPSHOT TARGET ' + $read)
    return $read
}

function Get-PlainSnapshotChildArguments {
    param([string]$Snapshot, [string]$Target)
    # Local\antiphon is writable by another account, so the default staging
    # parent fails CARD-1413. This profile directory's parents pass.
    $staging = 'C:\Users\lndco\antiphon-snapshot-staging'
    return @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Snapshot, '-Plain', '-Execute', '-OneDriveCopy',
        '-StagingPath', $staging, '-Target', $Target
    )
}

function Get-PlainSnapshotFailDetail {
    param([int]$Code, [string]$Text, [string]$Target)
    # The child prints SNAPSHOT fail exit=<n> reason=<r>, optionally followed by
    # key=value details. Anything else falls back to the bare exit code.
    $reason = ''
    foreach ($line in @($Text -split "`r?`n")) {
        if ($line -cmatch '^SNAPSHOT fail exit=([0-9]+) reason=([a-z-]{1,40})( [a-z0-9-]+=[^ =]*)*$') {
            if ([int]$Matches[1] -eq $Code) { $reason = $Matches[2] }
            break
        }
        if ($line -cmatch '^SNAPSHOT fail ') { break }
    }
    $detail = 'exit ' + $Code
    if ($reason -ne '') { $detail = $detail + ' ' + $reason }
    return $detail + ' src=' + $Target
}

function Invoke-PlainSnapshotChild {
    param([string]$Snapshot, [string[]]$ArgumentList)
    $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $stamp = [guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ('snapshot-out-' + $stamp + '.txt')
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ('snapshot-err-' + $stamp + '.txt')
    try {
        $proc = Start-Process -FilePath $exe -ArgumentList $ArgumentList -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
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

function Invoke-PlainSnapshotJob {
    param(
        [string]$RunnerDir = '',
        [string]$RecordDir = 'C:\Users\lndco\antiphon-backups\job-status',
        [string]$StatusPath = 'C:\Users\lndco\antiphon-backups\snapshot-status.json'
    )
    if ([string]::IsNullOrWhiteSpace($RunnerDir)) { $RunnerDir = $PSScriptRoot }
    $out = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $RecordDir)) { New-Item -ItemType Directory -Path $RecordDir | Out-Null }
    $recordPath = Join-Path $RecordDir 'db-backup.json'
    $code = 1
    $target = Get-SnapshotTarget -Dir $RunnerDir
    $detail = ''
    if ($target -eq '') {
        # Fail closed: a missing or unknown word must not pick a database.
        $code = 9
        [void]$out.Add('SNAPSHOT fail exit=9 reason=target-invalid')
        $detail = 'exit 9 target-invalid src=none'
    } else {
        [void]$out.Add('snapshot-target=' + $target)
        $snapshot = Resolve-PlainSnapshotScript
        if ([string]::IsNullOrWhiteSpace($snapshot)) {
            [void]$out.Add('SNAPSHOT fail exit=1 reason=missing-script')
            $detail = 'exit 1 missing-script src=' + $target
        } else {
            [void]$out.Add('snapshot-script=' + $snapshot)
            if (-not [string]::IsNullOrWhiteSpace($script:PlainSnapshotSourceSha)) {
                [void]$out.Add('source-sha=' + $script:PlainSnapshotSourceSha)
            }
            $ran = Invoke-PlainSnapshotChild -Snapshot $snapshot -ArgumentList (Get-PlainSnapshotChildArguments -Snapshot $snapshot -Target $target)
            $code = [int]$ran.Exit
            foreach ($line in @(Write-SafeSnapshotText $ran.Out)) { [void]$out.Add([string]$line) }
            if ($code -ne 0) {
                foreach ($line in @(Write-SafeSnapshotText $ran.Err 40)) { [void]$out.Add([string]$line) }
                $detail = Get-PlainSnapshotFailDetail -Code $code -Text ([string]$ran.Out) -Target $target
            }
        }
    }

    $record = $null
    if ($code -eq 0) {
        $bytes = -1L
        $obj = $null
        if (Test-Path -LiteralPath $StatusPath) {
            try {
                $obj = Get-Content -LiteralPath $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ([int]$obj.exit -eq 0 -and [string]$obj.mode -eq 'plain') { $bytes = [int64]$obj.bytes }
            } catch { $bytes = -1L }
        }
        if ($bytes -ge 0) {
            $oneDrive = ''
            if ($null -ne $obj) {
                $names = @($obj.PSObject.Properties.Name)
                if ($names -contains 'oneDrive') { $oneDrive = [string]$obj.oneDrive }
            }
            $record = New-PlainBackupCloudRecord -Bytes $bytes -OneDrive $oneDrive -Target $target
        }
        if ($null -eq $record) { $detail = 'exit 0 src=' + $target }
    }
    if ($null -eq $record) {
        $record = New-JobStatusRecord -Job 'db-backup' -Icon 'FAIL' -Status 'fail' -Detail $detail
    }
    if ($null -eq $record) {
        [void]$out.Add('JOB STATUS bad-record')
        return [pscustomobject]@{ Exit = 1; Lines = $out.ToArray() }
    }
    [void](Write-JobStatusRecord -Record $record -Path $recordPath)
    $statusLine = Format-JobStatusLine $record
    [void]$out.Add($statusLine)
    $sent = Send-JobStatusLine -Line $statusLine
    [void]$out.Add('telegram=' + $sent)
    $final = $code
    if ($sent -ne 0 -and $code -eq 0) { $final = $sent }
    return [pscustomobject]@{ Exit = [int]$final; Lines = $out.ToArray() }
}

if ($MyInvocation.InvocationName -eq '.') { return }

# job-status.ps1 is dot-sourced above, so its [string]$Job parameter lives in
# this scope; the result needs a name of its own.
$snapshotJob = Invoke-PlainSnapshotJob
foreach ($line in @($snapshotJob.Lines)) { Write-Output $line }
exit ([int]$snapshotJob.Exit)
