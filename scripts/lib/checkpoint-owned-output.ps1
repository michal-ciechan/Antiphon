#requires -Version 7.0
# Native exclusive creation; no deletion or recovery of user paths.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
function New-CheckpointFreshOutput {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('/', '\')
    $reason = Test-CheckpointWriteRefusal $full $full
    if ($null -ne $reason) { return $reason }
    if (-not ('Antiphon.Checkpoints.ExclusiveDirectory' -as [type])) {
        $nativeSource = Join-Path $PSScriptRoot '../../src/Antiphon.HostCleanup/Checkpoints/ExclusiveDirectory.cs'
        Add-Type -TypeDefinition ((Read-CheckpointText $nativeSource)) -CompilerOptions '/nullable:enable'
    }
    $reason = [Antiphon.Checkpoints.ExclusiveDirectory]::Create($full)
    if ($null -ne $reason) { return $reason }
    return Write-CheckpointSafeText (Join-Path $full '.checkpoint-output-owner.json') ([guid]::NewGuid().ToString('N')) $full
}

function Format-CheckpointInventoryRefusal {
    param([string]$Full, [long]$Limit, [string]$Reason)
    $size = -1L
    try { $size = [int64]([System.IO.FileInfo]::new($Full).Length) } catch { }
    $clean = ([string]$Reason).Replace("`r", ' ').Replace("`n", ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($clean)) { $clean = 'unreadable' }
    return "output-inventory-failed: $Full size=$size limit=${Limit}: $clean"
}

# Hash regular output through the streaming 1 GiB content digest. Receipt reads stay at 64 MiB.
function Seal-CheckpointFreshOutput {
    param([string]$Path)
    $entries = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $held = [System.Collections.Generic.List[object]]::new()
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $root = [System.IO.Path]::GetFullPath($Path)
    $queue.Enqueue($root)
    $limit = [int64][Antiphon.Checkpoints.Coverage.ConfinedFileReader]::ContentLimit
    while ($queue.Count -gt 0) {
        foreach ($full in [System.IO.Directory]::EnumerateFileSystemEntries($queue.Dequeue())) {
            $relative = [System.IO.Path]::GetRelativePath($root, $full).Replace('\', '/')
            if ($relative -eq '.checkpoint-output-ledger.json') { continue }
            $linked = $null -ne ([System.IO.FileInfo]::new($full)).LinkTarget -or $null -ne ([System.IO.DirectoryInfo]::new($full)).LinkTarget -or (([System.IO.File]::GetAttributes($full) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            if ($linked) { return (Format-CheckpointInventoryRefusal $full $limit 'linked output') }
            if ([System.IO.Directory]::Exists($full)) {
                $entries[$relative] = 'directory'
                $held.Add([pscustomobject]@{ Full = $full; Kind = 'directory' })
                $queue.Enqueue($full)
            }
            else {
                try {
                    $digest = Get-CheckpointFileDigest $full
                    if ($null -eq $digest -or $digest.Length -isnot [long] -or [int64]$digest.Length -lt 0 -or [int64]$digest.Length -gt $limit -or $digest.Sha256 -isnot [string] -or [string]$digest.Sha256 -cnotmatch '^[0-9A-F]{64}$') {
                        throw [System.IO.InvalidDataException]::new('content digest invalid')
                    }
                    $entries[$relative] = [string]$digest.Sha256
                    $held.Add([pscustomobject]@{ Full = $full; Kind = 'file'; Digest = $digest })
                } catch {
                    $inner = $_.Exception
                    while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
                    return (Format-CheckpointInventoryRefusal $full $limit ([string]$inner.Message))
                }
            }
        }
    }
    foreach ($item in $held) {
        $full = [string]$item.Full
        try {
            $linked = $null -ne ([System.IO.FileInfo]::new($full)).LinkTarget -or $null -ne ([System.IO.DirectoryInfo]::new($full)).LinkTarget -or (([System.IO.File]::GetAttributes($full) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            if ($linked -or ($item.Kind -eq 'directory' -and -not [System.IO.Directory]::Exists($full))) {
                return (Format-CheckpointInventoryRefusal $full $limit 'changed while sealing')
            }
            if ($item.Kind -eq 'directory') { continue }
            $digest = $item.Digest
            [Antiphon.Checkpoints.Coverage.ConfinedFileReader]::RequireSame($root, $full, [uint64]$digest.Volume, [uint64]$digest.FileId, [uint64]$digest.Length, [long]$digest.WriteSeconds, [uint32]$digest.WriteSubseconds, [uint64]$digest.Links)
        } catch {
            $inner = $_.Exception
            while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
            $reason = [string]$inner.Message
            if ($reason -notlike '*changed while sealing*') { $reason = 'changed while sealing' }
            return (Format-CheckpointInventoryRefusal $full $limit $reason)
        }
    }
    return Write-CheckpointSafeText (Join-Path $root '.checkpoint-output-ledger.json') ($entries | ConvertTo-Json -Depth 6) $root
}
