#requires -Version 5.1
# CARD-0221: find - and with -Execute, kill - agent processes that nobody owns.
# DRY RUN BY DEFAULT: without -Execute it only prints the census and the verdict per process.
#
# A process is a positive only when EVERY applicable Z-rule holds. A task row that says
# Succeeded does not, on its own, imply kill (warm pool, standing agent, CARD-0085 recovery).
#
# Identity (first that answers wins; unresolved = "unidentified", never touched):
#   I1  ancestor chain hits a runner pid/hostPid -> that session (runner-claimed)
#   I2  ancestor is Antiphon.PtyHost.exe with a manifest -> the manifest's sessionId
#   I3  --session-id <guid> on the command line (NOT --resume; Claude forks ids)
#   I4  --name <slug> -> Agents.Slug/Name -> PersistentSessionId
#   I5  cwd ending \card-task-<8hex> -> the task -> AgentSessionId
#
# Pre-filters (never candidates, printed under "ignored"):
#   executable path under WindowsApps\
#   ancestor chain reaches WindowsTerminal.exe / explorer.exe / Code.exe / rider64.exe /
#   ssh / sshd BEFORE any Antiphon.PtyHost.exe / herdr parent (operator-launched)
#
# Rules:
#   Z1  agent-shaped (claude.exe/grok.exe/codex.exe) and passed the pre-filters
#   Z2  identity resolved (I1-I5) to a session id S, and the DB answered for S
#   Z3  pid-reuse: process start >= AgentSessions.StartedAt of S minus 5 s
#   Z4  class from the rows:
#         A PoolExpired      - IsPoolDelegate, no open task, newest CompletedAt older than
#                              -MinDoneMinutes, S is Starting/Running
#         B ReconcilerOwned  - S is Stopped/Failed AND runner-claimed (I1). Never acted on.
#         C EndedButAlive    - S is Stopped/Failed, EndedAt older than -MinDoneMinutes,
#                              not runner-claimed (I2-I5), and Z5 holds
#         D Unclaimed        - no row resolves. Report only (CARD-0056)
#   Z5  (class C only) newest mtime of the session transcript dir and its ansi log is
#       older than -QuietHours
#   Z6  process is older than -MinDoneMinutes
#   Z7  -Execute names the class (-Class PoolExpired, or PoolExpired,EndedButAlive)
#
# Kill path: the most coherent path that still exists.
#   1. Class A, runner-claimed: POST {ServerUrl}/api/sessions/{S}/kill
#      (fallback POST {RunnerUrl}/sessions/{S}/kill if the server does not answer)
#   2. Class A, runner-unclaimed: runner kill 404s; fall through to 3
#   3. Class C or a class-A fall-through: taskkill /T /F from the topmost Antiphon-shaped
#      ancestor. Manifest left for the runner's next adoption pass.
#   4. Verify: leaf pid gone within -KillVerifySeconds (20); exit 1 otherwise
#
# -MinWorkingSetGB is REPORT-ONLY. It never feeds a class decision.
#
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -File scripts/reap-zombie-agents.ps1
#   pwsh -File scripts/reap-zombie-agents.ps1 -Execute
#   pwsh -File scripts/reap-zombie-agents.ps1 -Execute -Class PoolExpired -Limit 5
#
# Exit codes:
#   0  dry run with no positives, or every kill was verified
#   1  a kill was requested but the leaf pid is still alive afterwards
#   2  a prerequisite could not be read (runner unreachable, database did not answer)
#   3  dry run found positives (the schedule notifies on this)
[CmdletBinding()]
param(
    [switch]$Execute,

    [string]$RunnerUrl = 'http://localhost:17204',
    [string]$ServerUrl = 'http://localhost:17202',

    [string]$SessionLogPath = 'C:\logs\antiphon\session-runner',

    [string]$PgContainer = 'antiphon-postgres',
    [string]$PgUser = 'antiphon',
    [string]$PgDatabase = 'antiphon',

    [string]$Class = 'PoolExpired',

    [int]$QuietHours = 6,

    [int]$MinDoneMinutes = 120,

    [double]$MinWorkingSetGB = 0,

    [int]$PidReuseToleranceSec = 5,

    [int]$KillVerifySeconds = 20,

    [int]$Limit = 0,

    [string]$ReportPath = '',

    [string]$ProcessesJson = '',
    [string]$RunnerJson = '',
    [string]$DbJson = '',
    [string]$Now = '',
    [scriptblock]$HttpShim,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$RunnerUrl = $RunnerUrl.TrimEnd('/')
$ServerUrl = $ServerUrl.TrimEnd('/')
$script:PassThruEnabled = [bool]$PassThru
$injected = $false
if ($ProcessesJson -or $RunnerJson -or $DbJson) { $injected = $true }

# Injection never mutates the live machine. -Execute is still accepted so tests can assert
# that class B produces no kill call; HTTP goes through -HttpShim and taskkill is recorded.
if ($injected -and $Execute -and -not $HttpShim) {
    Write-Host 'Injection is set without -HttpShim; -Execute is ignored (dry run only).'
    $Execute = $false
}

$script:killCalls = New-Object System.Collections.Generic.List[object]
$script:nowUtc = [datetime]::UtcNow
if ($Now) {
    $script:nowUtc = [datetime]::SpecifyKind(([datetime]::Parse($Now)).ToUniversalTime(), 'Utc')
}

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ReportPath) {
    $ReportPath = Join-Path $repoRoot 'logs'
    $ReportPath = Join-Path $ReportPath 'zombie-agents'
}

function Convert-ToInt([object]$Value) {
    if ($null -eq $Value -or $Value -eq '') { return 0 }
    if ($Value -is [int]) { return [int]$Value }
    if ($Value -is [long]) { return [int]$Value }
    if ($Value -is [System.Array] -and $Value.Length -gt 0) { return (Convert-ToInt $Value[0]) }
    $n = 0
    [void][int]::TryParse([string]$Value, [ref]$n)
    return $n
}

function Convert-ToUtc([object]$Value) {
    if ($null -eq $Value -or $Value -eq '') { return $null }
    if ($Value -is [datetime]) {
        if ($Value.Kind -eq [datetimekind]::Utc) { return $Value }
        return $Value.ToUniversalTime()
    }
    try {
        return [datetime]::SpecifyKind(([datetime]::Parse([string]$Value)).ToUniversalTime(), 'Utc')
    } catch {
        return $null
    }
}

function Get-StatusName([object]$Value, [hashtable]$Map) {
    if ($null -eq $Value) { return '' }
    $s = [string]$Value
    if ($Map.ContainsKey($s)) { return [string]$Map[$s] }
    return $s
}

$sessionStatusMap = @{
    '0' = 'Created'; '1' = 'Starting'; '2' = 'Running'; '3' = 'Stopping'; '4' = 'Stopped'; '5' = 'Failed'
    'Created' = 'Created'; 'Starting' = 'Starting'; 'Running' = 'Running'
    'Stopping' = 'Stopping'; 'Stopped' = 'Stopped'; 'Failed' = 'Failed'
}
$agentStatusMap = @{
    '0' = 'Idle'; '1' = 'Ready'; '2' = 'Running'; '3' = 'WaitingForHumanReview'
    '4' = 'Stopped'; '5' = 'Disconnected'; '6' = 'Failed'
    'Idle' = 'Idle'; 'Ready' = 'Ready'; 'Running' = 'Running'
    'WaitingForHumanReview' = 'WaitingForHumanReview'; 'Stopped' = 'Stopped'
    'Disconnected' = 'Disconnected'; 'Failed' = 'Failed'
}
$taskStatusMap = @{
    '0' = 'Queued'; '1' = 'Dispatched'; '2' = 'Working'; '3' = 'Blocked'
    '4' = 'Succeeded'; '5' = 'Failed'; '6' = 'Canceled'
    'Queued' = 'Queued'; 'Dispatched' = 'Dispatched'; 'Working' = 'Working'
    'Blocked' = 'Blocked'; 'Succeeded' = 'Succeeded'; 'Failed' = 'Failed'; 'Canceled' = 'Canceled'
}
$kindMap = @{
    '0' = 'Raw'; '1' = 'ClaudeCode'; '2' = 'Codex'; '3' = 'OpenCode'; '4' = 'Grok'
    'Raw' = 'Raw'; 'ClaudeCode' = 'ClaudeCode'; 'Codex' = 'Codex'
    'OpenCode' = 'OpenCode'; 'Grok' = 'Grok'
}

$openTask = @{ Queued = $true; Dispatched = $true; Working = $true; Blocked = $true }
$liveSession = @{ Starting = $true; Running = $true }
$terminalSession = @{ Stopped = $true; Failed = $true }

function Invoke-Http {
    param(
        [string]$Method,
        [string]$Uri,
        [int]$TimeoutSec = 15
    )
    $script:killCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
    if ($HttpShim) {
        return & $HttpShim -Method $Method -Uri $Uri -Headers @{} -Body $null
    }
    if ($injected) {
        throw "injection mode has no HttpShim for $Method $Uri"
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -TimeoutSec $TimeoutSec
}

function Complete-Run {
    param([int]$Code, [string]$Message)
    if ($Message) { Write-Host $Message }
    $result = New-Object psobject -Property @{
        ExitCode      = $Code
        ReportPath    = $script:reportFile
        Positives     = $script:positives
        Ignored       = $script:ignored
        Unidentified  = $script:unidentified
        Rows          = $script:rows
        ClassCounts   = $script:classCounts
        KillCalls     = $script:killCalls.ToArray()
    }
    if ($script:PassThruEnabled) { return $result }
    exit $Code
}

$script:reportFile = $null
$script:positives = @()
$script:ignored = @()
$script:unidentified = @()
$script:rows = @()
$script:classCounts = [pscustomobject]@{
    PoolExpired      = 0
    ReconcilerOwned  = 0
    EndedButAlive    = 0
    Unclaimed        = 0
    Ignored          = 0
    Unidentified     = 0
}

# ---- class filter -------------------------------------------------------------------------

$executeClasses = @{}
foreach ($part in @($Class -split '[,; ]')) {
    $n = $part.Trim()
    if ($n) { $executeClasses[$n] = $true }
}

# ---- prerequisites ------------------------------------------------------------------------

$runnerSessions = @()
if ($RunnerJson) {
    if ($RunnerJson -eq 'UNREACHABLE' -or -not (Test-Path -LiteralPath $RunnerJson)) {
        return (Complete-Run -Code 2 -Message "ABORT: session-runner did not answer GET /sessions (injected RunnerJson unreachable)")
    }
    $runnerRaw = Get-Content -LiteralPath $RunnerJson -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runnerRaw.PSObject.Properties.Name -contains 'unreachable' -and $runnerRaw.unreachable) {
        return (Complete-Run -Code 2 -Message "ABORT: session-runner did not answer GET /sessions (injected)")
    }
    if ($runnerRaw -is [System.Array]) { $runnerSessions = @($runnerRaw) }
    elseif ($runnerRaw.sessions) { $runnerSessions = @($runnerRaw.sessions) }
    else { $runnerSessions = @($runnerRaw) }
} else {
    try {
        $runnerSessions = @(Invoke-RestMethod -Method GET -Uri "$RunnerUrl/sessions" -TimeoutSec 15)
    } catch {
        return (Complete-Run -Code 2 -Message ("ABORT: session-runner at {0} did not answer GET /sessions: {1}" -f $RunnerUrl, $_.Exception.Message))
    }
}

$db = $null
if ($DbJson) {
    if ($DbJson -eq 'UNREACHABLE' -or -not (Test-Path -LiteralPath $DbJson)) {
        return (Complete-Run -Code 2 -Message "ABORT: the database did not answer (injected DbJson unreachable)")
    }
    $db = Get-Content -LiteralPath $DbJson -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($db.PSObject.Properties.Name -contains 'unreachable' -and $db.unreachable) {
        return (Complete-Run -Code 2 -Message "ABORT: the database did not answer (injected)")
    }
} else {
    $sql = @'
SELECT json_build_object(
  'sessions', COALESCE((SELECT json_agg(json_build_object(
      'id', "Id", 'status', "Status", 'startedAt', "StartedAt", 'endedAt', "EndedAt",
      'cwd', "Cwd", 'agentKind', "AgentKind"
    )) FROM "AgentSessions"), '[]'::json),
  'agents', COALESCE((SELECT json_agg(json_build_object(
      'id', "Id", 'name', "Name", 'slug', "Slug", 'isPoolDelegate', "IsPoolDelegate",
      'status', "Status", 'persistentSessionId', "PersistentSessionId",
      'workingDirectory', "WorkingDirectory"
    )) FROM "Agents"), '[]'::json),
  'tasks', COALESCE((SELECT json_agg(json_build_object(
      'id', "Id", 'agentId', "AgentId", 'agentSessionId', "AgentSessionId",
      'status', "Status", 'completedAt', "CompletedAt", 'workspace', "Workspace",
      'workingDirectory', "WorkingDirectory", 'worktreePath', "WorktreePath"
    )) FROM "AgentTasks"), '[]'::json)
);
'@
    $dbRows = & docker exec $PgContainer psql -U $PgUser -d $PgDatabase -At -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ABORT: the database did not answer (docker exec $PgContainer psql ... exit $LASTEXITCODE):"
        Write-Host ($dbRows | Out-String)
        return (Complete-Run -Code 2 -Message '')
    }
    $dbText = (@($dbRows) | ForEach-Object { [string]$_ }) -join ''
    try {
        $db = $dbText | ConvertFrom-Json
    } catch {
        return (Complete-Run -Code 2 -Message ("ABORT: the database did not answer (JSON parse): {0}" -f $_.Exception.Message))
    }
}

$dbSessions = @{}
foreach ($s in @($db.sessions)) {
    if ($null -eq $s) { continue }
    $id = ([string]$s.id).ToLowerInvariant()
    $dbSessions[$id] = [pscustomobject]@{
        Id             = $id
        Status         = Get-StatusName $s.status $sessionStatusMap
        StartedAt      = Convert-ToUtc $s.startedAt
        EndedAt        = Convert-ToUtc $s.endedAt
        Cwd            = [string]$s.cwd
        AgentKind      = Get-StatusName $s.agentKind $kindMap
        TranscriptMtime = Convert-ToUtc $s.transcriptMtime
        AnsiMtime       = Convert-ToUtc $s.ansiMtime
    }
}

$dbAgents = @()
$agentBySession = @{}
$agentBySlug = @{}
foreach ($a in @($db.agents)) {
    if ($null -eq $a) { continue }
    $row = [pscustomobject]@{
        Id                   = [string]$a.id
        Name                 = [string]$a.name
        Slug                 = [string]$a.slug
        IsPoolDelegate       = [bool]$a.isPoolDelegate
        Status               = Get-StatusName $a.status $agentStatusMap
        PersistentSessionId  = ([string]$a.persistentSessionId).ToLowerInvariant()
        WorkingDirectory     = [string]$a.workingDirectory
    }
    $dbAgents += $row
    if ($row.PersistentSessionId) { $agentBySession[$row.PersistentSessionId] = $row }
    if ($row.Slug) { $agentBySlug[$row.Slug.ToLowerInvariant()] = $row }
    if ($row.Name) { $agentBySlug[$row.Name.ToLowerInvariant()] = $row }
}

$dbTasks = @()
$taskBySession = @{}
$tasksByAgent = @{}
foreach ($t in @($db.tasks)) {
    if ($null -eq $t) { continue }
    $row = [pscustomobject]@{
        Id              = [string]$t.id
        AgentId         = [string]$t.agentId
        AgentSessionId  = ([string]$t.agentSessionId).ToLowerInvariant()
        Status          = Get-StatusName $t.status $taskStatusMap
        CompletedAt     = Convert-ToUtc $t.completedAt
        Workspace       = [string]$t.workspace
        WorkingDirectory = [string]$t.workingDirectory
        WorktreePath    = [string]$t.worktreePath
    }
    $dbTasks += $row
    if ($row.AgentSessionId) { $taskBySession[$row.AgentSessionId] = $row }
    if ($row.AgentId) {
        if (-not $tasksByAgent.ContainsKey($row.AgentId)) { $tasksByAgent[$row.AgentId] = @() }
        $tasksByAgent[$row.AgentId] = @($tasksByAgent[$row.AgentId]) + $row
    }
}

$manifestsByHostPid = @{}
$manifestDir = Join-Path $SessionLogPath 'pty-hosts'
$manifestDir = Join-Path $manifestDir 'manifests'
if ($db.manifests) {
    foreach ($m in @($db.manifests)) {
        if ($null -eq $m) { continue }
        $hp = Convert-ToInt $m.hostPid
        if ($hp -gt 0) {
            $manifestsByHostPid[$hp] = [string]$m.sessionId
        }
    }
} elseif (-not $injected -and (Test-Path -LiteralPath $manifestDir)) {
    foreach ($file in @(Get-ChildItem -LiteralPath $manifestDir -File -Filter *.json -ErrorAction SilentlyContinue)) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $raw) { continue }
        $sidM = [regex]::Match($raw, '"sessionId"\s*:\s*"([^"]+)"')
        $pidM = [regex]::Match($raw, '"hostPid"\s*:\s*([0-9]+)')
        if ($sidM.Success -and $pidM.Success) {
            $manifestsByHostPid[[int]$pidM.Groups[1].Value] = $sidM.Groups[1].Value
        }
    }
}

# ---- processes ----------------------------------------------------------------------------

$procByPid = @{}
$procList = @()
if ($ProcessesJson) {
    if (-not (Test-Path -LiteralPath $ProcessesJson)) {
        return (Complete-Run -Code 2 -Message "ABORT: ProcessesJson not found: $ProcessesJson")
    }
    foreach ($p in @(Get-Content -LiteralPath $ProcessesJson -Raw -Encoding UTF8 | ConvertFrom-Json)) {
        $pidVal = Convert-ToInt $p.processId
        $row = [pscustomobject]@{
            ProcessId        = $pidVal
            ParentProcessId  = (Convert-ToInt $p.parentProcessId)
            Name             = [string]$p.name
            ExecutablePath   = [string]$p.executablePath
            CommandLine      = [string]$p.commandLine
            Cwd              = [string]$p.cwd
            CreationDate     = Convert-ToUtc $p.creationDate
            WorkingSetSize   = [int64]$p.workingSetSize
            CpuDeltaPercent  = $p.cpuDeltaPercent
            KernelModeTime   = 0
            UserModeTime     = 0
        }
        $procByPid[$pidVal] = $row
        $procList += $row
    }
} else {
    $sample1 = @{}
    foreach ($p in Get-CimInstance Win32_Process) {
        $pidVal = Convert-ToInt $p.ProcessId
        $row = [pscustomobject]@{
            ProcessId        = $pidVal
            ParentProcessId  = (Convert-ToInt $p.ParentProcessId)
            Name             = [string]$p.Name
            ExecutablePath   = [string]$p.ExecutablePath
            CommandLine      = [string]$p.CommandLine
            Cwd              = ''
            CreationDate     = $null
            WorkingSetSize   = [int64]$p.WorkingSetSize
            CpuDeltaPercent  = $null
            KernelModeTime   = [int64]$p.KernelModeTime
            UserModeTime     = [int64]$p.UserModeTime
        }
        if ($p.CreationDate) {
            $row.CreationDate = ([datetime]$p.CreationDate).ToUniversalTime()
        }
        $procByPid[$pidVal] = $row
        $procList += $row
        $sample1[$pidVal] = ([int64]$p.KernelModeTime) + ([int64]$p.UserModeTime)
    }
    Start-Sleep -Seconds 5
    $cores = [math]::Max(1, [int]$env:NUMBER_OF_PROCESSORS)
    $interval100ns = 5L * 10000000L
    foreach ($p in Get-CimInstance Win32_Process) {
        $pidVal = [int]$p.ProcessId
        if (-not $sample1.ContainsKey($pidVal)) { continue }
        $delta = (([int64]$p.KernelModeTime) + ([int64]$p.UserModeTime)) - $sample1[$pidVal]
        if ($delta -lt 0) { $delta = 0 }
        $pct = [math]::Round(($delta / [double]$interval100ns) * 100.0 / $cores, 1)
        if ($procByPid.ContainsKey($pidVal)) { $procByPid[$pidVal].CpuDeltaPercent = $pct }
    }
}

function Get-AncestorChain($LeafPid) {
    $chain = @()
    $seen = @{}
    $cur = Convert-ToInt $LeafPid
    while ($cur -gt 0 -and -not $seen.ContainsKey($cur)) {
        $seen[$cur] = $true
        $chain += $cur
        if (-not $procByPid.ContainsKey($cur)) { break }
        $cur = Convert-ToInt $procByPid[$cur].ParentProcessId
    }
    return $chain
}

function Get-ProcName($PidVal) {
    $id = Convert-ToInt $PidVal
    if ($procByPid.ContainsKey($id)) { return [string]$procByPid[$id].Name }
    return ''
}

function Test-AgentShaped([string]$Name) {
    $n = $Name.ToLowerInvariant()
    return ($n -eq 'claude.exe' -or $n -eq 'claude' -or $n -eq 'grok.exe' -or $n -eq 'grok' -or $n -eq 'codex.exe' -or $n -eq 'codex')
}

function Test-AntiphonParent([string]$Name) {
    $n = $Name.ToLowerInvariant()
    return ($n -eq 'antiphon.ptyhost.exe' -or $n -eq 'herdr.exe' -or $n -eq 'herdr')
}

function Test-OperatorParent([string]$Name) {
    $n = $Name.ToLowerInvariant()
    if ($n -eq 'windowsterminal.exe' -or $n -eq 'explorer.exe' -or $n -eq 'code.exe' -or $n -eq 'rider64.exe') { return $true }
    if ($n -eq 'ssh.exe' -or $n -eq 'sshd.exe' -or $n -eq 'ssh' -or $n -eq 'sshd') { return $true }
    return $false
}

function Get-JsonProp($Obj, [string]$Name) {
    if ($null -eq $Obj) { return $null }
    $p = $Obj.PSObject.Properties[$Name]
    if ($p) { return $p.Value }
    return $null
}

$runnerByPid = @{}
foreach ($s in @($runnerSessions)) {
    if ($null -eq $s) { continue }
    $rawSid = Get-JsonProp $s 'sessionId'
    if ($null -eq $rawSid) { $rawSid = Get-JsonProp $s 'SessionId' }
    if ($rawSid -is [System.Array]) { continue }
    $sid = [string]$rawSid
    if (-not $sid -or $sid.Contains(' ')) { continue }
    $childPid = Convert-ToInt (Get-JsonProp $s 'pid')
    if ($childPid -le 0) { $childPid = Convert-ToInt (Get-JsonProp $s 'Pid') }
    $hostPid = Convert-ToInt (Get-JsonProp $s 'hostPid')
    if ($hostPid -le 0) { $hostPid = Convert-ToInt (Get-JsonProp $s 'HostPid') }
    $status = [string](Get-JsonProp $s 'status')
    if (-not $status) { $status = [string](Get-JsonProp $s 'Status') }
    $dto = [pscustomobject]@{ SessionId = $sid.ToLowerInvariant(); Status = $status; Pid = $childPid; HostPid = $hostPid }
    if ($childPid -gt 0) { $runnerByPid[$childPid] = $dto }
    if ($hostPid -gt 0) { $runnerByPid[$hostPid] = $dto }
}

function Get-OwnerAgent([string]$SessionId) {
    if ($agentBySession.ContainsKey($SessionId)) { return $agentBySession[$SessionId] }
    if ($taskBySession.ContainsKey($SessionId)) {
        $aid = $taskBySession[$SessionId].AgentId
        foreach ($a in $dbAgents) { if ($a.Id -eq $aid) { return $a } }
    }
    return $null
}

function Test-AgentHasOpenTask($Agent) {
    if ($null -eq $Agent) { return $false }
    if (-not $tasksByAgent.ContainsKey($Agent.Id)) { return $false }
    foreach ($t in @($tasksByAgent[$Agent.Id])) {
        if ($openTask.ContainsKey($t.Status)) { return $true }
    }
    return $false
}

function Get-NewestCompletedAt($Agent) {
    $best = $null
    if ($null -eq $Agent) { return $null }
    if (-not $tasksByAgent.ContainsKey($Agent.Id)) { return $null }
    foreach ($t in @($tasksByAgent[$Agent.Id])) {
        if ($null -eq $t.CompletedAt) { continue }
        if ($null -eq $best -or $t.CompletedAt -gt $best) { $best = $t.CompletedAt }
    }
    return $best
}

function Encode-ClaudeProjectDir([string]$Cwd) {
    if (-not $Cwd) { return '' }
    $chars = $Cwd.ToCharArray()
    for ($i = 0; $i -lt $chars.Length; $i++) {
        $c = $chars[$i]
        if (-not (($c -ge 'A' -and $c -le 'Z') -or ($c -ge 'a' -and $c -le 'z') -or ($c -ge '0' -and $c -le '9'))) {
            $chars[$i] = [char]'-'
        }
    }
    return -join $chars
}

function Get-ActivityMtime([string]$SessionId, $DbSession, [string]$Kind) {
    if ($null -ne $DbSession -and $null -ne $DbSession.TranscriptMtime) { }
    $best = $null
    if ($null -ne $DbSession) {
        if ($null -ne $DbSession.TranscriptMtime) { $best = $DbSession.TranscriptMtime }
        if ($null -ne $DbSession.AnsiMtime -and ($null -eq $best -or $DbSession.AnsiMtime -gt $best)) {
            $best = $DbSession.AnsiMtime
        }
        if ($best) { return $best }
    }
    if ($injected) { return $null }
    $ansi = Join-Path $SessionLogPath ($SessionId.Replace('-', '') + '.ansi.log')
    if (Test-Path -LiteralPath $ansi) {
        $best = ([datetime](Get-Item -LiteralPath $ansi).LastWriteTimeUtc)
    }
    if ($Kind -eq 'ClaudeCode' -and $DbSession -and $DbSession.Cwd) {
        $root = Join-Path $env:USERPROFILE '.claude'
        $root = Join-Path $root 'projects'
        $proj = Join-Path $root (Encode-ClaudeProjectDir $DbSession.Cwd)
        if (Test-Path -LiteralPath $proj) {
            foreach ($f in @(Get-ChildItem -LiteralPath $proj -Filter *.jsonl -ErrorAction SilentlyContinue)) {
                $mt = [datetime]$f.LastWriteTimeUtc
                if ($null -eq $best -or $mt -gt $best) { $best = $mt }
            }
        }
    }
    return $best
}

function Get-SessionIdFromCommandLine([string]$Cmd) {
    if (-not $Cmd) { return $null }
    $m = [regex]::Match($Cmd, '--session-id(?:\s+|=)([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})')
    if ($m.Success) { return $m.Groups[1].Value.ToLowerInvariant() }
    return $null
}

function Get-NameFromCommandLine([string]$Cmd) {
    if (-not $Cmd) { return $null }
    $m = [regex]::Match($Cmd, '--name(?:\s+|=)([^\s"]+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Get-CardTaskLeaf([string]$PathValue) {
    if (-not $PathValue) { return $null }
    $m = [regex]::Match($PathValue, 'card-task-([0-9a-fA-F]{8})(?:\\|\"|$|/)')
    if ($m.Success) { return ('card-task-' + $m.Groups[1].Value.ToLowerInvariant()) }
    $leaf = [System.IO.Path]::GetFileName($PathValue.TrimEnd('\', '/'))
    if ($leaf -match '^card-task-[0-9a-fA-F]{8}$') { return $leaf.ToLowerInvariant() }
    return $null
}

function Resolve-Identity($Proc, $Chain) {
    foreach ($anc in $Chain) {
        $ancId = Convert-ToInt $anc
        if ($runnerByPid.ContainsKey($ancId)) {
            $hit = $runnerByPid[$ancId]
            return [pscustomobject]@{ Method = 'I1'; SessionId = $hit.SessionId; RunnerClaimed = $true }
        }
    }
    foreach ($anc in $Chain) {
        $ancId = Convert-ToInt $anc
        $nm = Get-ProcName $ancId
        if ($nm -ieq 'Antiphon.PtyHost.exe') {
            if ($manifestsByHostPid.ContainsKey($ancId)) {
                return [pscustomobject]@{
                    Method = 'I2'
                    SessionId = ([string]$manifestsByHostPid[$ancId]).ToLowerInvariant()
                    RunnerClaimed = $false
                }
            }
        }
    }
    $fromCmd = Get-SessionIdFromCommandLine $Proc.CommandLine
    if ($fromCmd) {
        return [pscustomobject]@{ Method = 'I3'; SessionId = $fromCmd; RunnerClaimed = $false }
    }
    $nmArg = Get-NameFromCommandLine $Proc.CommandLine
    if ($nmArg -and $agentBySlug.ContainsKey($nmArg.ToLowerInvariant())) {
        $ag = $agentBySlug[$nmArg.ToLowerInvariant()]
        if ($ag.PersistentSessionId) {
            return [pscustomobject]@{ Method = 'I4'; SessionId = $ag.PersistentSessionId; RunnerClaimed = $false }
        }
    }
    $leaf = Get-CardTaskLeaf $Proc.Cwd
    if (-not $leaf) { $leaf = Get-CardTaskLeaf $Proc.CommandLine }
    if ($leaf) {
        foreach ($t in $dbTasks) {
            $tLeaf = Get-CardTaskLeaf $t.WorktreePath
            if (-not $tLeaf) { $tLeaf = Get-CardTaskLeaf $t.WorkingDirectory }
            if ($tLeaf -eq $leaf -and $t.AgentSessionId) {
                return [pscustomobject]@{ Method = 'I5'; SessionId = $t.AgentSessionId; RunnerClaimed = $false }
            }
        }
    }
    return $null
}

function Test-OperatorLaunched($Chain) {
    foreach ($anc in $Chain) {
        $nm = Get-ProcName ([int]$anc)
        if (Test-AntiphonParent $nm) { return $false }
        if (Test-OperatorParent $nm) { return $true }
    }
    return $false
}

function Get-TopAntiphonAncestor($Chain) {
    $ids = @($Chain)
    if ($ids.Count -eq 0) { return 0 }
    $last = Convert-ToInt $ids[0]
    foreach ($anc in $ids) {
        $id = Convert-ToInt $anc
        $nm = Get-ProcName $id
        if (Test-AntiphonParent $nm) { return $id }
        if ($nm -ieq 'cmd.exe' -or $nm -ieq 'node.exe') { $last = $id }
    }
    return $last
}

function New-Row {
    param($Proc, [string]$IdentityMethod, [string]$SessionId, [string]$ClassName, [string]$RulesFailed, [string]$KillPath, [bool]$RunnerClaimed, $Agent, $DbSession)
    $wsGb = 0
    if ($Proc.WorkingSetSize) { $wsGb = [math]::Round(([double]$Proc.WorkingSetSize) / 1GB, 3) }
    $large = $false
    if ($MinWorkingSetGB -gt 0 -and $wsGb -ge $MinWorkingSetGB) { $large = $true }
    $agentName = ''
    if ($null -ne $Agent) { $agentName = $Agent.Name }
    $dbStatus = ''
    if ($null -ne $DbSession) { $dbStatus = $DbSession.Status }
    $cpu = $Proc.CpuDeltaPercent
    return [pscustomobject]@{
        Pid            = $Proc.ProcessId
        Exe            = $Proc.Name
        Start          = $Proc.CreationDate
        WorkingSetGB   = $wsGb
        CpuDeltaPct    = $cpu
        IdentityMethod = $IdentityMethod
        SessionId      = $SessionId
        DbStatus       = $dbStatus
        Agent          = $agentName
        Class          = $ClassName
        RulesFailed    = $RulesFailed
        KillPath       = $KillPath
        RunnerClaimed  = $RunnerClaimed
        LargeWs        = $large
        TreeKillPid    = 0
    }
}

# ---- classify -----------------------------------------------------------------------------

$ignored = New-Object System.Collections.Generic.List[object]
$unidentified = New-Object System.Collections.Generic.List[object]
$rows = New-Object System.Collections.Generic.List[object]
$positives = New-Object System.Collections.Generic.List[object]

foreach ($proc in $procList) {
    if (-not (Test-AgentShaped $proc.Name)) { continue }

    $chain = @(Get-AncestorChain (Convert-ToInt $proc.ProcessId))
    $pre = New-Object System.Collections.Generic.List[string]
    $exePath = [string]$proc.ExecutablePath
    if ($exePath -and $exePath.ToLowerInvariant().Contains('\windowsapps\')) {
        $pre.Add('WindowsApps') | Out-Null
    }
    if (Test-OperatorLaunched $chain) {
        $pre.Add('operator-launched') | Out-Null
    }
    if ($pre.Count -gt 0) {
        $row = New-Row -Proc $proc -IdentityMethod '' -SessionId '' -ClassName 'Ignored' -RulesFailed ($pre -join '; ') -KillPath '' -RunnerClaimed $false -Agent $null -DbSession $null
        $ignored.Add($row) | Out-Null
        $rows.Add($row) | Out-Null
        continue
    }

    $idn = Resolve-Identity $proc $chain
    if ($null -eq $idn -or -not $idn.SessionId) {
        $row = New-Row -Proc $proc -IdentityMethod '' -SessionId '' -ClassName 'Unclaimed' -RulesFailed 'Z2 identity unresolved' -KillPath '' -RunnerClaimed $false -Agent $null -DbSession $null
        $unidentified.Add($row) | Out-Null
        $rows.Add($row) | Out-Null
        continue
    }

    $sid = $idn.SessionId
    $dbSess = $null
    if ($dbSessions.ContainsKey($sid)) { $dbSess = $dbSessions[$sid] }
    $agent = Get-OwnerAgent $sid
    $failed = New-Object System.Collections.Generic.List[string]
    $className = ''
    $killPath = ''

    if ($null -eq $dbSess) {
        $className = 'Unclaimed'
        $failed.Add('Z2 no AgentSessions row') | Out-Null
        $row = New-Row -Proc $proc -IdentityMethod $idn.Method -SessionId $sid -ClassName $className -RulesFailed ($failed -join '; ') -KillPath '' -RunnerClaimed ([bool]$idn.RunnerClaimed) -Agent $agent -DbSession $null
        $rows.Add($row) | Out-Null
        continue
    }

    if ($null -ne $proc.CreationDate -and $null -ne $dbSess.StartedAt) {
        $floor = $dbSess.StartedAt.AddSeconds(-1 * $PidReuseToleranceSec)
        if ($proc.CreationDate -lt $floor) {
            $failed.Add('Z3 pid-reuse (process start before session StartedAt)') | Out-Null
        }
    }

    $ageOk = $true
    if ($null -ne $proc.CreationDate) {
        $ageMin = ($script:nowUtc - $proc.CreationDate).TotalMinutes
        if ($ageMin -lt $MinDoneMinutes) {
            $ageOk = $false
            $failed.Add(("Z6 process only {0:N0} min old (< {1})" -f $ageMin, $MinDoneMinutes)) | Out-Null
        }
    }

    $isPool = $false
    if ($null -ne $agent) { $isPool = [bool]$agent.IsPoolDelegate }
    $hasOpen = Test-AgentHasOpenTask $agent
    $newestDone = Get-NewestCompletedAt $agent
    $doneOld = $false
    if ($null -ne $newestDone) {
        $doneOld = (($script:nowUtc - $newestDone).TotalMinutes -ge $MinDoneMinutes)
    }

    if ($liveSession.ContainsKey($dbSess.Status) -and $isPool -and -not $hasOpen -and $doneOld) {
        $className = 'PoolExpired'
        if ($idn.RunnerClaimed) { $killPath = 'server' } else { $killPath = 'taskkill' }
    } elseif ($terminalSession.ContainsKey($dbSess.Status) -and $idn.RunnerClaimed) {
        $className = 'ReconcilerOwned'
        $killPath = ''
        if ($dbSess.Status -eq 'Failed') {
            $failed.Add('Z4 reconciler re-adopts Failed (CARD-0056)') | Out-Null
        } else {
            $failed.Add('Z4 reconciler RetryFailedKillAsync owns Stopped') | Out-Null
        }
    } elseif ($terminalSession.ContainsKey($dbSess.Status) -and -not $idn.RunnerClaimed) {
        $endedOld = $false
        if ($null -ne $dbSess.EndedAt) {
            $endedOld = (($script:nowUtc - $dbSess.EndedAt).TotalMinutes -ge $MinDoneMinutes)
        }
        if (-not $endedOld) {
            $failed.Add('Z4 EndedAt younger than -MinDoneMinutes') | Out-Null
            $className = ''
        } else {
            $mtime = Get-ActivityMtime $sid $dbSess $dbSess.AgentKind
            $quiet = $true
            if ($null -ne $mtime) {
                $quietHoursAge = ($script:nowUtc - $mtime).TotalHours
                if ($quietHoursAge -lt $QuietHours) {
                    $quiet = $false
                    $failed.Add(("Z5 activity {0:N1} h ago (< {1} QuietHours)" -f $quietHoursAge, $QuietHours)) | Out-Null
                }
            }
            if ($quiet) {
                $className = 'EndedButAlive'
                $killPath = 'taskkill'
            } else {
                $className = ''
            }
        }
    } else {
        $failed.Add('Z4 not a zombie class (warm/standing/live)') | Out-Null
    }

    $row = New-Row -Proc $proc -IdentityMethod $idn.Method -SessionId $sid -ClassName $className -RulesFailed ($failed -join '; ') -KillPath $killPath -RunnerClaimed ([bool]$idn.RunnerClaimed) -Agent $agent -DbSession $dbSess
    $row.TreeKillPid = Get-TopAntiphonAncestor $chain
    $rows.Add($row) | Out-Null

    $isPositive = ($className -eq 'PoolExpired' -or $className -eq 'EndedButAlive') -and ($failed.Count -eq 0) -and $ageOk
    if ($className -eq 'PoolExpired' -or $className -eq 'EndedButAlive') {
        if ($failed.Count -gt 0) { $isPositive = $false }
    }
    if ($isPositive) { $positives.Add($row) | Out-Null }
}

$script:ignored = @($ignored.ToArray())
$script:unidentified = @($unidentified.ToArray())
$script:rows = @($rows.ToArray())
$script:positives = @($positives.ToArray())

function Count-Class([string]$Name) {
    $n = 0
    foreach ($r in $script:rows) {
        if ($r.Class -eq $Name) { $n++ }
    }
    return $n
}
$script:classCounts = [pscustomobject]@{
    PoolExpired     = (Count-Class 'PoolExpired')
    ReconcilerOwned = (Count-Class 'ReconcilerOwned')
    EndedButAlive   = (Count-Class 'EndedButAlive')
    Unclaimed       = (Count-Class 'Unclaimed')
    Ignored         = $script:ignored.Count
    Unidentified    = $script:unidentified.Count
    Positives       = $script:positives.Count
}

# ---- output -------------------------------------------------------------------------------

Write-Host ("Processes (agent-shaped): {0}   ignored: {1}   unidentified: {2}   positives: {3}" -f `
    @($rows).Count, @($ignored).Count, @($unidentified).Count, @($positives).Count)
Write-Host ("  PoolExpired={0}  ReconcilerOwned={1}  EndedButAlive={2}  Unclaimed={3}" -f `
    $script:classCounts.PoolExpired, $script:classCounts.ReconcilerOwned, $script:classCounts.EndedButAlive, $script:classCounts.Unclaimed)
Write-Host ''
Write-Host '--- census ---'
$rows | Sort-Object Start | Format-Table Pid, Exe, Start, WorkingSetGB, CpuDeltaPct, IdentityMethod, SessionId, DbStatus, Agent, Class, RulesFailed, KillPath, LargeWs -AutoSize | Out-String -Width 280 | Write-Host

if (@($ignored).Count -gt 0) {
    Write-Host '--- ignored (pre-filter) ---'
    $ignored | Format-Table Pid, Exe, RulesFailed -AutoSize | Out-String -Width 200 | Write-Host
}
if (@($unidentified).Count -gt 0) {
    Write-Host '--- unidentified (no I1-I5) ---'
    $unidentified | Format-Table Pid, Exe, RulesFailed -AutoSize | Out-String -Width 200 | Write-Host
}
Write-Host '--- positives ---'
$positives | Sort-Object Start | Format-Table Pid, Exe, Class, IdentityMethod, SessionId, KillPath, Agent -AutoSize | Out-String -Width 220 | Write-Host

# ---- report file --------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $ReportPath)) {
    New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
}
$stamp = $script:nowUtc.ToString('yyyyMMddTHHmmssZ')
$script:reportFile = Join-Path $ReportPath ($stamp + '.json')
$reportRows = New-Object System.Collections.Generic.List[object]
foreach ($r in $script:rows) {
    $startOut = $null
    if ($r.Start) { $startOut = $r.Start.ToString('o') }
    $reportRows.Add((New-Object psobject -Property @{
        pid            = $r.Pid
        exe            = $r.Exe
        start          = $startOut
        workingSetGB   = $r.WorkingSetGB
        cpuDeltaPct    = $r.CpuDeltaPct
        identityMethod = $r.IdentityMethod
        sessionId      = $r.SessionId
        dbStatus       = $r.DbStatus
        agent          = $r.Agent
        'class'        = $r.Class
        rulesFailed    = $r.RulesFailed
        killPath       = $r.KillPath
        largeWs        = $r.LargeWs
        runnerClaimed  = $r.RunnerClaimed
    })) | Out-Null
}
$payload = @{
    generatedAt     = [datetime]::UtcNow.ToString('o')
    now             = $script:nowUtc.ToString('o')
    execute         = [bool]$Execute
    classFilter     = $Class
    quietHours      = $QuietHours
    minDoneMinutes  = $MinDoneMinutes
    minWorkingSetGB = $MinWorkingSetGB
    counts          = $script:classCounts
    rows            = $reportRows.ToArray()
    killCalls       = $script:killCalls.ToArray()
    exitCode        = 0
}

# ---- execute ------------------------------------------------------------------------------

$failedKills = 0
$killed = 0
if ($Execute) {
    $toKill = @($positives | Sort-Object Start)
    if ($Limit -gt 0 -and $toKill.Count -gt $Limit) {
        Write-Host ("-Limit {0}: killing the {0} oldest of {1} positives this run." -f $Limit, $toKill.Count)
        $toKill = @($toKill | Select-Object -First $Limit)
    }
    foreach ($o in $toKill) {
        if (-not $executeClasses.ContainsKey($o.Class)) {
            Write-Host ("skip         pid {0}  class {1} not in -Class {2}" -f $o.Pid, $o.Class, $Class)
            continue
        }
        $ok = $false
        $pathUsed = $o.KillPath
        if ($o.Class -eq 'PoolExpired' -and $o.RunnerClaimed) {
            try {
                $null = Invoke-Http -Method POST -Uri ($ServerUrl + '/api/sessions/' + $o.SessionId + '/kill') -TimeoutSec 30
                $ok = $true
                $pathUsed = 'server'
            } catch {
                Write-Host ("server kill failed  {0}  {1}  falling back to runner" -f $o.SessionId, $_.Exception.Message)
                try {
                    $null = Invoke-Http -Method POST -Uri ($RunnerUrl + '/sessions/' + $o.SessionId + '/kill') -TimeoutSec 30
                    $ok = $true
                    $pathUsed = 'runner'
                } catch {
                    Write-Host ("runner kill failed  {0}  {1}  falling through to taskkill" -f $o.SessionId, $_.Exception.Message)
                    $pathUsed = 'taskkill'
                }
            }
        } elseif ($o.Class -eq 'PoolExpired' -and -not $o.RunnerClaimed) {
            $pathUsed = 'taskkill'
        }

        if ($pathUsed -eq 'taskkill') {
            $treePid = $o.TreeKillPid
            if (-not $treePid) { $treePid = $o.Pid }
            $script:killCalls.Add([pscustomobject]@{ Method = 'TASKKILL'; Uri = ('pid:' + $treePid) }) | Out-Null
            if (-not $injected) {
                & taskkill /T /F /PID $treePid 2>&1 | Out-Null
            }
            $ok = $true
        }

        if (-not $ok) { $failedKills++; continue }

        $gone = $injected
        if (-not $injected) {
            $deadline = [datetime]::UtcNow.AddSeconds($KillVerifySeconds)
            while ([datetime]::UtcNow -lt $deadline) {
                if ($null -eq (Get-Process -Id $o.Pid -ErrorAction SilentlyContinue)) { $gone = $true; break }
                Start-Sleep -Milliseconds 500
            }
        }
        if ($gone) {
            $killed++
            Write-Host ("killed       pid {0}  {1}  {2}  {3}" -f $o.Pid, $o.Class, $pathUsed, $o.SessionId)
        } else {
            $failedKills++
            Write-Host ("STILL ALIVE  pid {0}  after {1}s" -f $o.Pid, $KillVerifySeconds)
        }
    }
    Write-Host ''
    Write-Host ("Killed and verified: {0}   failed: {1}   positives: {2}   ignored: {3}" -f `
        $killed, $failedKills, @($positives).Count, @($ignored).Count)
} else {
    Write-Host 'Dry run: nothing was killed. Re-run with -Execute to act on PoolExpired (default -Class).'
}

$exitCode = 0
if ($Execute) {
    if ($failedKills -gt 0) { $exitCode = 1 }
} else {
    if (@($positives).Count -gt 0) { $exitCode = 3 }
}
$payload['exitCode'] = $exitCode
$payload['killCalls'] = $script:killCalls.ToArray()
try {
    ($payload | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $script:reportFile -Encoding UTF8
    Write-Host ("Report: {0}" -f $script:reportFile)
} catch {
    Write-Host ('warning: failed to write report: ' + $_.Exception.Message)
}

return (Complete-Run -Code $exitCode -Message '')
