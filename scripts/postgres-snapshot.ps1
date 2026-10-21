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
    [string]$ScratchImage = 'postgres:16'
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
    return $Value.Trim().TrimStart('/')
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

function Initialize-SnapshotStaging {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $Path -Force -File -ErrorAction SilentlyContinue)) {
        if ($file.Extension.Equals('.dump', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
        }
    }
    return (Protect-SnapshotStaging $Path)
}

function Remove-SnapshotStaging {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $true }
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
        'staging-mode' { return 3 }
        'staging-cleanup' { return 3 }
        'missing-docker' { return 4 }
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
    $missing = [pscustomobject]@{ Found = $false; Parse = $false; Id = ''; Name = ''; Label = ''; Mounts = @(); Aliases = @() }
    if ([string]::IsNullOrWhiteSpace($Name)) { return $missing }
    $result = Invoke-SnapshotNative $Docker @('inspect', '--format', '{{json .}}', $Name)
    if ($result.Exit -ne 0) { return $missing }
    $text = (($result.Lines -join "`n").Trim())
    $bad = [pscustomobject]@{ Found = $true; Parse = $false; Id = ''; Name = ''; Label = ''; Mounts = @(); Aliases = @() }
    if ([string]::IsNullOrWhiteSpace($text)) { return $bad }
    try { $info = $text | ConvertFrom-Json } catch { return $bad }
    if ($info -is [System.Array]) { $info = $info[0] }
    $label = ''
    if ($info.Config -and $info.Config.Labels) {
        $prop = $info.Config.Labels.PSObject.Properties[$script:SnapshotDrillLabel]
        if ($prop) { $label = [string]$prop.Value }
    }
    $mounts = New-Object System.Collections.Generic.List[string]
    if ($info.Mounts) {
        foreach ($mount in @($info.Mounts)) {
            if ($mount -and -not [string]::IsNullOrWhiteSpace($mount.Name)) { [void]$mounts.Add([string]$mount.Name) }
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
    $id = ''
    if ($info.Id) { $id = [string]$info.Id }
    $bare = ''
    if ($info.Name) { $bare = Get-SnapshotBareName ([string]$info.Name) }
    return [pscustomobject]@{
        Found = $true; Parse = $true; Id = $id; Name = $bare; Label = $label
        Mounts = $mounts.ToArray(); Aliases = $aliases.ToArray()
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

function Test-SnapshotReuseGuard {
    param($Fact, [string]$SourceId, [string]$SourceName, [string]$SourceVolume, [string[]]$ProductionIds)
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
    $volumes = @($script:SnapshotProductionVolumes) + @($SourceVolume)
    foreach ($mount in @($Fact.Mounts)) {
        if (Test-SnapshotNameMatch $mount $volumes) { return 'production-mount' }
    }
    return 'ok'
}

function Remove-SnapshotDrillContainer {
    param(
        [string]$Docker,
        [string]$DrillContainer,
        [string]$DrillVolume,
        [string]$SourceId,
        [string]$SourceVolume
    )
    $fact = Get-SnapshotContainerFact $Docker $DrillContainer
    $removed = $false
    if ($fact.Found -and $fact.Parse -and $fact.Label -eq '1' -and -not (Test-SnapshotSameId $fact.Id $SourceId)) {
        if (-not (Test-SnapshotNameMatch $fact.Name $script:SnapshotProductionContainers)) {
            Invoke-SnapshotNative $Docker @('rm', '-f', $fact.Id) | Out-Null
            $removed = $true
        }
    }
    $denied = @($script:SnapshotProductionVolumes) + @($SourceVolume)
    if ($removed -and -not (Test-SnapshotNameMatch $DrillVolume $denied)) {
        Invoke-SnapshotNative $Docker @('volume', 'rm', $DrillVolume) | Out-Null
    }
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
    $sourceId = ''
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
        $productionIds = @(Get-SnapshotProductionIds $Docker $SourceContainer $source)
        if (Test-SnapshotIdPrefixHit $DrillContainer $productionIds) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=id-prefix' }
        }
        $existing = Get-SnapshotContainerFact $Docker $DrillContainer
        if ($existing.Found -and -not $existing.Parse) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=inspect' }
        }
        $targetId = ''
        if ($existing.Found) {
            if (Test-SnapshotIdPrefixHit $DrillContainer (@($existing.Id) + $productionIds)) {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=id-prefix' }
            }
            $guard = Test-SnapshotReuseGuard $existing $sourceId $source.Name $SourceVolume $productionIds
            if ($guard -ne 'ok') {
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $guard) }
            }
            $targetId = $existing.Id
        } else {
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
            $guard = Test-SnapshotReuseGuard $created $sourceId $source.Name $SourceVolume $productionIds
            if ($guard -ne 'ok') {
                if ($created.Found -and $created.Parse -and $created.Label -eq '1' -and -not (Test-SnapshotSameId $created.Id $sourceId)) {
                    $owned = $true
                }
                return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = ('guard=' + $guard) }
            }
            $targetId = $created.Id
        }
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-refused'; Detail = 'guard=inspect' }
        }
        $owned = $true
        $newest = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File | Sort-Object Name -Descending)
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
            Remove-SnapshotDrillContainer $Docker $DrillContainer $DrillVolume $sourceId $SourceVolume
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
    if ([string]::IsNullOrWhiteSpace($stage)) { $stage = Join-Path ([System.IO.Path]::GetTempPath()) 'antiphon-postgres-snapshot-staging' }

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
    if (Test-SnapshotPathInside $sync $stage) {
        return Write-SnapshotResult $planOnly 'staging-inside-sync' ''
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
        if (-not (Initialize-SnapshotStaging $stage)) { return Write-SnapshotResult $false 'staging-mode' '' }
        try {
            $drilled = Invoke-SnapshotDrill $stage $dest $identity $ScratchContainer $ScratchDatabase $ScratchVolume $ScratchImage $dockerCmd.Source $ageCmd.Source $Container $Database $SourceVolume $MinimumDumpBytes ([bool]$KeepScratch)
            return Write-SnapshotResult $false $drilled.Reason $drilled.Detail
        } finally {
            [void](Remove-SnapshotStaging $stage)
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
    if (-not (Initialize-SnapshotStaging $stage)) { return Write-SnapshotResult $false 'staging-mode' '' }
    try {
        $published = Invoke-SnapshotPublish $Container $Database $stage $dest $recipient $Retain $dockerCmd.Source $ageCmd.Source $MinimumDumpBytes
        return Write-SnapshotResult $false $published.Reason $published.Detail
    } finally {
        [void](Remove-SnapshotStaging $stage)
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$script:SnapshotExit = 1
Invoke-PostgresSnapshot
exit $script:SnapshotExit
