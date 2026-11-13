#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/rc-input-manifest.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('rim-'+[guid]::NewGuid().ToString('N')) }
$root=Join-Path $ResultsDirectory 'repository with spaces'
[void][IO.Directory]::CreateDirectory($root)
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Git([string[]]$a) { return Invoke-C487FixtureGit $root $a }
function Put([string]$path,[string]$text) {
    $full=Join-Path $root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full))
    [IO.File]::WriteAllText($full,$text)
}
function Commit {
    [void](Git @('add','--all')); [void](Git @('commit','-qm','fixture'))
    return Git @('rev-parse','HEAD')
}
function Select-Manifest([string]$a, $index=$null) { Get-RcInputManifest $root $baseline $a $index }
function Suite($s,[string]$id,[int]$snapshot=1) { @($s.snapshots[$snapshot].suites | Where-Object id -CEQ $id)[0] }
function Has($s,[string]$id,[string]$path) {
    if (@($s.snapshots).Count -ne 2) { return $false }
    return @((Suite $s $id).inputs | Where-Object { $_.path -ceq $path -and $_.present -and $_.objectId -cmatch '^[a-f0-9]{40}$' -and $_.reason }).Count -eq 1
}
[void](Git @('init','-q','-b','main'))
[void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
$owners=@{schemaVersion=1; suites=@(
    @{id='server';project='tests/Server/Server.csproj'}, @{id='runner';project='tests/Runner/Runner.csproj'},
    @{id='other';project='tests/Other/Other.csproj'}
); roots=@(
    @{path='tests/Shared/**';consumers=@('server','runner');reason='shared-helper'},
    @{path='scripts/**';consumers=@('server','runner');reason='harness'},
    @{path='tools/**';consumers=@('server','runner');reason='compiled-helper'},
    @{path='build/**';consumers=@('*');reason='build-import'},
    @{path='docs/**';consumers=@('server');reason='scanner-docs'},
    @{path='Dockerfile';consumers=@('server');reason='container'},
    @{path='client/**';consumers=@('server');reason='browser-client'},
    @{path='*.json';consumers=@('*');reason='root-config'},
    @{path='*.props';consumers=@('*');reason='build-config'},
    @{path='NuGet.config';consumers=@('*');reason='packages'},
    @{path='scripts/lib/rc-input-owners.json';consumers=@('*');reason='ownership-contract'}
)}
Put 'scripts/lib/rc-input-owners.json' ($owners | ConvertTo-Json -Depth 10)
Put 'src/Core/Core.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'src/Core/Queue.cs' 'old core'
Put 'server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk"><Import Project="../build/common.props" /><ItemGroup><ProjectReference Include="../src/Core/Core.csproj" /></ItemGroup></Project>'
Put 'server/Application/SessionMessageQueueService.cs' 'old product'
Put 'build/common.props' '<Project><ItemGroup><Compile Include="../linked/Shared.cs" /></ItemGroup></Project>'
Put 'linked/Shared.cs' 'linked source'
Put 'build/wildcard-only.txt' 'owned only by the wildcard rule'
Put 'tests/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../server/Server.csproj" /></ItemGroup></Project>'
Put 'tests/Server/SessionMessageQueueServiceTests.cs' 'unchanged test'
Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Core/Core.csproj" Condition="false" /></ItemGroup></Project>'
Put 'tests/Other/Other.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'tests/Other/Other.cs' 'unrelated'
Put 'tests/Shared/Helper.cs' 'old helper'
Put 'tests/Server/appsettings.test.json' '{}'
Put 'tests/Server/Fixtures/input.txt' 'fixture'
Put 'scripts/fixture.ps1' 'fixture script'
Put 'tools/Helper/Helper.csproj' '<Project />'
Put 'Directory.Packages.props' '<Project />'
Put 'NuGet.config' '<configuration />'
Put 'global.json' '{}'
Put 'client/package-lock.json' '{}'
Put 'Dockerfile' 'FROM pinned@sha256:fixture'
Put 'docs/contract.md' 'consumed docs'
$baseline=Commit
switch ($Case) {
    'ProductAndHelperFanout' {
        foreach ($path in @('server/Application/SessionMessageQueueService.cs','src/Core/Queue.cs','tests/Shared/Helper.cs','linked/Shared.cs')) {
            # Compare only this edit: earlier product edits must not dirty a missing helper owner.
            $baseline=Git @('rev-parse','HEAD')
            Put $path ('changed '+$path); $a=Commit; $s=Select-Manifest $a
            Note ($s.changes.Count -eq 1 -and $s.changes[0].path -ceq $path) "isolated-fanout-change:$path"
            Note ($s.dirtySuites -ccontains 'server') "product-or-helper-dirty:$path"
            Note (Has $s 'server' $path) "product-or-helper-inventory:$path"
            if ($path -ceq 'src/Core/Queue.cs' -or $path -ceq 'tests/Shared/Helper.cs') {
                Note ($s.dirtySuites -ccontains 'runner') "fanout:$path"
                Note (Has $s 'runner' $path) "fanout-inventory:$path"
            }
            Note ($s.unresolved.Count -eq 0) "fanout-no-fallback:$path"
            Note ($s.dirtySuites -cnotcontains 'other') "proven-unrelated:$path"
        }
        Note ((Git @('rev-parse',($baseline+':tests/Server/SessionMessageQueueServiceTests.cs'))) -ceq (Git @('rev-parse',($a+':tests/Server/SessionMessageQueueServiceTests.cs')))) 'unchanged-test-shape'
        Note (@($s.snapshots).Count -eq 2) 'both-committed-inventories'
        if (@($s.snapshots).Count -eq 2) {
            Note (@((Suite $s 'server').edges | Where-Object { $_.reason -ceq 'ProjectReference' -and $_.to -ceq 'src/Core/Core.csproj' }).Count -gt 0) 'transitive-edge-reason'
        }

        # No project/import reaches this input. Only build/** -> * can fan it out.
        $baseline=Git @('rev-parse','HEAD')
        Put 'build/wildcard-only.txt' 'changed wildcard input'; $s=Select-Manifest (Commit)
        Note ($s.changes.Count -eq 1 -and $s.changes[0].path -ceq 'build/wildcard-only.txt') 'wildcard-isolated-change'
        Note ($s.unresolved.Count -eq 0) 'wildcard-no-fallback'
        foreach ($id in @('server','runner','other')) {
            Note ($s.dirtySuites -ccontains $id) "wildcard-dirty:$id"
            Note (Has $s $id 'build/wildcard-only.txt') "wildcard-inventory:$id"
            foreach ($snapshot in $s.snapshots) {
                $suite=@($snapshot.suites | Where-Object id -CEQ $id)[0]
                Note (@($suite.edges | Where-Object to -CEQ 'build/wildcard-only.txt').Count -eq 0) "wildcard-not-project-reached:$id"
            }
        }

        # Exercise shipped map bytes, not a lookalike rule. Known fixture closure
        # prevents dirty-all fallback from hiding missing owners in either snapshot.
        $sourceRoot=Split-Path -Parent $PSScriptRoot
        $sourceSha=(Invoke-TestSelectionGit $sourceRoot @('rev-parse','HEAD')).Stdout.Trim()
        $mapText=Get-TestSelectionBlob $sourceRoot $sourceSha 'scripts/lib/rc-input-owners.json'
        $committedMap=ConvertFrom-Json -InputObject $mapText -AsHashtable
        Put 'scripts/lib/rc-input-owners.json' $mapText
        $baseline=Commit
        Put 'docs/contract.md' 'committed-map fanout'; $s=Select-Manifest (Commit)
        $expectedSuites=@('tests/Other/Other.csproj','tests/Runner/Runner.csproj','tests/Server/Server.csproj')
        Note ($s.unresolved.Count -eq 0) 'committed-map-no-fallback'
        Note (($s.dirtySuites -join ';') -ceq ($expectedSuites -join ';')) 'committed-map-all-projects-dirty'
        $tracked=@((Git @('ls-tree','-r','--name-only',$s.candidateSha)).Split([char]10))
        $covered=0
        foreach ($rule in $committedMap.roots) {
            if ($rule.consumers -cnotcontains '*') { continue }
            $pattern=New-RcInputPattern $rule.path
            foreach ($path in $tracked) {
                if (-not $pattern.IsMatch($path)) { continue }
                $covered++
                foreach ($snapshot in $s.snapshots) {
                    Note (($snapshot.suites.id -join ';') -ceq ($expectedSuites -join ';')) 'committed-map-suite-census'
                    foreach ($suite in $snapshot.suites) {
                        $record=@($suite.inputs | Where-Object path -CEQ $path)
                        Note ($record.Count -eq 1 -and $record[0].present -and
                            $record[0].reason.Split(';') -ccontains $rule.reason) "committed-map-owned:$($snapshot.sha):$($suite.id):$path"
                    }
                }
            }
        }
        Note ($covered -gt 0) 'committed-map-wildcard-coverage-nonempty'
    }
    'DynamicAndUnownedAreDirty' {
        Put 'unowned/new.txt' 'unknown'; $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 3 -and $s.unresolved -ccontains 'unknown-path:unowned/new.txt') 'unknown-path-all-dirty'
        Put 'server/Server.csproj' '<Project><Import Project="$(UnknownRoot)/dynamic.props" /></Project>'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 3 -and @($s.unresolved | Where-Object { $_ -like '*unresolved-import*' }).Count -gt 0) 'dynamic-import-all-dirty'
        Note (Has $s 'other' 'src/Core/Queue.cs') 'unresolved-inventory-broadens-to-all'
        # Old graph dependencies cannot disappear when an edge is removed in the candidate.
        Put 'tests/Runner/Runner.csproj' '<Project />'; Put 'src/Core/Queue.cs' 'old edge consumer'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains 'runner') 'old-graph-retained'
    }
    'ConfigFixtureScriptAndLockInputs' {
        $families=@('tests/Server/appsettings.test.json','tests/Server/Fixtures/input.txt','scripts/fixture.ps1',
            'tools/Helper/Helper.csproj','Directory.Packages.props','NuGet.config','global.json',
            'client/package-lock.json','Dockerfile','docs/contract.md')
        foreach ($path in $families) {
            $content=if ($path.EndsWith('.props',[StringComparison]::Ordinal)) { '<Project><PropertyGroup><FixtureValue>changed</FixtureValue></PropertyGroup></Project>' }
                elseif ($path.EndsWith('.csproj',[StringComparison]::Ordinal)) { '<Project Sdk="Microsoft.NET.Sdk" />' }
                else { 'changed '+$path }
            Put $path $content; $s=Select-Manifest (Commit)
            Note (Has $s 'server' $path) "family-inventory:$path"
            Note ($s.dirtySuites -ccontains 'server') "family-dirty:$path"
            Note ($s.unresolved.Count -eq 0) "family-closure-is-known:$path"
        }
        Put 'tests/Server/Fixtures/added.json' '{}'; $s=Select-Manifest (Commit)
        Note (Has $s 'server' 'tests/Server/Fixtures/added.json') 'new-input-not-old-glob-only'
        [IO.File]::Delete((Join-Path $root 'tests/Server/Fixtures/input.txt')); $s=Select-Manifest (Commit)
        Note (@($s.changes | Where-Object { $_.path -ceq 'tests/Server/Fixtures/input.txt' -and $_.status -ceq 'D' }).Count -eq 1) 'deleted-input-retained'
        if (@($s.snapshots).Count -eq 2) {
            Note (@((Suite $s 'server').inputs | Where-Object { $_.path -ceq 'tests/Server/Fixtures/input.txt' -and -not $_.present }).Count -eq 1) 'absence-record'
        }
        # Dirty working bytes must not stand in for either Git commit.
        Put 'src/Core/Queue.cs' 'uncommitted replacement'
        Note ((ConvertTo-TestSelectionCanonicalJson $s) -ceq (ConvertTo-TestSelectionCanonicalJson (Select-Manifest $s.candidateSha))) 'historical-object-only'
    }
    'PartialIndexNeverSubtracts' {
        Put 'src/Core/Queue.cs' 'index non-hit'; $a=Commit
        $plain=Select-Manifest $a
        Note ($plain.unresolved.Count -eq 0 -and $plain.dirtySuites -cnotcontains 'other') 'known-conservative-baseline'
        foreach ($index in @($null,@{version=1;edges=@();capped=@('src/Core/Queue.cs')},@{version=0;edges=@()},@{version=1;edges=@(@{path='src/Core/Queue.cs';suites=@('other')})})) {
            $s=Select-Manifest $a $index
            Note ($s.dirtySuites -ccontains 'server' -and $s.dirtySuites -ccontains 'runner') 'partial-index-keeps-conservative-consumers'
            Note (Has $s 'server' 'src/Core/Queue.cs') 'partial-index-keeps-inputs'
            foreach ($id in $plain.dirtySuites) { Note ($s.dirtySuites -ccontains $id) "index-cannot-subtract:$id" }
        }
        Note ($s.dirtySuites -ccontains 'other') 'positive-index-broadens'
        Note (Has $s 'other' 'src/Core/Queue.cs') 'positive-index-unions-input-record'
        Note (-not $s.executionEvidence -and -not $s.gateCredit -and $s.activation -ceq 'shadow-only') 'no-execution-authority'
    }
    default { throw "unknown case:$Case" }
}
Assert-C487 ($errors.Count -eq 0) ("RIM $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
