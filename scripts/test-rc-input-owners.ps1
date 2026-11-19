#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-input-manifest.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('rio-'+[guid]::NewGuid().ToString('N')) }
$root=Join-Path $ResultsDirectory 'repository with spaces'
[void][IO.Directory]::CreateDirectory($root)
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Git([string[]]$a) { return Invoke-C487FixtureGit $root $a }
function Put([string]$path,[string]$text) {
    $full=Join-Path $root ($path -replace '/',[IO.Path]::DirectorySeparatorChar)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full))
    [IO.File]::WriteAllText($full,$text)
}
function Commit {
    [void](Git @('add','--all')); [void](Git @('commit','-qm','fixture'))
    return Git @('rev-parse','HEAD')
}
function Select-Manifest([string]$a) { Get-RcInputManifest $root $baseline $a $null }
function SameSet($s,[string[]]$ids) {
    if ($null -eq $s -or @($s.dirtySuites).Count -eq 0 -and $ids.Count -gt 0) { return $false }
    $actual=@(Get-TestSelectionSorted @($s.dirtySuites)) -join ';'
    $expect=@(Get-TestSelectionSorted @($ids)) -join ';'
    return $actual -ceq $expect
}
function Owns($s,[string]$id,[string]$path,[string]$reason) {
    if (@($s.snapshots).Count -ne 2) { return $false }
    $suite=@($s.snapshots[1].suites | Where-Object id -CEQ $id)
    if ($suite.Count -ne 1) { return $false }
    $record=@($suite[0].inputs | Where-Object { $_.path -ceq $path -and $_.present })
    return $record.Count -eq 1 -and @($record[0].reason.Split(';')) -ccontains $reason
}
function Lacks($s,[string]$id,[string]$path) {
    if (@($s.snapshots).Count -ne 2) { return $false }
    $suite=@($s.snapshots[1].suites | Where-Object id -CEQ $id)
    if ($suite.Count -ne 1) { return $false }
    return @($suite[0].inputs | Where-Object { $_.path -ceq $path -and $_.present }).Count -eq 0
}
[void](Git @('init','-q','-b','main'))
[void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
$sourceRoot=Split-Path -Parent $PSScriptRoot
$sourceSha=(Invoke-TestSelectionGit $sourceRoot @('rev-parse','HEAD')).Stdout.Trim()
$mapText=Get-TestSelectionBlob $sourceRoot $sourceSha 'scripts/lib/rc-input-owners.json'
$map=ConvertFrom-Json -InputObject $mapText -AsHashtable
Put 'scripts/lib/rc-input-owners.json' $mapText
$antiphon='tests/Antiphon.Tests/Antiphon.Tests.csproj'
$e2e='tests/Antiphon.E2E/Antiphon.E2E.csproj'
$resilience='tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj'
$messaging='tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj'
$created=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($rule in @($map.roots)) {
    foreach ($id in @($rule.consumers)) {
        if ($id -cne '*' -and $id.EndsWith('.csproj',[StringComparison]::Ordinal) -and $created.Add($id)) {
            Put $id '<Project Sdk="Microsoft.NET.Sdk" />'
        }
    }
}
Put $antiphon '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../server/Server.csproj" /></ItemGroup></Project>'
Put $e2e '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../server/Server.csproj" /></ItemGroup></Project>'
Put $resilience '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="../Antiphon.Tests/TestHelpers/Linked.cs" /></ItemGroup></Project>'
Put $messaging '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'server/App.cs' 'server source'
Put 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'linked helper'
Put 'tests/Antiphon.Tests/TestHelpers/Sibling.cs' 'unlinked sibling'
Put 'tests/Antiphon.Messaging.Tests/Own.cs' 'messaging test'
Put 'docs/contract.md' 'docs'
Put 'Directory.Build.props' '<Project />'
Put 'global.json' '{}'
Put 'NuGet.config' '<configuration />'
$baseline=Commit
$shown={ param($s) if ($null -eq $s) { return 'no-manifest' }; return (@($s.dirtySuites) -join ',') }
switch ($Case) {
    'UnknownPathDirtiesAll' {
        Put 'stray/outside.txt' 'unknown'; $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note ($s.unresolved.Count -eq 0) "unknown-not-broadened:$(@($s.unresolved) -join ',')"
        Note (SameSet $s $ids) "unknown-dirties-all:$(& $shown $s)"
        Note (Owns $s $messaging 'stray/outside.txt' 'unmatched-path-fallback') 'unknown-fallback-reason'
        Note (Lacks $s $messaging 'server/App.cs') 'unknown-does-not-blanket-closure'
    }
    'ServerChangeDirtiesAntiphonAndE2E' {
        Put 'server/App.cs' 'changed server'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "server-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($antiphon,$e2e)) "server-readers-only:$(& $shown $s)"
        Note (Owns $s $antiphon 'server/App.cs' 'project-tree:server/Server.csproj') 'server-in-antiphon-closure'
        Note (Owns $s $e2e 'server/App.cs' 'project-tree:server/Server.csproj') 'server-in-e2e-closure'
        Note (Lacks $s $messaging 'server/App.cs') 'server-misses-messaging'
    }
    'DocsChangeDirtiesRepoReaders' {
        Put 'docs/contract.md' 'changed docs'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "docs-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($antiphon,$e2e)) "docs-readers-only:$(& $shown $s)"
        Note (Owns $s $antiphon 'docs/contract.md' 'repository-reader-non-code') 'docs-antiphon-reason'
        Note (Owns $s $e2e 'docs/contract.md' 'repository-reader-non-code') 'docs-e2e-reason'
        Note (Lacks $s $messaging 'docs/contract.md') 'docs-misses-messaging'
        Note (Lacks $s $resilience 'docs/contract.md') 'docs-misses-resilience'
    }
    'BuildGraphChangeForcesFull' {
        Put 'Directory.Build.props' '<Project><PropertyGroup><Fixture>changed</Fixture></PropertyGroup></Project>'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note ($s.unresolved.Count -eq 0) "build-graph-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s $ids) "build-graph-dirties-all:$(& $shown $s)"
        Note (Owns $s $messaging 'Directory.Build.props' 'forced-full-build-graph') 'build-graph-forced-reason'
    }
    'LinkedSourceSelectsIncludingProject' {
        Put 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'changed linked helper'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "link-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($antiphon,$resilience)) "link-selects-including:$(& $shown $s)"
        Note (Owns $s $resilience 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'Compile') 'link-compile-edge'
        Note (Lacks $s $messaging 'tests/Antiphon.Tests/TestHelpers/Linked.cs') 'link-misses-messaging'
    }
    'UnrelatedFileSelectsNothing' {
        Put 'tests/Antiphon.Tests/TestHelpers/Sibling.cs' 'changed unlinked sibling'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "sibling-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($antiphon)) "sibling-selects-owner-only:$(& $shown $s)"
        Note (Lacks $s $resilience 'tests/Antiphon.Tests/TestHelpers/Sibling.cs') 'sibling-not-a-compile-link'
        $baseline=Git @('rev-parse','HEAD')
        Put 'tests/Antiphon.Messaging.Tests/Own.cs' 'changed unrelated'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "unrelated-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($messaging)) "unrelated-selects-nothing-else:$(& $shown $s)"
        Note (Lacks $s $resilience 'tests/Antiphon.Messaging.Tests/Own.cs') 'unrelated-misses-including-project'
        Note (Lacks $s $antiphon 'tests/Antiphon.Messaging.Tests/Own.cs') 'unrelated-misses-antiphon'
        Note (Lacks $s $e2e 'tests/Antiphon.Messaging.Tests/Own.cs') 'unrelated-misses-e2e'
    }
    default { throw "unknown case:$Case" }
}
Assert-C487 ($errors.Count -eq 0) ("RIO $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
