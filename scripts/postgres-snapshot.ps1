#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1405 daily encrypted Postgres snapshot and scratch restore drill.

    Default and -WhatIf print the plan and run no docker, age, or pg_restore.
    -Execute dumps with pg_dump -Fc inside the source container, checks
    pg_restore --list and the byte size, encrypts with age, then copies the
    ciphertext into the snapshot directory. Retention keeps 14 dailies and
    prunes only after that new file is verified. -RestoreDrill restores the
    newest snapshot into a scratch container and volume.

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
    [string]$ScratchContainer = 'antiphon-postgres-drill',
    [string]$ScratchDatabase = 'antiphon_drill',
    [string]$ScratchVolume = 'antiphon-postgres-drill-pgdata',
    [string]$SourceVolume = 'antiphon_pgdata',
    [string]$ScratchImage = 'postgres:16'
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
        if (-not [string]::IsNullOrWhiteSpace($name)) { [void]$all.Add([string]$name) }
    }
    if (-not [string]::IsNullOrWhiteSpace($NewName) -and -not $all.Contains($NewName)) {
        [void]$all.Add($NewName)
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

function Test-SnapshotDrillRefused {
    param(
        [string]$SourceContainer,
        [string]$DrillContainer,
        [string]$DrillVolume,
        [string]$LiveVolume
    )
    $badContainers = @('antiphon-postgres')
    $badVolumes = @('antiphon_pgdata', 'antiphon-server2-postgres_pgdata', 'pgdata')
    if ($DrillContainer -eq $SourceContainer) { return $true }
    if ($badContainers -contains $DrillContainer) { return $true }
    if ($DrillVolume -eq $LiveVolume) { return $true }
    if ($badVolumes -contains $DrillVolume) { return $true }
    return $false
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
        [string]$Age
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
        if ($list.Exit -ne 0 -or $listed.Count -lt 1) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' }
        }
        $sizeText = Invoke-SnapshotNative $Docker @('exec', $SourceContainer, 'wc', '-c', $containerDump)
        $containerBytes = -1L
        if ($sizeText.Lines.Count -gt 0 -and ([string]$sizeText.Lines[0]) -match '(\d+)') {
            $containerBytes = [int64]$Matches[1]
        }
        if ($sizeText.Exit -ne 0 -or $containerBytes -le 0) {
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
        $names = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File | ForEach-Object { $_.Name })
        $prune = Get-SnapshotPruneSet -Names $names -Retain $Keep -NewName $destName -Verified $true
        if (-not $prune.Ok) {
            Write-SnapshotStatus $DestDir $stamp 8 $destBytes
            return [pscustomobject]@{ Code = 8; Reason = 'prune-failed' }
        }
        foreach ($name in @($prune.Drop)) {
            if ($name -eq $destName -or $name -notmatch '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump\.age$') {
                Write-SnapshotStatus $DestDir $stamp 8 $destBytes
                return [pscustomobject]@{ Code = 8; Reason = 'prune-failed' }
            }
            $victim = Join-Path $DestDir $name
            if (-not (Test-SnapshotPathInside $DestDir $victim)) {
                Write-SnapshotStatus $DestDir $stamp 8 $destBytes
                return [pscustomobject]@{ Code = 8; Reason = 'prune-failed' }
            }
            Remove-Item -LiteralPath $victim -Force
        }
        $left = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File)
        $still = @($left | Where-Object { $_.Name -eq $destName })
        if ($left.Count -lt 1 -or $left.Count -gt $Keep -or $still.Count -ne 1) {
            Write-SnapshotStatus $DestDir $stamp 8 $destBytes
            return [pscustomobject]@{ Code = 8; Reason = 'prune-failed' }
        }
        Write-SnapshotStatus $DestDir $stamp 0 $destBytes
        return [pscustomobject]@{ Code = 0; Reason = 'ok'; Detail = ('bytes=' + $destBytes + ' kept=' + $left.Count) }
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
        [string]$Age
    )
    $newest = @(Get-ChildItem -LiteralPath $DestDir -Filter 'antiphon-*.dump.age' -File | Sort-Object Name -Descending)
    if ($newest.Count -lt 1) { return [pscustomobject]@{ Code = 9; Reason = 'missing-snapshot' } }
    $cipher = $newest[0]
    $plainName = 'antiphon-drill-plain.dump'
    $plain = Join-Path $Stage $plainName
    try {
        $decoded = Invoke-SnapshotNative $Age @('-d', '-i', $IdentityPath, '-o', $plain, $cipher.FullName)
        if ($decoded.Exit -ne 0 -or -not (Test-Path -LiteralPath $plain)) {
            return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' }
        }
        $plainBytes = ([System.IO.FileInfo]::new($plain)).Length
        if ($plainBytes -le 0) { return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' } }
        $listed = Invoke-SnapshotNative $Docker @(
            'run', '--rm', '--network', 'none',
            '-v', ($Stage + ':/work:ro'),
            '--entrypoint', 'pg_restore', $Image,
            '--list', ('/work/' + $plainName)
        )
        $toc = @($listed.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($listed.Exit -ne 0 -or $toc.Count -lt 1) {
            return [pscustomobject]@{ Code = 5; Reason = 'verify-failed' }
        }
        $inspect = Invoke-SnapshotNative $Docker @('inspect', '--format', '{{.Name}}', $DrillContainer)
        if ($inspect.Exit -ne 0) {
            $started = Invoke-SnapshotNative $Docker @(
                'run', '-d', '--name', $DrillContainer, '--network', 'none',
                '-v', ($DrillVolume + ':/var/lib/postgresql/data'),
                '-e', 'POSTGRES_HOST_AUTH_METHOD=trust',
                '-e', 'POSTGRES_USER=antiphon',
                $Image
            )
            if ($started.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' } }
        }
        $ready = $false
        for ($try = 0; $try -lt 12; $try++) {
            $probe = Invoke-SnapshotNative $Docker @('exec', $DrillContainer, 'pg_isready', '-U', 'antiphon')
            if ($probe.Exit -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' } }
        if ($DrillDatabase -ne 'antiphon') {
            $created = Invoke-SnapshotNative $Docker @(
                'exec', $DrillContainer, 'createdb', '-U', 'antiphon', $DrillDatabase
            )
            if ($created.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' } }
        }
        $remote = '/tmp/antiphon-drill.dump'
        $sent = Invoke-SnapshotNative $Docker @('cp', $plain, ($DrillContainer + ':' + $remote))
        if ($sent.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' } }
        $restored = Invoke-SnapshotNative $Docker @(
            'exec', $DrillContainer, 'pg_restore', '--no-owner', '--no-acl', '--exit-on-error',
            '-U', 'antiphon', '-d', $DrillDatabase, $remote
        )
        Invoke-SnapshotNative $Docker @('exec', $DrillContainer, 'rm', '-f', $remote) | Out-Null
        if ($restored.Exit -ne 0) { return [pscustomobject]@{ Code = 9; Reason = 'drill-failed' } }
        return [pscustomobject]@{ Code = 0; Reason = 'ok'; Detail = ('drill=1 bytes=' + $plainBytes) }
    } finally {
        Remove-SnapshotPartial @($plain)
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
    if (-not $namesOk -or $Retain -lt 1) {
        return Write-SnapshotResult $planOnly 'bad-name' ''
    }
    if (Test-SnapshotDrillRefused $Container $ScratchContainer $ScratchVolume $SourceVolume) {
        return Write-SnapshotResult $planOnly 'drill-refused' ''
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
            Write-Output 'step drill: pg_restore --list and a size check on the plaintext'
            Write-Output ('step drill: pg_restore into scratch container ' + $ScratchContainer + ' database ' + $ScratchDatabase + ' volume ' + $ScratchVolume)
            Write-Output 'step drill: leave the production database and volume untouched'
            return Write-SnapshotResult $true 'ok' ('drill=1 scratch=' + $ScratchContainer)
        }
        $ageCmd = Get-Command age -CommandType Application -ErrorAction SilentlyContinue
        $dockerCmd = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue
        if (-not $ageCmd) { return Write-SnapshotResult $false 'missing-age' '' }
        if (-not $dockerCmd) { return Write-SnapshotResult $false 'missing-docker' '' }
        if (-not (Test-Path -LiteralPath $stage)) { New-Item -ItemType Directory -Path $stage -Force | Out-Null }
        $drilled = Invoke-SnapshotDrill $stage $dest $identity $ScratchContainer $ScratchDatabase $ScratchVolume $ScratchImage $dockerCmd.Source $ageCmd.Source
        return Write-SnapshotResult $false $drilled.Reason $drilled.Detail
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
        Write-Output 'step verify: pg_restore --list and a size check before the destination is touched'
        Write-Output 'step encrypt: age -R <recipient-file> -o <staging>.dump.age'
        Write-Output 'step copy: move the ciphertext into the snapshot directory as a new antiphon-<utc>.dump.age file'
        Write-Output ('step retention: keep ' + $Retain + ' dump.age files and prune only after the new file is verified')
        return Write-SnapshotResult $true 'ok' ('dump=1 encrypt=1 copy=1 retention=' + $Retain)
    }

    $ageCmd = Get-Command age -CommandType Application -ErrorAction SilentlyContinue
    $dockerCmd = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue
    if (-not $ageCmd) { return Write-SnapshotResult $false 'missing-age' '' }
    if (-not $dockerCmd) { return Write-SnapshotResult $false 'missing-docker' '' }
    if (-not (Test-Path -LiteralPath $stage)) { New-Item -ItemType Directory -Path $stage -Force | Out-Null }
    $published = Invoke-SnapshotPublish $Container $Database $stage $dest $recipient $Retain $dockerCmd.Source $ageCmd.Source
    return Write-SnapshotResult $false $published.Reason $published.Detail
}

if ($MyInvocation.InvocationName -eq '.') { return }
$script:SnapshotExit = 1
Invoke-PostgresSnapshot
exit $script:SnapshotExit
