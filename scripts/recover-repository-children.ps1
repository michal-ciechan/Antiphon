<#
.SYNOPSIS
    Inspect or recover crash-orphaned repository child journals (CARD-0448).
.DESCRIPTION
    Preview is the default. Execute requires an explicit confirmation that surviving
    descendants of the recorded children have exited. A dead/reused root PID alone
    cannot prove that, nor can a completed record (its root exited before its start
    identity was read). Live or ambiguous records are always retained. No process is
    killed and no Git lock, worktree or landing evidence is removed.
    Exit 0: no retained records; exit 3: busy, retained or unconfirmed evidence.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [switch]$Execute,
    [switch]$ConfirmDescendantsExited
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    # Windows PowerShell 5.1 runs on .NET Framework, which has no ResolveLinkTarget.
    # A directory handle gives the final path of a junction/symlink without trusting
    # the journal's recorded path or a provider-specific Target property.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class AntiphonRecoveryFinalPath {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path,
        uint capacity, uint flags);
    public static string Resolve(string path) {
        using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var buffer = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var final = buffer.ToString();
            if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + final.Substring(8);
            if (final.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return final.Substring(4);
            return final;
        }
    }
}
'@
}

function Get-CanonicalDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    $current = $root
    $parts = $full.Substring($root.Length).Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($part in $parts) {
        $current = [IO.Path]::Combine($current, $part)
        $info = [IO.DirectoryInfo]::new($current)
        if (-not $info.Exists) { throw 'Git common directory is missing or inaccessible.' }
        if ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
                $current = [AntiphonRecoveryFinalPath]::Resolve($current)
            } else {
                $target = $info.ResolveLinkTarget($true)
                if ($null -eq $target) { throw 'Git common directory alias cannot be resolved.' }
                $current = $target.FullName
            }
        }
    }
    return [IO.Path]::GetFullPath($current).TrimEnd('\', '/')
}

function Get-ChildState($Record, [string]$Common) {
    if ($Record.SchemaVersion -ne 1 -or
        $null -eq $Record.ProcessId -or $Record.ProcessId -le 0 -or
        -not [string]::Equals([IO.Path]::GetFullPath($Record.CommonDirectory).TrimEnd('\', '/'),
            $Common.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        return 'unknown'
    }
    # CARD-0661: the owner saw this child's exact handle exit before it could read a start
    # identity, then died before draining output. The root is gone; its PID may be reused and
    # is never looked up. Only descendants remain in question, as for a dead record.
    $completed = $Record.PSObject.Properties['Completed']
    if ($null -eq $Record.StartTicks -and $null -ne $completed -and $completed.Value -is [bool] -and $completed.Value) {
        return 'completed'
    }
    if ($null -eq $Record.StartTicks -or $Record.StartTicks -le 0) { return 'unknown' }
    $recordedProcess = $null
    try {
        try { $recordedProcess = [Diagnostics.Process]::GetProcessById($Record.ProcessId) }
        catch [ArgumentException] { return 'dead' }
        if ($recordedProcess.HasExited) { return 'dead' }
        if ($recordedProcess.StartTime.ToUniversalTime().Ticks -ne $Record.StartTicks) { return 'reused' }
        return 'alive'
    } catch { return 'unknown' }
    finally { if ($null -ne $recordedProcess) { $recordedProcess.Dispose() } }
}

function Get-ChildTag($Record) {
    $tag = ''
    $purpose = $Record.PSObject.Properties['Purpose']
    if ($null -ne $purpose -and -not [string]::IsNullOrEmpty([string]$purpose.Value)) {
        $tag += ' purpose=' + [string]$purpose.Value
    }
    $task = $Record.PSObject.Properties['TaskId']
    if ($null -ne $task -and -not [string]::IsNullOrEmpty([string]$task.Value)) {
        $tag += ' task=' + [string]$task.Value
    }
    return $tag
}

$lease = $null
try {
    $commonOutput = @(& git -C $Repository rev-parse --path-format=absolute --git-common-dir)
    if ($LASTEXITCODE -ne 0 -or $commonOutput.Count -ne 1) { throw 'Cannot resolve Git common directory.' }
    $common = Get-CanonicalDirectory $commonOutput[0]
    $admissionDirectory = Join-Path $common 'antiphon'
    [IO.Directory]::CreateDirectory($admissionDirectory) | Out-Null
    # Same exclusion as RepositoryMutationLease, including across linked worktrees.
    $lease = [IO.File]::Open((Join-Path $admissionDirectory 'landing.lock'),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $directory = Join-Path $admissionDirectory 'children'
    $retained = 0
    if (Test-Path -LiteralPath $directory) {
        $directoryInfo = Get-Item -LiteralPath $directory -Force
        if (-not $directoryInfo.PSIsContainer -or ($directoryInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Child journal directory is not a plain directory.'
        }
        foreach ($file in Get-ChildItem -LiteralPath $directory -File -Force) {
            $state = 'unknown'
            $tag = ''
            try {
                if ($file.Extension -ceq '.json' -and -not ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                    $state = Get-ChildState $record $common
                    $tag = Get-ChildTag $record
                }
                if ($state -in @('dead', 'reused', 'completed') -and $Execute -and $ConfirmDescendantsExited) {
                    Remove-Item -LiteralPath $file.FullName -Force
                    Write-Output "recovered ($state): $($file.FullName)"
                    continue
                }
            } catch { $state = 'unknown' }
            $retained++
            Write-Output "retained ($state): $($file.FullName)$tag"
        }
    }
    if ($retained -gt 0) {
        Write-Output 'After inspecting descendants, use -Execute -ConfirmDescendantsExited to recover only dead/reused/completed records.'
        exit 3
    }
    exit 0
} catch {
    Write-Warning "Repository recovery refused: $($_.Exception.Message)"
    exit 3
} finally {
    if ($null -ne $lease) { $lease.Dispose() }
    # Never unlink landing.lock: its open handle, not its existence, owns exclusion.
}
