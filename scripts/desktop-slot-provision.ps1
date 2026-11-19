#requires -Version 5.1
# CARD-0824 S2. Prepare the Windows desktop hot-slot root.
#
# Default is a dry run. -Execute is what changes the disk. -WhatIf forces a
# dry run even when -Execute is also set. The operator passes -Execute on
# the desktop later.
#
#   pwsh -NoProfile -File scripts/desktop-slot-provision.ps1 -Root C:\Antiphon\slots -Execute
#   pwsh -NoProfile -File scripts/desktop-slot-provision.ps1 -Root C:\Antiphon\slots -Execute -RemoveEmpty
#
# Creates worktrees, workspace-slots, retained, and repos under -Root.
# Does not clone, does not create ws-NN, does not write ledger.json, and
# does not invoke dotnet. When repos/antiphon is already a git repo, sets
# local core.longpaths=true on that clone only and checks the value.
# Otherwise prints the clone and config commands.
#
# Refuses a filesystem root, a bare drive root, UNC and \\?\ and \\.\ paths,
# a segment ending in '.' or a space or containing '~', a reparse-point
# ancestor, a root under or containing C:\src\Antiphon, C:\Antiphon\releases,
# or C:\Antiphon\worktrees, a root with an ancestor named releases, and free
# space below -DiskBudgetGiB (default 32). core.longpaths is written only
# when git resolves that clone to exactly the clone and its .git.
# 8.3 expansion and NTFS junctions are not resolved here. ASCII-only.
[CmdletBinding()]
param(
    [string]$Root = 'C:\Antiphon\slots',
    [switch]$Execute,
    [switch]$WhatIf,
    [switch]$RemoveEmpty,
    [int]$DiskBudgetGiB = 32,
    [scriptblock]$FreeSpaceProvider
)

$PSNativeCommandUseErrorActionPreference = $false

function ConvertTo-DriveSlotPath([string]$Path) {
    $slash = $Path.Replace('/', '\')
    $drive = $slash.Substring(0, 2)
    $tail = ''
    if ($slash.Length -gt 2) { $tail = $slash.Substring(2) }
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($segment in ($tail -split '\\')) {
        if ($segment -eq '' -or $segment -eq '.') { continue }
        if ($segment -eq '..') {
            if ($parts.Count -gt 0) { $parts.RemoveAt($parts.Count - 1) }
            continue
        }
        $parts.Add($segment)
    }
    if ($parts.Count -eq 0) { return $drive + '\' }
    return $drive + '\' + [string]::Join('\', $parts.ToArray())
}

function ConvertTo-SlotPath([string]$Path) {
    $text = $Path.Trim()
    if ($text -match '^[A-Za-z]:$') { $text = $text + '\' }
    if ($text -match '^[A-Za-z]:') { return (ConvertTo-DriveSlotPath $text) }
    return [System.IO.Path]::GetFullPath($text)
}

function Get-SlotSegments([string]$Comparable) {
    if ($Comparable -match '^[A-Za-z]:\\') {
        if ($Comparable.Length -le 3) { return @() }
        $tail = $Comparable.Substring(3)
        if ($tail.Length -eq 0) { return @() }
        return @($tail.Split('\'))
    }
    $unix = $Comparable
    if ($unix.Length -gt 1) { $unix = $unix.TrimEnd('/') }
    if ($unix -eq '' -or $unix -eq '/') { return @() }
    $unix = $unix.TrimStart('/')
    if ($unix.Length -eq 0) { return @() }
    return @($unix.Split('/'))
}

function Test-UnderPrefix([string]$Path, [string]$Prefix) {
    if ($Path.Equals($Prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    $with = $Prefix.TrimEnd('\') + '\'
    if ($Path.StartsWith($with, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $false
}

function Test-ForbiddenSlotRoot([string]$Comparable) {
    $prefixes = @(
        'C:\src\Antiphon',
        'C:\Antiphon\releases',
        'C:\Antiphon\worktrees'
    )
    foreach ($prefix in $prefixes) {
        if (Test-UnderPrefix $Comparable $prefix) { return $true }
        if (Test-UnderPrefix $prefix $Comparable) { return $true }
    }
    $segments = @(Get-SlotSegments $Comparable)
    $index = 0
    foreach ($segment in $segments) {
        $isLeaf = $false
        if ($index -eq ($segments.Count - 1)) { $isLeaf = $true }
        if (-not $isLeaf) {
            if ($segment.Equals('releases', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
        $index++
    }
    return $false
}

function Test-SlotAliasSegment([string]$Segment) {
    if ($Segment -eq '.' -or $Segment -eq '..') { return $false }
    if ($Segment.EndsWith('.')) { return $true }
    if ($Segment.EndsWith(' ')) { return $true }
    if ($Segment.Contains('~')) { return $true }
    return $false
}

function Test-SlotDriveAlias([string]$Path) {
    $slash = $Path.Replace('/', '\')
    $tail = ''
    if ($slash.Length -gt 2) { $tail = $slash.Substring(2) }
    foreach ($segment in ($tail -split '\\')) {
        if ($segment.Length -eq 0) { continue }
        if (Test-SlotAliasSegment $segment) { return $true }
    }
    return $false
}

function Test-SlotUnixAlias([string]$FullPath) {
    $unix = $FullPath
    if ($unix.Length -gt 1) { $unix = $unix.TrimEnd('/') }
    if ($unix -eq '' -or $unix -eq '/') { return $false }
    $body = $unix.TrimStart('/')
    if ($body.Length -eq 0) { return $false }
    foreach ($segment in ($body -split '/')) {
        if ($segment.Length -eq 0) { continue }
        if (Test-SlotAliasSegment $segment) { return $true }
    }
    return $false
}

function Test-ForbiddenSlotChild([string]$Comparable) {
    foreach ($name in @('worktrees', 'workspace-slots', 'retained', 'repos')) {
        $child = Join-SlotChild $Comparable $name
        if (Test-ForbiddenSlotRoot $child) { return $true }
    }
    return $false
}

function Get-SlotTextRefusal([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return 'filesystem-root' }
    $text = $Path.Trim()
    $slash = $text.Replace('/', '\')
    if ($slash.StartsWith('\\')) { return 'unsupported-root' }
    $comparable = ''
    if ($text -match '^[A-Za-z]:') {
        if (Test-SlotDriveAlias $text) { return 'alias-segment' }
        if ($text -match '^[A-Za-z]:$') { $text = $text + '\' }
        $comparable = ConvertTo-DriveSlotPath $text
    } else {
        $comparable = [System.IO.Path]::GetFullPath($text)
        if (Test-SlotUnixAlias $comparable) { return 'alias-segment' }
    }
    if (Test-FilesystemRoot $comparable) { return 'filesystem-root' }
    if (Test-ForbiddenSlotRoot $comparable) { return 'forbidden-root' }
    if (Test-ForbiddenSlotChild $comparable) { return 'forbidden-root' }
    return ''
}

function Test-SlotReparseAncestor([string]$Comparable) {
    $current = $Comparable
    $seen = 0
    $reparse = [System.IO.FileAttributes]::ReparsePoint
    while ($seen -lt 64) {
        $seen++
        if (Test-Path -LiteralPath $current) {
            $item = $null
            try {
                $item = Get-Item -Force -LiteralPath $current -ErrorAction SilentlyContinue
            } catch {
                $item = $null
            }
            if ($null -ne $item) {
                $attrs = $item.Attributes
                if (($attrs -band $reparse) -eq $reparse) { return $true }
            }
        }
        if ($current -match '^[A-Za-z]:[\\/]?$') { break }
        if ($current -eq '/' -or $current -eq '\') { break }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
    return $false
}

function Test-FilesystemRoot([string]$Comparable) {
    if ($Comparable -match '^[A-Za-z]:\\$') { return $true }
    if ($Comparable -eq '/') { return $true }
    if ($Comparable -eq '\') { return $true }
    return $false
}

function Test-SlotRootCreatable([string]$Comparable) {
    if ($Comparable -match '^[A-Za-z]:\\') {
        $driveRoot = $Comparable.Substring(0, 2) + '\'
        if (-not (Test-Path -LiteralPath $driveRoot)) { return $false }
    }
    return $true
}

function Join-SlotChild([string]$RootPath, [string]$Child) {
    if ($RootPath -match '^[A-Za-z]:\\') {
        $base = $RootPath.TrimEnd('\')
        return $base + '\' + $Child
    }
    return (Join-Path -Path $RootPath -ChildPath $Child)
}

function ConvertTo-SlotSlash([string]$Path) {
    return $Path.Replace('\', '/')
}

function Get-DesktopSlotFreeBytes([string]$Path) {
    $probe = $Path
    while ($true) {
        if (Test-Path -LiteralPath $probe) { break }
        $parent = Split-Path -Parent $probe
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $probe) { break }
        $probe = $parent
    }
    if (-not (Test-Path -LiteralPath $probe)) { return [int64]-1 }
    $full = $probe
    if ($probe -notmatch '^[A-Za-z]:') { $full = [System.IO.Path]::GetFullPath($probe) }
    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($root)) { return [int64]-1 }
    $info = New-Object System.IO.DriveInfo ($root)
    return [int64]$info.AvailableFreeSpace
}

function New-SlotResult([int]$Code, $Lines) {
    $copy = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($Lines)) { [void]$copy.Add([string]$line) }
    $row = @{
        ExitCode = $Code
        Lines = $copy.ToArray()
    }
    return $row
}

function ConvertTo-SlotCompare([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    $text = $Path.Trim().Replace('\', '/')
    while ($text.Length -gt 1 -and $text.EndsWith('/')) {
        $text = $text.Substring(0, $text.Length - 1)
    }
    return $text
}

function Test-SlotSamePath([string]$Left, [string]$Right) {
    $a = ConvertTo-SlotCompare $Left
    $b = ConvertTo-SlotCompare $Right
    if ($a.Length -eq 0) { return $false }
    return $a.Equals($b, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-SlotGit([string]$Ceiling, [string[]]$GitArgs) {
    $defined = Test-Path Env:GIT_CEILING_DIRECTORIES
    $previous = $env:GIT_CEILING_DIRECTORIES
    $code = 1
    $text = ''
    try {
        $env:GIT_CEILING_DIRECTORIES = $Ceiling
        $output = $null
        try {
            $output = & git @GitArgs 2>&1
            $code = $LASTEXITCODE
        } catch {
            $output = $_.Exception.Message
            $code = 1
        }
        $text = ($output | Out-String).Trim()
    } finally {
        if ($defined) {
            $env:GIT_CEILING_DIRECTORIES = $previous
        } else {
            Remove-Item Env:GIT_CEILING_DIRECTORIES -ErrorAction SilentlyContinue
        }
    }
    $row = @{
        Code = $code
        Text = $text
    }
    return $row
}

function Add-SlotClonePlan([string]$RepoPath, $Lines, [bool]$Mutating) {
    $gitDir = Join-SlotChild $RepoPath '.git'
    $slash = ConvertTo-SlotSlash $RepoPath
    $hasGit = Test-Path -LiteralPath $gitDir
    if ($hasGit) {
        if ($Mutating) {
            $gitCmd = Get-Command git -CommandType Application -ErrorAction SilentlyContinue
            if ($null -eq $gitCmd) {
                $Lines.Add('refused: git-missing')
                return 2
            }
            $ceiling = Split-Path -Parent $RepoPath
            if ([string]::IsNullOrEmpty($ceiling)) {
                $Lines.Add('refused: clone-unresolved ' + $slash)
                return 2
            }
            $top = Invoke-SlotGit $ceiling @('-C', $RepoPath, 'rev-parse', '--show-toplevel')
            if ($top.Code -ne 0) {
                $Lines.Add('refused: clone-unresolved ' + $slash)
                return 2
            }
            $abs = Invoke-SlotGit $ceiling @('-C', $RepoPath, 'rev-parse', '--absolute-git-dir')
            if ($abs.Code -ne 0) {
                $Lines.Add('refused: clone-unresolved ' + $slash)
                return 2
            }
            $topOk = Test-SlotSamePath $top.Text $RepoPath
            $gitOk = Test-SlotSamePath $abs.Text $gitDir
            if (-not $topOk -or -not $gitOk) {
                $Lines.Add('refused: clone-mismatch ' + $slash)
                return 2
            }
            $set = Invoke-SlotGit $ceiling @('-C', $RepoPath, '--git-dir', $gitDir, '--work-tree', $RepoPath, 'config', '--local', 'core.longpaths', 'true')
            if ($set.Code -ne 0) {
                $Lines.Add('refused: longpaths-not-true')
                return 2
            }
            $got = Invoke-SlotGit $ceiling @('-C', $RepoPath, '--git-dir', $gitDir, '--work-tree', $RepoPath, 'config', '--local', '--get', 'core.longpaths')
            if ($got.Code -ne 0 -or $got.Text -ne 'true') {
                $Lines.Add('refused: longpaths-not-true')
                return 2
            }
            $Lines.Add('longpaths=true')
            return 0
        }
        $Lines.Add('would-set-longpaths=true')
        return 0
    }
    $Lines.Add('next-command: git clone https://github.com/michal-ciechan/Antiphon ' + $slash)
    $Lines.Add('next-command: git -C ' + $slash + ' config --local core.longpaths true')
    return 0
}

function Invoke-SlotRemoveEmpty([string]$Comparable, [bool]$Mutating, $Lines, [scriptblock]$BeforeRemove) {
    $names = @('worktrees', 'workspace-slots', 'retained', 'repos')
    $ledger = Join-SlotChild (Join-SlotChild $Comparable 'workspace-slots') 'ledger.json'
    $blocked = $false
    $reason = ''
    if (Test-Path -LiteralPath $ledger) {
        $blocked = $true
        $reason = 'ledger'
    }
    if (-not $blocked) {
        foreach ($name in $names) {
            $path = Join-SlotChild $Comparable $name
            if (Test-Path -LiteralPath $path) {
                $kids = @(Get-ChildItem -LiteralPath $path -Force)
                if ($kids.Count -gt 0) {
                    $blocked = $true
                    $reason = $name + '-child'
                    break
                }
            }
        }
    }
    if ($blocked) {
        $Lines.Add('refused: remove-empty-blocked reason=' + $reason)
        return (New-SlotResult 2 $Lines)
    }
    foreach ($name in $names) {
        $path = Join-SlotChild $Comparable $name
        if ($Mutating) {
            if ($null -ne $BeforeRemove) { & $BeforeRemove $path }
            if (Test-Path -LiteralPath $path) {
                try {
                    [System.IO.Directory]::Delete($path, $false)
                } catch {
                    $Lines.Add('refused: remove-empty-blocked reason=not-empty')
                    return (New-SlotResult 2 $Lines)
                }
                $Lines.Add('removed: ' + $path)
            }
        } else {
            $Lines.Add('would-remove: ' + $path)
        }
    }
    return (New-SlotResult 0 $Lines)
}

function Invoke-DesktopSlotProvision {
    param(
        [string]$Root = 'C:\Antiphon\slots',
        [switch]$Execute,
        [switch]$WhatIf,
        [switch]$RemoveEmpty,
        [int]$DiskBudgetGiB = 32,
        [scriptblock]$FreeSpaceProvider,
        [scriptblock]$BeforeRemove
    )
    $lines = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Root)) {
        $lines.Add('refused: filesystem-root')
        return (New-SlotResult 2 $lines)
    }
    $textReason = Get-SlotTextRefusal $Root
    if ($textReason -eq 'unsupported-root') {
        $lines.Add('refused: unsupported-root ' + $Root.Trim())
        return (New-SlotResult 2 $lines)
    }
    $comparable = ConvertTo-SlotPath $Root
    if ($textReason -ne '') {
        $lines.Add('refused: ' + $textReason + ' ' + $comparable)
        return (New-SlotResult 2 $lines)
    }
    if (Test-SlotReparseAncestor $comparable) {
        $lines.Add('refused: reparse-ancestor ' + $comparable)
        return (New-SlotResult 2 $lines)
    }
    if (-not (Test-SlotRootCreatable $comparable)) {
        $lines.Add('refused: root-unavailable ' + $comparable)
        return (New-SlotResult 2 $lines)
    }

    $mutating = $false
    if ($Execute) { $mutating = $true }
    if ($WhatIf) { $mutating = $false }
    $mode = 'dry-run'
    if ($mutating) { $mode = 'execute' }
    $lines.Add('mode=' + $mode)
    $lines.Add('root=' + $comparable)

    if ($RemoveEmpty) {
        return (Invoke-SlotRemoveEmpty $comparable $mutating $lines $BeforeRemove)
    }

    if ($DiskBudgetGiB -lt 1) {
        $lines.Add('refused: disk-budget')
        return (New-SlotResult 2 $lines)
    }
    $oneGiB = [int64]1024 * 1024 * 1024
    $budgetBytes = [int64]$DiskBudgetGiB * $oneGiB
    $free = [int64]-1
    if ($null -ne $FreeSpaceProvider) {
        $free = [int64](& $FreeSpaceProvider $comparable)
    } else {
        $free = Get-DesktopSlotFreeBytes $comparable
    }
    $lines.Add('free-bytes=' + $free)
    $lines.Add('budget-bytes=' + $budgetBytes)
    if ($free -lt [int64]0) {
        $lines.Add('refused: free-space-unknown')
        return (New-SlotResult 2 $lines)
    }
    if ($free -lt $budgetBytes) {
        $lines.Add('refused: low-free-space free=' + $free + ' budget=' + $budgetBytes)
        return (New-SlotResult 2 $lines)
    }

    foreach ($name in @('worktrees', 'workspace-slots', 'retained', 'repos')) {
        $path = Join-SlotChild $comparable $name
        if ($mutating) {
            if (-not (Test-Path -LiteralPath $path)) {
                New-Item -ItemType Directory -Path $path | Out-Null
                $lines.Add('created: ' + $path)
            } else {
                $lines.Add('unchanged: ' + $path)
            }
        } else {
            $lines.Add('would-create: ' + $path)
        }
    }

    $clone = Join-SlotChild (Join-SlotChild $comparable 'repos') 'antiphon'
    $cloneCode = Add-SlotClonePlan $clone $lines $mutating
    return (New-SlotResult $cloneCode $lines)
}

if ($MyInvocation.InvocationName -ne '.') {
    $result = Invoke-DesktopSlotProvision -Root $Root -Execute:$Execute -WhatIf:$WhatIf -RemoveEmpty:$RemoveEmpty -DiskBudgetGiB $DiskBudgetGiB -FreeSpaceProvider $FreeSpaceProvider
    foreach ($line in @($result.Lines)) { Write-Host $line }
    exit $result.ExitCode
}
