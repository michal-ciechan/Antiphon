#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1405 daily encrypted Postgres snapshot and scratch restore drill.

    Default and -WhatIf print the plan and run no docker, age, or pg_restore.
    -Execute dumps with pg_dump -Fc inside the source container, checks
    pg_restore --list for TABLE DATA and a minimum byte size, encrypts with
    age, then copies the ciphertext into the snapshot directory. Retention
    keeps 14 exact dailies and prunes only after that new file is verified.
    -RestoreDrill restores the newest snapshot into a labelled scratch
    container and removes that container and volume unless -KeepScratch.

    The recipient and identity are -RecipientFile / -IdentityFile paths, or
    -RecipientRef / -IdentityRef environment-variable names whose values are
    those paths. This script has no schedule and does not enable one.
    Staging defaults to <LocalApplicationData>/antiphon/postgres-snapshot-staging;
    the root must be owned by the caller and owner-only, under parents that
    others cannot rename in. Docker must answer on a local unix:// or npipe://
    endpoint unless -AllowRemoteDocker is passed.
    ASCII-only. Do not pass a password or connection string.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [switch]$RestoreDrill,
    [string]$Container = 'antiphon-postgres',
    [string]$Database = 'antiphon',
    [string]$StagingPath = '',
    [string]$Destination = '',
    [string]$SyncRoot = '',
    [string]$RecipientFile = '',
    [string]$RecipientRef = '',
    [string]$IdentityFile = '',
    [string]$IdentityRef = '',
    [int]$Retain = 14,
    [int64]$MinimumDumpBytes = 1048576,
    [switch]$KeepScratch,
    [string]$ScratchContainer = 'antiphon-postgres-drill',
    [string]$ScratchDatabase = 'antiphon_drill',
    [string]$ScratchVolume = 'antiphon-postgres-drill-pgdata',
    [string]$SourceVolume = 'antiphon_pgdata',
    [string]$ScratchImage = 'postgres:16',
    [switch]$AllowRemoteDocker,
    [int]$StaleRunMinutes = 360
)

# A drill may reuse only a container that carries this label. These names stay
# refused even when that container is not the current source.
$script:SnapshotDrillLabel = 'antiphon.snapshot-drill'
$script:SnapshotDumpPattern = '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump\.age$'
$script:SnapshotProductionContainers = @(
    'antiphon-postgres',
    'antiphon-server2-postgres-postgres-1'
)
$script:SnapshotProductionVolumes = @(
    'antiphon_pgdata',
    'antiphon-server2-postgres_pgdata',
    'pgdata'
)

function Get-SnapshotPruneSet {
    param(
        [string[]]$Names,
        [int]$Retain = 14,
        [string]$NewName = '',
        [bool]$Verified = $false
    )
    $none = New-Object string[] 0
    if (-not $Verified) {
        return [pscustomobject]@{ Ok = $true; Drop = $none; Reason = 'unverified' }
    }
    if ($Retain -lt 1) {
        return [pscustomobject]@{ Ok = $false; Drop = $none; Reason = 'retain-below-one' }
    }
    $all = New-Object System.Collections.Generic.List[string]
    foreach ($name in @($Names)) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if ($name -notmatch $script:SnapshotDumpPattern) { continue }
        [void]$all.Add([string]$name)
    }
    if (-not [string]::IsNullOrWhiteSpace($NewName)) {
        if ($NewName -notmatch $script:SnapshotDumpPattern) {
            return [pscustomobject]@{ Ok = $false; Drop = $none; Reason = 'bad-name' }
        }
        if (-not $all.Contains($NewName)) { [void]$all.Add($NewName) }
    }
    $ordered = $all.ToArray()
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    [Array]::Reverse($ordered)
    if ($ordered.Length -le $Retain) {
        return [pscustomobject]@{ Ok = $true; Drop = $none; Reason = 'within-retain' }
    }
    $keep = New-Object System.Collections.Generic.List[string]
    $drop = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $ordered.Length; $i++) {
        if ($i -lt $Retain) { [void]$keep.Add($ordered[$i]) } else { [void]$drop.Add($ordered[$i]) }
    }
    if (-not [string]::IsNullOrWhiteSpace($NewName) -and -not $keep.Contains($NewName)) {
        return [pscustomobject]@{ Ok = $false; Drop = $none; Reason = 'would-drop-new' }
    }
    if (($ordered.Length - $drop.Count) -lt 1) {
        return [pscustomobject]@{ Ok = $false; Drop = $none; Reason = 'would-leave-zero' }
    }
    return [pscustomobject]@{ Ok = $true; Drop = $drop.ToArray(); Reason = 'prune' }
}

function Test-SnapshotToken {
    param([string]$Value)
    return $Value -match '^[A-Za-z0-9_.-]+$'
}

function Test-SnapshotImage {
    param([string]$Image)
    if ($Image -notmatch '^[A-Za-z0-9_.:/-]+$') { return $false }
    if ($Image -match '(?i)alpine') { return $false }
    return $true
}

function Test-SnapshotPathInside {
    param([string]$Parent, [string]$Child)
    if ([string]::IsNullOrWhiteSpace($Parent) -or [string]::IsNullOrWhiteSpace($Child)) { return $false }
    $sep = [System.IO.Path]::DirectorySeparatorChar
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd($sep) + $sep
    $childFull = [System.IO.Path]::GetFullPath($Child)
    return $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-SnapshotReferencePath {
    param([string]$File, [string]$RefName)
    if (-not [string]::IsNullOrWhiteSpace($File)) { return $File }
    if ([string]::IsNullOrWhiteSpace($RefName)) { return $null }
    $value = [Environment]::GetEnvironmentVariable($RefName)
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

function Get-SnapshotBareName {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    return $Value.Trim().TrimStart('/').TrimEnd('/')
}

function Test-SnapshotNameMatch {
    param([string]$Value, [string[]]$Candidates)
    $bare = Get-SnapshotBareName $Value
    if ([string]::IsNullOrWhiteSpace($bare)) { return $false }
    foreach ($candidate in @($Candidates)) {
        $other = Get-SnapshotBareName $candidate
        if ([string]::IsNullOrWhiteSpace($other)) { continue }
        if ($bare.Equals($other, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-SnapshotSameId {
    param([string]$Left, [string]$Right)
    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) { return $false }
    return $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)
}

function Test-SnapshotIdPrefixHit {
    param([string]$Token, [string[]]$Ids)
    if ([string]::IsNullOrWhiteSpace($Token)) { return $false }
    $token = $Token.Trim()
    if ($token -notmatch '^[0-9a-fA-F]{4,64}$') { return $false }
    foreach ($id in @($Ids)) {
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        if ($id.StartsWith($token, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        if ($token.Length -gt $id.Length -and $token.StartsWith($id, [StringComparison]::OrdinalIgnoreCase) -and $id.Length -ge 4) {
            return $true
        }
    }
    return $false
}

function Test-SnapshotTableData {
    param([string[]]$Lines)
    foreach ($line in @($Lines)) {
        if ($line -match '(?i)(^|[^A-Za-z])TABLE DATA([^A-Za-z]|$)') { return $true }
    }
    return $false
}

function Test-SnapshotDrillRefused {
    param(
        [string]$SourceContainer,
        [string]$SourceDatabase,
        [string]$DrillContainer,
        [string]$DrillDatabase,
        [string]$DrillVolume,
        [string]$LiveVolume
    )
    if (Test-SnapshotNameMatch $DrillContainer @($SourceContainer)) { return 'production-container' }
    if (Test-SnapshotNameMatch $DrillContainer $script:SnapshotProductionContainers) { return 'production-name' }
    if (Test-SnapshotNameMatch $DrillVolume @($LiveVolume)) { return 'production-volume' }
    if (Test-SnapshotNameMatch $DrillVolume $script:SnapshotProductionVolumes) { return 'production-volume' }
    if (Test-SnapshotNameMatch $DrillDatabase @($SourceDatabase)) { return 'database' }
    if (Test-SnapshotNameMatch $DrillDatabase @('antiphon')) { return 'database' }
    return ''
}

function Update-SnapshotRetention {
    param([string]$DestDir, [int]$Keep, [string]$NewName)
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($file in @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File -ErrorAction SilentlyContinue)) {
        if ($file.Name -match $script:SnapshotDumpPattern) { [void]$names.Add($file.Name) }
    }
    $prune = Get-SnapshotPruneSet -Names $names.ToArray() -Retain $Keep -NewName $NewName -Verified $true
    if (-not $prune.Ok) {
        return [pscustomobject]@{ Ok = $false; Reason = $prune.Reason; Kept = 0 }
    }
    foreach ($name in @($prune.Drop)) {
        if ($name -notmatch $script:SnapshotDumpPattern -or $name -eq $NewName) { continue }
        $victim = Join-Path $DestDir $name
        if (-not (Test-SnapshotPathInside $DestDir $victim)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'outside-destination'; Kept = 0 }
        }
        Remove-Item -LiteralPath $victim -Force
    }
    $left = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $script:SnapshotDumpPattern })
    $still = @($left | Where-Object { $_.Name -eq $NewName })
    if ($left.Count -lt 1 -or $left.Count -gt $Keep -or $still.Count -ne 1) {
        return [pscustomobject]@{ Ok = $false; Reason = 'prune-failed'; Kept = $left.Count }
    }
    return [pscustomobject]@{ Ok = $true; Reason = 'prune'; Kept = $left.Count }
}

function Protect-SnapshotStaging {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') {
        try {
            $acl = New-Object System.Security.AccessControl.DirectorySecurity
            $acl.SetAccessRuleProtection($true, $false)
            $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            $inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $identity,
                [System.Security.AccessControl.FileSystemRights]::FullControl,
                $inherit,
                [System.Security.AccessControl.PropagationFlags]::None,
                [System.Security.AccessControl.AccessControlType]::Allow)
            $acl.AddAccessRule($rule)
            $dir = New-Object System.IO.DirectoryInfo $Path
            $dir.SetAccessControl($acl)
            return $true
        } catch {
            return $false
        }
    }
    # Callers may isolate PATH to fixture binaries, so chmod is not looked up there.
    $chmod = $null
    foreach ($candidate in @('/bin/chmod', '/usr/bin/chmod')) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $chmod = $candidate; break }
    }
    if (-not $chmod) { return $false }
    & $chmod '0700' '--' $Path
    if ($LASTEXITCODE -ne 0) { return $false }
    return $true
}

function Test-SnapshotContainsLink {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $cursor = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
            if ($item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { return $true }
        }
        $parent = [System.IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $false
}

function Test-SnapshotRunLockFree {
    param([string]$LockPath)
    if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) { return $true }
    try {
        $handle = [System.IO.File]::Open($LockPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $handle.Dispose()
        return $true
    } catch {
        return $false
    }
}

# Sweeps only run directories that are the caller's, older than the stale
# threshold, and whose run.lock no process holds. A concurrent run keeps its
# files; a dead run loses its plaintext, its lock, and then its empty directory.
function Clear-SnapshotOwnedFiles {
    param([string]$Root, [int]$StaleMinutes = 360)
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) { return }
    $rootItem = Get-Item -LiteralPath $Root -Force
    if ($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return }
    $filePattern = '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump(\.age)?$|^antiphon-drill-plain\.dump$|^run\.lock$'
    $cutoff = [datetime]::UtcNow.AddMinutes(-1 * [Math]::Max(1, $StaleMinutes))
    foreach ($dir in @(Get-ChildItem -LiteralPath $Root -Force -Directory -ErrorAction SilentlyContinue)) {
        if ($dir.Name -notmatch '^run-[0-9a-f]{32}$') { continue }
        if ($dir.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
        if ($dir.LastWriteTimeUtc -gt $cutoff) { continue }
        if (-not (Test-SnapshotOwnedDirectory $dir.FullName)) { continue }
        if (-not (Test-SnapshotRunLockFree (Join-Path $dir.FullName 'run.lock'))) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $dir.FullName -Force -File -ErrorAction SilentlyContinue)) {
            if ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
            if ($file.Name -match $filePattern) {
                Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
            }
        }
        $left = @(Get-ChildItem -LiteralPath $dir.FullName -Force -ErrorAction SilentlyContinue)
        if ($left.Count -eq 0) {
            Remove-Item -LiteralPath $dir.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

# The default staging root is per user. On Linux the shared /tmp would let
# another local user create the root first and choose where plaintext lands.
function Get-SnapshotDefaultStaging {
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($local)) { $local = [System.IO.Path]::GetTempPath() }
    return Join-Path (Join-Path $local 'antiphon') 'postgres-snapshot-staging'
}

# Callers may isolate PATH to fixture binaries, so coreutils are not looked up there.
function Get-SnapshotUnixTool {
    param([string]$Name)
    foreach ($candidate in @(('/usr/bin/' + $Name), ('/bin/' + $Name))) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Get-SnapshotUnixStat {
    param([string]$Path)
    $stat = Get-SnapshotUnixTool 'stat'
    if (-not $stat) { return $null }
    $result = Invoke-SnapshotNative $stat @('-c', '%u %a', '--', $Path)
    if ($result.Exit -ne 0 -or $result.Lines.Count -lt 1) { return $null }
    if (([string]$result.Lines[0]).Trim() -match '^([0-9]+) ([0-7]{3,4})$') {
        return [pscustomobject]@{ Uid = [int]$Matches[1]; Mode = [Convert]::ToInt32($Matches[2], 8) }
    }
    return $null
}

function Get-SnapshotUnixUid {
    $id = Get-SnapshotUnixTool 'id'
    if (-not $id) { return -1 }
    $result = Invoke-SnapshotNative $id @('-u')
    if ($result.Exit -ne 0 -or $result.Lines.Count -lt 1) { return -1 }
    if (([string]$result.Lines[0]).Trim() -match '^[0-9]+$') { return [int](([string]$result.Lines[0]).Trim()) }
    return -1
}

function Test-SnapshotOwnedDirectory {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') { return (Test-SnapshotWindowsOwner $Path) -eq '' }
    $me = Get-SnapshotUnixUid
    $info = Get-SnapshotUnixStat $Path
    if ($me -lt 0 -or -not $info) { return $false }
    # 63 is 0077: any group or other permission bit.
    return ($info.Uid -eq $me) -and (($info.Mode -band 63) -eq 0)
}

function Test-SnapshotWindowsOwner {
    param([string]$Path)
    try {
        $acl = Get-Acl -LiteralPath $Path
        $owner = $acl.GetOwner([System.Security.Principal.SecurityIdentifier])
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        if ($owner -and $owner.Equals($identity.User)) { return '' }
        $admins = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        if ($owner -and $owner.Equals($admins) -and $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) { return '' }
        return 'staging-owner'
    } catch {
        return 'staging-owner'
    }
}

# The root must be the caller's and owner-only; each parent must be the
# caller's or root's, and writable by others only under a sticky bit. An
# absent root is created owner-only and then checked like an existing one.
function Test-SnapshotStagingRoot {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return 'staging-owner' }
    if (Test-SnapshotContainsLink $Path) { return 'staging-link' }
    if (-not (Test-Path -LiteralPath $Path)) {
        try { New-Item -ItemType Directory -Path $Path -Force -ErrorAction Stop | Out-Null } catch { return 'staging-owner' }
        if (-not (Protect-SnapshotStaging $Path)) { return 'staging-mode' }
    }
    if (Test-SnapshotContainsLink $Path) { return 'staging-link' }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return 'staging-owner' }
    if ($env:OS -eq 'Windows_NT') { return Test-SnapshotWindowsOwner $Path }
    $me = Get-SnapshotUnixUid
    if ($me -lt 0) { return 'staging-owner' }
    $root = Get-SnapshotUnixStat $Path
    if (-not $root) { return 'staging-owner' }
    if ($root.Uid -ne $me) { return 'staging-owner' }
    if (($root.Mode -band 63) -ne 0) { return 'staging-mode' }
    $cursor = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Path))
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        $info = Get-SnapshotUnixStat $cursor
        if (-not $info) { return 'staging-parent' }
        if ($info.Uid -ne $me -and $info.Uid -ne 0) { return 'staging-owner' }
        # 18 is 0022 (group/other write); 512 is 01000 (sticky).
        if (($info.Mode -band 18) -ne 0 -and ($info.Mode -band 512) -eq 0) { return 'staging-parent' }
        $parent = [System.IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    return ''
}

function Initialize-SnapshotStaging {
    param([string]$Path)
    $script:SnapshotRunStaging = ''
    $script:SnapshotRunLock = $null
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (Test-SnapshotContainsLink $Path) { return $false }
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    if (Test-SnapshotContainsLink $Path) { return $false }
    Clear-SnapshotOwnedFiles $Path $StaleRunMinutes
    $run = Join-Path $Path ('run-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run | Out-Null
    if (-not (Protect-SnapshotStaging $run)) {
        Remove-Item -LiteralPath $run -Force -ErrorAction SilentlyContinue
        return $false
    }
    try {
        $script:SnapshotRunLock = [System.IO.File]::Open((Join-Path $run 'run.lock'), [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        $script:SnapshotRunLock = $null
        Remove-Item -LiteralPath $run -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }
    $script:SnapshotRunStaging = $run
    return $true
}

function Remove-SnapshotStaging {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $true }
    if (Test-SnapshotContainsLink $Path) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $false }
    if ($item.Name -notmatch '^run-[0-9a-f]{32}$') { return $false }
    if (-not (Test-SnapshotOwnedDirectory $Path)) { return $false }
    if ($script:SnapshotRunLock) {
        try { $script:SnapshotRunLock.Dispose() } catch { }
        $script:SnapshotRunLock = $null
    }
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    return -not (Test-Path -LiteralPath $Path)
}

function Get-SnapshotReasonCode {
    param([string]$Reason)
    switch ($Reason) {
        'ok' { return 0 }
        'ambiguous-mode' { return 2 }
        'missing-recipient' { return 2 }
        'missing-identity' { return 2 }
        'bad-name' { return 2 }
        'recipient-inside-sync' { return 3 }
        'identity-inside-sync' { return 3 }
        'recipient-link' { return 3 }
        'identity-link' { return 3 }
        'missing-age' { return 3 }
        'missing-sync-root' { return 3 }
        'staging-inside-sync' { return 3 }
        'staging-inside-destination' { return 3 }
        'staging-link' { return 3 }
        'staging-mode' { return 3 }
        'staging-owner' { return 3 }
        'staging-parent' { return 3 }
        'staging-cleanup' { return 3 }
        'missing-docker' { return 4 }
        'docker-host' { return 4 }
        'docker-context' { return 4 }
        'dump-failed' { return 4 }
        'verify-failed' { return 5 }
        'encrypt-failed' { return 6 }
        'copy-failed' { return 7 }
        'prune-failed' { return 8 }
        'drill-refused' { return 9 }
        'drill-failed' { return 9 }
        'missing-snapshot' { return 9 }
        default { return 1 }
    }
}

function Write-SnapshotResult {
    param([bool]$PlanOnly, [string]$Reason, [string]$Detail)
    $code = Get-SnapshotReasonCode $Reason
    $mode = 'fail'
    if ($code -eq 0) {
        if ($PlanOnly) { $mode = 'plan' } else { $mode = 'ok' }
    }
    $line = "SNAPSHOT $mode exit=$code reason=$Reason"
    if (-not [string]::IsNullOrWhiteSpace($Detail)) { $line = $line + ' ' + $Detail }
    $script:SnapshotExit = $code
    Write-Output $line
}

function Remove-SnapshotPartial {
    param([string[]]$Paths)
    foreach ($path in @($Paths)) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-SnapshotNative {
    param([string]$Command, [string[]]$ArgumentList)
    $lines = New-Object System.Collections.Generic.List[string]
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command @ArgumentList 2>&1 | ForEach-Object { [void]$lines.Add([string]$_) }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($null -eq $code) { $code = 1 }
    return [pscustomobject]@{ Exit = [int]$code; Lines = $lines.ToArray() }
}

function Test-SnapshotOutsideFile {
    param([string]$Path, [string]$SyncRoot, [string]$Destination, [string]$InsideReason, [string]$LinkReason)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 'missing'
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $LinkReason }
    if ((Test-SnapshotPathInside $SyncRoot $Path) -or (Test-SnapshotPathInside $Destination $Path)) {
        return $InsideReason
    }
    return 'ok'
}

# The guards above apply to whichever daemon docker talks to. A tcp:// daemon
# would carry the plaintext dump across the network, so the endpoint must be a
# local socket or pipe unless the operator passes -AllowRemoteDocker. The
# endpoint itself is never printed.
function Test-SnapshotLocalEndpoint {
    param([string]$Endpoint)
    if ([string]::IsNullOrWhiteSpace($Endpoint)) { return $false }
    return $Endpoint.Trim() -match '^(?i)(unix|npipe)://'
}

function Test-SnapshotDockerDaemon {
    param([string]$Docker, [bool]$AllowRemote)
    $script:SnapshotRemoteDockerAllowed = $false
    $hostVar = [Environment]::GetEnvironmentVariable('DOCKER_HOST')
    if (-not [string]::IsNullOrWhiteSpace($hostVar) -and -not (Test-SnapshotLocalEndpoint $hostVar)) {
        if (-not $AllowRemote) { return 'docker-host' }
        $script:SnapshotRemoteDockerAllowed = $true
        return ''
    }
    $shown = Invoke-SnapshotNative $Docker @('context', 'inspect', '--format', '{{.Endpoints.docker.Host}}')
    $lines = @($shown.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($shown.Exit -ne 0 -or $lines.Count -ne 1 -or -not (Test-SnapshotLocalEndpoint ([string]$lines[0]))) {
        if (-not $AllowRemote) { return 'docker-context' }
        $script:SnapshotRemoteDockerAllowed = $true
    }
    return ''
}

function Write-SnapshotDaemonWarning {
    if ($script:SnapshotRemoteDockerAllowed) {
        Write-Output 'warning: remote docker endpoint allowed by -AllowRemoteDocker'
    }
}

function Write-SnapshotStatus {
    param([string]$Dir, [string]$Utc, [int]$Code, [int64]$Bytes)
    $path = Join-Path $Dir 'snapshot-status.json'
    $json = '{"utc":"' + $Utc + '","exit":' + $Code + ',"bytes":' + $Bytes + '}'
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, $json, $encoding)
}

function Invoke-SnapshotPublish {
    param(
        [string]$SourceContainer,
        [string]$SourceDatabase,
        [string]$Stage,
        [string]$DestDir,
        [string]$RecipientPath,
        [int]$Keep,
        [string]$Docker,
        [string]$Age,
        [int64]$MinimumBytes
    )
    $stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $containerDump = '/tmp/antiphon-snapshot-' + $stamp + '.dump'
    $hostDump = Join-Path $Stage ('antiphon-' + $stamp + '.dump')
    $hostCipher = Join-Path $Stage ('antiphon-' + $stamp + '.dump.age')
    $destName = 'antiphon-' + $stamp + '.dump.age'
    $destFile = Join-Path $DestDir $destName
    $published = $false
    try {
        $dump = Invoke-SnapshotNative $Docker @(
            'exec', $SourceContainer, 'pg_dump', '-Fc', '--no-owner', '--no-acl',
            '-U', 'antiphon', '-d', $SourceDatabase, '-f', $containerDump
        )
        if ($dump.Exit -ne 0) { return [pscustomobject]@{ Code = 4; Reason = 'dump-failed' } }
        $list = Invoke-SnapshotNative $Docker @('exec', $SourceContainer, 'pg_restore', '--list', $containerDump)
        $listed = @($list.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($list.Exit -ne 0 -or -not (Test-SnapshotTableData $listed)) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' }
        }
        $sizeText = Invoke-SnapshotNative $Docker @('exec', $SourceContainer, 'wc', '-c', $containerDump)
        $containerBytes = -1L
        if ($sizeText.Lines.Count -gt 0 -and ([string]$sizeText.Lines[0]) -match '(\d+)') {
            $containerBytes = [int64]$Matches[1]
        }
        if ($sizeText.Exit -ne 0 -or $containerBytes -lt $MinimumBytes) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' }
        }
        $copied = Invoke-SnapshotNative $Docker @('cp', ($SourceContainer + ':' + $containerDump), $hostDump)
        if ($copied.Exit -ne 0 -or -not (Test-Path -LiteralPath $hostDump)) {
            return [pscustomobject]@{ Code = 4; Reason = 'dump-failed' }
        }
        $hostBytes = ([System.IO.FileInfo]::new($hostDump)).Length
        if ($hostBytes -ne $containerBytes) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' }
        }
        $encrypted = Invoke-SnapshotNative $Age @('-R', $RecipientPath, '-o', $hostCipher, $hostDump)
        if ($encrypted.Exit -ne 0 -or -not (Test-Path -LiteralPath $hostCipher)) {
            return [pscustomobject]@{ Code = 6; Reason = 'encrypt-failed' }
        }
        $cipherBytes = ([System.IO.FileInfo]::new($hostCipher)).Length
        if ($cipherBytes -le 0) { return [pscustomobject]@{ Code = 6; Reason = 'encrypt-failed' } }
        if (Test-Path -LiteralPath $destFile) {
            return [pscustomobject]@{ Code = 7; Reason = 'copy-failed' }
        }
        if (-not (Test-Path -LiteralPath $DestDir)) {
            New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
        }
        Move-Item -LiteralPath $hostCipher -Destination $destFile
        $published = $true
        $destBytes = ([System.IO.FileInfo]::new($destFile)).Length
        if ($destBytes -ne $cipherBytes) {
            Remove-Item -LiteralPath $destFile -Force -ErrorAction SilentlyContinue
            $published = $false
            return [pscustomobject]@{ Code = 7; Reason = 'copy-failed' }
        }
        $prune = Update-SnapshotRetention $DestDir $Keep $destName
        if (-not $prune.Ok) {
            Write-SnapshotStatus $DestDir $stamp 8 $destBytes
            return [pscustomobject]@{ Code = 8; Reason = 'prune-failed' }
        }
        Write-SnapshotStatus $DestDir $stamp 0 $destBytes
        return [pscustomobject]@{ Code = 0; Reason = 'ok'; Detail = ('bytes=' + $destBytes + ' kept=' + $prune.Kept) }
    } finally {
        if (-not [string]::IsNullOrWhiteSpace($containerDump)) {
            Invoke-SnapshotNative $Docker @('exec', $SourceContainer, 'rm', '-f', $containerDump) | Out-Null
        }
        $partial = New-Object System.Collections.Generic.List[string]
        [void]$partial.Add($hostDump)
        if (-not $published) { [void]$partial.Add($hostCipher) }
        Remove-SnapshotPartial $partial.ToArray()
    }
}

function Get-SnapshotContainerFact {
    param([string]$Docker, [string]$Name)
    $missing = [pscustomobject]@{ Found = $false; Parse = $false; Id = ''; Name = ''; Label = ''; Mounts = @(); Aliases = @(); Env = @(); Binds = 0; NetworkMode = '' }
    if ([string]::IsNullOrWhiteSpace($Name)) { return $missing }
    $result = Invoke-SnapshotNative $Docker @('inspect', '--format', '{{json .}}', $Name)
    if ($result.Exit -ne 0) { return $missing }
    $text = (($result.Lines -join "`n").Trim())
    $bad = [pscustomobject]@{ Found = $true; Parse = $false; Id = ''; Name = ''; Label = ''; Mounts = @(); Aliases = @(); Env = @(); Binds = 0; NetworkMode = '' }
    if ([string]::IsNullOrWhiteSpace($text)) { return $bad }
    try { $info = $text | ConvertFrom-Json } catch { return $bad }
    if ($info -is [System.Array]) { $info = $info[0] }
    $label = ''
    if ($info.Config -and $info.Config.Labels) {
        $prop = $info.Config.Labels.PSObject.Properties[$script:SnapshotDrillLabel]
        if ($prop) { $label = [string]$prop.Value }
    }
    $binds = 0
    $mounts = New-Object System.Collections.Generic.List[string]
    if ($info.Mounts) {
        foreach ($mount in @($info.Mounts)) {
            if (-not $mount) { continue }
            $kind = ''
            if ($mount.PSObject.Properties['Type']) { $kind = [string]$mount.Type }
            if (-not [string]::IsNullOrWhiteSpace($mount.Name) -and ($kind -eq '' -or $kind -eq 'volume')) {
                [void]$mounts.Add([string]$mount.Name)
            } else {
                $binds++
            }
        }
    }
    $aliases = New-Object System.Collections.Generic.List[string]
    if ($info.NetworkSettings -and $info.NetworkSettings.Networks) {
        foreach ($net in $info.NetworkSettings.Networks.PSObject.Properties) {
            if (-not $net.Value -or -not $net.Value.Aliases) { continue }
            foreach ($alias in @($net.Value.Aliases)) {
                if (-not [string]::IsNullOrWhiteSpace($alias)) { [void]$aliases.Add([string]$alias) }
            }
        }
    }
    $envList = New-Object System.Collections.Generic.List[string]
    if ($info.Config -and $info.Config.Env) {
        foreach ($entry in @($info.Config.Env)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry)) { [void]$envList.Add([string]$entry) }
        }
    }
    $networkMode = ''
    if ($info.HostConfig -and $info.HostConfig.PSObject.Properties['NetworkMode']) { $networkMode = [string]$info.HostConfig.NetworkMode }
    $id = ''
    if ($info.Id) { $id = [string]$info.Id }
    $bare = ''
    if ($info.Name) { $bare = Get-SnapshotBareName ([string]$info.Name) }
    return [pscustomobject]@{
        Found = $true; Parse = $true; Id = $id; Name = $bare; Label = $label
        Mounts = $mounts.ToArray(); Aliases = $aliases.ToArray(); Env = $envList.ToArray()
        Binds = $binds; NetworkMode = $networkMode
    }
}

function Get-SnapshotProductionIds {
    param([string]$Docker, [string]$SourceContainer, $SourceFact)
    $ids = New-Object System.Collections.Generic.List[string]
    if ($SourceFact.Found -and $SourceFact.Parse -and -not [string]::IsNullOrWhiteSpace($SourceFact.Id)) {
        [void]$ids.Add($SourceFact.Id)
    }
    foreach ($name in @($script:SnapshotProductionContainers)) {
        if (Test-SnapshotNameMatch $name @($SourceContainer)) { continue }
        $other = Get-SnapshotContainerFact $Docker $name
        if ($other.Found -and $other.Parse -and -not [string]::IsNullOrWhiteSpace($other.Id)) {
            [void]$ids.Add($other.Id)
        }
    }
    return $ids.ToArray()
}

function Test-SnapshotRemoteEnv {
    param([string[]]$Entries)
    foreach ($entry in @($Entries)) {
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }
        if ($entry -match '^(?i)(PGHOST|PGPORT|PGHOSTADDR)=(.*)$' -and -not [string]::IsNullOrWhiteSpace($Matches[2])) { return $true }
    }
    return $false
}

# Any other libpq variable (PGSERVICE, PGPASSFILE, PGOPTIONS, ...) can point a
# reused container at another server; a scratch container needs none of them.
function Test-SnapshotPgEnv {
    param([string[]]$Entries)
    foreach ($entry in @($Entries)) {
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }
        if ($entry -match '^(?i)PG[A-Z0-9_]*=(.*)$' -and -not [string]::IsNullOrWhiteSpace($Matches[1])) { return $true }
    }
    return $false
}

function Test-SnapshotNetworkGuard {
    param([string]$Mode)
    if ([string]::IsNullOrWhiteSpace($Mode)) { return '' }
    $trim = $Mode.Trim()
    if ($trim.Equals('host', [StringComparison]::OrdinalIgnoreCase)) { return 'host-network' }
    if ($trim.StartsWith('container:', [StringComparison]::OrdinalIgnoreCase)) { return 'shared-network' }
    return ''
}

function Test-SnapshotReuseGuard {
    param($Fact, [string]$SourceId, [string]$SourceName, [string]$SourceVolume, [string[]]$ProductionIds, [string[]]$SourceMounts)
    if (-not $Fact.Found -or -not $Fact.Parse) { return 'inspect' }
    if (Test-SnapshotSameId $Fact.Id $SourceId) { return 'source-id' }
    if (Test-SnapshotIdPrefixHit $Fact.Id $ProductionIds) { return 'id-prefix' }
    $names = @($script:SnapshotProductionContainers) + @($SourceName)
    if (Test-SnapshotNameMatch $Fact.Name $names) { return 'production-name' }
    foreach ($alias in @($Fact.Aliases)) {
        if (Test-SnapshotNameMatch $alias @($Fact.Name)) { continue }
        if (Test-SnapshotNameMatch $alias $names) { return 'alias' }
    }
    if ($Fact.Label -ne '1') { return 'unlabelled' }
    $listed = @($script:SnapshotProductionVolumes) + @($SourceVolume)
    foreach ($mount in @($Fact.Mounts)) {
        if (Test-SnapshotNameMatch $mount $listed) { return 'production-mount' }
        if (Test-SnapshotNameMatch $mount $SourceMounts) { return 'source-mount' }
    }
    if (Test-SnapshotRemoteEnv $Fact.Env) { return 'pghost' }
    if (Test-SnapshotPgEnv $Fact.Env) { return 'pg-env' }
    if ([int]$Fact.Binds -gt 0) { return 'bind-mount' }
    $network = Test-SnapshotNetworkGuard $Fact.NetworkMode
    if ($network -ne '') { return $network }
    return 'ok'
}

function Get-SnapshotProtectedMounts {
    param([string]$Docker, $SourceFact, [string]$SourceContainer)
    $mounts = New-Object System.Collections.Generic.List[string]
    if ($SourceFact -and $SourceFact.Mounts) {
        foreach ($name in @($SourceFact.Mounts)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$name)) { [void]$mounts.Add([string]$name) }
        }
    }
    foreach ($name in @($script:SnapshotProductionContainers)) {
        if (Test-SnapshotNameMatch $name @($SourceContainer)) { continue }
        $other = Get-SnapshotContainerFact $Docker $name
        if ($other.Found -and $other.Parse) {
            foreach ($mount in @($other.Mounts)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$mount)) { [void]$mounts.Add([string]$mount) }
            }
        }
    }
    return $mounts
}

function Get-SnapshotVolumeFact {
    param([string]$Docker, [string]$Name)
    $missing = [pscustomobject]@{ Found = $false; Parse = $false; Name = ''; Label = '' }
    if ([string]::IsNullOrWhiteSpace($Name)) { return $missing }
    $result = Invoke-SnapshotNative $Docker @('volume', 'inspect', '--format', '{{json .}}', $Name)
    if ($result.Exit -ne 0) { return $missing }
    $text = (($result.Lines -join "`n").Trim())
    $bad = [pscustomobject]@{ Found = $true; Parse = $false; Name = ''; Label = '' }
    if ([string]::IsNullOrWhiteSpace($text)) { return $bad }
    try { $info = $text | ConvertFrom-Json } catch { return $bad }
    if ($info -is [System.Array]) { $info = $info[0] }
    $label = ''
    if ($info.Labels) {
        $prop = $info.Labels.PSObject.Properties[$script:SnapshotDrillLabel]
        if ($prop) { $label = [string]$prop.Value }
    }
    $volumeName = $Name
    if ($info.Name) { $volumeName = [string]$info.Name }
    return [pscustomobject]@{ Found = $true; Parse = $true; Name = $volumeName; Label = $label }
}

function Get-SnapshotVolumeUsers {
    param([string]$Docker, [string]$Volume)
    $ids = New-Object System.Collections.Generic.List[string]
    $result = Invoke-SnapshotNative $Docker @('ps', '-a', '--no-trunc', '--filter', ('volume=' + $Volume), '--format', '{{.ID}}')
    if ($result.Exit -ne 0) {
        return [pscustomobject]@{ Ok = $false; Ids = $ids }
    }
    foreach ($line in @($result.Lines)) {
        $trim = ([string]$line).Trim()
        if ($trim -match '^[0-9a-fA-F]{12,64}$') { [void]$ids.Add($trim) }
    }
    return [pscustomobject]@{ Ok = $true; Ids = $ids }
}

function Test-SnapshotVolumeGate {
    param(
        [string]$Docker,
        [string]$Volume,
        [string]$SourceVolume,
        $SourceMounts,
        [string]$AllowedId,
        [bool]$AllowAbsent
    )
    $denied = @($script:SnapshotProductionVolumes) + @($SourceVolume)
    if (Test-SnapshotNameMatch $Volume $denied) { return 'production-volume' }
    if (Test-SnapshotNameMatch $Volume @($SourceMounts)) { return 'source-mount' }
    $users = Get-SnapshotVolumeUsers $Docker $Volume
    if (-not $users.Ok) { return 'inspect' }
    foreach ($user in @($users.Ids)) {
        if ([string]::IsNullOrWhiteSpace([string]$user)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($AllowedId) -and (Test-SnapshotSameId ([string]$user) $AllowedId)) { continue }
        return 'foreign-mount'
    }
    $fact = Get-SnapshotVolumeFact $Docker $Volume
    if (-not $fact.Found) {
        if ($AllowAbsent) { return 'absent' }
        return 'volume-mismatch'
    }
    if (-not $fact.Parse) { return 'inspect' }
    if ($fact.Label -ne '1') { return 'unlabelled-volume' }
    return 'ok'
}

function Write-SnapshotVolumeWarning {
    param([string]$Volume)
    Write-Output ('warning: scratch volume left in place volume=' + $Volume)
}

function Remove-SnapshotCreatedVolume {
    param(
        [string]$Docker,
        [string]$Volume,
        [string]$SourceVolume,
        $SourceMounts
    )
    $denied = @($script:SnapshotProductionVolumes) + @($SourceVolume) + @($SourceMounts)
    if (Test-SnapshotNameMatch $Volume $denied) {
        Write-SnapshotVolumeWarning $Volume
        return
    }
    $fact = Get-SnapshotVolumeFact $Docker $Volume
    if (-not $fact.Found -or -not $fact.Parse -or $fact.Label -ne '1') {
        Write-SnapshotVolumeWarning $Volume
        return
    }
    $users = Get-SnapshotVolumeUsers $Docker $Volume
    if (-not $users.Ok -or @($users.Ids).Count -gt 0) {
        Write-SnapshotVolumeWarning $Volume
        return
    }
    Invoke-SnapshotNative $Docker @('volume', 'rm', $Volume) | Out-Null
}

function Remove-SnapshotDrillContainer {
    param(
        [string]$Docker,
        [string]$DrillContainer,
        [string]$DrillVolume,
        [string]$SourceId,
        [string]$SourceVolume,
        [string]$ExpectedId,
        $SourceMounts
    )
    if ([string]::IsNullOrWhiteSpace($ExpectedId)) {
        Write-Output 'warning: scratch container left in place'
        return
    }
    $fact = Get-SnapshotContainerFact $Docker $ExpectedId
    $same = $fact.Found -and $fact.Parse -and (Test-SnapshotSameId $fact.Id $ExpectedId) -and $fact.Label -eq '1'
    $safeName = -not (Test-SnapshotNameMatch $fact.Name $script:SnapshotProductionContainers)
    $notSource = -not (Test-SnapshotSameId $fact.Id $SourceId)
    if (-not $same -or -not $safeName -or -not $notSource) {
        Write-Output 'warning: scratch container left in place'
        return
    }
    $mounts = @($fact.Mounts)
    Invoke-SnapshotNative $Docker @('rm', '-f', $fact.Id) | Out-Null
    $mounted = $false
    foreach ($mount in $mounts) {
        if (Test-SnapshotNameMatch $DrillVolume @($mount)) { $mounted = $true }
    }
    $vol = Get-SnapshotVolumeFact $Docker $DrillVolume
    $denied = @($script:SnapshotProductionVolumes) + @($SourceVolume) + @($SourceMounts)
    if (-not $mounted -or -not $vol.Found -or -not $vol.Parse -or $vol.Label -ne '1' -or (Test-SnapshotNameMatch $DrillVolume $denied)) {
        Write-SnapshotVolumeWarning $DrillVolume
        return
    }
    Invoke-SnapshotNative $Docker @('volume', 'rm', $DrillVolume) | Out-Null
}

function Invoke-SnapshotDrill {
    param(
        [string]$Stage,
        [string]$DestDir,
        [string]$IdentityPath,
        [string]$DrillContainer,
        [string]$DrillDatabase,
        [string]$DrillVolume,
        [string]$Image,
        [string]$Docker,
        [string]$Age,
        [string]$SourceContainer,
        [string]$SourceDatabase,
        [string]$SourceVolume,
        [int64]$MinimumBytes,
        [bool]$Keep
    )
    $owned = $false
    $volumeCreated = $false
    $sourceId = ''
    $targetId = ''
    $protectedMounts = @()
    $plain = Join-Path $Stage 'antiphon-drill-plain.dump'
    try {
        if (Test-SnapshotNameMatch $DrillDatabase @($SourceDatabase, 'antiphon')) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=database' }
        }
        $source = Get-SnapshotContainerFact $Docker $SourceContainer
        if (-not $source.Found -or -not $source.Parse -or [string]::IsNullOrWhiteSpace($source.Id)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=source-unknown' }
        }
        $sourceId = $source.Id
        $protectedMounts = @(Get-SnapshotProtectedMounts $Docker $source $SourceContainer)
        $productionIds = @(Get-SnapshotProductionIds $Docker $SourceContainer $source)
        if (Test-SnapshotIdPrefixHit $DrillContainer $productionIds) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=id-prefix' }
        }
        $existing = Get-SnapshotContainerFact $Docker $DrillContainer
        if ($existing.Found -and -not $existing.Parse) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=inspect' }
        }
        if ($existing.Found) {
            if (Test-SnapshotIdPrefixHit $DrillContainer (@($existing.Id) + $productionIds)) {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=id-prefix' }
            }
            $guard = Test-SnapshotReuseGuard $existing $sourceId $source.Name $SourceVolume $productionIds $protectedMounts
            if ($guard -ne 'ok') {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $guard) }
            }
            $gate = Test-SnapshotVolumeGate $Docker $DrillVolume $SourceVolume $protectedMounts $existing.Id $false
            if ($gate -ne 'ok') {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $gate) }
            }
            $mounted = $false
            foreach ($mount in @($existing.Mounts)) {
                if (Test-SnapshotNameMatch $DrillVolume @($mount)) { $mounted = $true }
            }
            if (-not $mounted) {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=volume-mismatch' }
            }
            $targetId = $existing.Id
        } else {
            $gate = Test-SnapshotVolumeGate $Docker $DrillVolume $SourceVolume $protectedMounts '' $true
            if ($gate -eq 'absent') {
                $made = Invoke-SnapshotNative $Docker @('volume', 'create', '--label', ($script:SnapshotDrillLabel + '=1'), $DrillVolume)
                if ($made.Exit -ne 0) {
                    return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' }
                }
                $check = Get-SnapshotVolumeFact $Docker $DrillVolume
                if (-not $check.Found -or -not $check.Parse -or $check.Label -ne '1') {
                    return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' }
                }
                $volumeCreated = $true
            } elseif ($gate -ne 'ok') {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $gate) }
            }
            $started = Invoke-SnapshotNative $Docker @(
                'run', '-d', '--name', $DrillContainer,
                '--label', ($script:SnapshotDrillLabel + '=1'),
                '--network', 'none',
                '-v', ($DrillVolume + ':/var/lib/postgresql/data'),
                '-e', 'POSTGRES_HOST_AUTH_METHOD=trust',
                '-e', 'POSTGRES_USER=antiphon',
                $Image
            )
            if ($started.Exit -ne 0) {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' }
            }
            $created = Get-SnapshotContainerFact $Docker $DrillContainer
            $guard = Test-SnapshotReuseGuard $created $sourceId $source.Name $SourceVolume $productionIds $protectedMounts
            if ($guard -ne 'ok') {
                if ($created.Found -and $created.Parse -and $created.Label -eq '1' -and -not (Test-SnapshotSameId $created.Id $sourceId)) {
                    $owned = $true
                    $targetId = $created.Id
                }
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $guard) }
            }
            $targetId = $created.Id
        }
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=inspect' }
        }
        $owned = $true
        $newest = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $script:SnapshotDumpPattern } | Sort-Object Name -Descending)
        if ($newest.Count -lt 1) { return [pscustomobject]@{ Code = 9; Reason = 'missing-snapshot'; Detail = '' } }
        $decoded = Invoke-SnapshotNative $Age @('-d', '-i', $IdentityPath, '-o', $plain, $newest[0].FullName)
        if ($decoded.Exit -ne 0 -or -not (Test-Path -LiteralPath $plain)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' }
        }
        $plainBytes = ([System.IO.FileInfo]::new($plain)).Length
        if ($plainBytes -lt $MinimumBytes) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed'; Detail = '' }
        }
        $listed = Invoke-SnapshotNative $Docker @(
            'run', '--rm', '--network', 'none',
            '-v', ($Stage + ':/work:ro'),
            '--entrypoint', 'pg_restore', $Image,
            '--list', '/work/antiphon-drill-plain.dump'
        )
        $toc = @($listed.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($listed.Exit -ne 0 -or -not (Test-SnapshotTableData $toc)) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed'; Detail = '' }
        }
        $ready = $false
        for ($try = 0; $try -lt 12; $try++) {
            $probe = Invoke-SnapshotNative $Docker @('exec', $targetId, 'pg_isready', '-U', 'antiphon')
            if ($probe.Exit -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' } }
        if (Test-SnapshotNameMatch $DrillDatabase @('antiphon', $SourceDatabase)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=database' }
        }
        $dropped = Invoke-SnapshotNative $Docker @('exec', $targetId, 'dropdb', '--if-exists', '-U', 'antiphon', $DrillDatabase)
        if ($dropped.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' } }
        $createdDb = Invoke-SnapshotNative $Docker @('exec', $targetId, 'createdb', '-U', 'antiphon', $DrillDatabase)
        if ($createdDb.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' } }
        $remote = '/tmp/antiphon-drill.dump'
        $sent = Invoke-SnapshotNative $Docker @('cp', $plain, ($targetId + ':' + $remote))
        if ($sent.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' } }
        $restored = Invoke-SnapshotNative $Docker @(
            'exec', $targetId, 'pg_restore', '--no-owner', '--no-acl', '--exit-on-error',
            '-U', 'antiphon', '-d', $DrillDatabase, $remote
        )
        Invoke-SnapshotNative $Docker @('exec', $targetId, 'rm', '-f', $remote) | Out-Null
        if ($restored.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed'; Detail = '' } }
        return [pscustomobject]@{ Code = 0; Reason = 'ok'; Detail = ('drill=1 bytes=' + $plainBytes) }
    } finally {
        Remove-SnapshotPartial @($plain)
        if ($owned -and -not $Keep) {
            Remove-SnapshotDrillContainer $Docker $DrillContainer $DrillVolume $sourceId $SourceVolume $targetId $protectedMounts
        } elseif ($volumeCreated -and -not $owned) {
            Remove-SnapshotCreatedVolume $Docker $DrillVolume $SourceVolume $protectedMounts
        }
    }
}

function Invoke-PostgresSnapshot {
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
        $Global:PSNativeCommandUseErrorActionPreference = $false
    }
    $planOnly = -not $Execute
    if ($WhatIf -and $Execute) {
        return Write-SnapshotResult $true 'ambiguous-mode' ''
    }
    if ($WhatIf) { $planOnly = $true }
    $sync = $SyncRoot
    if ([string]::IsNullOrWhiteSpace($sync)) { $sync = 'C:\Users\lndco\OneDrive' }
    $dest = $Destination
    if ([string]::IsNullOrWhiteSpace($dest)) { $dest = Join-Path $sync 'antiphon-postgres' }
    $stage = $StagingPath
    if ([string]::IsNullOrWhiteSpace($stage)) { $stage = Get-SnapshotDefaultStaging }
    $Container = Get-SnapshotBareName $Container
    $Database = Get-SnapshotBareName $Database
    $ScratchContainer = Get-SnapshotBareName $ScratchContainer
    $ScratchDatabase = Get-SnapshotBareName $ScratchDatabase
    $ScratchVolume = Get-SnapshotBareName $ScratchVolume
    $SourceVolume = Get-SnapshotBareName $SourceVolume

    $namesOk = (Test-SnapshotToken $Container) -and (Test-SnapshotToken $Database) -and
        (Test-SnapshotToken $ScratchContainer) -and (Test-SnapshotToken $ScratchDatabase) -and
        (Test-SnapshotToken $ScratchVolume) -and (Test-SnapshotToken $SourceVolume) -and
        (Test-SnapshotImage $ScratchImage)
    if (-not $namesOk -or $Retain -lt 1 -or $MinimumDumpBytes -lt 1) {
        return Write-SnapshotResult $planOnly 'bad-name' ''
    }
    $drillGuard = Test-SnapshotDrillRefused $Container $Database $ScratchContainer $ScratchDatabase $ScratchVolume $SourceVolume
    if (-not [string]::IsNullOrWhiteSpace($drillGuard)) {
        return Write-SnapshotResult $planOnly 'drill-refused' ('guard=' + $drillGuard)
    }
    if (-not (Test-Path -LiteralPath $sync -PathType Container)) {
        return Write-SnapshotResult $planOnly 'missing-sync-root' ''
    }
    if (Test-SnapshotContainsLink $stage) {
        return Write-SnapshotResult $planOnly 'staging-link' ''
    }
    if (Test-SnapshotPathInside $sync $stage) {
        return Write-SnapshotResult $planOnly 'staging-inside-sync' ''
    }
    if (Test-SnapshotPathInside $dest $stage) {
        return Write-SnapshotResult $planOnly 'staging-inside-destination' ''
    }

    if ($RestoreDrill) {
        $identity = Resolve-SnapshotReferencePath $IdentityFile $IdentityRef
        if ([string]::IsNullOrWhiteSpace($identity)) {
            return Write-SnapshotResult $planOnly 'missing-identity' ''
        }
        $identityState = Test-SnapshotOutsideFile $identity $sync $dest 'identity-inside-sync' 'identity-link'
        if ($identityState -eq 'missing') { return Write-SnapshotResult $planOnly 'missing-identity' '' }
        if ($identityState -ne 'ok') { return Write-SnapshotResult $planOnly $identityState '' }
        if ($planOnly) {
            Write-Output 'step drill: decrypt the newest snapshot with age -d -i <identity-file>'
            Write-Output ('step drill: pg_restore --list must include TABLE DATA and the plaintext must be at least ' + $MinimumDumpBytes + ' bytes')
            Write-Output ('step drill: pg_restore into scratch container ' + $ScratchContainer + ' database ' + $ScratchDatabase + ' volume ' + $ScratchVolume)
            Write-Output 'step drill: leave the production database and volume untouched'
            Write-Output 'step drill: remove the labelled scratch container and volume unless -KeepScratch'
            return Write-SnapshotResult $true 'ok' ('drill=1 scratch=' + $ScratchContainer)
        }
        $ageCmd = Get-Command age -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $dockerCmd = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $ageCmd) { return Write-SnapshotResult $false 'missing-age' '' }
        if (-not $dockerCmd) { return Write-SnapshotResult $false 'missing-docker' '' }
        $rootState = Test-SnapshotStagingRoot $stage
        if ($rootState -ne '') { return Write-SnapshotResult $false $rootState '' }
        $daemon = Test-SnapshotDockerDaemon $dockerCmd.Source ([bool]$AllowRemoteDocker)
        if ($daemon -ne '') { return Write-SnapshotResult $false $daemon '' }
        Write-SnapshotDaemonWarning
        if (-not (Initialize-SnapshotStaging $stage)) { return Write-SnapshotResult $false 'staging-mode' '' }
        $run = [string]$script:SnapshotRunStaging
        if ([string]::IsNullOrWhiteSpace($run)) { return Write-SnapshotResult $false 'staging-mode' '' }
        try {
            $drilled = Invoke-SnapshotDrill $run $dest $identity $ScratchContainer $ScratchDatabase $ScratchVolume $ScratchImage $dockerCmd.Source $ageCmd.Source $Container $Database $SourceVolume $MinimumDumpBytes ([bool]$KeepScratch)
            return Write-SnapshotResult $false $drilled.Reason $drilled.Detail
        } finally {
            [void](Remove-SnapshotStaging $run)
        }
    }

    $recipient = Resolve-SnapshotReferencePath $RecipientFile $RecipientRef
    if ([string]::IsNullOrWhiteSpace($recipient)) {
        return Write-SnapshotResult $planOnly 'missing-recipient' ''
    }
    $recipientState = Test-SnapshotOutsideFile $recipient $sync $dest 'recipient-inside-sync' 'recipient-link'
    if ($recipientState -eq 'missing') { return Write-SnapshotResult $planOnly 'missing-recipient' '' }
    if ($recipientState -ne 'ok') { return Write-SnapshotResult $planOnly $recipientState '' }

    if ($planOnly) {
        Write-Output ('step dump: docker exec ' + $Container + ' pg_dump -Fc --no-owner --no-acl -U antiphon -d ' + $Database + ' -f /tmp/antiphon-snapshot.dump')
        Write-Output ('step verify: pg_restore --list must include TABLE DATA and the dump must be at least ' + $MinimumDumpBytes + ' bytes')
        Write-Output 'step encrypt: age -R <recipient-file> -o <staging>.dump.age'
        Write-Output 'step copy: move the ciphertext into the snapshot directory as a new antiphon-<utc>.dump.age file'
        Write-Output ('step retention: keep ' + $Retain + ' dump.age files and prune only after the new file is verified')
        return Write-SnapshotResult $true 'ok' ('dump=1 encrypt=1 copy=1 retention=' + $Retain)
    }

    $ageCmd = Get-Command age -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $dockerCmd = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ageCmd) { return Write-SnapshotResult $false 'missing-age' '' }
    if (-not $dockerCmd) { return Write-SnapshotResult $false 'missing-docker' '' }
    $rootState = Test-SnapshotStagingRoot $stage
    if ($rootState -ne '') { return Write-SnapshotResult $false $rootState '' }
    $daemon = Test-SnapshotDockerDaemon $dockerCmd.Source ([bool]$AllowRemoteDocker)
    if ($daemon -ne '') { return Write-SnapshotResult $false $daemon '' }
    Write-SnapshotDaemonWarning
    if (-not (Initialize-SnapshotStaging $stage)) { return Write-SnapshotResult $false 'staging-mode' '' }
    $run = [string]$script:SnapshotRunStaging
    if ([string]::IsNullOrWhiteSpace($run)) { return Write-SnapshotResult $false 'staging-mode' '' }
    try {
        $published = Invoke-SnapshotPublish $Container $Database $run $dest $recipient $Retain $dockerCmd.Source $ageCmd.Source $MinimumDumpBytes
        return Write-SnapshotResult $false $published.Reason $published.Detail
    } finally {
        [void](Remove-SnapshotStaging $run)
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$script:SnapshotExit = 1
Invoke-PostgresSnapshot
exit $script:SnapshotExit
