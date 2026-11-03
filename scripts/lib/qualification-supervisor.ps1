#requires -Version 5.1
# CARD-1667: rc qualification supervisor helpers. The supervisor only observes and records;
# nothing here stops, signals, or restarts a process or clears a lock. ASCII-only,
# Windows PowerShell 5.1 safe.
. (Join-Path $PSScriptRoot 'rc-qualification-status.ps1')

function Get-QualificationHostPlatform {
    if (($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows) { return 'Windows' }
    return 'Unix'
}

# One line per call: open, append, flush to disk, close. A 1> redirect through a wrapper
# buffers 4 KiB blocks and loses its tail when the wrapper dies (2026-11-02, run 7f8e9fd1).
function Write-QualificationSupervisorLine {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
    $line = [datetime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture) + ' ' + $Text + "`r`n"
    $bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($line)
    $stream = New-Object System.IO.FileStream($Path, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}

function Write-QualificationSupervisorRecord {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
    $temp = $Path + '.tmp'
    [IO.File]::WriteAllText($temp, $Text, (New-Object System.Text.UTF8Encoding $false))
    if ([IO.File]::Exists($Path)) { [IO.File]::Delete($Path) }
    [IO.File]::Move($temp, $Path)
}

# Windows CommandLineToArgvW quoting; PowerShell 7 also parses Start-Process arguments this way on Unix.
function ConvertTo-QualificationCommandLine {
    param([string[]]$Arguments)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @($Arguments)) {
        $text = [string]$argument
        if ($text.Length -gt 0 -and $text -notmatch '[\s"]') { $parts.Add($text); continue }
        $builder = New-Object System.Text.StringBuilder
        [void]$builder.Append('"')
        $slashes = 0
        foreach ($ch in $text.ToCharArray()) {
            if ($ch -eq '\') { $slashes++; continue }
            if ($ch -eq '"') { [void]$builder.Append('\', 2 * $slashes + 1); $slashes = 0; [void]$builder.Append('"'); continue }
            if ($slashes -gt 0) { [void]$builder.Append('\', $slashes); $slashes = 0 }
            [void]$builder.Append($ch)
        }
        if ($slashes -gt 0) { [void]$builder.Append('\', 2 * $slashes) }
        [void]$builder.Append('"')
        $parts.Add($builder.ToString())
    }
    return ($parts -join ' ')
}

# Start-Process parameters for a child that must not share the starter's console or session.
# Windows: Start-Process without -NoNewWindow creates a new console (CREATE_NEW_CONSOLE), so a
# teardown of the starter's console cannot reach it; launch-rc.ps1 survived 7f8e9fd1 this way.
# Unix: setsid gives a new session and process group. Output goes to the child's own files.
function New-QualificationDetachedStartParameters {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList,
        [Parameter(Mandatory)][ValidateSet('Windows', 'Unix')][string]$Platform,
        [Parameter(Mandatory)][string]$StandardOutput,
        [Parameter(Mandatory)][string]$StandardError,
        [switch]$SameSession
    )
    $parameters = @{ PassThru = $true; RedirectStandardOutput = $StandardOutput; RedirectStandardError = $StandardError }
    if ($Platform -ceq 'Windows') {
        $parameters.FilePath = $FilePath
        $parameters.ArgumentList = ConvertTo-QualificationCommandLine $ArgumentList
        $parameters.WindowStyle = 'Hidden'
    } elseif ($SameSession) {
        $parameters.FilePath = $FilePath
        $parameters.ArgumentList = ConvertTo-QualificationCommandLine $ArgumentList
    } else {
        $parameters.FilePath = 'setsid'
        $parameters.ArgumentList = ConvertTo-QualificationCommandLine (@($FilePath) + @($ArgumentList))
    }
    return $parameters
}

function Get-QualificationConsoleProcessIds {
    if ((Get-QualificationHostPlatform) -cne 'Windows') { return @() }
    try {
        if (-not ('Antiphon.QualificationConsole' -as [type])) {
            Add-Type -Namespace Antiphon -Name QualificationConsole -MemberDefinition '[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetConsoleProcessList(uint[] list, uint count);'
        }
        $list = New-Object 'uint32[]' 256
        $count = [Antiphon.QualificationConsole]::GetConsoleProcessList($list, 256)
        $ids = @()
        for ($index = 0; $index -lt [Math]::Min([int]$count, 256); $index++) { $ids += [int]$list[$index] }
        return $ids
    } catch { return @() }
}

function Get-QualificationSessionId {
    if ((Get-QualificationHostPlatform) -cne 'Unix') { return $null }
    try {
        $stat = [IO.File]::ReadAllText('/proc/self/stat')
        # Fields after the parenthesised command: state ppid pgrp session ...
        $rest = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
        return [int]$rest[3]
    } catch { return $null }
}

# pid + start-time identity. Windows start times are exact FILETIME ticks; Linux derives them
# from the boot clock, which differs by a few milliseconds between reading processes, so Unix
# allows one second (same pid restarted within a second of the recorded start is not credible).
function Get-QualificationProcessObservation {
    param($Record, [double]$ToleranceMilliseconds = -1)
    if ($ToleranceMilliseconds -lt 0) {
        $ToleranceMilliseconds = 0
        if ((Get-QualificationHostPlatform) -ceq 'Unix') { $ToleranceMilliseconds = 1000 }
    }
    $unknown = [pscustomobject]@{ status = 'unknown'; reason = 'invalid-owner-identity' }
    if (-not $Record) { return [pscustomobject]@{ status = 'unknown'; reason = 'no-record' } }
    if ($Record.pid -isnot [int] -and $Record.pid -isnot [long]) { return $unknown }
    if ($Record.pid -le 0 -or $Record.pid -gt [int]::MaxValue) { return $unknown }
    $expectedStart = ConvertTo-RcQualificationTime $Record.processStartedAt
    if ($null -eq $expectedStart) { return $unknown }
    $process = $null
    try {
        try { $process = [Diagnostics.Process]::GetProcessById([int]$Record.pid) }
        catch {
            $cause = $_.Exception
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -is [ArgumentException]) { return [pscustomobject]@{ status = 'absent'; reason = 'pid-not-found' } }
            throw
        }
        $actualStart = $process.StartTime.ToUniversalTime()
        if ([Math]::Abs(($actualStart - $expectedStart).TotalMilliseconds) -gt $ToleranceMilliseconds) {
            return [pscustomobject]@{ status = 'reused'; reason = 'start-mismatch' }
        }
        return [pscustomobject]@{ status = 'alive'; reason = 'pid-start-match' }
    } catch { return [pscustomobject]@{ status = 'unknown'; reason = 'process-probe-error' } }
    finally { if ($process) { $process.Dispose() } }
}

# D-1 (Review 9f29e134): last-run.json holds only the latest run, so the next run overwrites ours.
# While it names our run id, supervise-rc keeps its exact text in TaskRoot\own-last-run.json; a
# copy with completedAt is never replaced by one without. Returns copied, unchanged, kept,
# foreign, missing or unreadable; it never writes the state root.
function Save-QualificationOwnRunState {
    param([Parameter(Mandatory)][string]$TaskRoot, [Parameter(Mandatory)][string]$StateRoot, [Parameter(Mandatory)][string]$RunId)
    $source = [IO.Path]::GetFullPath([IO.Path]::Combine($StateRoot, 'last-run.json'))
    $target = [IO.Path]::GetFullPath([IO.Path]::Combine($TaskRoot, 'own-last-run.json'))
    if (-not [IO.File]::Exists($source)) { return 'missing' }
    $text = $null
    try {
        $stream = New-Object System.IO.FileStream($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            if ($stream.Length -gt 1048576) { return 'unreadable' }
            $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding($false, $true)))
            try { $text = $reader.ReadToEnd().TrimStart([char]0xfeff) } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
        $parsed = ConvertFrom-Json -InputObject $text -ErrorAction Stop
    } catch { return 'unreadable' }
    if ($parsed -isnot [pscustomobject] -or [string]$parsed.runId -cne $RunId) { return 'foreign' }
    $existing = Read-RcQualificationRecord -Path $target
    if ($existing.status -ceq 'ok' -and [string]$existing.record.runId -ceq $RunId) {
        $kept = ConvertTo-RcQualificationTime $existing.record.completedAt
        $incoming = ConvertTo-RcQualificationTime $parsed.completedAt
        if ($null -ne $kept -and $null -eq $incoming) { return 'kept' }
    }
    if ([IO.File]::Exists($target)) {
        $current = $null
        try { $current = [IO.File]::ReadAllText($target) } catch { }
        if ($current -ceq $text) { return 'unchanged' }
    }
    Write-QualificationSupervisorRecord -Path $target -Text $text
    return 'copied'
}

# Classification. completedAt in last-run.json is the end signal; commission-exit.txt and the
# heartbeat belong to the supervisor, which can die while nightly-run keeps going.
# The state root is shared by every nightly and rc run, so last-run.json and run.lock may belong
# to a previous or another run (Review b26811ff D-1): completed needs the bound run id in
# last-run.json or in the TaskRoot copy, and running / supervisor-lost-run-alive need it in
# run.lock and in last-run.json or the copy. A launch whose run id appears in neither is
# not-started, or launch-failed once the supervisor has recorded commission-exit.txt, unless
# last-run.json was superseded (Review 9f29e134 D-1): our copy exists, or last-run.json names a
# run that started at or after the launch child (or gives no start time), so "never seen" cannot
# be told from "seen then overwritten": unknown/state-superseded.
function Get-QualificationSupervisionClassification {
    param([string]$ExpectedRunId, [string]$StateStatus, $State, [string]$LockStatus, [string]$LockRunId,
        [string]$RunOwner, [string]$Supervisor, [string]$Heartbeat, $HeartbeatUtc, [datetime]$NowUtc,
        [string]$CommissionExit, [double]$StaleMinutes = 5, [string]$CopyStatus = 'missing', $Copy, $LaunchStartedUtc)
    if ([string]::IsNullOrWhiteSpace($ExpectedRunId)) { return [pscustomobject]@{ classification = 'unknown'; reason = 'run-id-unbound' } }
    $stateOurs = ($StateStatus -ceq 'ok') -and $State -and ([string]$State.runId -ceq $ExpectedRunId)
    $lockOurs = ($LockStatus -ceq 'ok') -and ($LockRunId -ceq $ExpectedRunId)
    $copyOurs = ($CopyStatus -ceq 'ok') -and $Copy -and ([string]$Copy.runId -ceq $ExpectedRunId)
    $completedAt = $null
    if ($stateOurs -and $State.PSObject.Properties['completedAt']) { $completedAt = ConvertTo-RcQualificationTime $State.completedAt }
    if ($null -ne $completedAt) { return [pscustomobject]@{ classification = 'completed'; reason = 'state-completedAt' } }
    if (-not $stateOurs -and $copyOurs -and $Copy.PSObject.Properties['completedAt']) {
        if ($null -ne (ConvertTo-RcQualificationTime $Copy.completedAt)) { return [pscustomobject]@{ classification = 'completed'; reason = 'state-copy-completedAt' } }
    }
    if ($StateStatus -cnotin @('ok', 'missing')) { return [pscustomobject]@{ classification = 'unknown'; reason = 'state-' + $StateStatus } }
    if ($LockStatus -cnotin @('ok', 'missing')) { return [pscustomobject]@{ classification = 'unknown'; reason = 'lock-' + $LockStatus } }
    if (-not $stateOurs -and -not $lockOurs) {
        $superseded = $copyOurs
        if (-not $superseded -and $StateStatus -ceq 'ok' -and $State -and $LaunchStartedUtc -is [datetime]) {
            $otherStart = ConvertTo-RcQualificationTime $State.startedAt
            $superseded = ($null -eq $otherStart) -or ($otherStart -ge $LaunchStartedUtc)
        }
        if ($superseded) { return [pscustomobject]@{ classification = 'unknown'; reason = 'state-superseded' } }
        if (-not [string]::IsNullOrEmpty($CommissionExit)) { return [pscustomobject]@{ classification = 'launch-failed'; reason = 'commission-exit-' + $CommissionExit } }
        return [pscustomobject]@{ classification = 'not-started'; reason = 'run-id-not-seen' }
    }
    # Our run started. Only our own run.lock record says whether its nightly-run is still alive; a
    # released (error path) or foreign lock without completedAt is unknown, not run-lost.
    if (-not $lockOurs) {
        $lockReason = 'run-lock-missing'
        if ($LockStatus -ceq 'ok') { $lockReason = 'run-lock-foreign' }
        return [pscustomobject]@{ classification = 'unknown'; reason = $lockReason }
    }
    if ($RunOwner -cin @('absent', 'reused')) { return [pscustomobject]@{ classification = 'run-lost'; reason = 'run-owner-' + $RunOwner } }
    if ($RunOwner -cne 'alive') { return [pscustomobject]@{ classification = 'unknown'; reason = 'run-owner-' + $RunOwner } }
    if (-not $stateOurs -and -not $copyOurs) { return [pscustomobject]@{ classification = 'not-started'; reason = 'state-not-written' } }
    if ($Supervisor -cne 'alive') { return [pscustomobject]@{ classification = 'supervisor-lost-run-alive'; reason = 'supervisor-' + $Supervisor } }
    if ($Heartbeat -cne 'ok' -or $null -eq $HeartbeatUtc) { return [pscustomobject]@{ classification = 'supervisor-lost-run-alive'; reason = 'heartbeat-' + $Heartbeat } }
    if (($NowUtc - $HeartbeatUtc).TotalMinutes -ge $StaleMinutes) { return [pscustomobject]@{ classification = 'supervisor-lost-run-alive'; reason = 'heartbeat-stale' } }
    return [pscustomobject]@{ classification = 'running'; reason = 'heartbeat-fresh' }
}

function Get-QualificationSupervision {
    param([Parameter(Mandatory)][string]$TaskRoot, [Parameter(Mandatory)][string]$StateRoot,
        [double]$StaleMinutes = 5, [scriptblock]$ObserveProcess)
    $observe = {
        param($Record)
        if (-not $Record) { return [pscustomobject]@{ status = 'unknown'; reason = 'no-record' } }
        try {
            if ($ObserveProcess) { return (& $ObserveProcess $Record) }
            return (Get-QualificationProcessObservation $Record)
        } catch { return [pscustomobject]@{ status = 'unknown'; reason = 'process-probe-error' } }
    }
    # Probe the run owner BEFORE reading last-run.json: nightly-run writes completedAt, then
    # releases run.lock and exits, so an absent owner read first cannot hide a completion.
    $lock = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($StateRoot, 'run.lock')))
    $runOwner = & $observe $lock.record
    $state = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($StateRoot, 'last-run.json')))
    $identity = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($TaskRoot, 'supervisor-identity.json')))
    $supervisor = & $observe $identity.record
    if ($identity.status -ceq 'missing') { $supervisor = [pscustomobject]@{ status = 'missing'; reason = 'no-identity' } }
    # The launch's run id, recorded by supervise-rc.ps1 from start-supervisor.ps1 -RunId.
    $expectedRunId = $null
    if ($identity.status -ceq 'ok' -and $identity.record.runId -is [string]) { $expectedRunId = $identity.record.runId }
    $lockRunId = $null
    if ($lock.status -ceq 'ok' -and $null -ne $lock.record.runId) { $lockRunId = [string]$lock.record.runId }
    $stateRunId = $null
    if ($state.status -ceq 'ok' -and $null -ne $state.record.runId) { $stateRunId = [string]$state.record.runId }
    $runLock = $lock.status
    if ($lock.status -ceq 'ok') {
        $runLock = 'foreign'
        if (-not [string]::IsNullOrWhiteSpace($expectedRunId) -and $lockRunId -ceq $expectedRunId) { $runLock = 'ours' }
    }
    $beat = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($TaskRoot, 'supervisor-heartbeat.json')))
    $beatUtc = $null
    $hang = $null
    if ($beat.status -ceq 'ok') {
        $stamp = $beat.record.observedAtUtc
        if ($null -eq $stamp) { $stamp = $beat.record.utc }
        $beatUtc = ConvertTo-RcQualificationTime $stamp
        if ($beat.record.hangSuspected -is [bool]) { $hang = $beat.record.hangSuspected }
    }
    $copy = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($TaskRoot, 'own-last-run.json')))
    $copyRecord = $null
    $stateCopy = $copy.status
    if ($copy.status -ceq 'ok') {
        $stateCopy = 'foreign'
        if (-not [string]::IsNullOrWhiteSpace($expectedRunId) -and [string]$copy.record.runId -ceq $expectedRunId) { $stateCopy = 'ours'; $copyRecord = $copy.record }
    }
    # The launch child start bounds which later last-run.json writer superseded ours.
    $launch = Read-RcQualificationRecord -Path ([IO.Path]::GetFullPath([IO.Path]::Combine($TaskRoot, 'launch-child.json')))
    $launchStartedUtc = $null
    if ($launch.status -ceq 'ok' -and -not [string]::IsNullOrWhiteSpace($expectedRunId) -and [string]$launch.record.runId -ceq $expectedRunId) {
        $launchStartedUtc = ConvertTo-RcQualificationTime $launch.record.processStartedAt
    }
    $exitPath = [IO.Path]::Combine($TaskRoot, 'commission-exit.txt')
    $commissionExit = $null
    if ([IO.File]::Exists($exitPath)) { try { $commissionExit = ([IO.File]::ReadAllText($exitPath)).Trim() } catch { $commissionExit = 'unreadable' } }
    $now = [datetime]::UtcNow
    $verdict = Get-QualificationSupervisionClassification -ExpectedRunId $expectedRunId -StateStatus $state.status -State $state.record `
        -LockStatus $lock.status -LockRunId $lockRunId -RunOwner $runOwner.status -Supervisor $supervisor.status -Heartbeat $beat.status `
        -HeartbeatUtc $beatUtc -NowUtc $now -CommissionExit $commissionExit -StaleMinutes $StaleMinutes `
        -CopyStatus $copy.status -Copy $copyRecord -LaunchStartedUtc $launchStartedUtc
    # Result fields come only from our own run's state (or its TaskRoot copy), never from another run's last-run.json.
    $ownState = $copyRecord
    if (-not [string]::IsNullOrWhiteSpace($expectedRunId) -and $stateRunId -ceq $expectedRunId) { $ownState = $state.record }
    $fields = Select-RcQualificationFields $ownState @('phase', 'completedAt', 'exitCode', 'testsPassed', 'coverageComplete')
    $launchStarted = $null
    if ($null -ne $launchStartedUtc) { $launchStarted = $launchStartedUtc.ToString('o') }
    $age = $null
    if ($null -ne $beatUtc) { $age = [Math]::Round(($now - $beatUtc).TotalMinutes, 1) }
    return [pscustomobject][ordered]@{
        observedAtUtc = $now.ToString('o'); classification = $verdict.classification; reason = $verdict.reason
        runOwner = $runOwner.status; supervisor = $supervisor.status; heartbeat = $beat.status; heartbeatAgeMinutes = $age
        hangSuspected = $hang; state = $state.status; stateCopy = $stateCopy; runId = $expectedRunId; stateRunId = $stateRunId; runLock = $runLock; lockRunId = $lockRunId
        phase = $fields.phase; completedAt = $fields.completedAt
        exitCode = $fields.exitCode; testsPassed = $fields.testsPassed; coverageComplete = $fields.coverageComplete
        commissionExit = $commissionExit; launchStartedAt = $launchStarted
    }
}

# Process table for the heartbeat: Pid, ParentPid, CpuSeconds.
function Get-QualificationProcessTable {
    if ((Get-QualificationHostPlatform) -ceq 'Windows') {
        return @(Get-CimInstance Win32_Process -ErrorAction Stop | ForEach-Object {
            [pscustomobject]@{ Pid = [int]$_.ProcessId; ParentPid = [int]$_.ParentProcessId
                CpuSeconds = ([double]$_.KernelModeTime + [double]$_.UserModeTime) / 10000000.0 } })
    }
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($process in @(Get-Process)) {
        try {
            $parent = 0
            if ($process.Parent) { $parent = $process.Parent.Id }
            $rows.Add([pscustomobject]@{ Pid = $process.Id; ParentPid = $parent; CpuSeconds = $process.TotalProcessorTime.TotalSeconds })
        } catch { }
        finally { $process.Dispose() }
    }
    return @($rows.ToArray())
}
