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
function Ensure-RioProject([string]$id,[string]$text) {
    if ($created.Contains($id)) { return }
    [void]$created.Add($id)
    Put $id $text
}
function Test-RioPresentOwned($suite,[string]$path) {
    if ($null -eq $suite) { return $false }
    $record=@($suite.inputs | Where-Object { $_.path -ceq $path -and $_.present })
    if ($record.Count -ne 1) { return $false }
    $reasons=@($record[0].reason.Split(';'))
    return ($reasons -cnotcontains 'unmatched-path-fallback') -and ($reasons -cnotcontains 'unresolved-conservative-all-inputs')
}
function UnownedRows($s,[string]$path) {
    if ($null -eq $s -or $null -eq $s.unowned) { return @() }
    return @(@($s.unowned) | Where-Object { $null -ne $_ -and [string]$_.path -ceq $path })
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
$gitinprocess='tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj'
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
    'UnknownPathSelectsNothing' {
        # Replaces UnknownPathDirtiesAll. That case required unmatched-path-fallback
        # to select every suite. An unknown path now selects nothing and is flagged.
        Put 'stray/outside.txt' 'unknown'; $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (-not (SameSet $s $ids)) "unknown-does-not-select-all:$(& $shown $s)"
        Note ($s.dirtySuites.Count -eq 0) "unknown-selects-nothing:$(& $shown $s)"
        Note (Lacks $s $messaging 'stray/outside.txt') 'unknown-not-an-input'
        Note (Lacks $s $antiphon 'stray/outside.txt') 'unknown-not-antiphon'
        $rows=UnownedRows $s 'stray/outside.txt'
        Note ($rows.Count -eq 1 -and [string]$rows[0].hint -ceq 'unknown') "unknown-hint:$($rows.hint)"
        Note ($s.needsOrchestratorDecision -eq $true) 'unknown-needs-decision'
        Note ([string]$s.creditBlockedBy -ceq 'unowned-paths') "unknown-credit:$($s.creditBlockedBy)"
        Note (-not $s.gateCredit -and $s.activation -ceq 'shadow-only' -and -not $s.executionEvidence) 'unknown-stays-shadow'
    }
    'NewCsprojNobodyReferencesIsFlagged' {
        $path='src/BrandNew.Thing/BrandNew.Thing.csproj'
        Put $path '<Project Sdk="Microsoft.NET.Sdk" />'
        $s=Select-Manifest (Commit)
        Note (Lacks $s $messaging $path) 'new-csproj-not-selected'
        Note (Lacks $s $antiphon $path) 'new-csproj-not-antiphon'
        Note ($s.dirtySuites.Count -eq 0) "new-csproj-selects-nothing:$(& $shown $s)"
        $rows=UnownedRows $s $path
        Note ($rows.Count -eq 1 -and [string]$rows[0].hint -ceq 'new-csproj') "new-csproj-hint:$($rows.hint)"
        Note ($s.needsOrchestratorDecision -eq $true) 'new-csproj-needs-decision'
    }
    'NoOpPathSelectsNothingAndIsSilent' {
        $noop=@{schemaVersion=1;rows=@(@{path='docs/reviews/**';reason='no test reads review notes';evidence='fixture'})}
        Put 'scripts/lib/rc-input-noop.json' ($noop | ConvertTo-Json -Depth 6)
        Put 'docs/reviews/note.md' 'review note'
        $baseline=Commit
        Put 'docs/reviews/note.md' 'changed review note'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "noop-selects-nothing:$(& $shown $s)"
        Note (Lacks $s $antiphon 'docs/reviews/note.md') 'noop-not-an-input'
        Note ((UnownedRows $s 'docs/reviews/note.md').Count -eq 0) 'noop-not-flagged'
        Note ($s.needsOrchestratorDecision -ne $true) 'noop-stays-silent'
        Note ([string]::IsNullOrEmpty([string]$s.creditBlockedBy)) "noop-credit:$($s.creditBlockedBy)"
    }
    'DirectoryWalkReadersSelectSuites' {
        $serverGit='tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj'
        Ensure-RioProject $serverGit '<Project Sdk="Microsoft.NET.Sdk" />'
        $sample='tests/Antiphon.Tests/Application/StaleWorkingTranscriptWatchdogTests.cs'
        $migration='src/Antiphon.Messaging.Service/Migrations/20990101000000_Example.cs'
        Put $sample 'class StaleWorkingTranscriptWatchdogTests {}'
        Put $migration 'migration'
        $baseline=Commit
        Put $sample 'class StaleWorkingTranscriptWatchdogTests { void Renamed() {} }'
        $s=Select-Manifest (Commit)
        Note (Owns $s $resilience $sample 'antiphon-tests-source-scan') 'd1a-resilience'
        Note (Owns $s $serverGit $sample 'antiphon-tests-source-scan') 'd1a-server-git'
        Note (Owns $s $gitinprocess $sample 'antiphon-tests-source-scan') 'd1a-gitinprocess'
        Note ($s.dirtySuites -ccontains $resilience -and $s.dirtySuites -ccontains $serverGit) "d1a-dirty:$(& $shown $s)"
        $baseline=Git @('rev-parse','HEAD')
        Put $migration 'migration changed'
        $s=Select-Manifest (Commit)
        Note (Owns $s $antiphon $migration 'messaging-migration-walk') 'd1b-antiphon'
        Note (Lacks $s $resilience $migration) 'd1b-not-resilience'
        Note ($s.dirtySuites -ccontains $antiphon) "d1b-dirty:$(& $shown $s)"
    }
    'RepoWalkingProjectSelectedForAnyPath' {
        $mapCopy=ConvertFrom-Json -InputObject (Get-TestSelectionBlob $sourceRoot $sourceSha 'scripts/lib/rc-input-owners.json') -AsHashtable
        $roots=New-Object System.Collections.Generic.List[object]
        foreach ($row in @($mapCopy.roots)) {
            if ([string]$row.path -ceq '**') { continue }
            $roots.Add($row)
        }
        $roots.Add(@{path='**';consumers=@($messaging);reason='repo-walk'})
        $mapCopy.roots=@($roots.ToArray())
        Put 'scripts/lib/rc-input-owners.json' ($mapCopy | ConvertTo-Json -Depth 8)
        $baseline=Commit
        Put 'server/App.cs' 'repo walk sees an owned product file'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains $messaging) "repo-walk-any-path:$(& $shown $s)"
        Note (Owns $s $messaging 'server/App.cs' 'repo-walk') 'repo-walk-reason'
        Note ($s.dirtySuites -ccontains $antiphon) 'repo-walk-keeps-project-owner'
    }
    'UnownedPathBlocksCarryCredit' {
        Put 'brand-new-area/file.txt' 'unowned area'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "credit-selects-nothing:$(& $shown $s)"
        Note ([string]$s.creditBlockedBy -ceq 'unowned-paths') "credit-blocked:$($s.creditBlockedBy)"
        Note (-not $s.gateCredit) 'credit-gate-stays-false'
        Note ($s.needsOrchestratorDecision -eq $true) 'credit-needs-decision'
        $rows=UnownedRows $s 'brand-new-area/file.txt'
        Note ($rows.Count -eq 1 -and [string]$rows[0].hint -ceq 'unknown') "credit-hint:$($rows.hint)"
    }
    'NamedDocSelectsReadingSuite' {
        $path='docs/testing-and-build.md'
        Put $path 'doc contract'
        $baseline=Commit
        Put $path 'doc contract changed'
        $s=Select-Manifest (Commit)
        Note (Owns $s $antiphon $path 'doc-contract-reader') 'doc-reader-reason'
        Note ($s.dirtySuites -ccontains $antiphon) "doc-reader-dirty:$(& $shown $s)"
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
        $ids=@($s.snapshots[1].suites.id)
        Note ($s.unresolved.Count -eq 0) "docs-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s $ids) "docs-every-suite:$(& $shown $s)"
        Note (Owns $s $antiphon 'docs/contract.md' 'repository-reader-non-code') 'docs-antiphon-reason'
        Note (Owns $s $e2e 'docs/contract.md' 'repository-reader-non-code') 'docs-e2e-reason'
        Note (Owns $s $messaging 'docs/contract.md' 'repository-reader-non-code') 'docs-messaging-reason'
        Note (Owns $s $resilience 'docs/contract.md' 'repository-reader-non-code') 'docs-resilience-reason'
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
        Note (SameSet $s @($antiphon,$resilience,$gitinprocess)) "link-selects-including:$(& $shown $s)"
        Note (Owns $s $resilience 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'Compile') 'link-compile-edge'
        Note (Lacks $s $messaging 'tests/Antiphon.Tests/TestHelpers/Linked.cs') 'link-misses-messaging'
    }
    'UnrelatedFileSelectsNothing' {
        Put 'tests/Antiphon.Tests/TestHelpers/Sibling.cs' 'changed unlinked sibling'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "sibling-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($antiphon,$gitinprocess)) "sibling-selects-owner-only:$(& $shown $s)"
        Note (Lacks $s $resilience 'tests/Antiphon.Tests/TestHelpers/Sibling.cs') 'sibling-not-a-compile-link'
        $baseline=Git @('rev-parse','HEAD')
        Put 'tests/Antiphon.Messaging.Tests/Own.cs' 'changed unrelated'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "unrelated-resolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s @($messaging,$antiphon)) "unrelated-selects-nothing-else:$(& $shown $s)"
        Note (Lacks $s $resilience 'tests/Antiphon.Messaging.Tests/Own.cs') 'unrelated-misses-including-project'
        Note (Owns $s $antiphon 'tests/Antiphon.Messaging.Tests/Own.cs' 'derived-impact-scan') 'unrelated-derived-impact-scan'
        Note (Lacks $s $e2e 'tests/Antiphon.Messaging.Tests/Own.cs') 'unrelated-misses-e2e'
    }
    'RuntimeReaderCensus' {
        # AppHost is claimed by Observability's project closure so the all-suite
        # fallback cannot pretend Antiphon.Tests owns Program.cs.
        $observability='tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj'
        $table=ConvertFrom-Json -InputObject (Get-TestSelectionBlob $sourceRoot $sourceSha 'scripts/lib/rc-input-reader-census.json') -AsHashtable
        Note ($table.schemaVersion -eq 1) 'census-schema'
        $rows=@($table.rows)
        Note ($rows.Count -ge 14) "census-groups:$($rows.Count)"
        Ensure-RioProject $observability '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../Antiphon.AppHost/Antiphon.AppHost.csproj" /></ItemGroup></Project>'
        Put 'Antiphon.AppHost/Antiphon.AppHost.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
        Put 'Antiphon.AppHost/Program.cs' 'seed program'
        foreach ($row in $rows) {
            Note (-not [string]::IsNullOrWhiteSpace([string]$row.group)) 'census-group-name'
            foreach ($id in @($row.suites)) {
                Ensure-RioProject $id '<Project Sdk="Microsoft.NET.Sdk" />'
            }
            foreach ($path in @($row.paths)) {
                # A csproj seed must stay well-formed or the project walk marks the whole census unresolved.
                if ($path.EndsWith('.csproj',[StringComparison]::Ordinal)) { Ensure-RioProject $path '<Project Sdk="Microsoft.NET.Sdk" />' }
                else { Put $path 'seed' }
            }
        }
        $inv=Read-RcInputInventory $root (Commit) $null -SkipUnresolvedBroadening
        Note (@($inv.unresolved).Count -eq 0) "census-unresolved:$(@($inv.unresolved) -join ',')"
        foreach ($row in $rows) {
            foreach ($path in @($row.paths)) {
                foreach ($id in @($row.suites)) {
                    $suite=@($inv.suites | Where-Object id -CEQ $id)
                    $owned=$suite.Count -eq 1 -and (Test-RioPresentOwned $suite[0] $path)
                    Note $owned "census:$($row.group):${id}:$path"
                }
            }
        }
    }
    'ScriptChangeSelectsSessionRunner' {
        $runner='tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj'
        Ensure-RioProject $runner '<Project Sdk="Microsoft.NET.Sdk" />'
        Put 'scripts/build-slot.ps1' 'seed slot'
        $baseline=Commit
        Put 'scripts/build-slot.ps1' 'changed slot'
        $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "slot-unresolved:$(@($s.unresolved) -join ',')"
        Note ($s.dirtySuites -ccontains $runner) "slot-selects-session-runner:$(& $shown $s)"
        Note (Owns $s $runner 'scripts/build-slot.ps1' 'repository-reader-non-code') 'slot-runner-reason'
        Note ($s.dirtySuites -ccontains $antiphon) 'slot-still-selects-antiphon'
    }
    'DockerAndComposeSelectRuntimeSuites' {
        $runner='tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj'
        $pty='tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj'
        Ensure-RioProject $runner '<Project Sdk="Microsoft.NET.Sdk" />'
        Ensure-RioProject $pty '<Project Sdk="Microsoft.NET.Sdk" />'
        Put 'docker/session-runner-grok/antiphon-custody-enter.sh' 'seed custody'
        Put 'docker-compose.runner-grok.yml' 'seed compose'
        Put 'Dockerfile' 'seed docker'
        $baseline=Commit
        Put 'docker/session-runner-grok/antiphon-custody-enter.sh' 'changed custody'
        $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "docker-unresolved:$(@($s.unresolved) -join ',')"
        Note ($s.dirtySuites -ccontains $runner) "docker-selects-runner:$(& $shown $s)"
        Note ($s.dirtySuites -ccontains $pty) "docker-selects-pty:$(& $shown $s)"
        Note (Owns $s $pty 'docker/session-runner-grok/antiphon-custody-enter.sh' 'repository-reader-non-code') 'docker-pty-reason'
        $baseline=Git @('rev-parse','HEAD')
        Put 'docker-compose.runner-grok.yml' 'changed compose'
        $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "compose-unresolved:$(@($s.unresolved) -join ',')"
        Note ($s.dirtySuites -ccontains $runner) "compose-selects-runner:$(& $shown $s)"
        Note (Owns $s $runner 'docker-compose.runner-grok.yml' 'repository-reader-non-code') 'compose-runner-reason'
        $baseline=Git @('rev-parse','HEAD')
        Put 'Dockerfile' 'changed dockerfile'
        $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "dockerfile-unresolved:$(@($s.unresolved) -join ',')"
        Note ($s.dirtySuites -ccontains $runner) "dockerfile-selects-runner:$(& $shown $s)"
        Note (Owns $s $runner 'Dockerfile' 'repository-reader-non-code') 'dockerfile-runner-reason'
    }
    'ExplicitForcedRootsNotFallback' {
        $paths=@(
            'antiphon.areas.json','.editorconfig','.github/workflows/ci.yml',
            'tests/derived-impact-index.json','tests/impacted-classes.json')
        foreach ($path in $paths) { Put $path 'seed' }
        $baseline=Commit
        foreach ($path in $paths) {
            $baseline=Git @('rev-parse','HEAD')
            Put $path ('changed '+$path)
            $s=Select-Manifest (Commit)
            Note ($s.unresolved.Count -eq 0) "forced-unresolved:${path}:$(@($s.unresolved) -join ',')"
            Note ($s.dirtySuites -ccontains $messaging) "forced-dirties-messaging:${path}:$(& $shown $s)"
            Note (Owns $s $messaging $path 'forced-full-root') "forced-reason:$path"
            $suite=@($s.snapshots[1].suites | Where-Object id -CEQ $messaging)
            $record=@($suite[0].inputs | Where-Object { $_.path -ceq $path -and $_.present })
            Note ($record.Count -eq 1 -and @($record[0].reason.Split(';')) -cnotcontains 'unmatched-path-fallback') "forced-not-fallback:$path"
        }
    }
    'NugetConfigIgnoreCaseForcesFull' {
        Put 'server/nuget.config' '<configuration />'
        $baseline=Commit
        Put 'server/nuget.config' '<configuration><changed /></configuration>'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note ($s.unresolved.Count -eq 0) "nuget-case-unresolved:$(@($s.unresolved) -join ',')"
        Note (SameSet $s $ids) "nuget-case-dirties-all:$(& $shown $s)"
        Note (Owns $s $messaging 'server/nuget.config' 'forced-full-build-graph') 'nuget-case-forced-reason'
    }
    'ReaderRootsAreEverySuite' {
        foreach ($path in @(
            'docs/**','**/*.md','scripts/**','docker/**','.claude/**',
            '**/docker-compose*.yml','**/Dockerfile*','client/**')) {
            $rule=@($map.roots | Where-Object { $_.path -ceq $path })
            Note ($rule.Count -eq 1) "reader-root:$path"
            if ($rule.Count -eq 1) {
                $consumers=@(Get-TestSelectionSorted @($rule[0].consumers))
                Note (($consumers -join ';') -ceq '*') "reader-star:$path"
                Note ($rule[0].reason -ceq 'repository-reader-non-code') "reader-reason:$path"
            }
        }
    }
    default { throw "unknown case:$Case" }
}
Assert-C487 ($errors.Count -eq 0) ("RIO $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
