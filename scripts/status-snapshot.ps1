#requires -Version 5.1
# Raw, read-only status facts. ASCII-only for Windows PowerShell fallback.
# Uses card.ps1/delegate.ps1's ANTIPHON_API and task-token header convention.
# No shared HTTP module exists; requests below are GET only. No fetch, refresh,
# dispatch, slot release, state writer, or output file is invoked.
# -ProjectId (alias -Project) scopes task counts. Omitted: discover a unique
# project from the pipeline; ambiguity is an incomplete observation, never fleet
# counts presented as project counts. -Repository defaults to this checkout.
# Rc paths are optional. The heartbeat normally lives in a separate task directory,
# so pass -RcHeartbeatPath; -RcStateDir alone also tries its heartbeat file.
# Exit 0: all requested facts read. Exit 2: partial facts, errors included in output.
[CmdletBinding()]
param(
    [Alias('Project')][string]$ProjectId,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]*$')][string]$Runner = 'server2',
    [switch]$Json,
    [string]$RcRunLogDir,
    [string]$RcStateDir,
    [string]$RcHeartbeatPath,
    [string]$Repository = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
$problems = New-Object 'System.Collections.Generic.List[string]'
. (Join-Path $PSScriptRoot 'lib/rc-qualification-status.ps1')

function Invoke-Antiphon {
    param([string]$Path)
    try { return Invoke-RestMethod -Method GET -Uri "$api$Path" -Headers $headers -TimeoutSec 20 }
    catch {
        # Never echo an exception body, URL, header or credential helper output.
        $status = $_.Exception.Response.StatusCode
        $problems.Add("GET $Path unavailable (HTTP $status).")
        return $null
    }
}
function Read-JsonFact {
    param([string]$Path)
    $read = Read-RcQualificationRecord -Path $Path
    if ($read.status -ne 'ok') { $problems.Add("$Path unavailable ($($read.reason))."); return $null }
    return $read.record
}
function Read-GitFact {
    param([string]$Arguments, [string]$Label)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.WorkingDirectory = [IO.Path]::GetFullPath($Repository)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    # Only fixed arguments from below; repository paths never enter command text.
    $start.Arguments = '-c credential.helper= -c credential.interactive=false ' + $Arguments
    $start.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $start.EnvironmentVariables['GIT_OPTIONAL_LOCKS'] = '0'
    $child = $null
    try {
        $child = [Diagnostics.Process]::Start($start)
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(20000)) { throw 'git deadline' }
        if (-not $stdout.Wait(1000) -or -not $stderr.Wait(1000) -or $child.ExitCode -ne 0) { throw 'git unavailable' }
        return $stdout.Result.Trim()
    } catch { $problems.Add("$Label unavailable."); return $null }
    finally {
        if ($child) {
            if (-not $child.HasExited) {
                try { $child.Kill($true) } catch { $child.Kill() }
                $null = $child.WaitForExit(5000)
            }
            $child.Dispose()
        }
    }
}

$pipeline = $null
if ([string]::IsNullOrWhiteSpace($ProjectId)) {
    $discovery = Invoke-Antiphon '/api/agent-tasks/pipeline'
    $projects = @($discovery.concurrencyScopes | ForEach-Object { $_.projectId } |
        Where-Object { $_ } | Sort-Object -Unique)
    if ($projects.Count -eq 1) { $ProjectId = [string]$projects[0] }
    else { $problems.Add('Project cannot be resolved uniquely from pipeline; supply -ProjectId.') }
}
if ($ProjectId) {
    $parsedProject = [guid]::Empty
    if (-not [guid]::TryParse($ProjectId, [ref]$parsedProject)) { $problems.Add('ProjectId must be a GUID.') }
    else { $pipeline = Invoke-Antiphon ("/api/agent-tasks/pipeline?projectId=" + $parsedProject.ToString()) }
}
$counts = $null
$details = New-Object 'System.Collections.Generic.List[object]'
if ($null -ne $pipeline -and $null -ne $pipeline.stages) {
    $counts = [ordered]@{ inFlight = 0; queued = 0; blocked = 0 }
    $taskIds = @()
    foreach ($stage in $pipeline.stages) {
        $counts.inFlight += @($stage.inFlight).Count
        $counts.queued += @($stage.queued).Count
        $counts.blocked += @($stage.blocked).Count
        foreach ($row in @($stage.inFlight) + @($stage.queued) + @($stage.blocked)) {
            if ($row.taskId) { $taskIds += [string]$row.taskId }
        }
        foreach ($row in $stage.ready) { if ($row.sourcePlanTaskId) { $taskIds += [string]$row.sourcePlanTaskId } }
    }
    foreach ($id in @($taskIds | Sort-Object -Unique)) {
        $detail = Invoke-Antiphon ("/api/agent-tasks/" + [uri]::EscapeDataString($id))
        if ($null -ne $detail) { $details.Add($detail) }
    }
} else { $problems.Add('Scoped pipeline stages unavailable; task counts unknown.') }
$slots = Invoke-Antiphon ("/api/session-runners/" + [uri]::EscapeDataString($Runner) + '/slots')
$instructions = Invoke-Antiphon '/api/orchestrator-instructions'
$refs = Read-GitFact 'ls-remote --heads origin refs/heads/master refs/heads/rc/*' 'Remote master/rc refs'
$tip = $null
$rcRefs = @()
foreach ($line in @($refs -split "`n")) {
    if ($line -match '^([0-9a-f]{40,64})\s+(refs/heads/.+)$') {
        if ($Matches[2] -eq 'refs/heads/master') { $tip = $Matches[1] }
        else { $rcRefs += [pscustomobject]@{ sha = $Matches[1]; ref = $Matches[2] } }
    }
}
if (-not $tip) { $problems.Add('Remote master tip unknown.') }
$history = Read-GitFact 'log -8 --format=%H%x09%s origin/master' 'Cached origin/master history'

$rc = $null
if ($RcRunLogDir -or $RcStateDir -or $RcHeartbeatPath) {
    $rc = [ordered]@{
        runLogDir = $RcRunLogDir; stateDir = $RcStateDir; trxCount = $null
        scheduledEntries = $null; newestFile = $null; newestFileAgeSeconds = $null
        activeChunk = $null; lastRun = $null; heartbeat = $null; heartbeatAgeSeconds = $null
    }
    if ($RcRunLogDir) {
        try {
            $files = @(Get-ChildItem -LiteralPath $RcRunLogDir -File -Recurse -ErrorAction Stop)
            $rc.trxCount = @($files | Where-Object { $_.Extension -eq '.trx' }).Count
            $newest = $files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            if ($newest) {
                $rc.newestFile = $newest.FullName
                $rc.newestFileAgeSeconds = [Math]::Max(0, ([datetime]::UtcNow - $newest.LastWriteTimeUtc).TotalSeconds)
            }
        } catch { $problems.Add('RcRunLogDir could not be enumerated; TRX count unknown.') }
        $schedule = Read-JsonFact (Join-Path $RcRunLogDir 'dispatch-schedule.json')
        if ($null -ne $schedule -and $null -ne $schedule.entries) { $rc.scheduledEntries = @($schedule.entries).Count }
        # Active identity may live beneath the run's timing root. Refuse ambiguity.
        $activeFiles = @($files | Where-Object { $_.Name -eq 'active-chunk.json' })
        $activePath = $null
        if ($activeFiles.Count -eq 1) { $activePath = $activeFiles[0].FullName }
        elseif ($activeFiles.Count -gt 1) { $problems.Add('Multiple active-chunk records; identity requires owner verification.') }
        if ($activePath) { $rc.activeChunk = Read-JsonFact $activePath }
    }
    if ($RcStateDir) {
        $rc.lastRun = Read-JsonFact (Join-Path $RcStateDir 'last-run.json')
        if (-not $rc.activeChunk -and $rc.lastRun.timingRoot) {
            $rc.activeChunk = Read-JsonFact (Join-Path $rc.lastRun.timingRoot 'active-chunk.json')
        }
        if (-not $RcHeartbeatPath) { $RcHeartbeatPath = Join-Path $RcStateDir 'supervisor-heartbeat.json' }
    }
    if ($RcHeartbeatPath) {
        $rc.heartbeat = Read-JsonFact $RcHeartbeatPath
        $stamp = $rc.heartbeat.observedAtUtc
        if (-not $stamp) { $stamp = $rc.heartbeat.utc }
        if ($stamp) {
            try { $rc.heartbeatAgeSeconds = [Math]::Max(0, ([datetimeoffset]::UtcNow - [datetimeoffset]$stamp).TotalSeconds) }
            catch { $problems.Add('Heartbeat timestamp could not be parsed.') }
        } elseif ($rc.heartbeat) { $problems.Add('Heartbeat timestamp unknown.') }
    }
    if ($RcRunLogDir -and -not $rc.activeChunk) { $problems.Add('Active chunk unknown; inspect the recorded timing root.') }
}
$snapshot = [ordered]@{
    observedAtUtc = [datetime]::UtcNow.ToString('o'); projectId = $ProjectId; runner = $Runner
    counts = $counts; pipeline = $pipeline; taskDetails = @($details.ToArray())
    slots = $slots; orchestratorInstructions = $instructions
    master = [ordered]@{ tip = $tip; cachedOriginMasterLog = $history; historyIsCached = $true }
    rcRefs = @($rcRefs); rc = $rc; errors = @($problems.ToArray())
}
if ($Json) { $snapshot | ConvertTo-Json -Depth 40 }
else {
    "OBSERVED $($snapshot.observedAtUtc) project=$ProjectId runner=$Runner"
    if ($counts) { "TASKS inFlight=$($counts.inFlight) queued=$($counts.queued) blocked=$($counts.blocked)" }
    else { 'TASKS counts unknown' }
    "MASTER tip=$tip (remote); history below is cached origin/master, fetch separately"
    foreach ($key in @('pipeline','taskDetails','slots','orchestratorInstructions','master','rcRefs','rc')) {
        "$key=" + (ConvertTo-Json -InputObject $snapshot[$key] -Depth 40 -Compress)
    }
    foreach ($problem in $problems) { "UNVERIFIED $problem" }
}
if ($problems.Count -gt 0) { exit 2 }
