#requires -Version 7.0
# CARD-1981: remove one owned review scratch container created by new-review-scratch.ps1.
# A top-level child that is a linked git worktree is removed with git worktree remove
# and never --force, and only when that repository's worktree list names the child path.
# A symlink or other reparse point, a nested .git, or a standalone clone is refused.
# Any refusal leaves the container. This does not enable the temp sweep.
param(
    [Parameter(Mandatory)][string]$Root,
    [string]$TempRoot = [IO.Path]::GetTempPath()
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:GIT_TERMINAL_PROMPT = '0'
$temp = [IO.Path]::GetFullPath($TempRoot).TrimEnd('\', '/')
$full = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
$leaf = [IO.Path]::GetFileName($full)
if ($leaf -cnotmatch '^antiphon-review-[0-9a-f]{8}$') { throw 'Not a review scratch container.' }
$parent = [IO.Path]::GetDirectoryName($full)
if ($parent -cne $temp) { throw 'Container must sit directly under the temp root.' }
$containerItem = Get-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
if ($null -eq $containerItem) { throw 'Scratch container does not exist.' }
if (($containerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Refusing scratch container reparse point; container kept.'
}
if (-not $containerItem.PSIsContainer) { throw 'Scratch container does not exist.' }

# Walk every entry without following links. A reparse point, and any .git that is
# not directly under a top-level child, stops removal before anything is deleted.
$pending = [System.Collections.Generic.Queue[string]]::new()
$pending.Enqueue($full)
while ($pending.Count -gt 0) {
    $dir = $pending.Dequeue()
    foreach ($entry in ([IO.DirectoryInfo]::new($dir)).EnumerateFileSystemInfos()) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Refusing reparse point inside the scratch container; container kept.'
        }
        if ($entry.Name -ceq '.git') {
            $relative = [IO.Path]::GetRelativePath($full, $entry.FullName)
            $parts = $relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)
            if ($parts.Length -ne 2) {
                throw 'Refusing nested git metadata inside the scratch container; container kept.'
            }
        }
        if (($entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
            $pending.Enqueue($entry.FullName)
        }
    }
}

$marker = Join-Path $full '.antiphon-scratch-owner'
$markerItem = Get-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
if ($null -eq $markerItem -or $markerItem.PSIsContainer) { throw 'Missing scratch owner marker.' }

$containerPrefix = $full + [IO.Path]::DirectorySeparatorChar
foreach ($child in [IO.Directory]::GetDirectories($full)) {
    $resolved = [IO.Path]::GetFullPath($child).TrimEnd('\', '/')
    if (-not $resolved.StartsWith($containerPrefix, [StringComparison]::Ordinal)) {
        throw 'Refusing child outside the scratch container; container kept.'
    }
    $git = Join-Path $child '.git'
    $gitItem = Get-Item -LiteralPath $git -Force -ErrorAction SilentlyContinue
    if ($null -eq $gitItem) { continue }
    if ($gitItem.PSIsContainer) { throw 'Refusing standalone clone; container kept.' }
    $common = & git -C $child rev-parse --path-format=absolute --git-common-dir 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$common)) {
        throw 'git worktree remove failed; container kept.'
    }
    $main = [IO.Path]::GetDirectoryName(([string]$common).Trim())
    if ([string]::IsNullOrWhiteSpace($main)) { throw 'git worktree remove failed; container kept.' }
    $porcelain = @(& git -C $main worktree list --porcelain 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'git worktree remove failed; container kept.' }
    $named = $false
    foreach ($line in $porcelain) {
        $text = [string]$line
        if ($text.StartsWith('worktree ', [StringComparison]::Ordinal)) {
            $listed = $text.Substring(9).TrimEnd('\', '/')
            if ($listed -ceq $resolved) { $named = $true }
        }
    }
    if (-not $named) { throw 'git worktree list does not name this child; container kept.' }
    & git -C $main worktree remove -- $resolved
    if ($LASTEXITCODE -ne 0) { throw 'git worktree remove failed; container kept.' }
}
Remove-Item -LiteralPath $full -Recurse -Force
