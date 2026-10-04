#requires -Version 7.0
<#
CARD-0701: SELECT-only desktop statistics capture. Does not reset shared statistics,
change settings, activate a build, or generate session traffic. Compare also works offline.
Before capture on an older build, ContextPath supplies the observed runtime identity,
live/unknown counts, resolved driver/provider versions and sanitized pool flags.
Every context supplies openClients, workloadKey and siblingShas (CARD-0696/0698/0699/0700).
Never put credentials, connection strings or transcript text in context/evidence.
#>
[CmdletBinding()]
param(
    [ValidateSet('Capture', 'Compare', 'Acceptance')] [string]$Mode = 'Capture',
    [ValidateSet('Before', 'After')] [string]$Phase = 'After',
    [ValidateSet('R1', 'R2', 'R3')] [string]$Round = 'R3',
    [string]$PlanCard = 'CARD-0701',
    [Parameter(Mandatory)] [string]$EvidenceRoot,
    [string]$BaselinePath,
    [string]$AfterPath,
    [string]$ContextPath,
    [string]$Api = $(if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }),
    [string]$PostgresContainer = 'antiphon-postgres',
    [string]$Database = 'antiphon',
    [string]$DatabaseUser = 'antiphon',
    [ValidateRange(60, 3600)] [int]$WarmSeconds = 60,
    [ValidateRange(180, 3600)] [int]$WindowSeconds = 180
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $BaselinePath) { $BaselinePath = Join-Path $EvidenceRoot 'baseline.json' }

function Invoke-Bounded {
    param([string]$File, [string[]]$Arguments, [int]$TimeoutSeconds = 30)
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in $Arguments) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$File observation timed out."
        }
        $output = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "$File observation failed (exit $($process.ExitCode)); no command payload is printed." }
        return $output.Trim()
    } finally { $process.Dispose() }
}

function Save-Immutable {
    param([string]$Path, $Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 40))
    $file = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $file.Write($bytes) } finally { $file.Dispose() }
}

function Wait-Observed {
    param([int]$Seconds, [string]$Label)
    $remaining = $Seconds
    while ($remaining -gt 0) {
        Write-Host "$Label - $remaining seconds remaining (read-only; use natural workload)."
        $step = [Math]::Min(30, $remaining)
        Start-Sleep -Seconds $step
        $remaining -= $step
    }
}

function Read-Api {
    param([string]$Path)
    $headers = @{}
    if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
    return Invoke-RestMethod ($Api.TrimEnd('/') + $Path) -Headers $headers -TimeoutSec 15
}

function Read-Runtime {
    param($Context)
    try { return Read-Api '/api/diagnostics/session-state' }
    catch {
        # Only the documented pre-feature 404 can use explicit baseline observations.
        if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404 -or $Phase -ne 'Before') { throw }
        if (-not $Context.runtime) { throw 'Pre-feature capture requires context.runtime observations.' }
        $runtime = $Context.runtime
        $process = Get-Process -Id $runtime.processId
        try {
            $runtime.cpuSeconds = $process.TotalProcessorTime.TotalSeconds
            $runtime.processStartedAt = $process.StartTime.ToUniversalTime().ToString('o')
            $runtime.workingSetBytes = $process.WorkingSet64
        } finally { $process.Dispose() }
        return $runtime
    }
}

function Get-StatisticsSql {
    # SQL reports only fingerprints and classified shapes, never query parameters or bodies.
    return @'
SELECT json_build_object(
 'recordedAt', clock_timestamp(), 'postmasterStartedAt', pg_postmaster_start_time(),
 'databaseOid', (SELECT oid FROM pg_database WHERE datname = current_database()),
 'info', (SELECT row_to_json(i) FROM pg_stat_statements_info i),
 'pendingQueues', (SELECT count(*) FROM "SessionQueuedMessages" WHERE "Status" = 0),
 'openTasks', (SELECT count(*) FROM "AgentTasks" WHERE "Status" IN (0,1,2,3)),
 'statements', COALESCE((SELECT json_agg(x) FROM (
   SELECT dbid, userid, queryid::text, toplevel, calls, rows, total_exec_time,
     md5(query) AS fingerprint,
     CASE
       WHEN query LIKE '%session-state.seed%' THEN 'state-seed'
       WHEN query LIKE '%session-state.fallback%' THEN 'working'
       WHEN query LIKE '%session-state.identity%' THEN 'uuid'
       WHEN query LIKE '%session-state.binding%' THEN 'binding'
       WHEN query LIKE '%session-state.pins%' THEN 'state-pins'
       WHEN query ~* '^DISCARD' THEN 'reset'
       WHEN query LIKE '%"TranscriptEntries"%' AND query ~* '\mINSERT\M' THEN 'transcript-insert'
       WHEN query LIKE '%"TranscriptEntries"%' AND (query LIKE '%end_seq%' OR
         (query LIKE '%"TurnEnd"%' AND query LIKE '%EXISTS%')) THEN 'working'
       WHEN query LIKE '%"TranscriptEntries"%' AND query LIKE '%"Uuid"%' AND query LIKE '%ANY%' THEN 'uuid'
       WHEN query LIKE '%"TranscriptEntries"%' AND query ~* '\mSELECT\M' THEN 'transcript-unknown'
       WHEN query ~ '^SELECT [a-z0-9_]+\."RunnerId", [a-z0-9_]+\."RunnerStoreId", [a-z0-9_]+\."RunnerCwd"[[:space:]]+FROM "AgentSessions"' THEN 'binding'
       WHEN query LIKE '%"Boards"%' AND query LIKE '%"ProjectId"%' THEN 'boards'
       WHEN query LIKE '%"AgentTasks"%' AND query LIKE '%CompletionNote%' AND query ~* '\mUPDATE\M' THEN 'completion-stamp'
       ELSE 'other'
     END AS shape,
     (query LIKE '%"TranscriptEntries"%' AND query ~* '\mSELECT\M' AND query !~* '\mINSERT\M') AS transcript_select
   FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
 ) x), '[]'::json))::text;
'@
}

function Read-Statistics {
    $sql = Get-StatisticsSql
    $raw = Invoke-Bounded 'docker' @('exec', $PostgresContainer, 'psql', '-X', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-U', $DatabaseUser, '-d', $Database, '-c', $sql)
    $stats = $raw | ConvertFrom-Json -AsHashtable
    $identity = Invoke-Bounded 'docker' @('inspect', '--format', '{{.Id}}|{{.State.StartedAt}}', $PostgresContainer)
    $cpu = Invoke-Bounded 'docker' @('exec', $PostgresContainer, 'sh', '-c', 'if [ -r /sys/fs/cgroup/cpu.stat ]; then cat /sys/fs/cgroup/cpu.stat; else cat /sys/fs/cgroup/cpuacct/cpuacct.usage; fi')
    if ($cpu -match '(?m)^usage_usec\s+(\d+)') { $cpuSeconds = [double]$Matches[1] / 1e6 }
    elseif ($cpu -match '^\d+$') { $cpuSeconds = [double]$cpu / 1e9 }
    else { throw 'PostgreSQL container CPU accounting is unavailable.' }
    return @{ statistics = $stats; containerIdentity = $identity; containerCpuSeconds = $cpuSeconds }
}

function New-Snapshot {
    param($Context)
    $version = Read-Api '/api/version'
    $runtime = Read-Runtime $Context
    $pg = Read-Statistics
    # Refresh the operator's sanitized observations at every sample, not only at startup.
    $observed = Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json -AsHashtable
    $observation = @{ openClients = $observed.openClients; workloadKey = $observed.workloadKey; siblingShas = $observed.siblingShas }
    return @{ utc = [DateTimeOffset]::UtcNow.ToString('o'); version = $version; runtime = $runtime; postgres = $pg; observation = $observation }
}

function Assert-Fields {
    param($Value, [string[]]$Fields)
    foreach ($field in $Fields) {
        if ($null -eq $Value -or -not $Value.ContainsKey($field) -or $null -eq $Value[$field]) {
            throw "Missing required observation: $field."
        }
    }
}

function Get-WindowDelta {
    param($Start, $End, [double]$MinimumSeconds = 180)
    foreach ($point in @($Start, $End)) {
        Assert-Fields $point @('utc','runtime','version','postgres')
        Assert-Fields $point.runtime @('processId','processStartedAt','cpuSeconds','liveSessions','unknownSessions')
        Assert-Fields $point.version @('version')
        Assert-Fields $point.postgres @('containerIdentity','containerCpuSeconds','statistics')
        Assert-Fields $point.postgres.statistics @('postmasterStartedAt','databaseOid','info','statements')
        Assert-Fields $point.postgres.statistics.info @('stats_reset','dealloc')
        foreach ($row in $point.postgres.statistics.statements) {
            Assert-Fields $row @('dbid','userid','queryid','toplevel','calls','rows','total_exec_time','fingerprint','shape','transcript_select')
        }
    }
    $seconds = ([DateTimeOffset]::Parse($End.utc) - [DateTimeOffset]::Parse($Start.utc)).TotalSeconds
    if ($seconds -le 0 -or $seconds -lt $MinimumSeconds) { throw 'A statistics window is shorter than its required duration.' }
    foreach ($field in @('processId','processStartedAt')) {
        if ([string]$Start.runtime[$field] -ne [string]$End.runtime[$field]) { throw 'Server restarted inside the window.' }
    }
    if ($Start.version.version -ne $End.version.version) { throw 'Server build changed inside the window.' }
    $a = $Start.postgres.statistics; $b = $End.postgres.statistics
    if ($Start.postgres.containerIdentity -ne $End.postgres.containerIdentity -or $a.postmasterStartedAt -ne $b.postmasterStartedAt) { throw 'PostgreSQL restarted inside the window.' }
    if ($a.info.stats_reset -ne $b.info.stats_reset -or $a.info.dealloc -ne $b.info.dealloc -or $a.databaseOid -ne $b.databaseOid) { throw 'Statistics reset, eviction, or database identity change invalidated the window.' }
    $prior = @{}; $current = @{}
    foreach ($row in $a.statements) { $prior["$($row.dbid)/$($row.userid)/$($row.queryid)/$($row.toplevel)"] = $row }
    foreach ($row in $b.statements) { $current["$($row.dbid)/$($row.userid)/$($row.queryid)/$($row.toplevel)"] = $row }
    foreach ($key in $prior.Keys) { if (-not $current.ContainsKey($key)) { throw 'A statement disappeared inside the window.' } }
    $deltas = foreach ($key in $current.Keys) {
        $row = $current[$key]; $old = $prior[$key]
        $calls = [long]$row.calls; $rows = [long]$row.rows; $execMs = [double]$row.total_exec_time
        if ($old) { $calls -= [long]$old.calls; $rows -= [long]$old.rows; $execMs -= [double]$old.total_exec_time }
        if ($calls -lt 0 -or $rows -lt 0 -or $execMs -lt 0) { throw 'Negative statistics delta invalidated the window.' }
        @{ key = $key; fingerprint = $row.fingerprint; shape = $row.shape; calls = $calls; rows = $rows;
           execMs = $execMs; callsPerSecond = $calls / $seconds; transcriptSelect = $row.transcript_select }
    }
    $serverCpu = ([double]$End.runtime.cpuSeconds - [double]$Start.runtime.cpuSeconds) / $seconds
    $postgresCpu = ([double]$End.postgres.containerCpuSeconds - [double]$Start.postgres.containerCpuSeconds) / $seconds
    if ($serverCpu -lt 0 -or $postgresCpu -lt 0) { throw 'Negative CPU delta invalidated the window.' }
    $cacheDelta = $null; $perIngest = $null
    $transcriptReads = [long](($deltas | Where-Object transcriptSelect | Measure-Object calls -Sum).Sum)
    # Old schema-1 content rows remain readable, but their unclassified work cannot certify zero.
    $unknownReads = [long](($deltas | Where-Object { $_.transcriptSelect -and $_.shape -notin @('working','uuid','state-seed','state-pins') } | Measure-Object calls -Sum).Sum)
    if ($Start.runtime.ContainsKey('cache') -and $End.runtime.ContainsKey('cache')) {
        if ($Start.runtime.cache.serverEpoch -ne $End.runtime.cache.serverEpoch) { throw 'Cache epoch changed inside the window.' }
        $cacheDelta = @{}
        foreach ($metric in @('hits','loads','loadFaults','evictions','capacityFallbacks','ingestCalls','committedRows','duplicateBatches')) {
            $cacheDelta[$metric] = [long]$End.runtime.cache[$metric] - [long]$Start.runtime.cache[$metric]
            if ($cacheDelta[$metric] -lt 0) { throw 'Negative cache counter delta invalidated the window.' }
        }
        if ($cacheDelta.ingestCalls -gt 0) { $perIngest = $transcriptReads / $cacheDelta.ingestCalls }
    }
    $efDelta = $null
    if ($Start.runtime.ContainsKey('efReadAttempts') -and $End.runtime.ContainsKey('efReadAttempts')) {
        $efDelta = @{}
        foreach ($path in @('seed','pins','fallback','identity','binding')) {
            $efDelta[$path] = [long]$End.runtime.efReadAttempts[$path] - [long]$Start.runtime.efReadAttempts[$path]
            if ($efDelta[$path] -lt 0) { throw 'Negative EF counter delta invalidated the window.' }
        }
    }
    return @{ seconds = $seconds; serverCores = $serverCpu; postgresContainerCores = $postgresCpu; statements = @($deltas);
        transcriptSelectCalls = $transcriptReads; unknownTranscriptSelectCalls = $unknownReads; classificationComplete = ($unknownReads -eq 0);
        cacheDelta = $cacheDelta; efReadAttemptDelta = $efDelta; transcriptSelectsPerIngest = $perIngest }
}

function Capture-Evidence {
    param([string]$Destination)
    if (-not $IsWindows) { throw 'Production capture requires the canonical desktop lane; Compare is portable.' }
    if (-not $ContextPath -or -not (Test-Path -LiteralPath $ContextPath)) { throw 'Capture requires a sanitized context JSON (openClients, workloadKey, siblingShas).' }
    $context = Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in @('openClients','workloadKey','siblingShas')) { if (-not $context.ContainsKey($key)) { throw "Context is missing $key." } }
    if ([int]$context.openClients -lt 0 -or [string]::IsNullOrWhiteSpace($context.workloadKey)) { throw 'Context has invalid workload/client observations.' }
    foreach ($sibling in @('CARD-0696','CARD-0698','CARD-0699','CARD-0700')) {
        if ($context.siblingShas[$sibling] -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') { throw "Context requires the observed deployed SHA for $sibling." }
    }
    $worktrees = Invoke-Bounded 'git' @('-C', $PSScriptRoot, 'worktree', 'list', '--porcelain')
    $canonical = [regex]::Match($worktrees, '(?m)^worktree (.+)$').Groups[1].Value.Trim()
    if (-not $canonical) { throw 'Canonical checkout is unavailable.' }
    $sha = Invoke-Bounded 'git' @('-C', $canonical, 'rev-parse', 'HEAD')
    $identity = Read-Api '/api/version'
    if ($Phase -eq 'After' -and $identity.version -ne $sha) { throw 'Activated /api/version does not equal canonical checkout HEAD.' }
    Wait-Observed $WarmSeconds 'Warm-up'
    $windows = @()
    foreach ($windowNumber in @(1,2)) {
        $start = New-Snapshot $context
        # Round trip through JSON produces independent dictionaries, including baseline runtime facts.
        $start = $start | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable
        Save-Immutable (Join-Path $Destination "window-$windowNumber-start.json") $start
        $samples = @($start)
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($timer.Elapsed.TotalSeconds -lt $WindowSeconds) {
            $pause = [int][Math]::Ceiling([Math]::Min(30, $WindowSeconds - $timer.Elapsed.TotalSeconds))
            Wait-Observed $pause "Observation window $windowNumber"
            $sample = New-Snapshot $context | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable
            Save-Immutable (Join-Path $Destination "window-$windowNumber-sample-$($samples.Count).json") $sample
            $samples += $sample
        }
        $end = $samples[-1]
        $delta = Get-WindowDelta $start $end
        $workload = Get-ObservedWorkload $delta
        $windows += @{ workload = $workload; start = $start; end = $end; samples = $samples; delta = $delta }
    }
    $evidence = @{ schemaVersion = 2; provenance = 'sampled-runtime-and-context-v1'; card = $PlanCard; phase = $Phase; round = $Round; canonicalSha = $sha;
        capturedAt = [DateTimeOffset]::UtcNow.ToString('o'); context = $context; windows = $windows }
    Save-Immutable (Join-Path $Destination 'capture.json') $evidence
    return $evidence
}

function Get-Rate {
    param($Delta, [string]$Shape)
    return [double](($Delta.statements | Where-Object shape -eq $Shape | Measure-Object calls -Sum).Sum) / $Delta.seconds
}

function Get-ObservedWorkload {
    param($Delta)
    # Completed inserts describe natural transcript activity, not the order of capture windows.
    if ((Get-Rate $Delta 'transcript-insert') -gt 0) { return 'activity' }
    return 'idle'
}

function Get-ConfigurationKey {
    param($Runtime)
    if (-not $Runtime.ContainsKey('pool') -or $null -eq $Runtime.pool) { return $null }
    $values = @()
    foreach ($field in @('pooling','noResetOnClose','maxAutoPrepare','multiplexing')) {
        if (-not $Runtime.pool.ContainsKey($field) -or $null -eq $Runtime.pool[$field]) { return $null }
        $value = $Runtime.pool[$field]
        if ($field -eq 'maxAutoPrepare') {
            if ($value -isnot [long] -and $value -isnot [int]) { return $null }
            if ($value -lt 0) { return $null }
        } elseif ($value -isnot [bool]) { return $null }
        $values += [string]$value
    }
    foreach ($field in @('driverVersion','providerVersion')) {
        if (-not $Runtime.ContainsKey($field) -or [string]::IsNullOrWhiteSpace($Runtime[$field])) { return $null }
        $values += [string]$Runtime[$field]
    }
    return ($values | ConvertTo-Json -Compress)
}

function Get-ObservationKey {
    param($Observation)
    if ($null -eq $Observation) { return $null }
    foreach ($field in @('openClients','workloadKey','siblingShas')) {
        if (-not $Observation.ContainsKey($field) -or $null -eq $Observation[$field]) { return $null }
    }
    if ($Observation.openClients -lt 0 -or [string]::IsNullOrWhiteSpace($Observation.workloadKey)) { return $null }
    $values = @([string]$Observation.openClients, [string]$Observation.workloadKey)
    foreach ($sibling in @('CARD-0696','CARD-0698','CARD-0699','CARD-0700')) {
        if ($Observation.siblingShas[$sibling] -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') { return $null }
        $values += [string]$Observation.siblingShas[$sibling]
    }
    return ($values | ConvertTo-Json -Compress)
}

function Assert-Baseline {
    param($Baseline)
    if ($Baseline.schemaVersion -notin @(1,2) -or $Baseline.phase -ne 'Before' -or $Baseline.round -ne 'R1' -or
        $Baseline.card -ne $PlanCard -or $Baseline.windows.Count -ne 2 -or
        $Baseline.canonicalSha -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'Missing or incompatible immutable R1 baseline identity.'
    }
}

function Compare-Evidence {
    param($Before, $After)
    Assert-Baseline $Before
    if ($After.schemaVersion -notin @(1,2) -or $After.phase -ne 'After' -or $After.round -ne $Round -or
        $After.card -ne $PlanCard -or $After.windows.Count -ne 2 -or
        $After.canonicalSha -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'Missing or incompatible immutable baseline/after evidence.'
    }
    $reasons = [Collections.Generic.HashSet[string]]::new()
    $configuration = Get-ConfigurationKey $Before.windows[0].start.runtime
    $observation = Get-ObservationKey $Before.context
    if (-not $observation -or (Get-ObservationKey $After.context) -cne $observation) { [void]$reasons.Add('workload_context_missing_or_changed') }
    $comparisons = @(); $allGates = $true
    foreach ($evidence in @($Before, $After)) {
        if (@($evidence.windows | Where-Object workload -eq 'idle').Count -ne 1 -or
            @($evidence.windows | Where-Object workload -eq 'activity').Count -ne 1) { [void]$reasons.Add('idle_and_activity_windows_required') }
    }
    for ($i = 0; $i -lt 2; $i++) {
        $beforeWindow = $Before.windows[$i]; $afterWindow = $After.windows[$i]
        $b = Get-WindowDelta $beforeWindow.start $beforeWindow.end
        $a = Get-WindowDelta $afterWindow.start $afterWindow.end
        if ($beforeWindow.workload -ne $afterWindow.workload -or
            $beforeWindow.workload -ne (Get-ObservedWorkload $b) -or $afterWindow.workload -ne (Get-ObservedWorkload $a)) {
            [void]$reasons.Add('observed_workload_mismatch')
        }
        foreach ($window in @($beforeWindow, $afterWindow)) {
            $points = @($window.start, $window.end)
            if (-not $window.ContainsKey('samples') -or $window.samples.Count -lt 3) {
                [void]$reasons.Add('intermediate_observations_missing')
            } else {
                $samples = @($window.samples)
                if ($samples[0].utc -ne $window.start.utc -or $samples[-1].utc -ne $window.end.utc) {
                    [void]$reasons.Add('sample_coverage_incomplete')
                }
                for ($j = 1; $j -lt $samples.Count; $j++) {
                    # Check continuity and counters at every interval, including restart-and-return.
                    $interval = Get-WindowDelta $samples[$j - 1] $samples[$j] 0
                    if ($interval.seconds -gt 60) { [void]$reasons.Add('sample_gap_over_60_seconds') }
                }
                $points += $samples
            }
            foreach ($point in $points) {
                $key = Get-ConfigurationKey $point.runtime
                if (-not $configuration -or -not $key -or $key -cne $configuration) { [void]$reasons.Add('effective_configuration_missing_or_changed') }
                if ($point.runtime.liveSessions -lt 10) { [void]$reasons.Add('population_below_ten') }
                if ($point.runtime.liveSessions -ne $beforeWindow.start.runtime.liveSessions -or
                    $point.runtime.unknownSessions -ne $beforeWindow.start.runtime.unknownSessions) { [void]$reasons.Add('population_changed') }
                $observedKey = if ($point.ContainsKey('observation')) { Get-ObservationKey $point.observation } else { $null }
                if (-not $observedKey -or $observedKey -cne $observation) { [void]$reasons.Add('workload_observation_missing_or_changed') }
                if ($point.version.version -ne $window.start.version.version) { throw 'Server build changed inside sampled window.' }
            }
        }
        if ($afterWindow.start.version.version -ne $After.canonicalSha) { throw 'After served SHA differs from canonical source identity.' }
        if (-not $b.classificationComplete -or -not $a.classificationComplete) { [void]$reasons.Add('unclassified_transcript_selects') }
        $workingBefore = Get-Rate $b 'working'; $workingAfter = Get-Rate $a 'working'
        $bindingBefore = Get-Rate $b 'binding'; $bindingAfter = Get-Rate $a 'binding'
        $workingGate = $workingBefore -gt 0 -and $workingAfter -lt $workingBefore
        if ($Round -eq 'R3') { $workingGate = $workingBefore -gt 0 -and $workingAfter -le $workingBefore * 0.05 }
        $bindingGate = $Round -ne 'R3' -or ($bindingBefore -gt 0 -and $bindingAfter -le $bindingBefore * 0.05)
        if ($a.cacheDelta -and ($a.cacheDelta.loadFaults -gt 0 -or $a.cacheDelta.capacityFallbacks -gt 0)) { $allGates = $false }
        $allGates = $allGates -and $workingGate -and $bindingGate
        $comparisons += @{ workload = $beforeWindow.workload; workingBefore = $workingBefore; workingAfter = $workingAfter;
            workingPercentageGate = $workingGate; bindingPercentageGate = $bindingGate;
            bindingBefore = $bindingBefore; bindingAfter = $bindingAfter; before = $b; after = $a }
    }
    # Median of two samples is their midpoint. Keep raw windows for noise/third-window review.
    $beforeCpu = ($comparisons.before.serverCores | Measure-Object -Average).Average
    $afterCpu = ($comparisons.after.serverCores | Measure-Object -Average).Average
    $beforePg = @($comparisons.before.postgresContainerCores | Sort-Object)
    $afterPg = ($comparisons.after.postgresContainerCores | Measure-Object -Average).Average
    $cpuGate = $afterCpu -lt $beforeCpu -and $afterPg -le $beforePg[-1]
    $attributionValid = $reasons.Count -eq 0
    $allGates = $allGates -and $attributionValid
    return @{ schemaVersion = 2; round = $Round; attributionValid = $attributionValid; attributionReasons = @($reasons | Sort-Object);
        queryAndWorkloadGates = $allGates; cpuGate = $cpuGate; comparisons = $comparisons;
        ordinaryEvidence = 'Required separately: CP query budgets, correctness, residual attribution and matching workload review';
        accepted = $false; disposition = $(if ($allGates -and $cpuGate) { 'Measured gates passed; operator reviews ordinary evidence and workload match' } else { 'Inconclusive or failed; retain evidence and collect another matched window' }) }
}

New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
if ($Mode -eq 'Acceptance') {
    if (-not (Test-Path -LiteralPath $BaselinePath)) { throw 'Acceptance requires a previously saved baseline; the changed build cannot become before.' }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -AsHashtable
    Assert-Baseline $baseline
    foreach ($window in $baseline.windows) { $null = Get-WindowDelta $window.start $window.end }
}
if ($Mode -eq 'Compare') {
    if (-not $AfterPath) { throw 'Compare requires AfterPath.' }
    $before = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -AsHashtable
    $after = Get-Content -LiteralPath $AfterPath -Raw | ConvertFrom-Json -AsHashtable
    $summary = Compare-Evidence $before $after
    $out = Join-Path $EvidenceRoot ('compare-' + [Guid]::NewGuid().ToString('N') + '.json')
    Save-Immutable $out $summary
    Write-Host "Comparison saved: $out"
    if (-not $summary.queryAndWorkloadGates -or -not $summary.cpuGate) { exit 1 }
    exit 0
}
if ($Mode -eq 'Acceptance') { $Phase = 'After' }
if ($Phase -eq 'Before' -and (Test-Path -LiteralPath $BaselinePath)) { throw 'Baseline already exists; select a new evidence root.' }
$destination = Join-Path $EvidenceRoot ($Phase.ToLowerInvariant() + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination | Out-Null
$capture = Capture-Evidence $destination
if ($Phase -eq 'Before') { Save-Immutable $BaselinePath $capture }
Write-Host "Capture saved: $destination"
if ($Mode -eq 'Acceptance') {
    $before = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -AsHashtable
    $after = $capture | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable
    $summary = Compare-Evidence $before $after
    Save-Immutable (Join-Path $destination 'comparison.json') $summary
    if (-not $summary.queryAndWorkloadGates -or -not $summary.cpuGate) { exit 1 }
}
