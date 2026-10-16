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

function Seal-CheckpointFreshOutput {
    param([string]$Path)
    $entries = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue([System.IO.Path]::GetFullPath($Path))
    while ($queue.Count -gt 0) {
        foreach ($full in [System.IO.Directory]::EnumerateFileSystemEntries($queue.Dequeue())) {
            if ($null -ne ([System.IO.FileInfo]::new($full)).LinkTarget -or $null -ne ([System.IO.DirectoryInfo]::new($full)).LinkTarget -or ([System.IO.File]::GetAttributes($full) -band [System.IO.FileAttributes]::ReparsePoint)) { return 'output-inventory-failed' }
            $relative = [System.IO.Path]::GetRelativePath($Path, $full).Replace('\', '/')
            if ($relative -eq '.checkpoint-output-ledger.json') { continue }
            if ([System.IO.Directory]::Exists($full)) { $entries[$relative] = 'directory'; $queue.Enqueue($full) }
            else {
                try { $entries[$relative] = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([byte[]](Read-CheckpointBytes $full))) }
                catch { return 'output-inventory-failed' }
            }
        }
    }
    return Write-CheckpointSafeText (Join-Path $Path '.checkpoint-output-ledger.json') ($entries | ConvertTo-Json -Depth 6) $Path
}
