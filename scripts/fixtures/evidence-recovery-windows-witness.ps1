# String-only Windows deletion witness: no real file, Git object or native process is touched.
$ErrorActionPreference='Stop'
$source=Get-Content -Raw tests/Antiphon.Tests/Scripts/EvidenceDeletionGuardTests.cs
$match=[regex]::Match($source,'var recoveryHook = (.*?);\r?\n', [Text.RegularExpressions.RegexOptions]::Singleline)
if(-not $match.Success){throw 'recovery hook expression missing'}
$code=@'
using System;
using System.IO;
public static class EvidenceGitFixture {
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
public static class WindowsBlob {
    public static bool ReadOnly = true;
    public static string Deleted;
    public static string Normalized;
    public static void SetAttributes(string path, FileAttributes attributes) {
        if (attributes != FileAttributes.Normal) throw new Exception("unexpected attributes");
        ReadOnly = false; Normalized = path;
    }
    public static void Delete(string path) {
        if (ReadOnly) throw new UnauthorizedAccessException("forced Windows read-only object");
        Deleted = path;
    }
}
public static class RecoveryWitness {
    public static string Build(string deletion, string ownedBlob) { return EXPRESSION; }
}
'@
Add-Type $code.Replace('EXPRESSION',$match.Groups[1].Value)
$deletion='0123456789012345678901234567890123456789'
$blob="C:\Users\fixture's home\AppData\Local\Temp\c1015\repo\.git\objects\ab\cdef"
$hook=[RecoveryWitness]::Build($deletion,$blob).Replace('[IO.File]','[WindowsBlob]')
function Invoke-EvidenceNativeGit {param($Repository,$Arguments) return @{ExitCode=0}}
Invoke-Expression $hook
[void](Get-EvidenceGitResult -Repository 'inert' -Arguments @('ls-tree',$deletion))
if([WindowsBlob]::Deleted -cne $blob -or [WindowsBlob]::Normalized -cne $blob){throw 'exact Windows path was not normalized before deletion'}
Write-Host 'Forced Windows read-only deletion and apostrophe/backslash path passed.'
