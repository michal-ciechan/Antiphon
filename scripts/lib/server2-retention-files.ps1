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
        '-o', '(', '!', '-type', 'd', '!', '-type', 'f', ')', '-o', '-name', '.git', '-o', '-iname', '*.key',
        '-o', '-iname', '*.pem', '-o', '-iname', '.env*', '-o', '-iname', 'credentials.json',
        '-o', '-iname', 'secrets.json', '-o', '-iname', 'token', '-o', '-iname', '*.token',
        '-o', '-iname', 'id_rsa', '-o', '-iname', 'id_ed25519', '-o', '-iname', '.netrc', ')', '-print', '-quit')
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

    foreach ($process in $processes) {
        if ([IO.Path]::GetFileName($process) -notmatch '^[0-9]+$') { continue }
        try {
            $links = @($process + '/cwd', $process + '/root') + [IO.Directory]::GetFiles($process + '/fd')
            foreach ($file in $links) {
                $target = [IO.FileInfo]::new($file).LinkTarget
                if ($target -and ($target -ceq $Path -or $target -ceq "$Path (deleted)" -or
                    $target.StartsWith($prefix, [StringComparison]::Ordinal))) {
                    return 'live-holder'
                }
            }
        }
        catch [IO.DirectoryNotFoundException] { continue }
        catch [IO.FileNotFoundException] { continue }
        catch [UnauthorizedAccessException] {
            # Root-owned supervisors are outside the runner uid's view. A runner-owned
            # unreadable process could be holding task output, so retain the directory.
            $status = Get-Content -LiteralPath ($process + '/status') -ErrorAction SilentlyContinue |
                Where-Object { $_ -match '^Uid:' } | Select-Object -First 1
            if ($status -and $status -notmatch '^Uid:\s+0\s+0\s+0\s+0\s*$') { return 'holder-scan-incomplete' }
            if (-not $status -and [IO.Directory]::Exists($process)) { return 'holder-scan-incomplete' }
        }
        catch [IO.IOException] {
            if ([IO.Directory]::Exists($process)) { return 'holder-scan-incomplete' }
        }
    }
}
