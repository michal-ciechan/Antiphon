#requires -Version 7.0
# CARD-1896: create an owned container for review/master scratch clones.
# Clone into a child (for example master). remove-review-scratch.ps1 refuses that
# standalone clone and keeps the container; it only removes a linked worktree child.
# This never registers an existing tree or grants deletion: the runner rechecks
# ownership, age, processes and live Git.
param(
    [Parameter(Mandatory)][Guid]$TaskId,
    [Parameter(Mandatory)][string]$OwnerWorktree,
    [string]$TempRoot = [IO.Path]::GetTempPath()
)
$ErrorActionPreference = 'Stop'
$owner = [IO.Path]::GetFullPath($OwnerWorktree).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not [IO.Directory]::Exists($owner)) { throw 'Owner worktree must exist.' }
$leaf = [IO.Path]::GetFileName($owner)
if ($leaf -cnotmatch '^(task-[0-9a-f]{8}|ws-[A-Za-z0-9-]+)$' -or
    [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($owner)) -cne 'worktrees') {
    throw 'Owner must be a task or workspace-slot directory under the runner worktrees root.'
}
$short = $TaskId.ToString('N').Substring(0, 8)
if ($leaf.StartsWith('task-') -and $leaf -cne ('task-' + $short)) { throw 'Task id must match its owner worktree.' }
$root = Join-Path ([IO.Path]::GetFullPath($TempRoot)) ('antiphon-review-' + $short)
if (Test-Path -LiteralPath $root) { throw 'Scratch container already exists; do not replace its ownership.' }
[void][IO.Directory]::CreateDirectory($root)
$registration = Join-Path $root '.antiphon-scratch-owner'
$stream = [IO.File]::Open($registration, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($owner.Replace('\', '/'))
    $stream.Write($bytes, 0, $bytes.Length)
} finally { $stream.Dispose() }
Write-Output $root
