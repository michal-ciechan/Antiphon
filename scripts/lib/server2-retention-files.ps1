# The caller establishes task ownership and reference clearance before checking a tree.
function Get-AntiphonRetentionRefusal {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Parent,
        [Parameter(Mandatory)] [string] $LeafPattern,
        [Parameter(Mandatory)] [int] $MinAgeHours
    )

    if ([IO.Path]::GetFullPath($Path) -cne $Path -or [IO.Path]::GetDirectoryName($Path) -cne $Parent -or
        [IO.Path]::GetFileName($Path) -cnotmatch $LeafPattern) {
        return 'path-mismatch'
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or -not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        return 'directory-missing-or-linked'
    }
    $physical = & realpath -e -- $Path
    if ($LASTEXITCODE -ne 0 -or $physical -cne $Path) { return 'path-linked' }
    if ($item.LastWriteTimeUtc -ge [DateTime]::UtcNow.AddHours(-$MinAgeHours)) { return 'recent' }

    foreach ($mount in [IO.File]::ReadLines('/proc/self/mountinfo')) {
        $mountPath = [regex]::Replace($mount.Split(' ')[4], '\\([0-7]{3})', {
            param($m)
            [string][char][Convert]::ToInt32($m.Groups[1].Value, 8)
        })
        if ($mountPath -ceq $Path -or $mountPath.StartsWith($Path + '/', [StringComparison]::Ordinal)) {
            return 'mount-under-tree'
        }
    }

    # GNU find follows no links. Any unexpected entry, protected descendant or recent write keeps the tree.
    $unsafe = @('(', '!' , '-user', [Environment]::UserName, '-o', '-mmin', "-$($MinAgeHours * 60)",
        '-o', '(', '!', '-type', 'd', '!', '-type', 'f', ')', '-o', '-iname', '*.key',
        '-o', '-iname', '*.pem', '-o', '-iname', '.env*', '-o', '-iname', 'credentials.json',
        '-o', '-iname', 'secrets.json', '-o', '-iname', 'token', '-o', '-iname', '*.token',
        '-o', '-iname', 'id_rsa', '-o', '-iname', 'id_ed25519', '-o', '-iname', '.netrc')
    # Keep the hard component list in CleanupFamilyRegistry.IsDenied on every visited entry.
    foreach ($component in @('.git', '.claude', '.codex', '.grok', 'node_modules', 'antiphon-pty-hosts',
            'build-slots', 'runner-state', 'hangfire', 'postgres', 'docker', 'reports', 'deliverables')) {
        $unsafe += @('-o', '-iname', $component)
    }
    $unsafe += @(')', '-print', '-quit')
    $unsafePath = & find -P $Path -xdev @unsafe
    if ($LASTEXITCODE -ne 0 -or $unsafePath) { return 'unsafe-or-recent-descendant' }

    return Get-AntiphonRetentionHolder $Path
}

function Get-AntiphonRetentionHolder {
    param([Parameter(Mandatory)] [string] $Path)

    $prefix = $Path + '/'
    try { $processes = [IO.Directory]::GetDirectories('/proc') }
    catch { return 'holder-scan-unavailable' }
    if ("/proc/$PID" -cnotin $processes) { return 'holder-scan-unavailable' }

    $incomplete = $false
    foreach ($process in $processes) {
        if ([IO.Path]::GetFileName($process) -notmatch '^[0-9]+$') { continue }
        try {
            $fd = "$process/fd/"
            foreach ($file in @("$process/cwd", "$process/root")) {
                $target = [IO.FileInfo]::new($file).LinkTarget
                if (Test-AntiphonHolderTarget $target $Path $prefix) {
                    return 'live-holder'
                }
                if (-not $target) { $incomplete = $true }
            }
            foreach ($file in [IO.Directory]::GetFileSystemEntries($fd)) {
                $target = [IO.FileInfo]::new($file).LinkTarget
                if (Test-AntiphonHolderTarget $target $Path $prefix) {
                    return 'live-holder'
                }
                if (-not $target) {
                    # Descriptors may close after enumeration, including this scanner's own fd.
                    try { $null = [IO.File]::GetAttributes($file) }
                    catch [IO.FileNotFoundException] { continue }
                    $incomplete = $true
                }
            }
        }
        catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] {
            if ([IO.Directory]::Exists($process)) { $incomplete = $true }
        }
        catch [IO.IOException], [UnauthorizedAccessException] { $incomplete = $true }
    }
    if ($incomplete) { return 'holder-scan-incomplete' }
}

function Test-AntiphonHolderTarget([string] $Target, [string] $Path, [string] $Prefix) {
    $Target -and ($Target -ceq $Path -or $Target -ceq "$Path (deleted)" -or
        $Target.StartsWith($Prefix, [StringComparison]::Ordinal))
}
