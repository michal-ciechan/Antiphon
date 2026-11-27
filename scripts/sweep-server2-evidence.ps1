#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReleaseList,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [guid] $ProjectId,
    [ValidateRange(24, 8760)] [int] $MinAgeHours = 24,
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
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    do {
        $route = '/api/cards/search?includeArchived=true&limit=100&q=' + [uri]::EscapeDataString($Leaf)
        if ($page) { $route += '&pageToken=' + [uri]::EscapeDataString($page) }
        $result = Read-Antiphon $route
        if ($result.truncated -and -not $result.nextPageToken) { throw 'The card search was incomplete.' }
        foreach ($card in $result.cards) {
            if ($card.status -notin @('Done', 'Canceled')) { return $true }
        }
        $page = $result.nextPageToken
        if ($page -and -not $seen.Add($page)) { throw 'The card search repeated a page.' }
    } while ($page)
    return $false
}

function Get-DirectoryIdentity([string] $Path) {
    $entry = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $entry -or -not $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
    $identity = & stat --printf '%d:%i' -- $Path
    if ($LASTEXITCODE -eq 0 -and $identity -cmatch '^\d+:\d+$') { $identity }
}

function Get-ReferenceRefusal($Candidate) {
    $ownerId = $Candidate.TaskId.ToString('D')
    $owner = Read-Antiphon "/api/agent-tasks/$ownerId"
    if ($owner.summary.id -ne $Candidate.TaskId -or
        $owner.summary.status -notin @('Succeeded', 'Failed', 'Canceled') -or
        -not $owner.summary.completedAt -or
        [DateTime]$owner.summary.completedAt -ge [DateTime]::UtcNow.AddHours(-$MinAgeHours) -or
        ($owner.session -and $owner.session.status -notin @('Stopped', 'Failed'))) {
        return 'owner-not-settled'
    }
    foreach ($task in @(Get-TaskClosure)) {
        if ($task.id -eq $Candidate.TaskId) { return 'active-reference' }
        $detail = Read-Antiphon "/api/agent-tasks/$($task.id)"
        if ($detail.summary.id -ne $task.id) { throw 'An active task detail did not match the census.' }
        $serialized = $detail | ConvertTo-Json -Depth 100 -Compress -WarningAction Stop
        if ($serialized.Contains($Candidate.Leaf, [StringComparison]::OrdinalIgnoreCase) -or
            $serialized.Contains($ownerId, [StringComparison]::OrdinalIgnoreCase)) { return 'active-reference' }
    }
    if ((Test-OpenCardReference $Candidate.Leaf) -or
        ($Candidate.Leaf -cne $ownerId -and (Test-OpenCardReference $ownerId))) { return 'active-reference' }
    & git -C $Repository grep -qiF -e $Candidate.Leaf -e $ownerId HEAD origin/master --
    if ($LASTEXITCODE -eq 0) { return 'git-reference' }
    if ($LASTEXITCODE -ne 1) { throw 'The Git reference check did not complete.' }
}

# Each release binds the full owner ID and the device/inode at the exact path.
$roots = @('/work/evidence', '/work/code-evidence', '/work/review-evidence', '/work/merge-evidence', '/work/test-evidence')
$release = Get-Content -LiteralPath $ReleaseList -Raw | ConvertFrom-Json -NoEnumerate
$listed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
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
    $identity = [string]$row.identity
    if ($taskId -eq [guid]::Empty -or $identity -cnotmatch '^\d+:\d+$' -or -not $listed.Add($path)) {
        throw "Release entry has invalid or duplicate authority: $path"
    }

    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and $item.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddHours(-$MinAgeHours)) {
        [pscustomobject]@{ Path = $path; Parent = $parent; Leaf = $leaf; Pattern = $leafPattern
            TaskId = $taskId; Identity = $identity; LastWrite = $item.LastWriteTimeUtc }
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
    foreach ($candidate in $candidates | Sort-Object -Property LastWrite, Path | Select-Object -First $MaxCandidates) {
        $path = $candidate.Path
        if ((Get-DirectoryIdentity $path) -cne $candidate.Identity) {
            Write-Output "kept directory-identity $path"
            continue
        }
        $refusal = Get-ReferenceRefusal $candidate
        if ($refusal) { Write-Output "kept $refusal $path"; continue }
        $size = & timeout 30 du -sx -B1 -- $path
        if ($LASTEXITCODE -ne 0) { Write-Output "kept size-unknown $path"; continue }
        $bytes = ($size -split '\s+', 2)[0]
        $refusal = Get-AntiphonRetentionRefusal -Path $path -Parent $candidate.Parent -LeafPattern $candidate.Pattern -MinAgeHours $MinAgeHours
        if ($refusal) { Write-Output "kept $refusal $path"; continue }
        if (-not $Apply) {
            Write-Output "would-remove $bytes $path"
            continue
        }

        # Detach the released inode from its public name before the last reference and holder checks.
        # A writer opening the old path now can only reach a new tree. Existing holders follow the inode.
        $graveyard = & mktemp -d -- (Join-Path $candidate.Parent '.retention-XXXXXXXXXX')
        if ($LASTEXITCODE -ne 0) { throw 'Unable to reserve a private retention directory.' }
        $detached = Join-Path $graveyard $candidate.Leaf
        $moved = $false
        $deleting = $false
        try {
            if ((Get-DirectoryIdentity $path) -cne $candidate.Identity) {
                Write-Output "kept directory-identity $path"
                continue
            }
            [IO.Directory]::Move($path, $detached)
            $moved = $true
            $refusal = Get-ReferenceRefusal $candidate
            if (-not $refusal) {
                $refusal = Get-AntiphonRetentionRefusal -Path $detached -Parent $graveyard -LeafPattern $candidate.Pattern -MinAgeHours $MinAgeHours
            }
            if (-not $refusal -and (Get-DirectoryIdentity $detached) -cne $candidate.Identity) { $refusal = 'directory-identity' }
            if ($refusal) { Write-Output "kept $refusal $path"; continue }

            $deleting = $true
            Remove-Item -LiteralPath $detached -Recurse -Force
            Write-Output "removed $bytes $path"
        } finally {
            if ($moved -and -not $deleting -and (Get-DirectoryIdentity $detached) -ceq $candidate.Identity) {
                # Never clobber a directory a writer created since the move.
                & mv --no-clobber --no-target-directory -- $detached $path
            }
            if ([IO.Directory]::EnumerateFileSystemEntries($graveyard).GetEnumerator().MoveNext()) {
                Write-Output "quarantined $detached original=$path owner=$($candidate.TaskId)"
            } else {
                [IO.Directory]::Delete($graveyard)
            }
        }
    }
} finally {
    if ($lock) { $lock.Dispose() }
}
