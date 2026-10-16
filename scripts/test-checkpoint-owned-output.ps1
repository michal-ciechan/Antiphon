#requires -Version 7.0
# CARD-0987: private native filesystem smoke. ScriptHarness owns this fresh pwsh child.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { throw 'fixture results directory required' }
$root = [IO.Path]::GetFullPath($ResultsDirectory)
[void][IO.Directory]::CreateDirectory($root)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repo 'src/Antiphon.HostCleanup/Checkpoints/ExclusiveDirectory.cs'
$oldSource = Join-Path $repo 'tools/Antiphon.Checkpoints/Cleanup/ExclusiveDirectory.cs'
$child = Join-Path $root ('native-' + [guid]::NewGuid().ToString('N'))

# The native identity probe uses only fixture-owned directory paths.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class C987DirectoryIdentity
{
    [DllImport("libc", EntryPoint="statx", SetLastError=true)]
    private static extern int Stat(int fd, string path, int flags, uint mask, IntPtr value);
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle Open(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, IntPtr value);
    public static string Read(string path)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var handle = Open(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, buffer))
                    throw new IOException("directory identity failed: " + Marshal.GetLastWin32Error());
                return Marshal.ReadInt32(buffer, 28) + ":" + Marshal.ReadInt32(buffer, 44) + ":" + Marshal.ReadInt32(buffer, 48);
            }
            if (Stat(-100, path, 0x100, 0x7ff, buffer) != 0)
                throw new IOException("directory identity failed: " + Marshal.GetLastWin32Error());
            return Marshal.ReadInt32(buffer, 136) + ":" + Marshal.ReadInt32(buffer, 140) + ":" + Marshal.ReadInt64(buffer, 32);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@

function Invoke-CheckpointGit { throw 'unexpected native Git command in private fixture' }
function dotnet { throw 'unexpected dotnet command' }
function git { throw 'unexpected git command' }
function docker { throw 'unexpected docker command' }
function Start-Process { throw 'unexpected process launch' }
function Stop-Process { throw 'unexpected process stop' }

function Test-C987_StandaloneNativeSource {
    $compiled = $false
    $detail = ''
    try {
        if ('Antiphon.Checkpoints.ExclusiveDirectory' -as [type]) { throw 'type existed before standalone compile' }
        Add-Type -TypeDefinition ([IO.File]::ReadAllText($source)) -CompilerOptions '/nullable:enable'
        $compiled = $null -ne ('Antiphon.Checkpoints.ExclusiveDirectory' -as [type])
    } catch { $detail = $_.Exception.Message }
    Assert-C487 $compiled 'C987 C987.C987_StandaloneNativeSource.native.compile' $detail
    if (!$compiled) { return }
    $created = [Antiphon.Checkpoints.ExclusiveDirectory]::Create($child)
    Assert-C487 ($null -eq $created -and [IO.Directory]::Exists($child)) 'C987 C987.C987_StandaloneNativeSource.native.create' ([string]$created)
    $sentinel = Join-Path $child 'sentinel'
    [IO.File]::WriteAllBytes($sentinel, [byte[]]@(1,2,3,4,5))
    $identity = [C987DirectoryIdentity]::Read($child)
    $collision = [Antiphon.Checkpoints.ExclusiveDirectory]::Create($child)
    Assert-C487 ($collision -ceq 'pre-existing-output') 'C987 C987.C987_StandaloneNativeSource.native.collision' ([string]$collision)
    Assert-C487 ([Convert]::ToHexString([IO.File]::ReadAllBytes($sentinel)) -ceq '0102030405') 'C987 C987.C987_StandaloneNativeSource.native.sentinel'
    Assert-C487 ([C987DirectoryIdentity]::Read($child) -ceq $identity) 'C987 C987.C987_StandaloneNativeSource.native.identity'
    $missing = [Antiphon.Checkpoints.ExclusiveDirectory]::Create((Join-Path $root 'absent-parent/child'))
    Assert-C487 ($missing -clike 'output-create-failed:*') 'C987 C987.C987_StandaloneNativeSource.native.missing-parent' ([string]$missing)
    $mode = if ($IsWindows) { $true } else { [int][IO.File]::GetUnixFileMode($child) -eq 448 }
    Assert-C487 $mode 'C987 C987.C987_StandaloneNativeSource.native.permissions'
    Assert-C487 (-not ([AppDomain]::CurrentDomain.GetAssemblies().GetName().Name -contains 'Antiphon.HostCleanup')) 'C987 C987.C987_StandaloneNativeSource.native.no-library'
}

function Test-C987_OwnedOutputHelper {
    # Load the driver's exact safe-write functions without invoking the build/test driver.
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/run-checkpoint.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw 'driver function parse failed' }
    foreach ($name in @('Test-CheckpointComponentLink', 'Find-CheckpointGitDir', 'Test-CheckpointPreflight',
            'Test-CheckpointWriteRefusal', 'Write-CheckpointSafeText')) {
        $matches = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))
        if ($matches.Count -ne 1) { throw ('missing unique driver function ' + $name) }
        . ([scriptblock]::Create($matches[0].Extent.Text))
    }
    $unrelated = Join-Path $root 'unrelated-cwd'
    [void][IO.Directory]::CreateDirectory($unrelated)
    Push-Location $unrelated
    try {
        . (Join-Path $repo 'scripts/lib/checkpoint-owned-output.ps1')
        $script:C987Reads = [Collections.Generic.List[string]]::new()
        $script:C987Read = ${function:Read-CheckpointText}
        function Read-CheckpointText {
            param([string]$Path)
            $script:C987Reads.Add([IO.Path]::GetFullPath($Path))
            & $script:C987Read $Path
        }
        $created = 'not-called'; $detail = ''; $loaded = $false
        try {
            $created = New-CheckpointFreshOutput $child
            $loaded = $null -ne ('Antiphon.Checkpoints.ExclusiveDirectory' -as [type]) -and $script:C987Reads.Contains($source)
        } catch { $detail = $_.Exception.Message }
        Assert-C487 $loaded 'C987 C987.C987_OwnedOutputHelper.helper.source-load' $detail
        Assert-C487 ($null -eq $created -and [IO.Directory]::Exists($child)) 'C987 C987.C987_OwnedOutputHelper.helper.create' ([string]$created)
        if (!$loaded -or $null -ne $created) { return }
        $marker = Join-Path $child '.checkpoint-output-owner.json'
        $markerBytes = [Convert]::ToHexString([IO.File]::ReadAllBytes($marker))
        Assert-C487 ($markerBytes.Length -gt 0) 'C987 C987.C987_OwnedOutputHelper.helper.marker'
        $sentinel = Join-Path $child 'sentinel'
        [IO.File]::WriteAllBytes($sentinel, [byte[]]@(9,8,7))
        $identity = [C987DirectoryIdentity]::Read($child)
        $collision = New-CheckpointFreshOutput $child
        Assert-C487 ($collision -ceq 'pre-existing-output') 'C987 C987.C987_OwnedOutputHelper.helper.collision' ([string]$collision)
        Assert-C487 ([Convert]::ToHexString([IO.File]::ReadAllBytes($marker)) -ceq $markerBytes) 'C987 C987.C987_OwnedOutputHelper.helper.marker-unchanged'
        Assert-C487 ([Convert]::ToHexString([IO.File]::ReadAllBytes($sentinel)) -ceq '090807') 'C987 C987.C987_OwnedOutputHelper.helper.sentinel'
        Assert-C487 ([C987DirectoryIdentity]::Read($child) -ceq $identity) 'C987 C987.C987_OwnedOutputHelper.helper.identity'
        Assert-C487 (-not [IO.File]::Exists($oldSource)) 'C987 C987.C987_OwnedOutputHelper.helper.obsolete-source-absent'
    } finally { Pop-Location }
}

switch -Exact ($Case) {
    'C987_StandaloneNativeSource' { Test-C987_StandaloneNativeSource }
    'C987_OwnedOutputHelper' { Test-C987_OwnedOutputHelper }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 8
