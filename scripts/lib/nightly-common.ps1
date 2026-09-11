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
    $text = Get-Content -LiteralPath $config -Raw -ErrorAction SilentlyContinue
    $m = [regex]::Match([string]$text, '(?im)^\s*url\s*=\s*(.+)$')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
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
    $output = & git -C $WorkingDirectory @Arguments 2>&1
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
    try {
        $raw = [System.IO.File]::ReadAllText($LockPath)
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
            $startOk = [string]::Equals($startText, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase) -or
                       [string]::Equals([string]$existing.startedAt, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase) -or
                       [string]::Equals([string]$existing.parentStartedAt, $ContinueParentStartedAt, [StringComparison]::OrdinalIgnoreCase)
        }
        if (-not ($parentAlive -and $runOk -and $pidOk -and $startOk)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'hop-identity'; OwnsLock = $false; Record = $existing }
        }
        $script:NightlyOwnedLock = $false
        return [pscustomobject]@{ Ok = $true; Reason = 'hop'; OwnsLock = $false; Record = $existing }
    }

    $record = [ordered]@{
        pid = $PID
        startedAt = (Get-NightlyUtcNow).ToString('o')
        runId = $RunId
        parentPid = [int]$parent.Id
        parentStartedAt = $(if ($parent.StartTimeUtc) { ([datetime]$parent.StartTimeUtc).ToUniversalTime().ToString('o') } else { $null })
    }
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
        $alive = $false
        if ($existing -and [int]$existing.pid -gt 0) {
            $alive = Test-NightlyProcessAlive -ProcessId ([int]$existing.pid)
        }
        if ($alive) {
            $samePid = [int]$existing.pid -eq $PID
            $sameRun = [string]::Equals([string]$existing.runId, $RunId, [StringComparison]::OrdinalIgnoreCase)
            if ($samePid -and $sameRun -and [string]::IsNullOrWhiteSpace($ContinueRunId)) {
                $script:NightlyOwnedLock = $true
                return [pscustomobject]@{ Ok = $true; Reason = 'acquired'; OwnsLock = $true; Record = $existing }
            }
            return [pscustomobject]@{ Ok = $false; Reason = 'live-owner'; OwnsLock = $false; Record = $existing }
        }
        return [pscustomobject]@{ Ok = $false; Reason = 'lock-held'; OwnsLock = $false; Record = $existing }
    }
}

function Exit-NightlyExclusiveLock {
    param([string]$StateRoot, [string]$RunId)
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
