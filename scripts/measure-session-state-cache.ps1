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

function Read-Statistics {
    # SQL reports only fingerprints and classified shapes, never query parameters or bodies.
    $sql = @'
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
       WHEN query ~* '^DISCARD' THEN 'reset'
       WHEN query LIKE '%"TranscriptEntries"%' AND query ~* '\mINSERT\M' THEN 'transcript-insert'
       WHEN query LIKE '%"TranscriptEntries"%' AND (query LIKE '%end_seq%' OR
         (query LIKE '%"TurnEnd"%' AND query LIKE '%EXISTS%')) THEN 'working'
       WHEN query LIKE '%"TranscriptEntries"%' AND query LIKE '%"Uuid"%' AND query LIKE '%ANY%' THEN 'uuid'
       WHEN query LIKE '%"TranscriptEntries"%' AND query ~* '\mSELECT\M' THEN 'transcript-content'
       WHEN query ~ '^SELECT [a-z0-9_]+\."RunnerId", [a-z0-9_]+\."RunnerStoreId", [a-z0-9_]+\."RunnerCwd"[[:space:]]+FROM "AgentSessions"' THEN 'binding'
       WHEN query LIKE '%"Boards"%' AND query LIKE '%"ProjectId"%' THEN 'boards'
       WHEN query LIKE '%"AgentTasks"%' AND query LIKE '%CompletionNote%' AND query ~* '\mUPDATE\M' THEN 'completion-stamp'
       ELSE 'other'
     END AS shape,
     (query LIKE '%"TranscriptEntries"%' AND query ~* '\mSELECT\M' AND query !~* '\mINSERT\M') AS transcript_select
   FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
 ) x), '[]'::json))::text;
'@
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
    return @{ utc = [DateTimeOffset]::UtcNow.ToString('o'); version = $version; runtime = $runtime; postgres = $pg }
}

function Get-WindowDelta {
    param($Start, $End)
    $seconds = ([DateTimeOffset]::Parse($End.utc) - [DateTimeOffset]::Parse($Start.utc)).TotalSeconds
    if ($seconds -lt 180) { throw 'A statistics window is shorter than 180 seconds.' }
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
    if ($Start.runtime.ContainsKey('cache') -and $End.runtime.ContainsKey('cache')) {
        if ($Start.runtime.cache.serverEpoch -ne $End.runtime.cache.serverEpoch) { throw 'Cache epoch changed inside the window.' }
        $cacheDelta = @{}
        foreach ($metric in @('hits','loads','loadFaults','evictions','capacityFallbacks','ingestCalls','committedRows','duplicateBatches')) {
            $cacheDelta[$metric] = [long]$End.runtime.cache[$metric] - [long]$Start.runtime.cache[$metric]
            if ($cacheDelta[$metric] -lt 0) { throw 'Negative cache counter delta invalidated the window.' }
        }
        if ($cacheDelta.ingestCalls -gt 0) { $perIngest = $transcriptReads / $cacheDelta.ingestCalls }
    }
    return @{ seconds = $seconds; serverCores = $serverCpu; postgresContainerCores = $postgresCpu; statements = @($deltas);
        transcriptSelectCalls = $transcriptReads; cacheDelta = $cacheDelta; transcriptSelectsPerIngest = $perIngest }
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
    foreach ($workload in @('idle','activity')) {
        $start = New-Snapshot $context
        # Round trip through JSON produces independent dictionaries, including baseline runtime facts.
        $start = $start | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable
        Save-Immutable (Join-Path $Destination "$workload-start.json") $start
        Wait-Observed $WindowSeconds "$workload window"
        $end = New-Snapshot $context | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable
        Save-Immutable (Join-Path $Destination "$workload-end.json") $end
        $windows += @{ workload = $workload; start = $start; end = $end; delta = Get-WindowDelta $start $end }
    }
    $evidence = @{ schemaVersion = 1; card = $PlanCard; phase = $Phase; round = $Round; canonicalSha = $sha;
        capturedAt = [DateTimeOffset]::UtcNow.ToString('o'); context = $context; windows = $windows }
    Save-Immutable (Join-Path $Destination 'capture.json') $evidence
    return $evidence
}

function Get-Rate {
    param($Delta, [string]$Shape)
    return [double](($Delta.statements | Where-Object shape -eq $Shape | Measure-Object calls -Sum).Sum) / $Delta.seconds
}

function Compare-Evidence {
    param($Before, $After)
    if ($Before.schemaVersion -ne 1 -or $After.schemaVersion -ne 1 -or $Before.phase -ne 'Before' -or $After.phase -ne 'After' -or
        $Before.card -ne $PlanCard -or $After.card -ne $PlanCard -or $Before.context.workloadKey -ne $After.context.workloadKey -or
        $Before.context.openClients -ne $After.context.openClients -or $Before.windows.Count -ne 2 -or $After.windows.Count -ne 2) {
        throw 'Missing or incompatible immutable baseline/after evidence.'
    }
    $comparisons = @(); $allGates = $true
    for ($i = 0; $i -lt 2; $i++) {
        $beforeWindow = $Before.windows[$i]; $afterWindow = $After.windows[$i]
        if ($beforeWindow.workload -ne $afterWindow.workload) { throw 'Workload windows do not match.' }
        $b = Get-WindowDelta $beforeWindow.start $beforeWindow.end
        $a = Get-WindowDelta $afterWindow.start $afterWindow.end
        foreach ($point in @($beforeWindow.start, $beforeWindow.end, $afterWindow.start, $afterWindow.end)) {
            if ($point.runtime.liveSessions -lt 10 -or $point.runtime.pool.noResetOnClose -ne $false) { $allGates = $false }
        }
        if ($beforeWindow.start.runtime.liveSessions -ne $afterWindow.start.runtime.liveSessions -or
            $beforeWindow.start.runtime.unknownSessions -ne $afterWindow.start.runtime.unknownSessions) { $allGates = $false }
        $workingBefore = Get-Rate $b 'working'; $workingAfter = Get-Rate $a 'working'
        $bindingBefore = Get-Rate $b 'binding'; $bindingAfter = Get-Rate $a 'binding'
        $workingGate = $workingBefore -gt 0 -and $workingAfter -lt $workingBefore
        if ($Round -eq 'R3') { $workingGate = $workingBefore -gt 0 -and $workingAfter -le $workingBefore * 0.05 }
        $bindingGate = $Round -ne 'R3' -or ($bindingBefore -gt 0 -and $bindingAfter -le $bindingBefore * 0.05)
        if ($a.cacheDelta -and ($a.cacheDelta.loadFaults -gt 0 -or $a.cacheDelta.capacityFallbacks -gt 0)) { $allGates = $false }
        $allGates = $allGates -and $workingGate -and $bindingGate
        $comparisons += @{ workload = $beforeWindow.workload; workingBefore = $workingBefore; workingAfter = $workingAfter;
            bindingBefore = $bindingBefore; bindingAfter = $bindingAfter; before = $b; after = $a }
    }
    # Median of two samples is their midpoint. Keep raw windows for noise/third-window review.
    $beforeCpu = ($comparisons.before.serverCores | Measure-Object -Average).Average
    $afterCpu = ($comparisons.after.serverCores | Measure-Object -Average).Average
    $beforePg = @($comparisons.before.postgresContainerCores | Sort-Object)
    $afterPg = ($comparisons.after.postgresContainerCores | Measure-Object -Average).Average
    $cpuGate = $afterCpu -lt $beforeCpu -and $afterPg -le $beforePg[-1]
    return @{ round = $Round; queryAndWorkloadGates = $allGates; cpuGate = $cpuGate; comparisons = $comparisons;
        ordinaryEvidence = 'Required separately: CP query budgets, correctness, residual attribution and matching workload review';
        accepted = $false; disposition = $(if ($allGates -and $cpuGate) { 'Measured gates passed; operator reviews ordinary evidence and workload match' } else { 'Inconclusive or failed; retain evidence and collect another matched window' }) }
}

New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
if ($Mode -eq 'Acceptance') {
    if (-not (Test-Path -LiteralPath $BaselinePath)) { throw 'Acceptance requires a previously saved baseline; the changed build cannot become before.' }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -AsHashtable
    if ($baseline.schemaVersion -ne 1 -or $baseline.phase -ne 'Before' -or $baseline.card -ne $PlanCard -or $baseline.windows.Count -ne 2) {
        throw 'Acceptance baseline is incompatible.'
    }
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
