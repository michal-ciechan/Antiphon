#requires -Version 5.1
# CARD-0295: delete one-off gym-stat agents minted by raw POST /api/agents, then archive
# the empty one-off projects that hid their boards. DRY RUN BY DEFAULT: without -Execute
# it only prints the census and the verdict per row.
#
# Allowlist is NAME-based and re-derived live from GET /api/agents + /api/boards +
# /api/projects. Guids are never frozen in this file.
#
#   Cohort A  33 named one-off agents (each on its own same-named board, 0 cards).
#             DELETE the agent, then POST /api/projects/{id}/archive once that
#             project's agents are gone.
#   Cohort B  7 named children on the real Gym Stat board. DELETE the agent only.
#             Never archive Gym Stat / gym-stat.
#
# Protected and skipped (printed under "protected"):
#   status Running, alwaysOn, liveSession != null
#   board Gym Stat / project gym-stat AND not Cohort B
#   keep-set names: Gym Stat Orchestrator, gym-stat-addonpin-plan, gym-stat-setupmockups
#
# Never DELETE /api/boards. Never DELETE /api/projects. Never PATCH. Never POST /stop.
# Never touch a filesystem path.
#
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -File scripts/cleanup-gym-stat-one-off-agents.ps1
#   pwsh -File scripts/cleanup-gym-stat-one-off-agents.ps1 -Execute
#
# Exit codes:
#   0  dry run completed, or every -Execute call succeeded
#   1  a -Execute call failed (including 409 on project archive)
#   2  the API did not answer
[CmdletBinding()]
param(
    [switch]$Execute,

    [string]$ServerUrl = 'http://localhost:17202'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$api = $ServerUrl.TrimEnd('/')
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

# Frozen NAMES from the CARD-0295 census, not guids. Live ids are looked up each run.
$cohortANames = @(
    'gym-stat-accountgymforms',
    'gym-stat-auth-code',
    'gym-stat-auth-plan',
    'gym-stat-datamodel-code',
    'gym-stat-datamodel-plan',
    'gym-stat-deploy-code',
    'gym-stat-deploy-plan',
    'gym-stat-fieldeditor-code',
    'gym-stat-fieldeditor-plan',
    'gym-stat-floorplan-code',
    'gym-stat-floorplan-plan',
    'gym-stat-floorplanux',
    'gym-stat-floorspace-code',
    'gym-stat-floorspace-plan',
    'gym-stat-googlesignin-code',
    'gym-stat-googlesignin-plan',
    'gym-stat-install-code',
    'gym-stat-install-plan',
    'gym-stat-logging-code',
    'gym-stat-logging-plan',
    'gym-stat-machinetypeeditor',
    'gym-stat-memberroles-code',
    'gym-stat-memberroles-plan',
    'gym-stat-mock',
    'gym-stat-numericoverflow',
    'gym-stat-offline-code',
    'gym-stat-offline-plan',
    'gym-stat-privacypolicy',
    'gym-stat-scaffold-code',
    'gym-stat-scaffold-plan',
    'gym-stat-tech',
    'gym-stat-uireview-auth',
    'gym-stat-uireview-flows'
)

$cohortBNames = @(
    'gym-stat-dupmachine-impl',
    'gym-stat-dupmachine-plan',
    'gym-stat-fieldkeyautogen',
    'gym-stat-googledarktheme',
    'gym-stat-googleusername',
    'gym-stat-weightsteps-impl',
    'gym-stat-weightsteps-plan'
)

$keepAgentNames = @(
    'Gym Stat Orchestrator',
    'gym-stat-addonpin-plan',
    'gym-stat-setupmockups'
)

$keepBoardName = 'Gym Stat'
$keepProjectName = 'gym-stat'
$archiveReason = 'CARD-0295 one-off POST /api/agents debris'
$archiveBy = 'card-0295'

$cohortASet = @{}
foreach ($n in $cohortANames) { $cohortASet[$n] = $true }
$cohortBSet = @{}
foreach ($n in $cohortBNames) { $cohortBSet[$n] = $true }
$keepAgentSet = @{}
foreach ($n in $keepAgentNames) { $keepAgentSet[$n] = $true }

function Convert-AntiphonJson {
    param([string]$Content)
    if ([string]::IsNullOrWhiteSpace($Content)) { return $null }
    return $Content | ConvertFrom-Json
}

function Invoke-Antiphon {
    param([string]$Method, [string]$Path, $Body)
    try {
        if ($null -ne $Body) {
            $payload = $Body | ConvertTo-Json -Depth 10 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
            $response = Invoke-WebRequest -Method $Method -Uri "$api$Path" -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8' -UseBasicParsing
            return Convert-AntiphonJson $response.Content
        }
        $response = Invoke-WebRequest -Method $Method -Uri "$api$Path" -Headers $headers -UseBasicParsing
        return Convert-AntiphonJson $response.Content
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        throw "Antiphon $Method $Path failed: $detail"
    }
}

function Get-AntiphonList {
    param([string]$Path)
    $raw = Invoke-Antiphon GET $Path
    $list = New-Object System.Collections.ArrayList
    if ($null -eq $raw) { return ,$list }
    $isCollection = $false
    if ($raw -is [System.Array]) { $isCollection = $true }
    elseif ($raw -is [System.Collections.IList]) { $isCollection = $true }
    if ($isCollection) {
        foreach ($item in $raw) { [void]$list.Add($item) }
    }
    else {
        [void]$list.Add($raw)
    }
    return ,$list
}

function Get-Scalar($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) {
        if ($value.Length -eq 0) { return $null }
        return $value[0]
    }
    return $value
}

function Get-Id($item) {
    if ($null -eq $item) { return $null }
    $id = Get-Scalar $item.id
    if ($null -eq $id) { $id = Get-Scalar $item.Id }
    if ($null -eq $id) { return $null }
    return [string]$id
}

function Get-Name($item) {
    if ($null -eq $item) { return '' }
    $n = Get-Scalar $item.name
    if ($null -eq $n) { $n = Get-Scalar $item.Name }
    if ($null -eq $n) { return '' }
    return [string]$n
}

function Test-NameInSet([string]$Name, $Set) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    if ($Set.ContainsKey($Name)) { return $true }
    foreach ($key in $Set.Keys) {
        if ([string]::Equals($Name, $key, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-Bool($value) {
    if ($null -eq $value) { return $false }
    if ($value -is [bool]) { return [bool]$value }
    $s = [string]$value
    if ($s -eq 'True' -or $s -eq 'true') { return $true }
    return $false
}

# ---- census ------------------------------------------------------------------------------------

try {
    $agents = Get-AntiphonList '/api/agents'
    $boards = Get-AntiphonList '/api/boards'
    $projects = Get-AntiphonList '/api/projects'
}
catch {
    Write-Host ("ABORT: API at " + $api + " did not answer: " + $_.Exception.Message)
    exit 2
}

$boardById = @{}
foreach ($board in $boards) {
    $bid = Get-Id $board
    if ($bid) { $boardById[$bid] = $board }
}

$projectById = @{}
foreach ($project in $projects) {
    $projId = Get-Id $project
    if ($projId) { $projectById[$projId] = $project }
}

$candidates = New-Object System.Collections.ArrayList
$protected = New-Object System.Collections.ArrayList
$keepPrinted = @{}

foreach ($agent in $agents) {
    $name = Get-Name $agent
    $id = Get-Id $agent
    $status = [string](Get-Scalar $agent.status)
    $alwaysOn = Get-Bool $agent.alwaysOn
    $live = $null -ne (Get-Scalar $agent.liveSession)
    $boardId = [string](Get-Scalar $agent.boardId)
    $board = $null
    if ($boardId -and $boardById.ContainsKey($boardId)) { $board = $boardById[$boardId] }
    $boardName = Get-Name $board
    if ([string]::IsNullOrWhiteSpace($boardName)) { $boardName = [string](Get-Scalar $agent.boardName) }
    $projectId = $null
    $projectName = ''
    if ($board) {
        $projectId = [string](Get-Scalar $board.projectId)
        $projectName = [string](Get-Scalar $board.projectName)
        if ([string]::IsNullOrWhiteSpace($projectName) -and $projectId -and $projectById.ContainsKey($projectId)) {
            $projectName = Get-Name $projectById[$projectId]
        }
    }

    $inA = Test-NameInSet $name $cohortASet
    $inB = Test-NameInSet $name $cohortBSet
    $inKeep = Test-NameInSet $name $keepAgentSet
    $onKeepBoard = [string]::Equals($boardName, $keepBoardName, [System.StringComparison]::OrdinalIgnoreCase)
    $onKeepProject = [string]::Equals($projectName, $keepProjectName, [System.StringComparison]::OrdinalIgnoreCase)

    $reason = $null
    $cohort = $null
    if ($inA) { $cohort = 'A' }
    elseif ($inB) { $cohort = 'B' }

    if ($inKeep) {
        $reason = 'keep-set name'
    }
    elseif ($status -eq 'Running') {
        $reason = 'status Running'
    }
    elseif ($alwaysOn) {
        $reason = 'alwaysOn'
    }
    elseif ($live) {
        $reason = 'liveSession'
    }
    elseif (($onKeepBoard -or $onKeepProject) -and -not $inB) {
        if ($inA -or $inB -or $inKeep) { $reason = 'Gym Stat / gym-stat keep-set' }
    }

    if ($inB -and -not $reason) {
        if (-not ($onKeepBoard -or $onKeepProject)) {
            $reason = 'Cohort B name but not on Gym Stat / gym-stat'
        }
        elseif ($status -ne 'Stopped' -and $status -ne 'Failed') {
            $reason = 'Cohort B not Stopped/Failed (status=' + $status + ')'
        }
    }

    $row = [pscustomobject]@{
        Cohort      = $cohort
        Id          = $id
        Name        = $name
        Status      = $status
        AlwaysOn    = $alwaysOn
        Live        = $live
        BoardId     = $boardId
        BoardName   = $boardName
        ProjectId   = $projectId
        ProjectName = $projectName
        Reason      = $reason
    }

    if ($inKeep -and -not $keepPrinted.ContainsKey($id)) {
        [void]$protected.Add($row)
        $keepPrinted[$id] = $true
        continue
    }

    if (-not $inA -and -not $inB) { continue }

    if ($reason) {
        [void]$protected.Add($row)
    }
    else {
        [void]$candidates.Add($row)
    }
}

$deleteA = @($candidates | Where-Object { $_.Cohort -eq 'A' })
$deleteB = @($candidates | Where-Object { $_.Cohort -eq 'B' })

$projectsToArchive = @{}
foreach ($row in $deleteA) {
    if ([string]::IsNullOrWhiteSpace($row.ProjectId)) { continue }
    if ([string]::Equals($row.ProjectName, $keepProjectName, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    if (-not $projectsToArchive.ContainsKey($row.ProjectId)) {
        $projectsToArchive[$row.ProjectId] = [pscustomobject]@{
            Id   = $row.ProjectId
            Name = $row.ProjectName
        }
    }
}

$archiveList = @($projectsToArchive.Values | Sort-Object Name)

# ---- print census ------------------------------------------------------------------------------

if ($Execute) {
    Write-Host 'EXECUTE - deleting candidate agents then archiving Cohort A projects'
}
else {
    Write-Host 'DRY RUN - listing what would be deleted/archived; nothing was written'
}
Write-Host ("API " + $api)
Write-Host ("Live: " + $agents.Count + " agents, " + $boards.Count + " boards (default), " + $projects.Count + " projects (default)")
Write-Host ("Candidates: " + $candidates.Count + " agent deletes (" + $deleteA.Count + " Cohort A + " + $deleteB.Count + " Cohort B), " + $archiveList.Count + " project archives")
Write-Host ("Protected: " + $protected.Count)
Write-Host ""

function Write-Section {
    param([string]$Title, $Rows, [string]$Empty)
    Write-Host $Title
    if (-not $Rows -or @($Rows).Count -eq 0) {
        Write-Host ("  " + $Empty)
        return
    }
    foreach ($row in $Rows) {
        $extra = ''
        if ($row.Reason) { $extra = '  ' + $row.Reason }
        Write-Host ("  " + $row.Cohort + "  " + $row.Name + "  " + $row.Id + "  status=" + $row.Status + "  board=" + $row.BoardName + "  project=" + $row.ProjectName + $extra)
    }
}

Write-Section 'AGENTS TO DELETE (Cohort A)' $deleteA 'none'
Write-Host ""
Write-Section 'AGENTS TO DELETE (Cohort B, Gym Stat board, agent only)' $deleteB 'none'
Write-Host ""
Write-Host 'PROJECTS TO ARCHIVE (after their agents are gone)'
if ($archiveList.Count -eq 0) {
    Write-Host '  none'
}
else {
    foreach ($p in $archiveList) {
        Write-Host ("  " + $p.Name + "  " + $p.Id)
    }
}
Write-Host ""
Write-Section 'PROTECTED (skipped)' $protected 'none'

$frozenAgents = 40
$frozenProjects = 21
$agentDrift = [math]::Abs($candidates.Count - $frozenAgents)
$projectDrift = [math]::Abs($archiveList.Count - $frozenProjects)
Write-Host ""
Write-Host ("Frozen census was " + $frozenAgents + " agent deletes / " + $frozenProjects + " project archives.")
Write-Host ("This run: " + $candidates.Count + " / " + $archiveList.Count + " (agent drift " + $agentDrift + ", project drift " + $projectDrift + ").")

if (-not $Execute) {
    exit 0
}

# ---- execute -----------------------------------------------------------------------------------

$errors = New-Object System.Collections.ArrayList
$deletedAgents = New-Object System.Collections.ArrayList
$archivedProjects = New-Object System.Collections.ArrayList

foreach ($row in $candidates) {
    try {
        $null = Invoke-Antiphon DELETE ("/api/agents/" + $row.Id)
        [void]$deletedAgents.Add($row)
        Write-Host ("DELETED agent " + $row.Name + "  " + $row.Id)
    }
    catch {
        [void]$errors.Add([pscustomobject]@{ Kind = 'agent'; Id = $row.Id; Name = $row.Name; Error = [string]$_ })
        Write-Host ("FAIL DELETE agent " + $row.Name + "  " + $_.Exception.Message)
    }
}

if ($errors.Count -gt 0) {
    Write-Host ("Stopping before project archive: " + $errors.Count + " agent delete(s) failed.")
    Write-Host ("Deleted agents: " + $deletedAgents.Count + "; errors: " + $errors.Count)
    exit 1
}

foreach ($p in $archiveList) {
    try {
        $impact = Invoke-Antiphon GET ("/api/projects/" + $p.Id + "/deletion-impact")
        $detached = 0
        $running = 0
        $openCards = 0
        $cards = 0
        if ($null -ne $impact) {
            if ($null -ne $impact.detachedAgentCount) { $detached = [int]$impact.detachedAgentCount }
            if ($null -ne $impact.runningSessionCount) { $running = [int]$impact.runningSessionCount }
            if ($null -ne $impact.openCardCount) { $openCards = [int]$impact.openCardCount }
            if ($null -ne $impact.cardCount) { $cards = [int]$impact.cardCount }
        }
        Write-Host ("IMPACT " + $p.Name + " agents=" + $detached + " runningSessions=" + $running + " cards=" + $cards + " openCards=" + $openCards)
        if ($detached -gt 0) {
            throw ("deletion-impact still lists " + $detached + " attached agent(s); not archiving (would 409).")
        }
        if ($running -gt 0) {
            throw ("deletion-impact lists " + $running + " running session(s); not archiving.")
        }
        $body = @{ reason = $archiveReason; archivedBy = $archiveBy }
        $updated = Invoke-Antiphon POST ("/api/projects/" + $p.Id + "/archive") $body
        [void]$archivedProjects.Add($p)
        Write-Host ("ARCHIVED project " + $p.Name + "  " + $p.Id + "  archivedAt=" + $updated.archivedAt)
    }
    catch {
        [void]$errors.Add([pscustomobject]@{ Kind = 'project'; Id = $p.Id; Name = $p.Name; Error = [string]$_ })
        Write-Host ("FAIL ARCHIVE project " + $p.Name + "  " + $_.Exception.Message)
        Write-Host '409 or archive failure means an agent was missed; stopping, not force-deleting.'
        break
    }
}

Write-Host ""
Write-Host ("Deleted agents: " + $deletedAgents.Count + "; archived projects: " + $archivedProjects.Count + "; errors: " + $errors.Count)
foreach ($err in $errors) {
    Write-Host ("  FAIL " + $err.Kind + " " + $err.Name + "  " + $err.Error)
}

try {
    $afterAgents = Get-AntiphonList '/api/agents'
    $afterBoards = Get-AntiphonList '/api/boards'
    $gymStat = $null
    foreach ($b in $afterBoards) {
        if ([string]::Equals((Get-Name $b), $keepBoardName, [System.StringComparison]::OrdinalIgnoreCase)) {
            $gymStat = $b
            break
        }
    }
    $orch = $null
    foreach ($a in $afterAgents) {
        if ([string]::Equals((Get-Name $a), 'Gym Stat Orchestrator', [System.StringComparison]::OrdinalIgnoreCase)) {
            $orch = $a
            break
        }
    }
    $cardCount = 0
    if ($gymStat -and $null -ne $gymStat.cardCount) { $cardCount = [int]$gymStat.cardCount }
    $orchStatus = '(missing)'
    if ($orch) { $orchStatus = [string](Get-Scalar $orch.status) }
    Write-Host ("AFTER: " + $afterAgents.Count + " agents, " + $afterBoards.Count + " boards (default); Gym Stat cards=" + $cardCount + "; Orchestrator status=" + $orchStatus)
}
catch {
    Write-Host ("AFTER census failed: " + $_.Exception.Message)
}

if ($errors.Count -gt 0) { exit 1 }
exit 0
