$ErrorActionPreference='Stop'
$source=Get-Content -Raw tests/Antiphon.Tests/Scripts/ScriptHarnessLinuxOwnershipTests.cs
$match=[regex]::Match($source,'internal static void RequirePlatform\(string platform\)\s*(\{[^}]*\})')
$body=if($match.Success){$match.Groups[1].Value}else{'{}'}
$body=$body.Replace('TUnit.Core.Exceptions.SkipTestException','System.PlatformNotSupportedException')
Add-Type ('public static class AdmissionWitness { public static void RequirePlatform(string platform) '+$body+' }')
[AdmissionWitness]::RequirePlatform('linux')
foreach($platform in @('windows','osx')) {
    $errorSeen=$null
    try{[AdmissionWitness]::RequirePlatform($platform)}catch{$errorSeen=$_.Exception.InnerException}
    if($null -eq $errorSeen -or $errorSeen.Message -ne 'Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals).'){throw "forced-$platform input was admitted without the Linux-only reason"}
}
if($source -notmatch '\[Before\(Test\)\]\s*public void RequireNativeLinux\(\) =>\s*RequirePlatform\(OperatingSystem.IsLinux\(\) \? "linux" : "other"\);'){throw 'guard must run before every test'}
Write-Host '3 platform inputs passed; Before(Test) admission connected.'
