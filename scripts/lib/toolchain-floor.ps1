# CARD-1698: host toolchain floors. Warn, never refuse (operator decision 2026-11-03).
# ASCII-only and Windows PowerShell 5.1 syntax: restart and nightly entry points dot-source it.
#
# The manifest below names where each floor is declared; it restates no version number.
# - git: docker/git-minimum-version.sh (GIT_MINIMUM_VERSION=, CARD-1260), the same file both
#   images run at build time, so the images and the hosts read one declaration.
# - .NET SDK: global.json sdk.version (rollForward latestMinor: an SDK of the same major at or
#   above the pin satisfies it).
# - pwsh, node/npm, docker: floor still to decide, listed but not enforced.
#
# Every function here returns data and never throws: a floor that cannot be read or a version
# that cannot be read becomes a warning, so no entry point changes its exit code.

function Get-ToolchainFloorManifest {
    return @(
        [pscustomobject]@{ tool = 'git'; source = 'docker/git-minimum-version.sh'; enforced = $true
            impact = 'lands will fail: git show-ref --exists needs the floor (LandingGit, LandOperationFactory, guarded worktree and verification removal return source_ref_error)'
            fixWindows = 'winget upgrade Git.Git'
            fixOther = 'install Git at or above the floor (the images build Git from source; see docs/docker-stack.md)' }
        [pscustomobject]@{ tool = 'dotnet-sdk'; source = 'global.json'; enforced = $true
            impact = 'builds and tests will fail: global.json pins the SDK (rollForward latestMinor needs the same major at or above the pin)'
            fixWindows = 'winget install Microsoft.DotNet.SDK.10'
            fixOther = 'install the pinned SDK or a later patch of its major (https://dot.net/v1/dotnet-install.sh --channel 10.0)' }
        [pscustomobject]@{ tool = 'pwsh'; source = ''; enforced = $false; impact = ''; fixWindows = ''; fixOther = '' }
        [pscustomobject]@{ tool = 'node'; source = ''; enforced = $false; impact = ''; fixWindows = ''; fixOther = '' }
        [pscustomobject]@{ tool = 'docker'; source = ''; enforced = $false; impact = ''; fixWindows = ''; fixOther = '' }
    )
}

function Test-ToolchainFloorWindowsHost {
    return ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT)
}

function ConvertTo-ToolchainFloorVersion([string]$Text) {
    if ($Text -notmatch '^(\d+)\.(\d+)(?:\.(\d+))?') { return $null }
    $build = 0
    if ($Matches[3]) { $build = [int]$Matches[3] }
    return [System.Version]::new([int]$Matches[1], [int]$Matches[2], $build)
}

function Get-ToolchainFloorValue([string]$RepoRoot, [string]$Tool) {
    try {
        if ($Tool -eq 'git') {
            $path = Join-Path $RepoRoot (Join-Path 'docker' 'git-minimum-version.sh')
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
            $m = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^GIT_MINIMUM_VERSION=(\d+\.\d+)\r?$')
            if ($m.Success) { return $m.Groups[1].Value }
            return ''
        }
        if ($Tool -eq 'dotnet-sdk') {
            $path = Join-Path $RepoRoot 'global.json'
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
            $json = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            if ($json.sdk -and ([string]$json.sdk.version) -match '^\d+\.\d+\.\d+$') { return [string]$json.sdk.version }
            return ''
        }
    } catch { }
    return ''
}

# Raw version text per tool: `git --version` for git, `dotnet --list-sdks` for the SDK.
function Get-ToolchainFloorVersionText([string]$Tool) {
    if ($Tool -eq 'git') { return ((& git --version 2>$null) | Out-String) }
    if ($Tool -eq 'dotnet-sdk') { return ((& dotnet --list-sdks 2>$null) | Out-String) }
    return ''
}

function Get-ToolchainFloorFound([string]$Tool, [string]$Text, $Floor) {
    if ($Tool -eq 'git') {
        $m = [regex]::Match($Text, '^\s*git version (\d+\.\d+(?:\.\d+)?)')
        if (-not $m.Success) { return $null }
        return $m.Groups[1].Value
    }
    if ($Tool -eq 'dotnet-sdk') {
        $best = $null; $bestSame = $null
        foreach ($line in ($Text -split "`r?`n")) {
            $m = [regex]::Match($line, '^\s*(\d+\.\d+\.\d+)\S*\s+\[')
            if (-not $m.Success) { continue }
            $v = ConvertTo-ToolchainFloorVersion $m.Groups[1].Value
            if ($null -eq $best -or $v -gt (ConvertTo-ToolchainFloorVersion $best)) { $best = $m.Groups[1].Value }
            if ($null -ne $Floor -and $v.Major -eq $Floor.Major) {
                if ($null -eq $bestSame -or $v -gt (ConvertTo-ToolchainFloorVersion $bestSame)) { $bestSame = $m.Groups[1].Value }
            }
        }
        if ($null -ne $bestSame) { return $bestSame }
        return $best
    }
    return $null
}

# One row per manifest entry. status: ok, below-floor, version-unreadable, floor-unreadable,
# not-enforced. VersionSource is { param($tool) <raw version text> }; tests inject it.
function Get-ToolchainFloorReport {
    param([string]$RepoRoot, [scriptblock]$VersionSource = $null)
    $rows = @()
    $warnings = @()
    $windows = Test-ToolchainFloorWindowsHost
    foreach ($entry in (Get-ToolchainFloorManifest)) {
        $row = [ordered]@{ tool = $entry.tool; found = ''; floor = ''; source = $entry.source; enforced = [bool]$entry.enforced; status = 'not-enforced' }
        if (-not $entry.enforced) { $row.floor = 'to decide'; $rows += [pscustomobject]$row; continue }
        $fix = $entry.fixOther
        if ($windows) { $fix = $entry.fixWindows }
        $row.floor = Get-ToolchainFloorValue -RepoRoot $RepoRoot -Tool $entry.tool
        $text = ''
        $readError = ''
        try {
            if ($VersionSource) { $text = [string](& $VersionSource $entry.tool) }
            else { $text = [string](Get-ToolchainFloorVersionText $entry.tool) }
        } catch { $readError = $_.Exception.Message }
        $floor = $null
        if ($row.floor) { $floor = ConvertTo-ToolchainFloorVersion $row.floor }
        $found = Get-ToolchainFloorFound -Tool $entry.tool -Text $text -Floor $floor
        if ($found) { $row.found = $found }
        $message = ''
        if ($null -eq $floor) {
            $row.status = 'floor-unreadable'
            $message = ('WARNING: the {0} floor could not be read from {1}; {0} was not checked.' -f $entry.tool, $entry.source)
        } elseif (-not $found) {
            $row.status = 'version-unreadable'
            $raw = $text.Trim()
            if ($readError) { $raw = $readError }
            if ($raw.Length -gt 120) { $raw = $raw.Substring(0, 120) }
            $message = ("WARNING: the {0} version could not be read ('{1}'); the minimum is {2}; if it is older, {3}. Fix: {4}" -f $entry.tool, $raw, $row.floor, $entry.impact, $fix)
        } elseif ((ConvertTo-ToolchainFloorVersion $found) -lt $floor -or
            ($entry.tool -eq 'dotnet-sdk' -and (ConvertTo-ToolchainFloorVersion $found).Major -ne $floor.Major)) {
            $row.status = 'below-floor'
            $message = ('WARNING: {0} {1} is below the minimum {2}; {3}. Fix: {4}' -f $entry.tool, $found, $row.floor, $entry.impact, $fix)
        } else {
            $row.status = 'ok'
        }
        if ($message) {
            $warnings += [pscustomobject]@{ tool = $entry.tool; found = $row.found; floor = $row.floor; impact = $entry.impact
                fix = $fix; reason = $row.status; message = $message }
        }
        $rows += [pscustomobject]$row
    }
    return [pscustomobject]@{ tools = @($rows); warnings = @($warnings) }
}

function Get-ToolchainFloorWarnings {
    param([string]$RepoRoot, [scriptblock]$VersionSource = $null)
    try { return @((Get-ToolchainFloorReport -RepoRoot $RepoRoot -VersionSource $VersionSource).warnings) }
    catch {
        return @([pscustomobject]@{ tool = 'toolchain'; found = ''; floor = ''; impact = ''; fix = ''; reason = 'check-failed'
            message = ('WARNING: the toolchain floor check failed: {0}' -f $_.Exception.Message) })
    }
}

# Entry-point helper: print each warning, return nothing, never throw.
function Write-ToolchainFloorWarnings {
    param([string]$RepoRoot, [scriptblock]$VersionSource = $null)
    try {
        foreach ($w in @(Get-ToolchainFloorWarnings -RepoRoot $RepoRoot -VersionSource $VersionSource)) {
            Write-Host $w.message -ForegroundColor Yellow
        }
    } catch { }
}
