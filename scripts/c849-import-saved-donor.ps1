# CARD-0849 host cache import. Invoked inside the pinned runner image with only the
# explicit source and a private staging directory mounted. Emits diagnosis only.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Stage)
$ErrorActionPreference = 'Stop'
$roots = [ordered]@{
    'home/app/.nuget/packages' = 'packages'
    'home/app/.npm/_cacache' = 'npm'
    '.nuget/packages' = 'packages'
    '.npm/_cacache' = 'npm'
    'packages' = 'packages'
    'npm' = 'npm'
}
$budgets = @{ packages = 10GB; npm = 2GB }
$sizes = @{ packages = [long]0; npm = [long]0 }
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Resolve-Entry([string]$Name, [bool]$Directory) {
    if ([string]::IsNullOrEmpty($Name) -or $Name.StartsWith('/') -or $Name.Contains('\') -or
        $Name.Contains("`n") -or $Name.Contains("`r")) { throw 'CacheDonorUnsafePath' }
    while ($Name.StartsWith('./')) { $Name = $Name.Substring(2) }
    $Name = $Name.TrimEnd('/')
    if ($Name -eq '.' -or $Name -eq '') { return $null }
    $parts = $Name.Split('/')
    if (@($parts | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) { throw 'CacheDonorUnsafePath' }
    foreach ($prefix in $roots.Keys) {
        if ($Name -eq $prefix -or $Name.StartsWith($prefix + '/', [StringComparison]::Ordinal)) {
            $rest = $Name.Substring($prefix.Length).TrimStart('/')
            if (-not $rest) { return $null }
            return [pscustomobject]@{ Role = $roots[$prefix]; Rest = $rest }
        }
        if ($Directory -and $prefix.StartsWith($Name + '/', [StringComparison]::Ordinal)) { return $null }
    }
    throw 'CacheDonorUnsafePath'
}

function Check-Entry([string]$Name, [bool]$Directory, [long]$Length) {
    $mapped = Resolve-Entry $Name $Directory
    if ($null -eq $mapped) { return $null }
    $key = $mapped.Role + '/' + $mapped.Rest
    if (-not $seen.Add($key)) { throw 'CacheDonorDuplicateEntry' }
    if (-not $Directory) {
        $sizes[$mapped.Role] += $Length
        if ($sizes[$mapped.Role] -gt $budgets[$mapped.Role]) { throw 'CacheBudgetExceeded' }
    }
    return $mapped
}

function Check-Space {
    $free = ([System.IO.DriveInfo]::new($Stage)).AvailableFreeSpace
    if ($free -lt (20GB + $sizes.packages + $sizes.npm)) { throw 'CacheDiskLow' }
}

function Write-Entry($Mapped, [bool]$Directory, [System.IO.UnixFileMode]$Mode, $InputStream) {
    if ($null -eq $Mapped) { return }
    $target = Join-Path (Join-Path $Stage $Mapped.Role) $Mapped.Rest
    if ($Directory) { [System.IO.Directory]::CreateDirectory($target) | Out-Null; return }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    $out = [System.IO.File]::Open($target, [System.IO.FileMode]::CreateNew)
    try { $InputStream.CopyTo($out) } finally { $out.Dispose() }
    [System.IO.File]::SetUnixFileMode($target, $Mode)
}

function Read-Tar([bool]$Copy) {
    $stream = [System.IO.File]::OpenRead($Source)
    try {
        $reader = [System.Formats.Tar.TarReader]::new($stream, $true)
        try {
            while ($null -ne ($entry = $reader.GetNextEntry())) {
                $kind = $entry.EntryType.ToString()
                $directory = $kind -eq 'Directory'
                if (-not $directory -and $kind -notin @('RegularFile', 'V7RegularFile')) {
                    throw 'CacheDonorUnsafeEntry'
                }
                if ($Copy) {
                    $mapped = Resolve-Entry $entry.Name $directory
                    Write-Entry $mapped $directory $entry.Mode $entry.DataStream
                } else {
                    [void](Check-Entry $entry.Name $directory ([long]$entry.Length))
                }
            }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Read-Directory([bool]$Copy) {
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Source)
    while ($pending.Count -gt 0) {
        $base = $pending.Pop()
        foreach ($path in [System.IO.Directory]::EnumerateFileSystemEntries($base)) {
            $attributes = [System.IO.File]::GetAttributes($path)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'CacheDonorUnsafeEntry' }
            $directory = ($attributes -band [System.IO.FileAttributes]::Directory) -ne 0
            if ($directory) { $pending.Push($path) }
            $relative = [System.IO.Path]::GetRelativePath($Source, $path).Replace('\', '/')
            if ($Copy) {
                $mapped = Resolve-Entry $relative $directory
                if ($directory) { Write-Entry $mapped $true 0 $null }
                else {
                    $input = [System.IO.File]::OpenRead($path)
                    try { Write-Entry $mapped $false ([System.IO.File]::GetUnixFileMode($path)) $input }
                    finally { $input.Dispose() }
                }
            } else {
                $length = if ($directory) { [long]0 } else { ([System.IO.FileInfo]::new($path)).Length }
                [void](Check-Entry $relative $directory $length)
            }
        }
    }
}

try {
    if ([System.IO.Directory]::Exists($Source)) {
        Read-Directory $false
        Check-Space
        Read-Directory $true
    } elseif ([System.IO.File]::Exists($Source)) {
        Read-Tar $false
        Check-Space
        Read-Tar $true
    } else { throw 'CacheSavedDonorInvalid' }
} catch {
    $diagnosis = $_.Exception.Message
    if ($diagnosis -notmatch '^Cache[A-Za-z]+$') { $diagnosis = 'CacheSavedDonorReadFailed' }
    [Console]::Out.WriteLine($diagnosis)
    exit 2
}
