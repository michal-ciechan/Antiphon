#requires -Version 5.1
<#
.SYNOPSIS
    Report (default) or archive boards and projects whose names match test-residue shapes
    and that have no agent, live session or non-terminal task attached.

    ASCII-only on purpose: must parse under both pwsh 7 and Windows PowerShell 5.1.

    Default is dry-run. -Execute is deliberately opt-in and archives (never deletes) via
    POST /api/boards/{id}/archive and POST /api/projects/{id}/archive.

.PARAMETER Execute
    Archive the qualifying rows. Absent means report-only.

.PARAMETER Match
    Replace the default name patterns. Each value is a .NET regular expression matched
    against the board or project name. When omitted, the defaults (tuned against the live
    2026-08-28 database) are used.

.PARAMETER Reason
    Archive reason body. Default names this script.

.PARAMETER Json
    Emit a single JSON object instead of the text report.

.PARAMETER PassThru
    Return a result object rather than exiting, for fixture tests.
#>
[CmdletBinding()]
param(
    [switch]$Execute,
    [string[]]$Match,
    [string]$Reason = 'Test residue (prune-test-data.ps1)',
    [switch]$Json,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

# Defaults were checked against the live database on 2026-08-28. The plan listed
# card-task-*, card\d+-*, Catalog Test*, PwshCreateTest, TUI Probe, CARD-\d+ Repro *.
# Live names needed four extra shapes: task-{8 hex} (the board name for a card-task
# worktree), card-\d+-* (card-0142-verify / card-0164-*), Card0007 * (board display
# names) and CARD-\d+ Verify * (sibling of Repro).
$defaultPatterns = @(
    '^card-task-',
    '^task-[0-9a-fA-F]{8}$',
    '^card\d+-',
    '^card-\d+-',
    '^Card0007 ',
    '^Catalog Test',
    '^PwshCreateTest$',
    '^TUI Probe$',
    '^CARD-\d+ Repro ',
    '^CARD-\d+ Verify '
)

if ($PSBoundParameters.ContainsKey('Match') -and $Match -and $Match.Count -gt 0) {
    $patterns = @($Match)
}
else {
    $patterns = $defaultPatterns
}

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
        $response = Invoke-WebRequest -Method GET -Uri "$api$Path" -Headers $headers -UseBasicParsing
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

function Get-MatchingPattern {
    param([string]$Name)
    foreach ($pattern in $patterns) {
        if ($Name -match $pattern) { return $pattern }
    }
    return $null
}

function Get-Id($item) {
    if ($null -eq $item) { return $null }
    $id = Get-Scalar $item.id
    if ($null -eq $id) { $id = Get-Scalar $item.Id }
    if ($null -eq $id) { return $null }
    return [string]$id
}

function Get-Scalar($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) {
        if ($value.Length -eq 0) { return $null }
        return $value[0]
    }
    return $value
}

function Get-Name($item) {
    if ($null -eq $item) { return '' }
    $n = Get-Scalar $item.name
    if ($null -eq $n) { $n = Get-Scalar $item.Name }
    if ($null -eq $n) { return '' }
    return [string]$n
}

$openStatuses = @('Queued', 'Dispatched', 'Working', 'Blocked')

$projects = Get-AntiphonList '/api/projects?includeArchived=true'
$boards = Get-AntiphonList '/api/boards?includeArchived=true'
$agents = Get-AntiphonList '/api/agents'
$tasks = Get-AntiphonList '/api/agent-tasks'

$boardById = @{}
foreach ($board in $boards) {
    $bid = Get-Id $board
    if ($bid) { $boardById[$bid] = $board }
}

$agentsByBoard = @{}
$agentsByProject = @{}
$liveBoards = @{}
$liveProjects = @{}
foreach ($agent in $agents) {
    $boardId = $null
    if ($agent.boardId) { $boardId = [string]$agent.boardId }
    $agentName = Get-Name $agent
    if ($boardId) {
        if (-not $agentsByBoard.ContainsKey($boardId)) { $agentsByBoard[$boardId] = New-Object System.Collections.ArrayList }
        [void]$agentsByBoard[$boardId].Add($agentName)
        $board = $boardById[$boardId]
        if ($board) {
            $projectId = [string]$board.projectId
            if (-not $agentsByProject.ContainsKey($projectId)) { $agentsByProject[$projectId] = New-Object System.Collections.ArrayList }
            [void]$agentsByProject[$projectId].Add($agentName)
        }
    }
    $live = $false
    if ($null -ne $agent.liveSession) { $live = $true }
    if ($live -and $boardId) {
        $liveBoards[$boardId] = $true
        $board = $boardById[$boardId]
        if ($board) { $liveProjects[[string]$board.projectId] = $true }
    }
}

$openBoards = @{}
$openProjects = @{}
$cardBoardCache = @{}
foreach ($task in $tasks) {
    $status = [string]$task.status
    $isOpen = $false
    foreach ($open in $openStatuses) {
        if ($status -eq $open) { $isOpen = $true; break }
    }
    if (-not $isOpen) { continue }

    $cardId = $null
    if ($task.cardId) { $cardId = [string]$task.cardId }
    $boardId = $null
    if ($cardId) {
        if ($cardBoardCache.ContainsKey($cardId)) {
            $boardId = $cardBoardCache[$cardId]
        }
        else {
            $card = Invoke-Antiphon GET "/api/cards/$cardId"
            $boardId = [string]$card.boardId
            $cardBoardCache[$cardId] = $boardId
        }
    }
    if ($boardId) {
        $openBoards[$boardId] = $true
        $board = $boardById[$boardId]
        if ($board) { $openProjects[[string]$board.projectId] = $true }
    }
}

function New-Row {
    param($Item, [string]$Kind, [string]$Pattern)
    $id = Get-Id $Item
    $name = Get-Name $Item
    $already = $false
    $archivedAt = Get-Scalar $Item.archivedAt
    if ($null -eq $archivedAt) { $archivedAt = Get-Scalar $Item.ArchivedAt }
    if ($null -ne $archivedAt -and [string]$archivedAt -ne '') { $already = $true }

    $agentNames = @()
    $hasLive = $false
    $hasOpen = $false
    if ($Kind -eq 'project') {
        if ($agentsByProject.ContainsKey($id)) { $agentNames = @($agentsByProject[$id]) }
        if ($liveProjects.ContainsKey($id)) { $hasLive = $true }
        if ($openProjects.ContainsKey($id)) { $hasOpen = $true }
    }
    else {
        if ($agentsByBoard.ContainsKey($id)) { $agentNames = @($agentsByBoard[$id]) }
        if ($liveBoards.ContainsKey($id)) { $hasLive = $true }
        if ($openBoards.ContainsKey($id)) { $hasOpen = $true }
    }

    $skip = $null
    if ($already) {
        $skip = 'already archived'
    }
    elseif ($agentNames.Count -gt 0) {
        $skip = 'agent attached (' + ($agentNames -join ', ') + ')'
    }
    elseif ($hasLive) {
        $skip = 'live session attached'
    }
    elseif ($hasOpen) {
        $skip = 'non-terminal task attached'
    }

    $why = 'matched ' + $Pattern + '; agents=0 liveSessions=0 openTasks=0'
    if ($skip) {
        $why = 'matched ' + $Pattern + '; skip=' + $skip
    }

    return [pscustomobject]@{
        Kind      = $Kind
        Id        = $id
        Name      = $name
        Pattern   = $Pattern
        Qualify   = [bool](-not $skip)
        Skip      = $skip
        Why       = $why
        Agents    = $agentNames.Count
        Live      = $hasLive
        OpenTask  = $hasOpen
        Archived  = $already
    }
}

$projectRows = New-Object System.Collections.ArrayList
$boardRows = New-Object System.Collections.ArrayList
foreach ($project in $projects) {
    $pattern = Get-MatchingPattern (Get-Name $project)
    if ($pattern) { [void]$projectRows.Add((New-Row $project 'project' $pattern)) }
}
foreach ($board in $boards) {
    $pattern = Get-MatchingPattern (Get-Name $board)
    if ($pattern) { [void]$boardRows.Add((New-Row $board 'board' $pattern)) }
}

$qualifyProjects = @($projectRows | Where-Object { $_.Qualify })
$skipProjects = @($projectRows | Where-Object { -not $_.Qualify })
$qualifyBoards = @($boardRows | Where-Object { $_.Qualify })
$skipBoards = @($boardRows | Where-Object { -not $_.Qualify })

$mutations = New-Object System.Collections.ArrayList
$errors = New-Object System.Collections.ArrayList
$mode = 'dry-run'
if ($Execute) {
    $mode = 'execute'
    $body = @{ reason = $Reason; archivedBy = 'prune-test-data' }
    foreach ($row in $qualifyBoards) {
        try {
            $updated = Invoke-Antiphon POST ("/api/boards/$($row.Id)/archive") $body
            [void]$mutations.Add([pscustomobject]@{ Kind = 'board'; Id = $row.Id; Name = $row.Name; ArchivedAt = $updated.archivedAt })
        }
        catch {
            [void]$errors.Add([pscustomobject]@{ Kind = 'board'; Id = $row.Id; Name = $row.Name; Error = [string]$_ })
        }
    }
    foreach ($row in $qualifyProjects) {
        try {
            $updated = Invoke-Antiphon POST ("/api/projects/$($row.Id)/archive") $body
            [void]$mutations.Add([pscustomobject]@{ Kind = 'project'; Id = $row.Id; Name = $row.Name; ArchivedAt = $updated.archivedAt })
        }
        catch {
            [void]$errors.Add([pscustomobject]@{ Kind = 'project'; Id = $row.Id; Name = $row.Name; Error = [string]$_ })
        }
    }
}

$result = [pscustomobject]@{
    Mode             = $mode
    Patterns         = $patterns
    ProjectQualify   = $qualifyProjects.Count
    ProjectSkip      = $skipProjects.Count
    BoardQualify     = $qualifyBoards.Count
    BoardSkip        = $skipBoards.Count
    Projects         = @($projectRows)
    Boards           = @($boardRows)
    Mutations        = @($mutations)
    Errors           = @($errors)
    TouchedNothing   = [bool](-not $Execute)
}

function Write-Section {
    param([string]$Title, $Rows, [string]$Empty)
    Write-Host $Title
    if (-not $Rows -or $Rows.Count -eq 0) {
        Write-Host ("  " + $Empty)
        return
    }
    foreach ($row in $Rows) {
        Write-Host ("  " + $row.Kind + "  " + $row.Name + "  " + $row.Id + "  " + $row.Why)
    }
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6
}
else {
    if ($Execute) {
        Write-Host "EXECUTE - archiving qualifying rows (never deleting)"
    }
    else {
        Write-Host "DRY RUN - listing what would be archived; nothing was written"
    }
    Write-Host ("API $api")
    Write-Host ("Patterns: " + ($patterns -join ', '))
    Write-Host ("Projects: $($qualifyProjects.Count) qualify, $($skipProjects.Count) skipped of $($projectRows.Count) name-matched ($($projects.Count) total)")
    Write-Host ("Boards:   $($qualifyBoards.Count) qualify, $($skipBoards.Count) skipped of $($boardRows.Count) name-matched ($($boards.Count) total)")
    Write-Host ""
    Write-Section 'PROJECTS TO ARCHIVE' $qualifyProjects 'none'
    Write-Host ""
    Write-Section 'PROJECTS SKIPPED' $skipProjects 'none'
    Write-Host ""
    Write-Section 'BOARDS TO ARCHIVE' $qualifyBoards 'none'
    Write-Host ""
    Write-Section 'BOARDS SKIPPED' $skipBoards 'none'
    if ($Execute) {
        Write-Host ""
        Write-Host ("Archived: $($mutations.Count); errors: $($errors.Count)")
        foreach ($err in $errors) {
            Write-Host ("  FAIL " + $err.Kind + " " + $err.Name + "  " + $err.Error)
        }
    }
}

if ($PassThru) { return $result }
if ($errors.Count -gt 0) { exit 1 }
exit 0
