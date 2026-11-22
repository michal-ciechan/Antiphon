#requires -Version 5.1
# CARD-0487: shared nightly path, reparse, hash, atomic write, process, git, clock and seam helpers.
# ASCII-only: parseable under Windows PowerShell 5.1.
# W2-nightly S27: only what the remaining PowerShell (the W2-rc scripts, the C487 harness and its drivers) calls.
# The run lock, the run-context ownership check and the London calendar are compiled (Antiphon.Nightly
# NightlyRunLock.cs, NightlyRun.cs, NightlyHealth.cs); Read-NightlyLockRecord stays for the PowerShell readers.

if ($script:AntiphonNightlyCommonLoaded) { return }
$script:AntiphonNightlyCommonLoaded = $true
. (Join-Path $PSScriptRoot 'nightly-core.ps1')

$script:NightlySeams = $null

function Get-NightlyUtcNow {
    if ($script:NightlySeams -and $script:NightlySeams.UtcNow) {
        $value = $script:NightlySeams.UtcNow.Invoke()
        $arr = @($value)
        if ($arr.Count -gt 0) { return [datetime]$arr[0] }
    }
    return [datetime]::UtcNow
}

function ConvertTo-NightlyCanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalised = $Path.Replace([System.IO.Path]::AltDirectorySeparatorChar, [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath($normalised)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        $fullPath = $fullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    }
    return $fullPath
}

function Test-NightlyReparseItem {
    param($Item)
    if ($null -eq $Item) { return $false }
    try {
        return ([int]$Item.Attributes -band [int][System.IO.FileAttributes]::ReparsePoint) -ne 0
    } catch {
        return $false
    }
}

function Get-NightlyReparseTarget {
    param($Item)
    if ($null -eq $Item) { return $null }
    $target = $null
    try { $target = $Item.Target } catch { $target = $null }
    if ($target -is [System.Array]) {
        if ($target.Count -gt 0) { $target = $target[0] } else { $target = $null }
    }
    if ([string]::IsNullOrWhiteSpace([string]$target)) { return $null }
    $text = [string]$target
    if (-not [System.IO.Path]::IsPathRooted($text)) {
        $parent = $null
        try { $parent = $Item.DirectoryName } catch { $parent = $null }
        if ([string]::IsNullOrWhiteSpace($parent)) {
            try { $parent = [System.IO.Path]::GetDirectoryName($Item.FullName) } catch { $parent = $null }
        }
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            $text = Join-Path $parent $text
        }
    }
    return $text
}

function Resolve-NightlyReparsePath {
    param([Parameter(Mandatory = $true)][string]$Path, [int]$MaxHops = 32)
    if ($script:NightlySeams -and $script:NightlySeams.ResolveReparse) {
        return [string]$script:NightlySeams.ResolveReparse.Invoke($Path)
    }
    $current = ConvertTo-NightlyCanonicalPath -Path $Path
    $guard = 0
    while ($true) {
        if ($guard -ge $MaxHops) {
            throw ('reparse loop resolving {0}' -f $Path)
        }
        $guard++
        if (-not (Test-Path -LiteralPath $current)) {
            return $current
        }
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (-not (Test-NightlyReparseItem -Item $item)) {
            return ConvertTo-NightlyCanonicalPath -Path $item.FullName
        }
        $target = Get-NightlyReparseTarget -Item $item
        if ([string]::IsNullOrWhiteSpace($target)) {
            throw ('unresolved reparse at {0}' -f $current)
        }
        $current = ConvertTo-NightlyCanonicalPath -Path $target
    }
}

function Test-NightlyAncestorReparse {
    param([Parameter(Mandatory = $true)][string]$Path)
    $canonical = ConvertTo-NightlyCanonicalPath -Path $Path
    $root = [System.IO.Path]::GetPathRoot($canonical)
    $cursor = $canonical
    while ($cursor.Length -gt $root.Length) {
        # Exists follows links like Test-Path (a dangling link is skipped); GetAttributes reads the entry itself
        # like Get-Item. Direct calls instead of the provider: this runs for every ancestor of every state path.
        if ([System.IO.File]::Exists($cursor) -or [System.IO.Directory]::Exists($cursor)) {
            $attributes = $null
            try { $attributes = [System.IO.File]::GetAttributes($cursor) } catch { $attributes = $null }
            if ($null -ne $attributes -and (([int]$attributes -band [int][System.IO.FileAttributes]::ReparsePoint) -ne 0)) { return $true }
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $false
}

function ConvertTo-NightlyResolvedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $canonical = ConvertTo-NightlyCanonicalPath -Path $Path
    $resolved = Resolve-NightlyReparsePath -Path $canonical
    if (Test-NightlyAncestorReparse -Path $resolved) {
        throw ('reparse ancestor remains after resolve: {0}' -f $Path)
    }
    $recheck = Resolve-NightlyReparsePath -Path $canonical
    if (-not [string]::Equals($resolved, $recheck, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('reparse swapped before use: {0}' -f $Path)
    }
    return $resolved
}

function Test-NightlySharedTree {
    param([string]$Path)
    $canonical = ConvertTo-NightlyCanonicalPath -Path $Path
    $sharedMain = ConvertTo-NightlyCanonicalPath -Path 'C:\src\Antiphon'
    $worktrees = ConvertTo-NightlyCanonicalPath -Path 'C:\Antiphon\worktrees'
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if ([string]::Equals($canonical, $sharedMain, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($canonical.StartsWith($sharedMain + $sep, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ([string]::Equals($canonical, $worktrees, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($canonical.StartsWith($worktrees + $sep, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $false
}

function Test-NightlyGitWorktree {
    param([string]$Path)
    $gitPath = Join-Path $Path '.git'
    if (-not (Test-Path -LiteralPath $gitPath)) { return $false }
    $gitItem = Get-Item -LiteralPath $gitPath -Force
    if ($gitItem.PSIsContainer) { return $false }
    $text = Get-Content -LiteralPath $gitPath -Raw -ErrorAction SilentlyContinue
    if ($text -match '(?im)^gitdir:\s*(.+)$') {
        $gitDir = $Matches[1].Trim()
        if (-not [System.IO.Path]::IsPathRooted($gitDir)) {
            $gitDir = Join-Path $Path $gitDir
        }
        $resolvedGitDir = ConvertTo-NightlyCanonicalPath -Path $gitDir
        $clone = ConvertTo-NightlyCanonicalPath -Path $Path
        $sep = [System.IO.Path]::DirectorySeparatorChar
        if (-not ($resolvedGitDir.StartsWith($clone + $sep, [StringComparison]::OrdinalIgnoreCase) -or
                  [string]::Equals($resolvedGitDir, $clone, [StringComparison]::OrdinalIgnoreCase))) {
            return $true
        }
        $common = Join-Path $resolvedGitDir 'commondir'
        if (Test-Path -LiteralPath $common) { return $true }
    }
    return $true
}

function Get-NightlyOriginUrl {
    param([string]$ClonePath)
    $config = Join-Path $ClonePath (Join-Path '.git' 'config')
    if (-not (Test-Path -LiteralPath $config)) { return '' }
    # Let Git decode quoted/escaped config values, and select origin specifically.
    # --file reads only this clone's config; includes and global config grant no authority.
    # -c overrides do not change the origin url this function returns.
    $value = & git -c core.fsmonitor=false -c gc.auto=0 -c maintenance.auto=false config --file $config --no-includes --get remote.origin.url 2>$null
    if ($LASTEXITCODE -eq 0) { return ($value -join "`n") }
    return ''
}

function Get-NightlySha256Text {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $sha.Dispose()
    }
}

function Get-NightlyFileSha256 {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        throw ('missing file for hash {0}' -f $Path)
    }
    # The disk read is compiled (NightlyClassNames.FileSha256).
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Get-NightlyFileSha256' }
    return [Antiphon.Nightly.NightlyClassNames]::FileSha256($Path)
}

# Path helpers for the atomic writers, called several times per chunk: .NET calls with the same answers as
# Test-Path -LiteralPath, New-Item -ItemType Directory -Force and Remove-Item -Force -ErrorAction
# SilentlyContinue, without each cmdlet's per-call cost.
function Test-NightlyPathExists([string]$Path) {
    return ([System.IO.File]::Exists($Path) -or [System.IO.Directory]::Exists($Path))
}

function Remove-NightlyFileQuietly([string]$Path) {
    if (-not [System.IO.File]::Exists($Path)) { return }
    try {
        [System.IO.File]::SetAttributes($Path, [System.IO.FileAttributes]::Normal)
        [System.IO.File]::Delete($Path)
    } catch { }
}

function Write-NightlyAtomicJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Object
    )
    $json = $Object | ConvertTo-Json -Depth 12
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-NightlyPathExists $dir)) {
        [void][System.IO.Directory]::CreateDirectory($dir)
    }
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $bak = $Path + '.bak'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tmp, $json, $utf8)
    if (Test-NightlyPathExists $Path) {
        [System.IO.File]::Replace($tmp, $Path, $bak)
        Remove-NightlyFileQuietly $bak
    } else {
        [System.IO.File]::Move($tmp, $Path)
    }
}

function Import-NightlySeams {
    param([string]$SeamsPath)
    $script:NightlySeams = @{
        UtcNow = $null
        Git = $null
        StartProcess = $null
        IsProcessAlive = $null
        GetProcessStartTimeUtc = $null
        GetParentProcess = $null
        ResolveReparse = $null
        DockerInfo = $null
        DiskFree = $null
        TracePath = $null
        WatchdogSnapshot = $null
    }
    if ([string]::IsNullOrWhiteSpace($SeamsPath)) { return }
    if (-not (Test-Path -LiteralPath $SeamsPath)) {
        throw ('SeamsPath not found: {0}' -f $SeamsPath)
    }
    . $SeamsPath
    if ($NightlySeams) {
        foreach ($k in @($NightlySeams.Keys)) {
            $script:NightlySeams[$k] = $NightlySeams[$k]
        }
    }
}

function Write-NightlyTrace {
    param([string]$Kind, [string]$Message)
    $line = ('{0} {1} {2}' -f (Get-NightlyUtcNow).ToString('o'), $Kind, $Message)
    if ($script:NightlySeams -and $script:NightlySeams.TracePath) {
        # Add-Content's line, ASCII and the platform newline, without the provider's cost per trace line.
        $tracePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath([string]$script:NightlySeams.TracePath)
        [System.IO.File]::AppendAllText($tracePath, $line + [Environment]::NewLine, [System.Text.Encoding]::ASCII)
    }
}

function Start-NightlyProcess {
    param(
        [hashtable]$StartParams,
        [hashtable]$Environment = $null
    )
    if ($null -eq $Environment) {
        return Start-Process @StartParams
    }
    $backup = @{}
    $current = [System.Environment]::GetEnvironmentVariables('Process')
    foreach ($entry in $current.GetEnumerator()) {
        $backup[[string]$entry.Key] = [string]$entry.Value
    }
    try {
        foreach ($name in @($backup.Keys)) {
            $present = $false
            foreach ($k in $Environment.Keys) {
                if ([string]::Equals([string]$k, [string]$name, [StringComparison]::OrdinalIgnoreCase)) {
                    $present = $true
                    break
                }
            }
            if (-not $present) {
                [System.Environment]::SetEnvironmentVariable([string]$name, $null, 'Process')
            }
        }
        foreach ($k in $Environment.Keys) {
            $val = [string]$Environment[$k]
            if ([string]::IsNullOrEmpty($val)) {
                [System.Environment]::SetEnvironmentVariable([string]$k, $null, 'Process')
            } else {
                [System.Environment]::SetEnvironmentVariable([string]$k, $val, 'Process')
            }
        }
        return Start-Process @StartParams
    } finally {
        $after = [System.Environment]::GetEnvironmentVariables('Process')
        foreach ($entry in $after.GetEnumerator()) {
            $name = [string]$entry.Key
            if (-not $backup.ContainsKey($name)) {
                [System.Environment]::SetEnvironmentVariable($name, $null, 'Process')
            }
        }
        foreach ($name in $backup.Keys) {
            [System.Environment]::SetEnvironmentVariable([string]$name, [string]$backup[$name], 'Process')
        }
    }
}

function Test-NightlyProcessAlive {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    if ($script:NightlySeams -and $script:NightlySeams.IsProcessAlive) {
        return [bool]$script:NightlySeams.IsProcessAlive.Invoke($ProcessId)
    }
    try {
        $null = Get-Process -Id $ProcessId -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Get-NightlyProcessStartUtc {
    param([int]$ProcessId)
    if ($script:NightlySeams -and $script:NightlySeams.GetProcessStartTimeUtc) {
        return $script:NightlySeams.GetProcessStartTimeUtc.Invoke($ProcessId)
    }
    try {
        $p = Get-Process -Id $ProcessId -ErrorAction Stop
        return $p.StartTime.ToUniversalTime()
    } catch {
        return $null
    }
}

function Get-NightlyParentProcess {
    param([int]$ProcessId = $PID)
    if ($script:NightlySeams -and $script:NightlySeams.GetParentProcess) {
        return $script:NightlySeams.GetParentProcess.Invoke($ProcessId)
    }
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Get-NightlyParentProcess' }
    try {
        # Compiled (NightlyProcesses.ParentProcessId): the process's own record on Windows, /proc on Linux.
        $parentId = [Antiphon.Nightly.NightlyProcesses]::ParentProcessId($ProcessId)
        if ($parentId -lt 0) { return [pscustomobject]@{ Id = 0; StartTimeUtc = $null } }
        return [pscustomobject]@{
            Id = $parentId
            StartTimeUtc = (Get-NightlyProcessStartUtc -ProcessId $parentId)
        }
    } catch {
        return [pscustomobject]@{ Id = 0; StartTimeUtc = $null }
    }
}

function Invoke-NightlyGit {
    param([string]$WorkingDirectory, [string[]]$Arguments)
    Write-NightlyTrace -Kind 'git' -Message ($Arguments -join ' ')
    if ($script:NightlySeams -and $script:NightlySeams.Git) {
        $r = $script:NightlySeams.Git.Invoke($WorkingDirectory, $Arguments)
        if ($r -is [hashtable] -or $r.PSObject) { return $r }
        return [pscustomobject]@{ ExitCode = 0; Output = [string]$r }
    }
    # -c overrides a system core.fsmonitor=true without writing repo config.
    # Object ids, refs and the caller's arguments are unchanged. This does not
    # identify a Windows pipe holder.
    $output = & git -c core.fsmonitor=false -c gc.auto=0 -c maintenance.auto=false -C $WorkingDirectory @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = [int]$LASTEXITCODE
        Output = ($output | Out-String)
    }
}

function New-NightlyRunId {
    return [guid]::NewGuid().ToString('N')
}

function Read-NightlyLockRecord {
    param([string]$LockPath)
    if (-not (Test-Path -LiteralPath $LockPath)) { return $null }
    # File.ReadAllText uses FileShare.Read. Windows refuses that while the
    # owner holds FileAccess.Write and FileShare.Read: the reader's share
    # does not allow the existing writer (ERROR_SHARING_VIOLATION). Exit
    # then leaves run.lock in place and a hop child sees missing-lock.
    # FileShare.ReadWrite allows that writer. A FileShare.None holder,
    # including the reclaim compare, still denies this read. This Linux
    # host allows FileShare.Read against the same owner handle, so the
    # Windows refusal is not reproduced here.
    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::Open(
            $LockPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true)
        $stream = $null
        $raw = $reader.ReadToEnd()
        if ($raw.Trim().StartsWith('{')) {
            return ($raw | ConvertFrom-Json)
        }
        $parts = @($raw.Trim() -split '\s+', 3)
        $lockPid = 0
        [void][int]::TryParse($parts[0], [ref]$lockPid)
        return [pscustomobject]@{
            pid = $lockPid
            startedAt = $(if ($parts.Count -gt 1) { $parts[1] } else { $null })
            runId = $(if ($parts.Count -gt 2) { $parts[2] } else { $null })
            parentPid = 0
            parentStartedAt = $null
        }
    } catch {
        return $null
    } finally {
        if ($null -ne $reader) {
            try { $reader.Dispose() } catch { }
        }
        if ($null -ne $stream) {
            try { $stream.Dispose() } catch { }
        }
    }
}

function Get-NightlySeamText {
    param($Delegate)
    if (-not $Delegate) { return '' }
    $items = @($Delegate.Invoke())
    if ($items.Count -lt 1 -or $null -eq $items[0]) { return '' }
    return ([string]$items[0]).Trim()
}

function Test-NightlyWindows {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Test-NightlyLinux {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
}

function Get-NightlyBootId {
    if ($script:NightlySeams -and $script:NightlySeams.GetBootId) {
        return (Get-NightlySeamText -Delegate $script:NightlySeams.GetBootId)
    }
    if (Test-NightlyWindows) {
        try {
            $item = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters' -Name 'BootId' -ErrorAction Stop
            if ($null -ne $item -and $null -ne $item.BootId) {
                return ([int64]$item.BootId).ToString([System.Globalization.CultureInfo]::InvariantCulture)
            }
        } catch { }
        return ''
    }
    if (Test-NightlyLinux) {
        try {
            $path = '/proc/sys/kernel/random/boot_id'
            if (Test-Path -LiteralPath $path) {
                return ([System.IO.File]::ReadAllText($path)).Trim()
            }
        } catch { }
    }
    return ''
}

function Get-NightlyHostId {
    if ($script:NightlySeams -and $script:NightlySeams.GetHostId) {
        return (Get-NightlySeamText -Delegate $script:NightlySeams.GetHostId)
    }
    if (Test-NightlyWindows) {
        try {
            $item = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name 'MachineGuid' -ErrorAction Stop
            if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace([string]$item.MachineGuid)) {
                return ([string]$item.MachineGuid).Trim()
            }
        } catch { }
        return ''
    }
    if (Test-NightlyLinux) {
        foreach ($path in @('/etc/machine-id', '/var/lib/dbus/machine-id')) {
            try {
                if (Test-Path -LiteralPath $path) {
                    $text = ([System.IO.File]::ReadAllText($path)).Trim()
                    if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
                }
            } catch { }
        }
    }
    return ''
}

function Write-NightlyRunLine {
    param([string]$Message)
    Write-Host ('[{0}] {1}' -f (Get-NightlyUtcNow).ToString('o'), $Message)
}
