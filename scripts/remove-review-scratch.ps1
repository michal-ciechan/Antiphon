#requires -Version 7.0
# CARD-1981: remove one owned review scratch container created by new-review-scratch.ps1.
# A child that is a git worktree is removed with git worktree remove and never --force.
# If that remove fails, the container stays. This does not enable the temp sweep.
param(
    [Parameter(Mandatory)][string]$Root,
    [string]$TempRoot = [IO.Path]::GetTempPath()
)
$ErrorActionPreference = 'Stop'
$env:GIT_TERMINAL_PROMPT = '0'
$temp = [IO.Path]::GetFullPath($TempRoot).TrimEnd('\', '/')
$full = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
$leaf = [IO.Path]::GetFileName($full)
if ($leaf -cnotmatch '^antiphon-review-[0-9a-f]{8}$') { throw 'Not a review scratch container.' }
$parent = [IO.Path]::GetDirectoryName($full)
if ($parent -cne $temp) { throw 'Container must sit directly under the temp root.' }
if (-not [IO.Directory]::Exists($full)) { throw 'Scratch container does not exist.' }
$marker = Join-Path $full '.antiphon-scratch-owner'
if (-not [IO.File]::Exists($marker)) { throw 'Missing scratch owner marker.' }
foreach ($child in [IO.Directory]::GetDirectories($full)) {
    $git = Join-Path $child '.git'
    if (-not [IO.File]::Exists($git) -and -not [IO.Directory]::Exists($git)) { continue }
    $common = & git -C $child rev-parse --path-format=absolute --git-common-dir
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($common)) {
        throw 'git worktree remove failed; container kept.'
    }
    $main = [IO.Path]::GetDirectoryName($common.Trim())
    & git -C $main worktree remove -- $child
    if ($LASTEXITCODE -ne 0) { throw 'git worktree remove failed; container kept.' }
}
Remove-Item -LiteralPath $full -Recurse -Force
