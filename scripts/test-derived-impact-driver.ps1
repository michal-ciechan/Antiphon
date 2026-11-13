#requires -Version 7.0
# CARD-1861: run through the checkpoint tool after building the native-default driver.
param([Parameter(Mandatory)][string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $PSScriptRoot 'regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj'
[xml]$xml = [IO.File]::ReadAllText($project)
# Zero project references closes the transitive graph too. In particular this must not
# compile Antiphon.Tests, its server, checkpoint tool, or staged helper programs.
if ($xml.SelectNodes('//ProjectReference').Count -ne 0) {
    throw 'ASSERT driver build graph must contain no ProjectReference (Antiphon.Tests cold build regression)'
}
Write-Host 'PASS driver has no project-reference graph'

$root = [IO.Path]::GetFullPath((Join-Path $repo $EvidenceRoot))
if ([IO.Directory]::Exists($root)) { throw "witness evidence already exists: $root" }
$tests = Join-Path $root 'tests/Antiphon.Tests'
$source = Join-Path $root 'server/Subject.cs'
[void][IO.Directory]::CreateDirectory($tests)
[void][IO.Directory]::CreateDirectory((Split-Path $source))
[IO.File]::WriteAllText($source, "namespace Example; public sealed class Subject { }`n")
[IO.File]::WriteAllText((Join-Path $tests 'DriverProbeTests.cs'), @'
using Example;
public sealed class DriverProbeTests { [Test] public void Uses_subject() { _ = new Subject(); } }
[LinuxOnly("probe")] public sealed class DriverLinuxTests { [Test] public void T() { } }
[WindowsOnly("probe")] public sealed class DriverWindowsTests { [Test] public void T() { } }
'@)
$indexPath = Join-Path $root 'tests/derived-impact-index.json'
[IO.File]::WriteAllText($indexPath, "{}`n")
& git -C $root init --quiet
if ($LASTEXITCODE -ne 0) { throw 'fixture git init failed' }
& git -C $root add -- server/Subject.cs tests/Antiphon.Tests/DriverProbeTests.cs tests/derived-impact-index.json
if ($LASTEXITCODE -ne 0) { throw 'fixture git add failed' }

$regen = Join-Path $PSScriptRoot 'regenerate-derived-impact-index.ps1'
$first = @(& pwsh -NoProfile -File $regen -Repo $root -NoBuild 2>&1)
if ($LASTEXITCODE -ne 0) { throw "first regeneration failed: $first" }
if (($first -join "`n") -notmatch '(?m)^derived-impact-index changed$') { throw "missing changed verdict: $first" }
$bytes = [IO.File]::ReadAllBytes($indexPath)
$index = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
$edge = @($index.edges | Where-Object path -eq 'server/Subject.cs')
if ($edge.Count -ne 1 -or $edge[0].classes -notcontains 'DriverProbeTests') { throw 'missing real reverse type edge' }
if ($index.linuxOnly -notcontains 'DriverLinuxTests' -or $index.windowsOnly -notcontains 'DriverWindowsTests') {
    throw 'shared taxonomy classification differs'
}
Write-Host 'PASS real regeneration changes stale index with type edges and both platform labels'

$second = @(& pwsh -NoProfile -File $regen -Repo $root -NoBuild 2>&1)
if ($LASTEXITCODE -ne 0) { throw "second regeneration failed: $second" }
if (($second -join "`n") -notmatch '(?m)^derived-impact-index unchanged$') { throw "missing unchanged verdict: $second" }
if ([Convert]::ToBase64String($bytes) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($indexPath))) {
    throw 'second regeneration changed bytes'
}
Write-Host 'PASS second real regeneration is byte-identical'
Write-Host 'DRIVER WITNESS passed=3 failed=0'
