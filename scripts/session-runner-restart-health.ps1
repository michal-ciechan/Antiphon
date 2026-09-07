# Import is inert. All machine control and clocks are supplied by the platform.
function Get-RunnerProcessIdentity([int]$ProcessId) {
    try {
        $p = Get-Process -Id $ProcessId -ErrorAction Stop
        return @{ pid = $p.Id; startTimeUtc = $p.StartTime.ToUniversalTime().ToString('o'); path = $p.Path }
    } catch { return $null }
}

function Invoke-RunnerHealthHttp([string]$Url, [double]$BudgetMs) {
    Add-Type -AssemblyName System.Net.Http
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseProxy = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $cts = [Threading.CancellationTokenSource]::new()
    $cts.CancelAfter([TimeSpan]::FromMilliseconds([Math]::Max(1, $BudgetMs)))
    $response = $null
    try {
        $response = $client.GetAsync($Url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cts.Token).GetAwaiter().GetResult()
        return @{ status = [int]$response.StatusCode; result = "http-$([int]$response.StatusCode)" }
    } catch { return @{ status = 0; result = 'transport-error-or-timeout' } }
    finally { if ($response) { $response.Dispose() }; $cts.Dispose(); $client.Dispose(); $handler.Dispose() }
}

function New-RunnerRestartPlatform($Root, $CommandClock) {
    # A local TCP owner lookup avoids the slow CIM enumeration in Get-NetTCPConnection.
    # Define it during read-only setup, outside the observation budget (PS 5.1 compatible).
    if (-not ('AntiphonRestartTcpOwner' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class AntiphonRestartTcpOwner {
    [DllImport("iphlpapi.dll", SetLastError=true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    public static int Find(int port) {
        int size=0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        IntPtr table=Marshal.AllocHGlobal(size);
        try {
            if(GetExtendedTcpTable(table, ref size, false, 2, 3, 0)!=0) return 0;
            int count=Marshal.ReadInt32(table);
            for(int i=0;i<count;i++) {
                int offset=4+i*24;
                int localPort=(Marshal.ReadByte(table,offset+8)<<8)|Marshal.ReadByte(table,offset+9);
                int address=Marshal.ReadInt32(table,offset+4);
                if(Marshal.ReadInt32(table,offset)==2 && localPort==port && (address==0 || address==16777343)) return Marshal.ReadInt32(table,offset+20);
            }
            return 0;
        } finally { Marshal.FreeHGlobal(table); }
    }
}
'@
    }
    $logDir = Join-Path $Root 'logs'
    $clock = $CommandClock
    if (-not $clock) { $clock = [Diagnostics.Stopwatch]::StartNew() }
    $expectedExe = [IO.Path]::GetFullPath((Join-Path $Root 'src\Antiphon.SessionRunner\bin\Debug\net9.0\Antiphon.SessionRunner.exe'))
    $readPid = {
        param($name)
        try { $v = [IO.File]::ReadAllText((Join-Path $logDir $name)).Trim(); if ($v -match '^\d+$') { Get-RunnerProcessIdentity ([int]$v) } } catch { }
    }.GetNewClosure()
    $stop = {
        param($identity)
        if (-not $identity) { return }
        $now = Get-RunnerProcessIdentity $identity.pid
        if (-not $now) { return }
        if ($now.startTimeUtc -ne $identity.startTimeUtc -or $now.path -ne $identity.path) { throw 'Process identity changed before stop' }
        & taskkill.exe /T /F /PID $identity.pid 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0 -and (Get-RunnerProcessIdentity $identity.pid)) { throw "taskkill failed (exit $LASTEXITCODE)" }
    }.GetNewClosure()
    return @{
        CommandOrigin = 0
        Now = { $clock.Elapsed.TotalMilliseconds }.GetNewClosure()
        Utc = { [DateTime]::UtcNow.ToString('o') }
        Sleep = { param($ms) Start-Sleep -Milliseconds ([int][Math]::Ceiling($ms)) }
        Probe = { param($ms) Invoke-RunnerHealthHttp 'http://127.0.0.1:17204/health' $ms }
        Identity = {
            $owner = [AntiphonRestartTcpOwner]::Find(17204)
            if ($owner) { Get-RunnerProcessIdentity $owner }
        }
        Verify = { param($identity) $null -ne $identity -and $identity.path -eq $expectedExe -and
            (Get-RunnerProcessIdentity $identity.pid).startTimeUtc -eq $identity.startTimeUtc }.GetNewClosure()
        Census = { @{ supervisor = (& $readPid 'session-runner.supervisor.pid'); wrapper = (& $readPid 'session-runner.service.pid') } }.GetNewClosure()
        Process = { param($id) Get-RunnerProcessIdentity $id }
        Control = {
            param($operation, $identity)
            switch ($operation) {
                'write-state' { New-Item -ItemType Directory -Force $logDir | Out-Null; Set-Content -LiteralPath (Join-Path $logDir 'session-runner.state') -Value 'running' -Encoding UTF8 }
                'stop-service' {
                    # Stop the recorded service wrapper and runners at this checkout's expected
                    # Debug executable path. Unlike the old port-blind kill, a foreign listener
                    # (including a worktree or Release runner) is left alone, even with -Hard.
                    & $stop (& $readPid 'session-runner.service.pid')
                    Get-Process Antiphon.SessionRunner -ErrorAction SilentlyContinue | ForEach-Object {
                        $owned = Get-RunnerProcessIdentity $_.Id
                        if ($owned.path -eq $expectedExe) { & $stop $owned }
                    }
                }
                'stop-supervisor' { & $stop $identity }
                'delete-pids' { foreach ($file in @('session-runner.service.pid','session-runner.supervisor.pid')) { Remove-Item -LiteralPath (Join-Path $logDir $file) -ErrorAction SilentlyContinue } }
                'start-task' { Start-ScheduledTask -TaskName 'Antiphon Session Runner' }
                'kill-sessions' {
                    try { Invoke-WebRequest 'http://127.0.0.1:17204/sessions/kill-all' -Method Post -UseBasicParsing -TimeoutSec 15 | Out-Null }
                    catch { Write-Host 'kill-all endpoint unavailable; checking detached pty-host stragglers.' }
                    Get-Process Antiphon.PtyHost -ErrorAction SilentlyContinue | ForEach-Object { & $stop (Get-RunnerProcessIdentity $_.Id) }
                }
                default { throw "Unknown control operation $operation" }
            }
        }.GetNewClosure()
        LogPath = (Join-Path $logDir 'session-runner.log')
    }
}

function New-RunnerMilestoneReader([string]$Path) {
    @{ Path = $Path; Offset = 0L; Creation = $null; Pending = [byte[]]@(); Initial = $true; Phase = $null; Attempt = $null; ErrorCode = $null; RunnerIdentity = $null; SupervisorIdentity = $null; Anchor = $null; RetiredAttempts = @(); LastUtc = $null }
}

function Read-RunnerMilestones($Reader, $Platform, $NotBeforeUtc) {
    # Bounded byte reads retain incomplete UTF-8 and JSON until the newline arrives.
    $stream = $null
    try {
        $info = Get-Item -LiteralPath $Reader.Path -ErrorAction Stop
        $stream = [IO.File]::Open($Reader.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $reset = $Reader.Initial -or $Reader.Creation -ne $info.CreationTimeUtc.Ticks -or $stream.Length -lt $Reader.Offset
        if (-not $reset -and $Reader.Anchor) {
            $size = [int][Math]::Min(128, $Reader.Offset)
            $null = $stream.Seek($Reader.Offset - $size, [IO.SeekOrigin]::Begin)
            $anchor = New-Object byte[] $size
            $read = $stream.Read($anchor, 0, $size)
            $reset = $read -ne $size -or [Convert]::ToBase64String($anchor) -ne $Reader.Anchor
        }
        if ($reset -or ($stream.Length - $Reader.Offset) -gt 262144) {
            $Reader.Phase = $null; $Reader.Attempt = $null; $Reader.ErrorCode = $null; $Reader.Pending = [byte[]]@(); $Reader.LastUtc = $null; $Reader.RetiredAttempts = @()
            $Reader.Offset = [Math]::Max(0, $stream.Length - 262144)
            $Reader.DiscardFirst = $Reader.Offset -gt 0
        }
        $Reader.Initial = $false; $Reader.Creation = $info.CreationTimeUtc.Ticks
        $null = $stream.Seek($Reader.Offset, [IO.SeekOrigin]::Begin)
        $bytes = New-Object byte[] ([int][Math]::Min(262144, $stream.Length - $Reader.Offset))
        $n = $stream.Read($bytes, 0, $bytes.Length); $Reader.Offset += $n
        if ($n -eq 0) { return }
        $size = [int][Math]::Min(128, $Reader.Offset)
        $null = $stream.Seek($Reader.Offset - $size, [IO.SeekOrigin]::Begin)
        $anchor = New-Object byte[] $size
        $null = $stream.Read($anchor, 0, $size)
        $Reader.Anchor = [Convert]::ToBase64String($anchor)
        $all = [byte[]]($Reader.Pending + $bytes[0..($n-1)])
        $start = 0
        for ($i = 0; $i -lt $all.Length; $i++) {
            if ($all[$i] -ne 10) { continue }
            $length = $i - $start
            if ($Reader.DiscardFirst) { $Reader.DiscardFirst = $false; $start = $i + 1; continue }
            if ($length -gt 8192) { $Reader.Phase = $null; $start = $i + 1; continue }
            $line = [Text.Encoding]::UTF8.GetString($all, $start, $length); $start = $i + 1
            $marker = $line.IndexOf('ANTIPHON_STARTUP ')
            if ($marker -lt 0) { continue }
            try { $record = $line.Substring($marker + 17) | ConvertFrom-Json -ErrorAction Stop } catch { $Reader.Phase = $null; continue }
            if ($record.version -ne 1) { $Reader.Phase = $null; continue }
            $current = & $Platform.Process $record.producerPid
            $matches = $current -and (([DateTime]$current.startTimeUtc).ToUniversalTime() -eq ([DateTime]$record.producerStartTimeUtc).ToUniversalTime()) -and
                $record.attemptId -and $record.attemptId -notin $Reader.RetiredAttempts -and
                (([DateTime]$record.utc).ToUniversalTime() -ge ([DateTime]$NotBeforeUtc).ToUniversalTime())
            if (-not $matches) { continue }
            $census = & $Platform.Census
            $supervisor = $census.supervisor
            $trusted = ($record.producer -eq 'supervisor' -and $supervisor -and $supervisor.pid -eq $current.pid -and $supervisor.startTimeUtc -eq $current.startTimeUtc) -or
                ($record.producer -eq 'runner' -and (& $Platform.Verify $current))
            if (-not $trusted) { continue }
            $utc = ([DateTime]$record.utc).ToUniversalTime()
            if ($Reader.LastUtc -and $utc -lt $Reader.LastUtc) { continue }
            if ($record.producer -eq 'runner') { $Reader.RunnerIdentity = $current } else { $Reader.SupervisorIdentity = $current }
            if ($Reader.Attempt -ne $record.attemptId) {
                if ($record.event -notin @('build-start','launch-requested','managed-entry')) { continue }
                if ($Reader.Attempt) { $Reader.RetiredAttempts = @($Reader.RetiredAttempts + $Reader.Attempt | Select-Object -Last 32) }
                $Reader.Attempt = $record.attemptId; $Reader.ErrorCode = $null
            }
            $Reader.LastUtc = $utc
            $Reader.Phase = $record
            if ($null -ne $record.exitCode -and $record.exitCode -ne 0) { $Reader.ErrorCode = $record.exitCode }
        }
        $Reader.Pending = if ($start -lt $all.Length) { [byte[]]$all[$start..($all.Length-1)] } else { [byte[]]@() }
        if ($Reader.Pending.Length -gt 8192) { $Reader.Pending = [byte[]]@(); $Reader.DiscardFirst = $true; $Reader.Phase = $null }
    } catch { $Reader.Phase = $null; $Reader.Pending = [byte[]]@(); $Reader.Initial = $true }
    finally { if ($stream) { $stream.Dispose() } }
}

function Invoke-RunnerRestart($Platform, [switch]$Hard, [switch]$KillSessions, [switch]$WaitOnly, [int]$TimeoutSec = 180, [string]$CommandEnteredAtUtc) {
    $entry = & $Platform.Now
    if ($Platform.ContainsKey('CommandOrigin')) { $entry = $Platform.CommandOrigin }
    if (-not $CommandEnteredAtUtc) { $CommandEnteredAtUtc = & $Platform.Utc }
    $result = [ordered]@{ outcome = 'action-failed'; mode = $(if ($WaitOnly) { 'wait-only' } else { 'restart' }); exitCode = 1;
        timeoutSec = $TimeoutSec; waitElapsedMs = 0; commandElapsedMs = 0; observedAtUtc = $null; lastObservedPhase = 'unknown';
        phaseAgeMs = $null; attemptId = $null; supervisor = $null; wrapper = $null; runner = $null; firstHealth200ObservedAtUtc = $null;
        lastProbeResult = $null; errorCode = $null; operation = $null; error = $null; commandEnteredAtUtc = $CommandEnteredAtUtc;
        supervisorAlive = $null; runnerAlive = $null; portOwner = $null;
        relaunchRequestedAtUtc = $null; waitStartedAtUtc = $null; pollingIntervalMs = 2000 }
    $waitStart = $null
    try {
        $result.operation = 'validate-arguments'
        if ($TimeoutSec -le 0 -or ($WaitOnly -and ($Hard -or $KillSessions))) { throw 'TimeoutSec must be positive; WaitOnly cannot be combined with Hard or KillSessions' }
        $census = & $Platform.Census
        $result.supervisor = $census.supervisor; $result.wrapper = $census.wrapper
        $reader = New-RunnerMilestoneReader $Platform.LogPath
        $notBefore = if ($WaitOnly) { [DateTime]::MinValue.ToString('o') } else { & $Platform.Utc }
        if (-not $WaitOnly) {
            $result.operation = 'write-state'; & $Platform.Control 'write-state' $null
            if ($KillSessions) { $result.operation = 'kill-sessions'; & $Platform.Control 'kill-sessions' $null }
            $result.operation = 'stop-service'; & $Platform.Control 'stop-service' $null
            if ($Hard -or -not $census.supervisor) {
                if ($Hard -and $census.supervisor) { $result.operation = 'stop-supervisor'; & $Platform.Control 'stop-supervisor' $census.supervisor }
                $result.operation = 'delete-pids'; & $Platform.Control 'delete-pids' $null
                $result.operation = 'start-task'; & $Platform.Control 'start-task' $null
            }
            $result.relaunchRequestedAtUtc = & $Platform.Utc
        }
        $result.operation = $null
        $waitStart = & $Platform.Now; $result.waitStartedAtUtc = & $Platform.Utc
        Write-Host "Health wait started at $($result.waitStartedAtUtc); budget ${TimeoutSec}s, poll interval up to 2000 ms."
        $budget = [double]$TimeoutSec * 1000; $lastMessage = $waitStart; $lastPhase = 'unknown'
        $result.outcome = 'wait-expired'; $result.exitCode = 2
        while ($true) {
            $remaining = $budget - ((& $Platform.Now) - $waitStart)
            if ($remaining -le 0) { break }
            Read-RunnerMilestones $reader $Platform $notBefore
            $phase = if ($reader.Phase) { $reader.Phase.event } else { 'unknown' }
            $now = & $Platform.Now
            if ($phase -ne $lastPhase) { Write-Host "Last observed phase: $phase at $(& $Platform.Utc)"; $lastPhase = $phase; $lastMessage = $now }
            elseif ($now - $lastMessage -ge 15000) { Write-Host "Continuing health wait; last observed phase: $phase"; $lastMessage = $now }
            $remaining = $budget - ((& $Platform.Now) - $waitStart)
            if ($remaining -le 0) { break }
            $probe = & $Platform.Probe ([Math]::Min(5000, $remaining))
            $result.lastProbeResult = $probe.result
            $completed = & $Platform.Now; $observed = & $Platform.Utc
            if ($probe.status -eq 200) {
                $identity = & $Platform.Identity
                $result.portOwner = $identity
                $verified = & $Platform.Verify $identity
                $completed = & $Platform.Now
                if ($verified) { $result.runner = $identity }
                if ($verified -and $completed - $waitStart -le $budget) {
                    $result.runner = $identity; $result.firstHealth200ObservedAtUtc = $observed
                    $result.outcome = 'healthy'; $result.exitCode = 0
                    break
                }
                $result.lastProbeResult = if ($verified) { 'http-200-late' } else { 'http-200-identity-unconfirmed' }
            }
            $remaining = $budget - ((& $Platform.Now) - $waitStart)
            if ($remaining -gt 0) { & $Platform.Sleep ([Math]::Min(2000, $remaining)) }
        }
        $result.waitElapsedMs = [Math]::Round((& $Platform.Now) - $waitStart, 3)
        $result.lastObservedPhase = if ($reader.Phase) { $reader.Phase.event } else { 'unknown' }
        if ($reader.Phase) { $result.phaseAgeMs = [Math]::Max(0, (([DateTime](& $Platform.Utc)).ToUniversalTime() - ([DateTime]$reader.Phase.utc).ToUniversalTime()).TotalMilliseconds) }
        $result.attemptId = $reader.Attempt; $result.errorCode = $reader.ErrorCode
        try { $census = & $Platform.Census; $result.supervisor = $census.supervisor; $result.wrapper = $census.wrapper } catch { }
        if (-not $result.supervisor) { $result.supervisor = $reader.SupervisorIdentity }
        if (-not $result.runner) { $result.runner = $reader.RunnerIdentity }
        foreach ($kind in @('supervisor','runner')) {
            if ($result[$kind]) {
                try {
                    $live = & $Platform.Process $result[$kind].pid
                    $result[$kind + 'Alive'] = [bool]($live -and $live.startTimeUtc -eq $result[$kind].startTimeUtc)
                } catch { }
            }
        }
        if ($result.outcome -eq 'healthy') { Write-Host "healthy: first completed HTTP 200 at $($result.firstHealth200ObservedAtUtc); wait $($result.waitElapsedMs) ms." }
        else {
            try { $result.portOwner = & $Platform.Identity } catch { }
            Write-Host 'Health wait expired; runner readiness is unconfirmed. Startup may still be in progress.'
            Write-Host 'wait-expired: the script stopped waiting and has not stopped background startup.'
            Write-Host "Last observed phase: $($result.lastObservedPhase); age ms: $($result.phaseAgeMs); probe: $($result.lastProbeResult)."
            Write-Host "Identified supervisor alive: $($result.supervisorAlive); runner alive: $($result.runnerAlive) (blank means unknown)."
            if ($result.portOwner) {
                Write-Host "Last observed port 17204 owner: PID $($result.portOwner.pid); path: $($result.portOwner.path)."
                Write-Host 'A runner at another executable path is not stopped, even with -Hard; establish its ownership before stopping it.'
            }
            Write-Host 'Continue: pwsh -NoProfile -File scripts/restart-session-runner.ps1 -WaitOnly -TimeoutSec 180'
            Write-Host "Inspect: Get-Content '$($Platform.LogPath)' -Tail 30; also dated logs under %TEMP%\antiphon-logs\session-runner-*.log."
        }
    } catch {
        $result.error = $_.Exception.Message
        if ($null -ne $waitStart) {
            $result.outcome = 'wait-failed'; $result.exitCode = 1; $result.operation = 'health-wait'
            try { $result.waitElapsedMs = [Math]::Round((& $Platform.Now) - $waitStart, 3) } catch { $result.waitElapsedMs = $null }
            Write-Host "wait-failed: observation failed: $($result.error). Background startup has not been stopped."
        } else { Write-Host "action-failed: $($result.operation): $($result.error)" }
    }
    # Clock/observation failures must not suppress the front door's stable final JSON line.
    try { $result.observedAtUtc = & $Platform.Utc } catch { }
    try { $result.commandElapsedMs = [Math]::Round((& $Platform.Now) - $entry, 3) } catch { $result.commandElapsedMs = $null }
    return [pscustomobject]$result
}
