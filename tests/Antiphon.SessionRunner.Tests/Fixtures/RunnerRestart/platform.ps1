function New-RunnerRestartPlatform($Root, $CommandClock) {
    $cfg = Get-Content -LiteralPath (Join-Path $Root 'config.json') -Raw | ConvertFrom-Json
    $state = @{ ms = [double]$cfg.startMs; probes = 0; calls = 0; emitted = @{} }
    $origin = [DateTime]::Parse('2026-09-07T10:00:00.0000000Z').ToUniversalTime()
    $identity = @{ pid = 900001; startTimeUtc = '2026-09-07T09:59:00.0000000Z'; path = 'synthetic-runner.exe' }
    $supervisor = @{ pid = 900002; startTimeUtc = '2026-09-07T09:58:00.0000000Z'; path = 'synthetic-supervisor.exe' }
    $tracePath = Join-Path $Root 'trace.jsonl'
    $trace = {
        param($op, $value)
        $state.calls++
        if ($state.calls -gt 1000) { throw 'fixture operation fuse' }
        [IO.File]::AppendAllText($tracePath, ((@{ op = $op; value = $value; ms = $state.ms } | ConvertTo-Json -Compress) + "`n"))
    }.GetNewClosure()
    $log = Join-Path $Root 'startup.log'
    return @{
        Now = { if ($cfg.failWaitOperation -eq 'clock' -and $state.probes -gt 0) { throw 'injected wait failure: clock' }; $state.ms }.GetNewClosure()
        Utc = { if ($cfg.failWaitOperation -eq 'utc' -and $state.probes -gt 0) { throw 'injected wait failure: utc' }; $origin.AddMilliseconds($state.ms + $(if ($state.ms -ge 1000) { [double]$cfg.utcJump } else { 0 })).ToString('o') }.GetNewClosure()
        Sleep = { param($ms) & $trace 'sleep' $ms; $state.ms += $ms }.GetNewClosure()
        Probe = {
            param($ms)
            & $trace 'probe' $ms
            $state.probes++
            if ($state.probes -eq 1) { $state.ms += [double]$cfg.firstProbeMs }
            if ($cfg.failWaitOperation -eq 'probe') { throw 'injected wait failure: probe' }
            foreach ($r in $cfg.records) {
                if ($state.ms -ge $r.atMs -and -not $state.emitted.ContainsKey([string]$r.atMs)) {
                    [IO.File]::AppendAllText($log, ('ANTIPHON_STARTUP ' + ($r.record | ConvertTo-Json -Compress) + "`n"))
                    $state.emitted[[string]$r.atMs] = $true
                }
            }
            $status = if ($state.ms -ge [double]$cfg.healthyAt) { [int]$cfg.status } else { 0 }
            @{ status = $status; result = "fixture-$status" }
        }.GetNewClosure()
        Identity = {
            if ($cfg.foreign) { return @{ pid = 900003; startTimeUtc = $identity.startTimeUtc; path = 'C:\fixture\other-build\Antiphon.SessionRunner.exe' } }
            if ($cfg.reused) { return @{ pid = $identity.pid; startTimeUtc = '2026-09-07T09:57:00.0000000Z'; path = $identity.path } }
            $identity
        }.GetNewClosure()
        Verify = { param($i) $state.ms += [double]$cfg.verifyMs; $i -and $i.pid -eq $identity.pid -and $i.startTimeUtc -eq $identity.startTimeUtc -and $i.path -eq $identity.path }.GetNewClosure()
        Process = { param($id) if ($id -eq $identity.pid) { $identity } elseif ($id -eq $supervisor.pid) { $supervisor } }.GetNewClosure()
        Census = { @{ supervisor = $(if (-not $cfg.noSupervisor) { $supervisor }); wrapper = $null } }.GetNewClosure()
        Control = {
            param($op, $i)
            & $trace $op $i
            if ($cfg.forbid -or $cfg.failOperation -eq $op) { throw "injected operation failure: $op" }
            if ($op -eq 'stop-service') { $state.ms += [double]$cfg.actionMs }
            if ($op -eq 'write-state') { [IO.File]::WriteAllText((Join-Path $Root 'state'), 'running') }
            elseif ($op -eq 'delete-pids') { [IO.File]::WriteAllText((Join-Path $Root 'pid'), '') }
            elseif ($op -notin @('write-state','stop-service','start-task','stop-supervisor','kill-sessions')) { throw "unconfigured operation: $op" }
        }.GetNewClosure()
        LogPath = $log
    }
}
