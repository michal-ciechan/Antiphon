#requires -Version 7.0
# CARD-0835 source identity. Keep this file ASCII-only.
. (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
. (Join-Path $PSScriptRoot 'checkpoint-index-paths.ps1')
. (Join-Path $PSScriptRoot 'checkpoint-tool-reads.ps1')

function Get-CheckpointGitBytes {
    param([string]$Repository, [string[]]$Arguments)
    # A read the named tool build answers in process (rev-parse --show-toplevel, for run-checkpoint.ps1's root).
    if ($Arguments.Count -eq 2 -and $Arguments[0] -ceq 'rev-parse' -and $Arguments[1] -ceq '--show-toplevel') {
        $answered = Get-CheckpointToolRead $Repository $Arguments
        if ($null -ne $answered) { return ,([System.Text.Encoding]::UTF8.GetBytes([string]$answered)) }
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'git'
    $psi.WorkingDirectory = $Repository
    foreach ($argument in $Arguments) { [void]$psi.ArgumentList.Add($argument) }
    try {
        $result = [Antiphon.Checkpoints.CheckpointProcessRunner]::RunBytes(
            $psi, 30000, $true, [System.Threading.CancellationToken]::None)
    } catch [System.ComponentModel.Win32Exception] { throw 'git_start' }
    if ($result.TimedOut) { throw 'git_timeout' }
    if ($result.Exit -ne 0) { throw 'git_exit' }
    return ,($result.Stdout)
}

function Add-CheckpointSourceBytes {
    param([System.Security.Cryptography.IncrementalHash]$Hash, [byte[]]$Bytes)
    $length = [System.BitConverter]::GetBytes([long]$Bytes.Length)
    if ([System.BitConverter]::IsLittleEndian) { [array]::Reverse($length) }
    $Hash.AppendData($length)
    $Hash.AppendData($Bytes)
}

# Fingerprint v2 (the tool's SourceSnapshot computes the same identity from libgit2): SHA-256 over length-framed
# records: version 2, HEAD, then for each record status prints (ignored files excluded), in git's byte order of
# paths with a path's tracked record before its untracked one (`git rm --cached` prints both), the path, its HEAD
# mode and id, its index mode and id (empty when absent) and its work-tree token. One git read per capture:
# status --porcelain=v2 (HEAD, modes and ids); index flags come from a plain index file, else ls-files -v.
function Get-CheckpointWorkTreeToken {
    param([string]$Root, [string]$Relative)
    $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $Relative.TrimEnd('/')))
    $within = [System.IO.Path]::GetRelativePath([System.IO.Path]::GetFullPath($Root), $full)
    if ($within -eq '..' -or $within.StartsWith('../') -or $within.StartsWith('..\') -or [System.IO.Path]::IsPathRooted($within)) { throw 'path_escape' }
    $info = [System.IO.FileInfo]::new($full)
    if ($null -ne $info.LinkTarget) { return 'link:' + $info.LinkTarget }
    $directory = [System.IO.DirectoryInfo]::new($full)
    if ($null -ne $directory.LinkTarget) { return 'link:' + $directory.LinkTarget }
    if ($directory.Exists) { return 'directory' }
    if (-not $info.Exists) { return 'absent' }
    $beforeLength = $info.Length
    $beforeWrite = $info.LastWriteTimeUtc
    $digest = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([byte[]](Read-CheckpointBytes $full))).ToLowerInvariant()
    $info.Refresh()
    if ($info.Length -ne $beforeLength -or $info.LastWriteTimeUtc -ne $beforeWrite) { throw 'file_changed' }
    $executable = (-not $IsWindows) -and (([int]$info.UnixFileMode -band [int][System.IO.UnixFileMode]::UserExecute) -ne 0)
    if ($executable) { return 'file:' + $digest + ':x' }
    return 'file:' + $digest
}

function Get-CheckpointSourceOnce {
    param([string]$Repository, [string]$GitDir = '')
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    [byte[]]$status = Get-CheckpointGitBytes $Repository @('status', '--porcelain=v2', '--branch', '-z', '--untracked-files=all', '--ignore-submodules=none', '--no-renames')
    # Porcelain omits worktree edits hidden by index flags. Refuse to
    # certify any such index until its working content has been verified.
    # A plain version-2 index in the git dir git reported answers from the file (git status just accepted this
    # repository; that index can carry only the assume-unchanged flag); anything else asks ls-files -v
    # (CheckpointIndexPaths.AnyHiddenEntry), as does a session holding an older type without that method.
    $hidden = $null
    $indexPaths = 'Antiphon.Checkpoints.CheckpointIndexPaths' -as [type]
    if ($null -ne $indexPaths -and $null -ne $indexPaths.GetMethod('AnyHiddenEntry', [Type[]]@([string], [string]))) {
        $hidden = $indexPaths::AnyHiddenEntry($Repository, $GitDir)
    }
    if ($true -eq $hidden) { throw 'indexed_path_hidden' }
    if ($null -eq $hidden) {
        [byte[]]$indexFlags = Get-CheckpointGitBytes $Repository @('ls-files', '-v', '-z')
        $flagIndex = 0
        while ($flagIndex -lt $indexFlags.Length) {
            $flagEnd = [array]::IndexOf($indexFlags, [byte]0, $flagIndex)
            if ($flagEnd -lt $flagIndex + 3 -or $indexFlags[$flagIndex + 1] -ne 32) { throw 'index_flags_invalid' }
            $flag = [char]$indexFlags[$flagIndex]
            if ($flag -ceq 'S' -or [char]::IsLower($flag)) { throw 'indexed_path_hidden' }
            if ($flag -cnotmatch '^[A-Z]$') { throw 'index_flags_invalid' }
            $flagIndex = $flagEnd + 1
        }
    }
    $commit = $null
    $entries = [System.Collections.Generic.List[object]]::new()
    $index = 0
    while ($index -lt $status.Length) {
        $end = [array]::IndexOf($status, [byte]0, $index)
        if ($end -le $index) { throw 'status_invalid' }
        $record = $utf8.GetString($status, $index, $end - $index)
        $index = $end + 1
        if ($record.StartsWith('# ', [StringComparison]::Ordinal)) {
            if ($record.StartsWith('# branch.oid ', [StringComparison]::Ordinal)) { $commit = $record.Substring(13) }
            continue
        }
        switch -CaseSensitive ($record.Substring(0, 2)) {
            '1 ' {
                # 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
                $fields = $record.Split(' ', 9)
                if ($fields.Count -ne 9 -or $fields[8].Length -eq 0) { throw 'status_invalid' }
                if ($fields[2] -cne 'N...') { throw 'submodule_unsupported' }
                $absent = '000000'
                $entries.Add([pscustomobject]@{
                    Path = $fields[8]
                    HeadMode = if ($fields[3] -ceq $absent) { '' } else { $fields[3] }
                    HeadOid = if ($fields[3] -ceq $absent) { '' } else { $fields[6] }
                    IndexMode = if ($fields[4] -ceq $absent) { '' } else { $fields[4] }
                    IndexOid = if ($fields[4] -ceq $absent) { '' } else { $fields[7] }
                })
            }
            '? ' {
                $entries.Add([pscustomobject]@{ Path = $record.Substring(2); HeadMode = ''; HeadOid = ''; IndexMode = ''; IndexOid = '' })
            }
            'u ' { throw 'unmerged_index' }
            default { throw 'status_invalid' }
        }
    }
    if ($null -eq $commit -or $commit -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { throw 'head_invalid' }
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        Add-CheckpointSourceBytes $hash ([byte[]]@(2))
        Add-CheckpointSourceBytes $hash ($utf8.GetBytes($commit))
        # git's byte order of paths; a path's tracked record before its untracked one (`git rm --cached` prints both).
        $ordered = @($entries | Sort-Object -CaseSensitive -Property @(
            @{ Expression = { [System.Convert]::ToHexString($utf8.GetBytes($_.Path)) } },
            @{ Expression = { if ($_.HeadMode -eq '' -and $_.IndexMode -eq '') { 1 } else { 0 } } }))
        foreach ($entry in $ordered) {
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes($entry.Path))
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes($entry.HeadMode))
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes($entry.HeadOid))
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes($entry.IndexMode))
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes($entry.IndexOid))
            Add-CheckpointSourceBytes $hash ($utf8.GetBytes((Get-CheckpointWorkTreeToken $Repository $entry.Path)))
        }
        return [pscustomobject]@{
            commit = $commit
            dirtyFiles = $entries.Count
            fingerprint = [System.Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            captureStatus = 'known'
            errorCode = $null
        }
    } finally { $hash.Dispose() }
}

function Get-CheckpointSource {
    param([string]$Repository = (Get-Location).Path)
    # A run that names a checkpoint tool build reads its source the one way the tool does (checkpoint-tool-reads.ps1);
    # the git reader below answers every other run, and this one when that build cannot be loaded.
    $viaTool = Get-CheckpointToolSource $Repository
    if ($null -ne $viaTool) { return $viaTool }
    $drivers = $null
    try {
        # One filter/textconv driver lookup for this snapshot's reads (CheckpointProcessRunner.DriverSettingsScope).
        $drivers = New-CheckpointDriverScope
        # The toplevel and the git dir git uses: read from the repository's files where they settle it as git's
        # discovery does (CheckpointProcessRunner.FindRepository), one rev-parse otherwise, or when the loaded runner
        # predates it. The index-file flag check trusts only that git dir.
        $found = $null
        $runner = [Antiphon.Checkpoints.CheckpointProcessRunner]
        if ($null -ne $runner.GetMethod('FindRepository', [Type[]]@([string]))) { $found = $runner::FindRepository($Repository) }
        if ($null -ne $found) {
            $root = [string]$found[0]
            $gitDir = [string]$found[1]
        } else {
            $located = ([System.Text.Encoding]::UTF8.GetString((Get-CheckpointGitBytes $Repository @('rev-parse', '--show-toplevel', '--absolute-git-dir')))) -split "`n"
            $root = ([string]$located[0]).Trim()
            $gitDir = if ($located.Count -gt 1) { ([string]$located[1]).Trim() } else { '' }
        }
        $first = Get-CheckpointSourceOnce $root $gitDir
        $second = Get-CheckpointSourceOnce $root $gitDir
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
    } finally { if ($null -ne $drivers) { $drivers.Dispose() } }
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
