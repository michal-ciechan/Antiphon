# String-only Windows fixture witnesses. No native host, provider home or daemon is used.
param([ValidateSet('admission','blob','literal-path','cleanup')][string]$Case)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Read-Source([string]$Path) { [IO.File]::ReadAllText((Join-Path $root $Path)) }
function Require([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
switch ($Case) {
    admission {
        foreach ($name in @('RetiredTempContainerHostTests','RollingProductionMountTests')) {
            $source = Read-Source "tests/Antiphon.Tests/Scripts/$name.cs"
            Require ($source -match '\[Before\(Test\)\]\s*public void RequireNativeLinux\(\)\s*=>\s*C1008HostFixture.RequireNativeLinux\(\);') "Windows input reaches Linux-only $name before admission"
        }
        $fixture = Read-Source 'tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs'
        Require ($fixture -match 'internal static void RequireNativeLinux\(\)\s*\{\s*NativeLinuxGate.Require\("CARD-1050: C1008 host contracts require native Linux') 'host admission is disconnected'
        $gate = Read-Source 'tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs'
        $body = [regex]::Match($gate,'internal static void Require\(string reason\)\s*(\{[^}]*\})').Groups[1].Value
        foreach ($platform in @('windows','linux')) {
            $type = 'GroupAdmission' + $platform
            $forced = if ($platform -eq 'linux') { 'true' } else { 'false' }
            Add-Type ('public static class '+$type+' { public static void Require(string reason) '+$body.Replace('OperatingSystem.IsLinux()',$forced).Replace('TUnit.Core.Exceptions.SkipTestException','System.PlatformNotSupportedException')+' }')
            $seen = $null
            try { ($type -as [type])::Require('Linux host contract') } catch { $seen = $_.Exception.InnerException }
            Require (($platform -eq 'windows') -eq ($null -ne $seen)) "incorrect $platform admission"
            if ($seen) { Require ($seen.Message -ceq 'Linux host contract') 'skip reason lost' }
        }
        Write-Host 'WINDOWS_WITNESS admission: both class hooks connected; forced Windows refuses, Linux admits'
    }
    blob {
        $source = Read-Source 'tests/Antiphon.Tests/Scripts/EvidenceDiffGuardTests.cs'
        $start = $source.IndexOf('        var e = (await f.TreeAsync(head)).Single(')
        $end = $source.IndexOf('        (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(2);', $start)
        Require ($start -ge 0 -and $end -gt $start) 'loose object fault cut missing'
        $body = $source.Substring($start,$end-$start)
        $body = $body.Substring($body.IndexOf("`n")+1).Replace('File.', 'WindowsBlob.')
        $code = @'
using System;
using System.IO;
public sealed class Fixture { public string Repo = @"C:\Users\fixture's home\Temp\repo"; }
public static class WindowsBlob {
    public static bool ReadOnly = true;
    public static string Normalized, Deleted;
    public static void SetAttributes(string path, FileAttributes attributes) {
        if (attributes != FileAttributes.Normal) throw new Exception("attributes");
        ReadOnly = false; Normalized = path;
    }
    public static void Delete(string path) {
        if (ReadOnly) throw new UnauthorizedAccessException("forced Windows read-only loose object");
        Deleted = path;
    }
}
public static class BlobWitness {
    public static void Run() {
        var f = new Fixture(); var e = new { Oid = "abcdef0123456789012345678901234567890123" };
        BODY
    }
}
'@
        Add-Type $code.Replace('BODY',$body)
        [BlobWitness]::Run()
        Require ([WindowsBlob]::Deleted -ceq [WindowsBlob]::Normalized -and $null -ne [WindowsBlob]::Deleted) 'exact object must be normalized before deletion'
        Write-Host 'WINDOWS_WITNESS blob: read-only fault cut reaches missing-object assertion'
    }
    literal-path {
        $source = Read-Source 'tests/Antiphon.Tests/Scripts/EvidenceDiffGuardTests.cs'
        $body = [regex]::Match($source,'var canary = .*?;\r?\n\s*var shell = .*?;', 'Singleline').Value
        Require ($body.Length -gt 0) 'literal path construction missing'
        Add-Type ('using System; using System.IO; public sealed class Fixture { public string Root = @"C:\Users\fixture\Temp\c1015"; public string Repo => Root + @"\repo"; } public static class LiteralWitness { public static string[] Run() { var f = new Fixture(); '+$body+' return new[]{canary,shell}; } }')
        $values = [LiteralWitness]::Run()
        Require ($values[0].Contains('C:')) 'Windows input was not forced'
        Require (-not $values[1].Contains(':') -and -not $values[1].Contains('\')) 'Windows drive or backslash leaked into a Git tree path'
        Require ($values[1].Contains('$(New-Item -ItemType File -Path ''path-executed'')')) 'injection canary weakened'
        Require ($values[0] -ceq (([Fixture]::new()).Repo + [IO.Path]::DirectorySeparatorChar + 'path-executed')) 'relative child must target the asserted owned repository canary'
        Write-Host 'WINDOWS_WITNESS literal-path: injection retained without a drive-qualified Git path'
    }
    cleanup {
        $source = Read-Source 'tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs'
        $start = $source.IndexOf('                    if (Directory.Exists(results))')
        $end = $source.IndexOf("`n                }", $start)
        Require ($start -ge 0 -and $end -gt $start) 'owned-path cleanup block missing'
        $body = $source.Substring($start,$end-$start).Replace('Directory.', 'WindowsFs.')
        $helper = Read-Source 'tests/Antiphon.Tests/TestHelpers/StartRefGit.cs'
        $helperStart = $helper.IndexOf('    public static void DeleteDirectory(string path)')
        $helperEnd = $helper.IndexOf('    /// <summary>', $helperStart)
        $helperBody = $helper.Substring($helperStart,$helperEnd-$helperStart).Replace('Directory.', 'WindowsFs.').Replace('File.', 'WindowsFs.')
        $code = @'
using System;
using System.IO;
using System.Collections.Generic;
public static class WindowsFs {
    public const string Root = @"C:\Users\fixture\Temp\results";
    public const string Blob = Root + @"\.git\objects\ab\cdef";
    public static bool ReadOnly = true;
    public static HashSet<string> Entries = new() { Root, Blob, "control" };
    public static bool Exists(string path) => Entries.Contains(path);
    public static FileAttributes GetAttributes(string path) => path == Blob ? (ReadOnly ? FileAttributes.ReadOnly : FileAttributes.Normal) : FileAttributes.Directory;
    public static string[] EnumerateFileSystemEntries(string path) => path == Root ? new[] { Blob } : Array.Empty<string>();
    public static void SetAttributes(string path, FileAttributes attributes) {
        if (path != Blob || attributes != FileAttributes.Normal) throw new Exception("foreign attribute write");
        ReadOnly = false;
    }
    public static void Delete(string path, bool recursive = false) {
        if ((path == Blob || path == Root && recursive) && ReadOnly) throw new UnauthorizedAccessException("forced Windows Git object at cleanup");
        Entries.Remove(path); if (path == Root && recursive) Entries.Remove(Blob);
    }
}
public static class StartRefGit { HELPER }
public static class CleanupWitness {
    public static void Run() { var results = WindowsFs.Root; var control = "control"; BODY }
}
'@
        Add-Type $code.Replace('HELPER',$helperBody).Replace('BODY',$body)
        [CleanupWitness]::Run()
        Require ([WindowsFs]::Entries.Count -eq 0) 'cleanup retained an owned path'
        Require (-not [WindowsFs]::ReadOnly) 'cleanup never normalized the owned Git object'
        Write-Host 'WINDOWS_WITNESS cleanup: actual cleanup and existing deletion helper remove read-only owned objects'
    }
}
