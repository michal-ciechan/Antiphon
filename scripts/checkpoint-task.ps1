# Commit the uncommitted work in a stalled task's worktree as a WIP checkpoint. Never kills
# the session. Safe to run on a healthy session: a WIP commit on a delegate's branch costs
# nothing and the delegate's next commit sits on top of it.
#
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -File scripts/checkpoint-task.ps1 -TaskId <id> [-Push] [-DryRun]
#
# Exit codes:
#   0  committed, or nothing to checkpoint (clean tree)
#   2  not a git repo (or the API could not resolve the task)
#   3  shared checkout of another active worker - would sweep up a colleague's edits
#   1  anything else
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TaskId,

    [switch]$Push,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Invoke-AntiphonGet([string]$Path) {
    return Invoke-RestMethod -Method GET -Uri "$api$Path" -Headers $headers
}

function Get-WorktreePath($task) {
    if ($null -ne $task.summary -and $task.summary.worktreePath) {
        return [string]$task.summary.worktreePath
    }
    if ($task.worktreePath) { return [string]$task.worktreePath }
    return $null
}

# A Worktree task's real edits live in its worktree, not workingDirectory - that field is the
# CALLER's directory (where delegate.ps1 was invoked from), unrelated to where the agent actually
# wrote anything. Checking workingDirectory alone silently found "nothing to checkpoint" on a
# Worktree task with real uncommitted work sitting in its worktreePath (CARD-0217 S5, 2026-08-28).
function Get-WorkingDirectory($task) {
    $worktree = Get-WorktreePath $task
    if (-not [string]::IsNullOrWhiteSpace($worktree)) { return $worktree }
    if ($null -ne $task.summary -and $task.summary.workingDirectory) {
        return [string]$task.summary.workingDirectory
    }
    if ($task.workingDirectory) { return [string]$task.workingDirectory }
    return $null
}

function Get-TaskId($task) {
    if ($null -ne $task.summary -and $task.summary.id) { return [string]$task.summary.id }
    if ($task.id) { return [string]$task.id }
    return $null
}

function Get-Workspace($task) {
    if ($null -ne $task.summary -and $task.summary.workspace) { return [string]$task.summary.workspace }
    if ($task.workspace) { return [string]$task.workspace }
    return 'Shared'
}

function Get-Title($task) {
    if ($null -ne $task.summary -and $task.summary.title) { return [string]$task.summary.title }
    if ($task.title) { return [string]$task.title }
    return 'task'
}

function Get-ShortId([string]$id) {
    if ([string]::IsNullOrWhiteSpace($id) -or $id.Length -lt 8) { return $id }
    return $id.Substring(0, 8)
}

function Write-Fail([int]$Code, [string]$Message) {
    [Console]::Error.WriteLine($Message)
    exit $Code
}

function Normalize-Dir([string]$p) {
    return [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/').ToLowerInvariant()
}

try {
    $task = Invoke-AntiphonGet "/api/agent-tasks/$TaskId"
} catch {
    Write-Fail 2 "Could not resolve task '$TaskId' from $api : $_"
}

$dir = Get-WorkingDirectory $task
$resolvedId = Get-TaskId $task
if ([string]::IsNullOrWhiteSpace($dir) -or -not (Test-Path -LiteralPath $dir)) {
    Write-Fail 2 "Task $(Get-ShortId $resolvedId) has no usable working directory."
}

Push-Location $dir
try {
    git rev-parse --is-inside-work-tree 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 2 "Not a git repository: $dir"
    }

    $open = [System.Collections.Generic.List[object]]::new()
    foreach ($path in @('/api/agent-tasks?status=Working', '/api/agent-tasks?status=Dispatched')) {
        try {
            $response = Invoke-AntiphonGet $path
            foreach ($row in @($response)) {
                if ($null -ne $row) { $open.Add($row) }
            }
        } catch { }
    }

    $dirFull = Normalize-Dir $dir
    $others = @()
    foreach ($row in $open) {
        $otherId = Get-TaskId $row
        $otherDir = Get-WorkingDirectory $row
        if ([string]::IsNullOrWhiteSpace($otherId)) { continue }
        if ($otherId -eq $resolvedId) { continue }
        if ([string]::IsNullOrWhiteSpace($otherDir)) { continue }
        try {
            $otherFull = Normalize-Dir $otherDir
        } catch { continue }
        if ($dirFull -eq $otherFull) {
            $others += $otherId
        }
    }
    if ($others.Count -gt 0) {
        Write-Fail 3 ("Refusing to checkpoint a shared checkout with another active worker: " +
            (( $others | ForEach-Object { Get-ShortId $_ } ) -join ', '))
    }

    $status = git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 1 "git status failed in $dir"
    }
    if ([string]::IsNullOrWhiteSpace($status)) {
        Write-Host "nothing to checkpoint"
        exit 0
    }

    if ($DryRun) {
        Write-Host "Would checkpoint:"
        $status | ForEach-Object { Write-Host "  $_" }
        exit 0
    }

    $short = Get-ShortId $resolvedId
    $title = Get-Title $task
    $card = $null
    if ($title -match '(CARD-\d+)') { $card = $Matches[1] }
    $cardBit = if ($card) { "$card " } else { '' }
    $files = @($status -split "`n" | Where-Object { $_ } ).Count
    $subject = "wip(checkpoint): ${cardBit}task $short - stalled, $files files, not verified"

    git add -A
    if ($LASTEXITCODE -ne 0) { Write-Fail 1 "git add failed" }
    git commit -m $subject
    if ($LASTEXITCODE -ne 0) { Write-Fail 1 "git commit failed" }
    $sha = (git rev-parse HEAD).Trim()
    Write-Host "checkpoint $sha"
    Write-Host "re-dispatch: delegate.ps1 -Role Code -Goal `"resume from checkpoint $sha`" -Dir `"$dir`""

    if ($Push) {
        git push
        if ($LASTEXITCODE -ne 0) { Write-Fail 1 "git push failed" }
    }
    exit 0
} finally {
    Pop-Location
}
