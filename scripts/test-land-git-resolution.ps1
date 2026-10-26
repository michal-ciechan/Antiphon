param([string]$Repository = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
$file = Join-Path $Repository 'tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs'
$source = [IO.File]::ReadAllText($file)
$start = $source.IndexOf('    private static string WhichGit()', [StringComparison]::Ordinal)
$end = $source.IndexOf('    private static string RenderAuditArgument', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'WhichGit source boundary missing' }
$method = $source.Substring($start, $end - $start).
    Replace('private static string WhichGit()', 'public static string WhichGit()').
    Replace('Environment.GetEnvironmentVariable("PATH")', 'FixturePath').
    Replace('Path.PathSeparator', 'FixtureSeparator').
    Replace('OperatingSystem.IsWindows()', 'Windows').
    Replace('File.Exists(candidate)', 'string.Equals(candidate, Expected, StringComparison.Ordinal)')
Add-Type -TypeDefinition (@'
using System;
using System.IO;
public static class LandGitResolutionWitness {
    public static string FixturePath = "";
    public static char FixtureSeparator;
    public static bool Windows;
    public static string Expected = "";
'@ + $method + '}')
$failed = 0
foreach ($windows in @($false, $true)) {
    [LandGitResolutionWitness]::Windows = $windows
    [LandGitResolutionWitness]::FixtureSeparator = ';'
    [LandGitResolutionWitness]::FixturePath = 'Q:\absent;C:\Program Files\Git\cmd;R:\other'
    [LandGitResolutionWitness]::Expected = [IO.Path]::Combine('C:\Program Files\Git\cmd', $(if ($windows) { 'git.exe' } else { 'git' }))
    try {
        $actual = [LandGitResolutionWitness]::WhichGit()
        if ($actual -cne [LandGitResolutionWitness]::Expected) { throw 'Wrong executable selected' }
        Write-Host "PASS windows=$windows"
    } catch {
        $failed++
        Write-Host "FAIL windows=$windows $($_.Exception.GetBaseException().Message)"
    }
}
Write-Host "LAND_GIT_RESOLUTION executed=2 passed=$(2 - $failed) failed=$failed"
if ($failed) { exit 1 }
