#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReleaseList,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [guid] $ProjectId,
    [ValidateRange(72, 8760)] [int] $MinAgeHours = 72,
    [ValidateRange(1, 100)] [int] $MaxCandidates = 25,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/server2-retention-files.ps1')

if (-not $IsLinux -or [Environment]::UserName -eq 'root') { throw 'Run as the Linux runner user.' }
if (-not $env:ANTIPHON_API) { throw 'ANTIPHON_API is required.' }
$api = $env:ANTIPHON_API.TrimEnd('/')
$headers = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }

function Read-Antiphon([string] $Route) {
    Invoke-RestMethod ($api + $Route) -Headers $headers -TimeoutSec 15 -MaximumRedirection 0
}

function Get-FilteredTasks([string] $Filter) {
    $route = "/api/agent-tasks?includeChecks=true&$Filter"
    $primary = Read-Antiphon "$route&projectId=$ProjectId&unscoped=include"
    $excludedCount = 0
    foreach ($scope in $primary.excluded.byProject) {
        $page = Read-Antiphon "$route&projectId=$($scope.projectId)"
        if (@($page.items | Where-Object { $_ }).Count -ne $scope.count) {
            throw 'Active tasks changed during the census.'
        }
        $page.items
        $excludedCount += $scope.count
    }
    if ($excludedCount -ne $primary.excluded.total) { throw 'The task census was incomplete.' }
    $primary.items
}

function Get-TaskClosure {
    @(
        Get-FilteredTasks 'status=Queued,Dispatched,Working,Blocked'
        Get-FilteredTasks 'landPending=true'
    ) | Sort-Object -Property id -Unique
}

function Test-OpenCardReference([string] $Leaf) {
    $page = $null
    do {
        $route = '/api/cards/search?includeArchived=true&limit=100&q=' + [uri]::EscapeDataString($Leaf)
        if ($page) { $route += '&pageToken=' + [uri]::EscapeDataString($page) }
        $result = Read-Antiphon $route
        if ($result.truncated -and -not $result.nextPageToken) { throw 'The card search was incomplete.' }
        foreach ($card in $result.cards) {
            if ($card.status -notin @('Done', 'Canceled')) { return $true }
        }
        $page = $result.nextPageToken
    } while ($page)
    return $false
}

# Release records name a known owner and exact evidence path. Every sweep rechecks
# open references, task settlement, the tree's age and process holders.
$roots = @('/work/evidence', '/work/code-evidence', '/work/review-evidence', '/work/merge-evidence', '/work/test-evidence')
$release = Get-Content -LiteralPath $ReleaseList -Raw | ConvertFrom-Json -NoEnumerate
$candidates = foreach ($row in $release) {
    $path = [string]$row.path
    $taskId = [guid]$row.taskId
    $parent = [IO.Path]::GetDirectoryName($path)
    $leaf = [IO.Path]::GetFileName($path)
    $short = $taskId.ToString('N').Substring(0, 8)
    $leafPattern = '^(task-' + $short + '|' + $taskId.ToString('D') + ')$'
    if ($parent -cnotin $roots -or $leaf -cnotmatch $leafPattern -or [IO.Path]::GetFullPath($path) -cne $path) {
        throw "Release entry is outside the task's evidence directory: $path"
    }

    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and $item.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddHours(-$MinAgeHours)) {
        [pscustomobject]@{ Path = $path; Parent = $parent; Leaf = $leaf; Pattern = $leafPattern
            TaskId = $taskId; LastWrite = $item.LastWriteTimeUtc }
    }
}
if (-not $candidates) { return }

$lock = $null
if ($Apply) {
    $lockPath = '/work/.evidence-retention.lock'
    if ([IO.FileInfo]::new($lockPath).LinkTarget) { throw 'The retention lock path is a link.' }
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
try {
    $openTasks = @(Get-TaskClosure)
    $openIds = @($openTasks | ForEach-Object { $_.id })
    $openDetails = @($openTasks | ForEach-Object { Read-Antiphon "/api/agent-tasks/$($_.id)" |
        ConvertTo-Json -Depth 30 -Compress })
    foreach ($candidate in $candidates | Sort-Object -Property LastWrite, Path -Unique | Select-Object -First $MaxCandidates) {
        $path = $candidate.Path
        $owner = Read-Antiphon "/api/agent-tasks/$($candidate.TaskId)"
        if ($owner.summary.id -ne $candidate.TaskId -or
            $owner.summary.status -notin @('Succeeded', 'Failed', 'Canceled') -or
            -not $owner.summary.completedAt -or
            [DateTime]$owner.summary.completedAt -ge [DateTime]::UtcNow.AddHours(-$MinAgeHours) -or
            ($owner.session -and $owner.session.status -notin @('Stopped', 'Failed'))) {
            Write-Output "kept owner-not-settled $path"
            continue
        }
        if ($candidate.TaskId -in $openIds -or
            ($openDetails | Where-Object { $_.Contains($candidate.Leaf, [StringComparison]::OrdinalIgnoreCase) }) -or
            (Test-OpenCardReference $candidate.Leaf)) {
            Write-Output "kept active-reference $path"
            continue
        }
        & git -C $Repository grep -qiF -e $candidate.Leaf HEAD origin/master --
        if ($LASTEXITCODE -eq 0) { Write-Output "kept git-reference $path"; continue }
        if ($LASTEXITCODE -ne 1) { throw 'The Git reference check did not complete.' }

        if ($Apply) {
            $currentIds = @(Get-TaskClosure | ForEach-Object { $_.id })
            if ($candidate.TaskId -in $currentIds -or ($currentIds -join '|') -cne ($openIds -join '|')) {
                throw 'Active task membership changed during the retention sweep.'
            }
        }
        $size = & timeout 30 du -sx -B1 -- $path
        if ($LASTEXITCODE -ne 0) { Write-Output "kept size-unknown $path"; continue }
        $bytes = ($size -split '\s+', 2)[0]
        $refusal = Get-AntiphonRetentionRefusal -Path $path -Parent $candidate.Parent -LeafPattern $candidate.Pattern -MinAgeHours $MinAgeHours
        if ($refusal) { Write-Output "kept $refusal $path"; continue }
        if ($Apply) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Output "removed $bytes $path"
        } else {
            Write-Output "would-remove $bytes $path"
        }
    }
} finally {
    if ($lock) { $lock.Dispose() }
}
