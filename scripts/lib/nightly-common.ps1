#requires -Version 5.1
# CARD-0487: shared nightly path, ownership, lock, atomic write, clock and seam helpers.
# ASCII-only: parseable under Windows PowerShell 5.1.

if ($script:AntiphonNightlyCommonLoaded) { return }
$script:AntiphonNightlyCommonLoaded = $true

$script:NightlySeams = $null
$script:NightlyOwnedLock = $null
$script:NightlyLockStream = $null

function Get-NightlyUtcNow {
    if ($script:NightlySeams -and $script:NightlySeams.UtcNow) {
        $value = $script:NightlySeams.UtcNow.Invoke()
        $arr = @($value)
        if ($arr.Count -gt 0) { return [datetime]$arr[0] }
    }
    return [datetime]::UtcNow
}

# CARD-0545 D-10: shared by the wrapper (localDueDate) and the health evaluator.
function Get-NightlyLondonTimeZone {
    $tz = $null
    try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('GMT Standard Time') } catch { }
    if ($null -eq $tz) {
        try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Europe/London') } catch { }
    }
    if ($null -eq $tz) { throw 'Europe/London timezone not available' }
    return $tz
}

function ConvertTo-NightlyLondonLocal {
    param([datetime]$Utc)
    if ($Utc.Kind -eq [DateTimeKind]::Unspecified) {
        $Utc = [datetime]::SpecifyKind($Utc, [DateTimeKind]::Utc)
    } else {
        $Utc = $Utc.ToUniversalTime()
    }
    return [TimeZoneInfo]::ConvertTimeFromUtc($Utc, (Get-NightlyLondonTimeZone))
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
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
            if (Test-NightlyReparseItem -Item $item) { return $true }
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

function Test-NightlyPathContained {
    param([string]$Inner, [string]$Outer)
    $a = ConvertTo-NightlyCanonicalPath -Path $Inner
    $b = ConvertTo-NightlyCanonicalPath -Path $Outer
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if ([string]::Equals($a, $b, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $a.StartsWith($b + $sep, [StringComparison]::OrdinalIgnoreCase)
}

function Test-NightlyRunContextOwnership {
    param(
        [Parameter(Mandatory = $true)][string]$ClonePath,
        [string]$StateRoot,
        [string]$LogRoot,
        [string]$ExpectedOrigin = 'https://github.com/michal-ciechan/Antiphon'
    )
    $result = [ordered]@{
        Ok = $false
        Reason = ''
        Field = ''
        Clone = $ClonePath
    }
    try {
        $clone = ConvertTo-NightlyResolvedPath -Path $ClonePath
    } catch {
        $result.Reason = $_.Exception.Message
        $result.Field = 'reparse'
        return [pscustomobject]$result
    }
    if (Test-NightlySharedTree -Path $clone) {
        $result.Reason = 'shared-tree'
        $result.Field = 'clone'
        return [pscustomobject]$result
    }
    if (Test-NightlyGitWorktree -Path $clone) {
        $result.Reason = 'linked-worktree'
        $result.Field = 'clone'
        return [pscustomobject]$result
    }
    $marker = Join-Path $clone '.antiphon-nightly-owned'
    if (-not (Test-Path -LiteralPath $marker)) {
        $result.Reason = 'unmarked-clone'
        $result.Field = 'ownership'
        return [pscustomobject]$result
    }
    $origin = Get-NightlyOriginUrl -ClonePath $clone
    if (-not [string]::Equals($origin, $ExpectedOrigin, [StringComparison]::OrdinalIgnoreCase)) {
        $result.Reason = 'wrong-origin'
        $result.Field = 'origin'
        return [pscustomobject]$result
    }
    if (-not [string]::IsNullOrWhiteSpace($StateRoot)) {
        $state = ConvertTo-NightlyCanonicalPath -Path $StateRoot
        if (Test-NightlyPathContained -Inner $state -Outer $clone) {
            $result.Reason = 'state-overlaps-clone'
            $result.Field = 'state'
            return [pscustomobject]$result
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($LogRoot)) {
        $logs = ConvertTo-NightlyCanonicalPath -Path $LogRoot
        if (Test-NightlyPathContained -Inner $logs -Outer $clone) {
            $result.Reason = 'log-overlaps-clone'
            $result.Field = 'log'
            return [pscustomobject]$result
        }
    }
    $result.Ok = $true
    $result.Clone = $clone
    return [pscustomobject]$result
}

function ConvertTo-NightlyCanonicalJson {
    param($Object)
    function Write-Node {
        param($Node)
        if ($null -eq $Node) { return 'null' }
        if ($Node -is [string]) {
            $escaped = $Node.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
            return ('"{0}"' -f $escaped)
        }
        if ($Node -is [bool]) {
            if ($Node) { return 'true' } else { return 'false' }
        }
        if ($Node -is [int] -or $Node -is [long] -or $Node -is [double] -or $Node -is [decimal]) {
            return ([string]$Node)
        }
        if ($Node -is [System.Collections.IDictionary]) {
            $pairs = @()
            $dictNode = [System.Collections.IDictionary]$Node
            $enumNode = $dictNode.GetEnumerator()
            while ($enumNode.MoveNext()) {
                $pairs += [pscustomobject]@{ K = [string]$enumNode.Key; V = $enumNode.Value }
            }
            $pairs = @($pairs | Where-Object { $_.K } | Sort-Object K)
            $parts = @()
            foreach ($pair in $pairs) {
                $parts += ('"{0}":{1}' -f $pair.K, (Write-Node -Node $pair.V))
            }
            return ('{{{0}}}' -f ($parts -join ','))
        }
        if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string]) -and -not ($Node -is [System.Collections.IDictionary])) {
            $parts = @()
            foreach ($item in $Node) { $parts += (Write-Node -Node $item) }
            return ('[{0}]' -f ($parts -join ','))
        }
        if ($Node.PSObject -and $Node.PSObject.Properties) {
            $map = New-Object 'System.Collections.Specialized.OrderedDictionary'
            foreach ($p in $Node.PSObject.Properties) {
                if ($p.Name -eq 'Count' -and $Node -is [System.Array]) { continue }
                if ($p.Name -eq 'Keys' -or $p.Name -eq 'Values' -or $p.Name -eq 'SyncRoot' -or $p.Name -eq 'IsFixedSize' -or $p.Name -eq 'IsReadOnly' -or $p.Name -eq 'IsSynchronized') { continue }
                if (-not $map.Contains([string]$p.Name)) {
                    $map.Add([string]$p.Name, $p.Value)
                }
            }
            return (Write-Node -Node $map)
        }
        return (Write-Node -Node ([string]$Node))
    }
    return (Write-Node -Node $Object)
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
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = $sha.ComputeHash($stream)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function ConvertFrom-NightlyJsonMap {
    param($Object)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) { return $Object }
    $map = [ordered]@{}
    foreach ($p in $Object.PSObject.Properties) {
        $map[$p.Name] = $p.Value
    }
    return $map
}

function Write-NightlyAtomicJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Object
    )
    $json = $Object | ConvertTo-Json -Depth 12
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $bak = $Path + '.bak'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tmp, $json, $utf8)
    if (Test-Path -LiteralPath $Path) {
        [System.IO.File]::Replace($tmp, $Path, $bak)
        if (Test-Path -LiteralPath $bak) {
            Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue
        }
    } else {
        [System.IO.File]::Move($tmp, $Path)
    }
}

function Read-NightlyJsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $raw = [System.IO.File]::ReadAllText($Path)
    return ($raw | ConvertFrom-Json)
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
        Add-Content -LiteralPath ([string]$script:NightlySeams.TracePath) -Value $line -Encoding ASCII
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
    try {
        $row = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $ProcessId) -ErrorAction Stop
        $parentId = [int]$row.ParentProcessId
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

function Get-NightlyLockPath {
    param([string]$StateRoot)
    return (Join-Path $StateRoot 'run.lock')
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

function Get-NightlyLockFieldText {
    param($Record, [string]$Name)
    if ($null -eq $Record) { return '' }
    $prop = $Record.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return '' }
    $value = $prop.Value
    if ($value -is [datetime]) {
        return $value.ToUniversalTime().ToString('o')
    }
    return ([string]$value).Trim()
}

function Get-NightlyLockFieldPid {
    param($Record, [string]$Name)
    $text = Get-NightlyLockFieldText -Record $Record -Name $Name
    if ([string]::IsNullOrWhiteSpace($text)) { return 0 }
    $n = 0
    if ([int]::TryParse($text, [ref]$n)) { return $n }
    return 0
}

function Get-NightlyLockOptionalPid {
    param($Record, [string]$Name)
    if ($null -eq $Record) { return '' }
    $prop = $Record.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return '' }
    $text = [string]$prop.Value
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $n = 0
    if ([int]::TryParse($text, [ref]$n)) { return $n }
    return ''
}

function Test-NightlyDecimalBootId {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return $Value -match '^[0-9]+$'
}

function Resolve-NightlyStaleLock {
    param($Existing, [string]$RunId, [string]$BootId, [string]$HostId)
    if ($null -eq $Existing) {
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    $recordBoot = Get-NightlyLockFieldText -Record $Existing -Name 'bootId'
    $recordHost = Get-NightlyLockFieldText -Record $Existing -Name 'hostId'
    $currentBoot = ''
    $currentHost = ''
    if (-not [string]::IsNullOrWhiteSpace($BootId)) { $currentBoot = $BootId.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($HostId)) { $currentHost = $HostId.Trim() }
    $bootBoth = (-not [string]::IsNullOrWhiteSpace($recordBoot)) -and (-not [string]::IsNullOrWhiteSpace($currentBoot))
    $hostBoth = (-not [string]::IsNullOrWhiteSpace($recordHost)) -and (-not [string]::IsNullOrWhiteSpace($currentHost))
    # A recycled pid after reboot is reclaimed before the live-owner check.
    # dead-owner-rebooted requires both boot ids to be registry BootId decimals.
    # CARD-1389 D-2 removed reclaim when a Linux boot_id UUID changes, so a
    # stale run.lock after a Linux reboot stays lock-held until removed by hand.
    # A lock written before that pin, whose bootId is a LastBootUpTime string,
    # also stays lock-held across one Windows reboot and needs that removal.
    if ($hostBoth -and $bootBoth -and [string]::Equals($recordHost, $currentHost, [StringComparison]::Ordinal) -and -not [string]::Equals($recordBoot, $currentBoot, [StringComparison]::Ordinal)) {
        if ((Test-NightlyDecimalBootId -Value $recordBoot) -and (Test-NightlyDecimalBootId -Value $currentBoot)) {
            return [pscustomobject]@{ Action = 'reclaim'; Reason = 'dead-owner-rebooted' }
        }
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    $ownerPid = Get-NightlyLockFieldPid -Record $Existing -Name 'pid'
    $ownerAlive = $false
    if ($ownerPid -gt 0) {
        $ownerAlive = Test-NightlyProcessAlive -ProcessId $ownerPid
    }
    if ($ownerAlive) {
        $samePid = ($ownerPid -eq $PID)
        $recordRun = Get-NightlyLockFieldText -Record $Existing -Name 'runId'
        $sameRun = [string]::Equals($recordRun, $RunId, [StringComparison]::OrdinalIgnoreCase)
        if ($samePid -and $sameRun) {
            return [pscustomobject]@{ Action = 'reacquire'; Reason = 'acquired' }
        }
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'live-owner' }
    }
    if ($ownerPid -le 0) {
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    if (($hostBoth -and -not [string]::Equals($recordHost, $currentHost, [StringComparison]::Ordinal)) -or ((-not [string]::IsNullOrWhiteSpace($recordHost)) -and [string]::IsNullOrWhiteSpace($currentHost))) {
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    if ((-not [string]::IsNullOrWhiteSpace($recordBoot)) -and [string]::IsNullOrWhiteSpace($currentBoot)) {
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    $parentPid = Get-NightlyLockFieldPid -Record $Existing -Name 'parentPid'
    $parentAlive = $false
    if ($parentPid -gt 0) {
        $parentAlive = Test-NightlyProcessAlive -ProcessId $parentPid
    }
    if ($parentPid -le 0 -or $parentAlive) {
        return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
    }
    if ($bootBoth -and $hostBoth -and [string]::Equals($recordBoot, $currentBoot, [StringComparison]::Ordinal) -and [string]::Equals($recordHost, $currentHost, [StringComparison]::Ordinal)) {
        return [pscustomobject]@{ Action = 'reclaim'; Reason = 'dead-owner-same-boot' }
    }
    if ([string]::IsNullOrWhiteSpace($recordBoot) -and [string]::IsNullOrWhiteSpace($recordHost)) {
        return [pscustomobject]@{ Action = 'reclaim'; Reason = 'dead-owner-legacy' }
    }
    return [pscustomobject]@{ Action = 'refuse'; Reason = 'lock-held' }
}

function New-NightlyLockRecord {
    param(
        [string]$RunId,
        [string]$BootId,
        [string]$HostId,
        $Parent
    )
    $processStart = Get-NightlyProcessStartUtc -ProcessId $PID
    $processText = ''
    if ($processStart) { $processText = ([datetime]$processStart).ToUniversalTime().ToString('o') }
    $parentId = 0
    $parentStart = $null
    if ($Parent) {
        $parentId = [int]$Parent.Id
        if ($Parent.StartTimeUtc) { $parentStart = ([datetime]$Parent.StartTimeUtc).ToUniversalTime().ToString('o') }
    }
    $bootText = ''
    $hostText = ''
    if (-not [string]::IsNullOrWhiteSpace($BootId)) { $bootText = $BootId.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($HostId)) { $hostText = $HostId.Trim() }
    return [ordered]@{
        pid = $PID
        processStartedAt = $processText
        startedAt = (Get-NightlyUtcNow).ToString('o')
        runId = $RunId
        parentPid = $parentId
        parentStartedAt = $parentStart
        bootId = $bootText
        hostId = $hostText
    }
}

function Write-NightlyLockReclaim {
    param(
        [string]$StateRoot,
        [string]$Reason,
        $Existing,
        [string]$NewRunId,
        [string]$BootId,
        [string]$HostId
    )
    $bootText = ''
    $hostText = ''
    if (-not [string]::IsNullOrWhiteSpace($BootId)) { $bootText = $BootId.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($HostId)) { $hostText = $HostId.Trim() }
    $body = [ordered]@{
        reason = $Reason
        at = (Get-NightlyUtcNow).ToString('o')
        previousPid = (Get-NightlyLockOptionalPid -Record $Existing -Name 'pid')
        previousParentPid = (Get-NightlyLockOptionalPid -Record $Existing -Name 'parentPid')
        previousRunId = (Get-NightlyLockFieldText -Record $Existing -Name 'runId')
        previousProcessStartedAt = (Get-NightlyLockFieldText -Record $Existing -Name 'processStartedAt')
        previousBootId = (Get-NightlyLockFieldText -Record $Existing -Name 'bootId')
        previousHostId = (Get-NightlyLockFieldText -Record $Existing -Name 'hostId')
        newRunId = $NewRunId
        bootId = $bootText
        hostId = $hostText
    }
    Write-NightlyAtomicJson -Path (Join-Path $StateRoot 'lock-reclaim.json') -Object $body
    Write-NightlyRunLine ('{0} {1}' -f $Reason, (Get-NightlyLockFieldText -Record $Existing -Name 'runId'))
}

function Test-NightlyReclaimIdentity {
    param($Left, $Right)
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ((Get-NightlyLockFieldPid -Record $Left -Name 'pid') -ne (Get-NightlyLockFieldPid -Record $Right -Name 'pid')) {
        return $false
    }
    foreach ($name in @('runId', 'bootId', 'hostId')) {
        $leftText = Get-NightlyLockFieldText -Record $Left -Name $name
        $rightText = Get-NightlyLockFieldText -Record $Right -Name $name
        if (-not [string]::Equals($leftText, $rightText, [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function Get-NightlyReclaimDenied {
    param([string]$LockPath)
    $raced = $null
    try {
        $raced = Read-NightlyLockRecord -LockPath $LockPath
    } catch {
        $raced = $null
    }
    if ($null -ne $raced) {
        $racedPid = Get-NightlyLockFieldPid -Record $raced -Name 'pid'
        if ($racedPid -gt 0 -and (Test-NightlyProcessAlive -ProcessId $racedPid)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'live-owner'; OwnsLock = $false; Record = $raced }
        }
    }
    return [pscustomobject]@{ Ok = $false; Reason = 'lock-held'; OwnsLock = $false; Record = $raced }
}

function Read-NightlyLockBytes {
    param($Stream)
    $length = 0
    try { $length = [int]$Stream.Length } catch { $length = 0 }
    if ($length -lt 0) { $length = 0 }
    $bytes = New-Object byte[] $length
    $offset = 0
    while ($offset -lt $length) {
        $n = $Stream.Read($bytes, $offset, ($length - $offset))
        if ($n -le 0) { break }
        $offset += $n
    }
    return $bytes
}

function ConvertFrom-NightlyLockBytes {
    param([byte[]]$Bytes)
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) { return $null }
    $start = 0
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 239 -and $Bytes[1] -eq 187 -and $Bytes[2] -eq 191) {
        $start = 3
    }
    if ($start -ge $Bytes.Length) { return $null }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $text = $utf8.GetString($Bytes, $start, ($Bytes.Length - $start))
    if ([string]::IsNullOrWhiteSpace($text) -or -not $text.Trim().StartsWith('{')) { return $null }
    try {
        return ($text | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Restore-NightlyLockBytes {
    param($Stream, [byte[]]$Previous)
    try {
        $Stream.SetLength(0)
        $Stream.Position = 0
        if ($null -ne $Previous -and $Previous.Length -gt 0) {
            $Stream.Write($Previous, 0, $Previous.Length)
        }
        $Stream.Flush()
    } catch { }
}

function Open-NightlySharedLockStream {
    param([string]$LockPath)
    return [System.IO.File]::Open(
        $LockPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::Read)
}

function Enter-NightlyReclaimedLock {
    param(
        [string]$StateRoot,
        [string]$LockPath,
        $Existing,
        [string]$RunId,
        [string]$Reason,
        [string]$BootId,
        [string]$HostId,
        $Parent
    )
    # Rewrite through the open handle so a second attempt cannot unlink this record.
    $record = New-NightlyLockRecord -RunId $RunId -BootId $BootId -HostId $HostId -Parent $Parent
    $payload = ($record | ConvertTo-Json -Compress)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)
    $stream = $null
    $owned = $false
    try {
        try {
            $stream = [System.IO.File]::Open(
                $LockPath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        } catch {
            return (Get-NightlyReclaimDenied -LockPath $LockPath)
        }
        $previous = Read-NightlyLockBytes -Stream $stream
        $onDisk = ConvertFrom-NightlyLockBytes -Bytes $previous
        if (-not (Test-NightlyReclaimIdentity -Left $onDisk -Right $Existing)) {
            try { $stream.Dispose() } catch { }
            $stream = $null
            return (Get-NightlyReclaimDenied -LockPath $LockPath)
        }
        try {
            $stream.SetLength(0)
            $stream.Position = 0
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
        } catch {
            Restore-NightlyLockBytes -Stream $stream -Previous $previous
            try { $stream.Dispose() } catch { }
            $stream = $null
            return (Get-NightlyReclaimDenied -LockPath $LockPath)
        }
        try {
            Write-NightlyLockReclaim -StateRoot $StateRoot -Reason $Reason -Existing $Existing -NewRunId $RunId -BootId $BootId -HostId $HostId
        } catch {
            Restore-NightlyLockBytes -Stream $stream -Previous $previous
            try { $stream.Dispose() } catch { }
            $stream = $null
            return [pscustomobject]@{ Ok = $false; Reason = 'lock-held'; OwnsLock = $false; Record = $Existing }
        }
        # Readers share the handle after the exclusive compare and publish.
        $exclusive = $stream
        $stream = $null
        try { $exclusive.Dispose() } catch { }
        $shared = $null
        try {
            $shared = Open-NightlySharedLockStream -LockPath $LockPath
        } catch {
            $shared = $null
            # The published record stays ours. A live pid still refuses a
            # contender, so ownership does not change when the shared handle
            # cannot be retaken. The line is the only signal. A log failure
            # must not replace that Ok result.
            $detail = 'unreadable'
            try { $detail = [string]$_.Exception.Message } catch { }
            $detail = $detail.Replace("`r", ' ').Replace("`n", ' ')
            $line = 'reclaim-shared-reopen-failed ' + $detail
            try { Write-NightlyRunLine $line } catch { }
            try { Write-NightlyTrace -Kind 'lock' -Message $line } catch { }
        }
        $script:NightlyLockStream = $shared
        $owned = $true
        $script:NightlyOwnedLock = $true
        return [pscustomobject]@{ Ok = $true; Reason = 'reclaimed'; OwnsLock = $true; Record = [pscustomobject]$record }
    } finally {
        if (-not $owned -and $null -ne $stream) {
            try { $stream.Dispose() } catch { }
        }
    }
}

function Enter-NightlyExclusiveLock {
    param(
        [Parameter(Mandatory = $true)][string]$StateRoot,
        [Parameter(Mandatory = $true)][string]$RunId,
        [string]$ContinueRunId = '',
        [int]$ContinueParentPid = 0,
        [string]$ContinueParentStartedAt = ''
    )
    New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
    $lockPath = Get-NightlyLockPath -StateRoot $StateRoot
    $parent = Get-NightlyParentProcess -ProcessId $PID
    if (-not [string]::IsNullOrWhiteSpace($ContinueRunId)) {
        $existing = Read-NightlyLockRecord -LockPath $lockPath
        if ($null -eq $existing) {
            return [pscustomobject]@{ Ok = $false; Reason = 'missing-lock'; OwnsLock = $false }
        }
        $parentAlive = Test-NightlyProcessAlive -ProcessId $ContinueParentPid
        $parentStart = $null
        if ($parentAlive) { $parentStart = Get-NightlyProcessStartUtc -ProcessId $ContinueParentPid }
        $startText = ''
        if ($null -ne $parentStart) { $startText = ([datetime]$parentStart).ToUniversalTime().ToString('o') }
        $runOk = [string]::Equals([string]$existing.runId, $ContinueRunId, [StringComparison]::OrdinalIgnoreCase)
        $pidOk = ([int]$existing.pid -eq $ContinueParentPid) -or ([int]$existing.parentPid -eq $ContinueParentPid)
        $startOk = $true
        if (-not [string]::IsNullOrWhiteSpace($ContinueParentStartedAt)) {
            # Bind the owner timestamp captured at acquisition across the hop.
            # JSON can deserialize the stored ISO value into DateTime.
            $recordStart = [string]$existing.processStartedAt
            if ($existing.processStartedAt -is [datetime]) { $recordStart = $existing.processStartedAt.ToUniversalTime().ToString('o') }
            $startOk = [string]::Equals($startText, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase) -or
                       (-not [string]::IsNullOrWhiteSpace($recordStart) -and [string]::Equals($recordStart, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase)) -or
                       [string]::Equals([string]$existing.startedAt, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase) -or
                       [string]::Equals([string]$existing.parentStartedAt, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase)
        }
        if (-not ($parentAlive -and $runOk -and $pidOk -and $startOk)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'hop-identity'; OwnsLock = $false; Record = $existing }
        }
        $script:NightlyOwnedLock = $false
        return [pscustomobject]@{ Ok = $true; Reason = 'hop'; OwnsLock = $false; Record = $existing }
    }

    $bootId = Get-NightlyBootId
    $hostId = Get-NightlyHostId
    $record = New-NightlyLockRecord -RunId $RunId -BootId $bootId -HostId $hostId -Parent $parent
    $payload = ($record | ConvertTo-Json -Compress)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($payload)
    try {
        $stream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::Read)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
        $script:NightlyLockStream = $stream
        $script:NightlyOwnedLock = $true
        return [pscustomobject]@{ Ok = $true; Reason = 'acquired'; OwnsLock = $true; Record = [pscustomobject]$record }
    } catch [System.IO.IOException] {
        $existing = Read-NightlyLockRecord -LockPath $lockPath
        $decision = Resolve-NightlyStaleLock -Existing $existing -RunId $RunId -BootId $bootId -HostId $hostId
        if ($decision.Action -eq 'reacquire') {
            $script:NightlyOwnedLock = $true
            return [pscustomobject]@{ Ok = $true; Reason = 'acquired'; OwnsLock = $true; Record = $existing }
        }
        if ($decision.Action -eq 'reclaim') {
            return Enter-NightlyReclaimedLock -StateRoot $StateRoot -LockPath $lockPath -Existing $existing -RunId $RunId -Reason $decision.Reason -BootId $bootId -HostId $hostId -Parent $parent
        }
        return [pscustomobject]@{ Ok = $false; Reason = $decision.Reason; OwnsLock = $false; Record = $existing }
    }
}

function Exit-NightlyExclusiveLock {
    param([string]$StateRoot, [string]$RunId)
    # lock-reclaim.json is the durable reason the per-task lock was replaced.
    if (-not $script:NightlyOwnedLock) { return }
    $lockPath = Get-NightlyLockPath -StateRoot $StateRoot
    $existing = Read-NightlyLockRecord -LockPath $lockPath
    $same = $false
    if ($existing) {
        $same = ([int]$existing.pid -eq $PID) -and
                ([string]::IsNullOrWhiteSpace($RunId) -or [string]::Equals([string]$existing.runId, $RunId, [StringComparison]::OrdinalIgnoreCase))
    }
    if ($script:NightlyLockStream) {
        try { $script:NightlyLockStream.Dispose() } catch { }
        $script:NightlyLockStream = $null
    }
    if ($same -and (Test-Path -LiteralPath $lockPath)) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }
    $script:NightlyOwnedLock = $false
}

function Write-NightlyRunLine {
    param([string]$Message)
    Write-Host ('[{0}] {1}' -f (Get-NightlyUtcNow).ToString('o'), $Message)
}
