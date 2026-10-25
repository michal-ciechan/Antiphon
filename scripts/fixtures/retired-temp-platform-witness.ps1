$ErrorActionPreference='Stop'
$source=Get-Content -Raw tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs
foreach($name in @('C994_Host_strict_volume_admission_remains_independent','C994_Host_resume_never_runs_container_cleanup','C994_Host_bridge_transport_preserves_literal_context')) {
    $pattern='public async Task '+$name+'\(\)\s*\{\s*C1008HostFixture.RequireNativeLinux\(\);'
    if($source -notmatch $pattern){throw "forced-Windows admission lacks an entry guard for $name"}
}
$fixture=Get-Content -Raw tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs
if($fixture -notmatch 'internal static void RequireNativeLinux\(\)\s*\{\s*NativeLinuxGate.Require\("CARD-1050: C1008 host contracts require native Linux') {throw 'host guard is disconnected'}
$gate=Get-Content -Raw tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs
$body=[regex]::Match($gate,'internal static void Require\(string reason\)\s*(\{[^}]*\})').Groups[1].Value
foreach($platform in @('windows','linux')) {
    $forced=if($platform -eq 'linux'){'true'}else{'false'}
    $type='RetiredAdmission'+$platform
    $code='public static class '+$type+' { public static void Require(string reason) '+$body.Replace('OperatingSystem.IsLinux()',$forced).Replace('TUnit.Core.Exceptions.SkipTestException','System.PlatformNotSupportedException')+' }'
    Add-Type $code
    $seen=$null
    try{($type -as [type])::Require('Linux host fixture')}catch{$seen=$_.Exception.InnerException}
    if(($platform -eq 'windows') -ne ($null -ne $seen)){throw "wrong decision for $platform"}
}
Write-Host 'Three Linux host entries guarded; Windows skips, Linux admits; portable entries retained.'
