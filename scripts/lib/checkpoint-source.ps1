#requires -Version 7.0
# CARD-0835 source identity. Keep this file ASCII-only.

function Get-CheckpointGitBytes {
    param([string]$Repository, [string[]]$Arguments)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'git'
    $psi.WorkingDirectory = $Repository
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$psi.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $process) { throw 'git_start' }
    try {
        $stream = [System.IO.MemoryStream]::new()
        try {
            $copy = $process.StandardOutput.BaseStream.CopyToAsync($stream)
            $errorRead = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) {
                $process.Kill($true)
                throw 'git_timeout'
            }
            $null = $copy.GetAwaiter().GetResult()
            $null = $errorRead.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw 'git_exit' }
            return ,($stream.ToArray())
        } finally { $stream.Dispose() }
    } finally { $process.Dispose() }
}

function Add-CheckpointSourceBytes {
    param([System.Security.Cryptography.IncrementalHash]$Hash, [byte[]]$Bytes)
    $length = [System.BitConverter]::GetBytes([long]$Bytes.Length)
    if ([System.BitConverter]::IsLittleEndian) { [array]::Reverse($length) }
    $Hash.AppendData($length)
    $Hash.AppendData($Bytes)
}

function Get-CheckpointSourceOnce {
    param([string]$Repository)
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $commit = $utf8.GetString((Get-CheckpointGitBytes $Repository @('rev-parse', 'HEAD'))).Trim()
    if ($commit -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { throw 'head_invalid' }
    [byte[]]$status = Get-CheckpointGitBytes $Repository @('status', '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=none')
    # Porcelain and diff omit worktree edits hidden by index flags. Refuse to
    # certify any such index until its working content has been verified.
    [byte[]]$indexFlags = Get-CheckpointGitBytes $Repository @('ls-files', '-v', '-z')
    $flagIndex = 0
    while ($flagIndex -lt $indexFlags.Length) {
        $flagEnd = [array]::IndexOf($indexFlags, [byte]0, $flagIndex)
        if ($flagEnd -lt $flagIndex + 3 -or $indexFlags[$flagIndex + 1] -ne 32) { throw 'index_flags_invalid' }
        $flag = [char]$indexFlags[$flagIndex]
        if ($flag -ceq 'S' -or [char]::IsLower($flag)) { throw 'indexed_path_hidden' }
        $flagIndex = $flagEnd + 1
    }
    $untracked = [System.Collections.Generic.List[byte[]]]::new()
    $count = 0
    $index = 0
    while ($index -lt $status.Length) {
        $end = [array]::IndexOf($status, [byte]0, $index)
        if ($end -lt $index -or $end - $index -lt 4 -or $status[$index + 2] -ne 32) { throw 'status_invalid' }
        $x = [char]$status[$index]
        $y = [char]$status[$index + 1]
        if ($x -eq 'U' -or $y -eq 'U' -or ($x -eq 'A' -and $y -eq 'A') -or ($x -eq 'D' -and $y -eq 'D')) { throw 'unmerged_index' }
        [byte[]]$path = $status[($index + 3)..($end - 1)]
        if ($x -eq '?' -and $y -eq '?') { $untracked.Add($path) }
        elseif ($x -ceq 'm' -or $y -ceq 'm' -or $x -ceq '?' -or $y -ceq '?') { throw 'submodule_unsupported' }
        $count++
        $index = $end + 1
        if ($x -in @('R', 'C') -or $y -in @('R', 'C')) {
            $end = [array]::IndexOf($status, [byte]0, $index)
            if ($end -lt $index) { throw 'status_invalid' }
            $index = $end + 1
        }
    }
    [byte[]]$headDiff = Get-CheckpointGitBytes $Repository @('-c', 'core.quotePath=false', 'diff', 'HEAD', '--binary', '--no-ext-diff', '--no-textconv', '--no-color', '--no-renames')
    [byte[]]$cachedDiff = Get-CheckpointGitBytes $Repository @('-c', 'core.quotePath=false', 'diff', '--cached', '--binary', '--no-ext-diff', '--no-textconv', '--no-color', '--no-renames')
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        Add-CheckpointSourceBytes $hash ([byte[]]@(1))
        Add-CheckpointSourceBytes $hash ($utf8.GetBytes($commit))
        Add-CheckpointSourceBytes $hash $status
        Add-CheckpointSourceBytes $hash $headDiff
        Add-CheckpointSourceBytes $hash $cachedDiff
        foreach ($path in @($untracked | Sort-Object { [System.Convert]::ToHexString($_) } -CaseSensitive)) {
            Add-CheckpointSourceBytes $hash $path
            $relative = $utf8.GetString($path)
            $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Repository, $relative))
            $within = [System.IO.Path]::GetRelativePath([System.IO.Path]::GetFullPath($Repository), $full)
            if ($within -eq '..' -or $within.StartsWith('../') -or $within.StartsWith('..\')) { throw 'path_escape' }
            $info = [System.IO.FileInfo]::new($full)
            if (-not $info.Exists -and $null -eq $info.LinkTarget) { throw 'untracked_missing' }
            if ($null -ne $info.LinkTarget) {
                [byte[]]$digest = [System.Security.Cryptography.SHA256]::HashData($utf8.GetBytes($info.LinkTarget))
            } else {
                $beforeLength = $info.Length
                $beforeWrite = $info.LastWriteTimeUtc
                $stream = [System.IO.FileStream]::new($full, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                try { [byte[]]$digest = [System.Security.Cryptography.SHA256]::HashData($stream) }
                finally { $stream.Dispose() }
                $info.Refresh()
                if ($info.Length -ne $beforeLength -or $info.LastWriteTimeUtc -ne $beforeWrite) { throw 'file_changed' }
            }
            Add-CheckpointSourceBytes $hash $digest
        }
        return [pscustomobject]@{
            commit = $commit
            dirtyFiles = $count
            fingerprint = [System.Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            captureStatus = 'known'
            errorCode = $null
        }
    } finally { $hash.Dispose() }
}

function Get-CheckpointSource {
    param([string]$Repository = (Get-Location).Path)
    try {
        $root = ([System.Text.Encoding]::UTF8.GetString((Get-CheckpointGitBytes $Repository @('rev-parse', '--show-toplevel')))).Trim()
        $first = Get-CheckpointSourceOnce $root
        $second = Get-CheckpointSourceOnce $root
        if ($first.commit -cne $second.commit -or $first.dirtyFiles -ne $second.dirtyFiles -or $first.fingerprint -cne $second.fingerprint) { throw 'capture_changed' }
        return $second
    } catch {
        $code = [string]$_.Exception.Message
        if ($code -cnotmatch '^[a-z_]+$') { $code = 'capture_failed' }
        return [pscustomobject]@{
            commit = $null; dirtyFiles = $null; fingerprint = $null
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            captureStatus = 'unknown'; errorCode = $code
        }
    }
}

function Get-CheckpointSourceState {
    param($Start, $End)
    if ($null -eq $End -or $Start.captureStatus -cne 'known' -or $End.captureStatus -cne 'known') { return 'unknown' }
    if ($Start.commit -cne $End.commit -or $Start.fingerprint -cne $End.fingerprint -or $Start.dirtyFiles -ne $End.dirtyFiles) { return 'changed' }
    if ($Start.dirtyFiles -eq 0) { return 'clean' }
    return 'dirty'
}

function Get-CheckpointSourceToken {
    param($Observation)
    if ($Observation.captureStatus -cne 'known') { return 'unknown' }
    if ($Observation.dirtyFiles -eq 0) { return [string]$Observation.commit }
    return ('{0}+dirty:{1}' -f $Observation.commit, $Observation.fingerprint)
}

function Test-CheckpointSourceEvidence {
    param($Evidence, [string]$ExpectedSourceSha, [switch]$AllowCommand)
    if ($ExpectedSourceSha -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { return $false }
    if ($null -eq $Evidence -or $Evidence.version -ne 1 -or $null -eq $Evidence.start -or $null -eq $Evidence.end) { return $false }
    if ($Evidence.start.captureStatus -cne 'known' -or $Evidence.end.captureStatus -cne 'known') { return $false }
    if ($Evidence.start.commit -cne $ExpectedSourceSha -or $Evidence.end.commit -cne $ExpectedSourceSha) { return $false }
    if ($Evidence.start.dirtyFiles -ne 0 -or $Evidence.end.dirtyFiles -ne 0 -or $Evidence.state -cne 'clean') { return $false }
    if ($Evidence.start.fingerprint -cnotmatch '^[0-9a-f]{64}$' -or $Evidence.start.fingerprint -cne $Evidence.end.fingerprint) { return $false }
    return $Evidence.buildSource -ceq 'verified' -or ($AllowCommand -and $Evidence.buildSource -ceq 'notApplicable')
}
