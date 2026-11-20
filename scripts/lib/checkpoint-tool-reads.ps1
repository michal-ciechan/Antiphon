#requires -Version 7.0
# run-checkpoint.ps1's git reads answered by a named checkpoint tool build (CheckpointToolReads.cs), so a run reads
# its source and its confinement state the one way the tool does (libgit2, in process). Only when
# ANTIPHON_CHECKPOINT_TOOL_ASSEMBLY names a build of Antiphon.Checkpoints.dll; otherwise, or when that build cannot
# be loaded (said once with -Verbose), every function here returns $null and git answers, as before.
# Keep this file ASCII-only.
. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')

# The named tool build's path once it has loaded, or $null. One load attempt per process.
function Get-CheckpointToolAssembly {
    $named = [Environment]::GetEnvironmentVariable('ANTIPHON_CHECKPOINT_TOOL_ASSEMBLY')
    if ([string]::IsNullOrWhiteSpace($named)) { return $null }
    if ($null -eq $script:CheckpointToolLoads) {
        $script:CheckpointToolLoads = [System.Collections.Generic.Dictionary[string,bool]]::new([StringComparer]::Ordinal)
    }
    $ready = $false
    if ($script:CheckpointToolLoads.TryGetValue($named, [ref]$ready)) {
        if ($ready) { return $named } else { return $null }
    }
    try {
        if (-not ('Antiphon.Checkpoints.Script.CheckpointToolReads' -as [type])) {
            Import-CheckpointCompiledSource -Source (Read-CheckpointText (Join-Path $PSScriptRoot 'CheckpointToolReads.cs')) `
                -TypeName 'Antiphon.Checkpoints.Script.CheckpointToolReads' -Name 'CheckpointToolReads'
        }
        $ready = [Antiphon.Checkpoints.Script.CheckpointToolReads]::Ready($named)
    } catch {
        Write-Verbose ('checkpoint tool reads unavailable, git answers: ' + $_.Exception.Message)
        $ready = $false
    }
    $script:CheckpointToolLoads[$named] = $ready
    if ($ready) { return $named } else { return $null }
}

# A tool call that throws or passes its deadline leaves every later read of this run to git: one reader from there
# on, not a mix decided call by call.
function Disable-CheckpointToolReads {
    param([string]$Tool, $Failure)
    $message = if ($null -ne $Failure.Exception.InnerException) { $Failure.Exception.InnerException.Message } else { $Failure.Exception.Message }
    Write-Verbose ('checkpoint tool reads stopped, git answers the rest of this run: ' + $message)
    $script:CheckpointToolLoads[$Tool] = $false
}

# The source snapshot by the tool's SourceSnapshot, shaped as Get-CheckpointSource's observation; $null when no
# build is named or it cannot be loaded.
function Get-CheckpointToolSource {
    param([string]$Repository)
    $tool = Get-CheckpointToolAssembly
    if ($null -eq $tool) { return $null }
    try { $fields = [Antiphon.Checkpoints.Script.CheckpointToolReads]::Capture($tool, $Repository) }
    catch {
        Disable-CheckpointToolReads $tool $_
        return $null
    }
    $known = [string]$fields[3] -ceq 'known'
    return [pscustomobject]@{
        commit = if ($known) { [string]$fields[0] } else { $null }
        dirtyFiles = if ($known) { [int]$fields[1] } else { $null }
        fingerprint = if ($known) { [string]$fields[2] } else { $null }
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        captureStatus = if ($known) { 'known' } else { 'unknown' }
        errorCode = if ($known) { $null } elseif ([string]$fields[4] -cmatch '^[a-z_]+$') { [string]$fields[4] } else { 'capture_failed' }
    }
}

# git's stdout for a confinement read the tool answers in process (rev-parse --show-toplevel or HEAD, ls-files -z -s,
# ls-files -z --others [--ignored] --exclude-standard); $null when git must answer.
function Get-CheckpointToolRead {
    param([string]$WorkDir, [string[]]$GitArgs)
    $tool = Get-CheckpointToolAssembly
    if ($null -eq $tool) { return $null }
    try { return [Antiphon.Checkpoints.Script.CheckpointToolReads]::Read($tool, $WorkDir, [string[]]$GitArgs) }
    catch {
        Disable-CheckpointToolReads $tool $_
        return $null
    }
}

# The tracked paths git status --porcelain=v2 -z --untracked-files=no lists, read in process; $null when git must
# answer.
function Get-CheckpointToolTrackedStatus {
    param([string]$Repo)
    $tool = Get-CheckpointToolAssembly
    if ($null -eq $tool) { return $null }
    try { $paths = [Antiphon.Checkpoints.Script.CheckpointToolReads]::TrackedStatus($tool, $Repo) }
    catch {
        Disable-CheckpointToolReads $tool $_
        return $null
    }
    if ($null -eq $paths) { return $null }
    return ,$paths
}
