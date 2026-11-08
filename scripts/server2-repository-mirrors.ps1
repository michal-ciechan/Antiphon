#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1776 repository mirrors for Linux land checkouts.

    Default is -WhatIf. It prints the before/after table and does not call git
    or docker. -Execute clones or fetches each row under -ReposRoot only after
    the mounted token file is readable and non-empty. A missing credential
    refuses with credential-absent before any git process.
    The Antiphon project's gitRepositoryUrl is empty; the table remote is the
    desktop checkout origin, recorded by the operator. Rollback restores the
    before column of Project.LocalRepositoryPath. This script does not rewrite
    the database and never prints a token.
    Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.
    ASCII-only.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [string]$TableFile = '',
    [string]$TokenFile = '/run/antiphon/github-token/token',
    [string]$PolicyFile = '/run/antiphon/push-allow-list',
    # Layout: /work/repos/<name> on the production work volume.
    [string]$ReposRoot = '/work/repos'
)

$ErrorActionPreference = 'Stop'

function Stop-Mirror {
    param([string]$Kind, [int]$Code, [string]$Reason)
    Write-Output ("SERVER2-MIRRORS " + $Kind + " exit=" + $Code + " reason=" + $Reason)
    exit $Code
}

function Test-MirrorSecret {
    param([string]$Value)
    if ($Value -match '(?i)ghp_|github_pat_|gho_|ghu_|ghs_|ghr_') { return $true }
    if ($Value -match '://[^/\s]+:[^/\s]+@') { return $true }
    return $false
}

function Test-MirrorEscape {
    param([string]$Value)
    foreach ($segment in $Value.Replace('\', '/').Split('/')) {
        if ($segment -eq '..') { return $true }
    }
    return $false
}

function Test-MirrorForbiddenVolume {
    param([string]$Value)
    $lower = $Value.ToLowerInvariant()
    return $lower.Contains('antiphon_pgdata') -or $lower.Contains('dind-data') -or $lower.Contains('windmill')
}

function Test-MirrorFileUsable {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (-not [System.IO.Path]::IsPathRooted($Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.LinkType) { return $false }
    if ($item.Length -le 0) { return $false }
    $stream = $null
    try {
        $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        return $stream.ReadByte() -ge 0
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Get-MirrorSlug {
    param([string]$Before)
    $normalized = $Before.Replace('\', '/').TrimEnd('/')
    if ($normalized.Length -eq 0) { return '' }
    $parts = $normalized.Split('/')
    return $parts[$parts.Length - 1].ToLowerInvariant()
}

function Get-MirrorIdentity {
    param([string]$Remote)
    $prefix = 'https://github.com/'
    $path = $Remote.Substring($prefix.Length)
    if (-not $path.EndsWith('.git')) { $path = $path + '.git' }
    return $prefix + $path.ToLowerInvariant()
}

if ($WhatIf -and $Execute) { Stop-Mirror 'refuse' 3 'ambiguous-mode' }
$planOnly = -not $Execute
if ($planOnly) { $WhatIf = $true }

if ([string]::IsNullOrWhiteSpace($TableFile) -or -not (Test-Path -LiteralPath $TableFile -PathType Leaf)) {
    Stop-Mirror 'refuse' 2 'table-missing'
}

$root = $ReposRoot.TrimEnd('/').TrimEnd('\')
if ($root.Length -eq 0 -or -not $root.StartsWith('/')) { Stop-Mirror 'refuse' 2 'path-forbidden' }

$rows = @()
foreach ($raw in [System.IO.File]::ReadAllLines($TableFile)) {
    $line = $raw.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
    if (Test-MirrorSecret $line) { Stop-Mirror 'refuse' 2 'secret-in-table' }
    if ((Test-MirrorForbiddenVolume $line) -or (Test-MirrorEscape $line)) {
        if (Test-MirrorForbiddenVolume $line) { Stop-Mirror 'refuse' 2 'volume-forbidden' }
        Stop-Mirror 'refuse' 2 'path-forbidden'
    }
    $parts = $line.Split('|')
    $name = ''
    $before = ''
    $after = ''
    $remote = ''
    if ($parts.Length -eq 3) {
        $name = $parts[0].Trim()
        $before = $parts[1].Trim()
        $remote = $parts[2].Trim()
    }
    elseif ($parts.Length -eq 4) {
        $name = $parts[0].Trim()
        $before = $parts[1].Trim()
        $after = $parts[2].Trim()
        $remote = $parts[3].Trim()
    }
    else { Stop-Mirror 'refuse' 2 'path-forbidden' }

    if ($remote.Length -eq 0) { Stop-Mirror 'refuse' 2 'remote-unrecorded' }
    $slug = Get-MirrorSlug $before
    if ($slug -notmatch '^[a-z0-9][a-z0-9._-]{0,80}$') { Stop-Mirror 'refuse' 2 'path-forbidden' }
    if ($name -ne $slug) { Stop-Mirror 'refuse' 2 'path-forbidden' }
    $derived = $root + '/' + $slug
    if ($after.Length -eq 0) { $after = $derived }
    if ($after -ne $derived) { Stop-Mirror 'refuse' 2 'path-forbidden' }
    if ($remote -notmatch '^https://github.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(\.git)?$') {
        Stop-Mirror 'refuse' 2 'remote-not-https'
    }
    $rows += [pscustomobject]@{
        Name = $name
        Before = $before
        After = $after
        Remote = $remote
        Identity = (Get-MirrorIdentity $remote)
    }
}

if ($rows.Count -eq 0) { Stop-Mirror 'refuse' 2 'table-missing' }

if ($planOnly) {
    Write-Output 'SERVER2-MIRRORS plan exit=0 reason=planned'
    foreach ($row in $rows) {
        Write-Output ("mirror name=" + $row.Name + " before=" + $row.Before + " after=" + $row.After + " remote=" + $row.Remote)
    }
    Write-Output 'rollback restores the before column'
    exit 0
}

$env:GIT_TERMINAL_PROMPT = '0'
if (-not (Test-MirrorFileUsable $TokenFile)) { Stop-Mirror 'refuse' 2 'credential-absent' }
if ([string]::IsNullOrWhiteSpace($PolicyFile) -or -not [System.IO.Path]::IsPathRooted($PolicyFile)) {
    Stop-Mirror 'refuse' 2 'credential-absent'
}
foreach ($row in $rows) {
    $parent = [System.IO.Path]::GetDirectoryName($row.After)
    if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
        Stop-Mirror 'refuse' 2 'mirror-parent-missing'
    }
}
$policyParent = [System.IO.Path]::GetDirectoryName($PolicyFile)
if ([string]::IsNullOrWhiteSpace($policyParent) -or -not (Test-Path -LiteralPath $policyParent -PathType Container)) {
    Stop-Mirror 'refuse' 2 'policy-unwritable'
}

$policyTemp = $PolicyFile + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
$policyText = ''
foreach ($row in $rows) {
    if ($policyText.Length -gt 0) { $policyText += "`n" }
    $policyText += $row.Identity
}
$policyText += "`n"
[System.IO.File]::WriteAllText($policyTemp, $policyText)
if (Test-Path -LiteralPath $PolicyFile) { Remove-Item -LiteralPath $PolicyFile }
[System.IO.File]::Move($policyTemp, $PolicyFile)

foreach ($row in $rows) {
    $gitDir = Join-Path $row.After '.git'
    if (Test-Path -LiteralPath $gitDir) {
        & git -C $row.After fetch --prune origin
        if ($LASTEXITCODE -ne 0) { Stop-Mirror 'refuse' 2 'fetch-failed' }
    }
    else {
        # git clone -- <remote> <directory>; git fetch updates an existing mirror
        & git clone -- $row.Remote $row.After
        if ($LASTEXITCODE -ne 0) { Stop-Mirror 'refuse' 2 'clone-failed' }
    }
}

Write-Output 'SERVER2-MIRRORS ok exit=0 reason=mirrored'
Write-Output 'rollback restores the before column'
exit 0
