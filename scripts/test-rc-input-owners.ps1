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
    # The comma keeps a one-row result an array. A bare return unwraps it,
    # and an ordered dictionary's Count is its key count.
    if ($null -eq $s -or $null -eq $s.unowned) { return ,@() }
    $hits=@(@($s.unowned) | Where-Object { $null -ne $_ -and [string]$_.path -ceq $path })
    return ,$hits
}
function DeferredRows($s) {
    if ($null -eq $s -or $null -eq $s.testOnlyDeferred) { return ,@() }
    return ,@(@($s.testOnlyDeferred))
}
function ChangedNotRunRows($s) {
    if ($null -eq $s -or $null -eq $s.changedNotRun) { return ,@() }
    return ,@(@($s.changedNotRun))
}
function ConflictRows($s) {
    if ($null -eq $s -or $null -eq $s.noopConflicts) { return ,@() }
    return ,@(@($s.noopConflicts))
}
function Copy-RioMap([bool]$Enabled) {
    $copy=ConvertFrom-Json -InputObject $mapText -AsHashtable
    $copy.testOnly.enabled=$Enabled
    return ($copy | ConvertTo-Json -Depth 8)
}
function Add-RioDeferredSuiteIds($Manifest, $Union) {
    # testOnlyDeferred is suite:path. A one-row result stays an array; do not
    # stringify that array or only the first suite is counted.
    $raw=$Manifest.testOnlyDeferred
    if ($null -eq $raw) { return }
    $rows=@($raw)
    if ($rows.Count -eq 1 -and $rows[0] -is [System.Array]) { $rows=@($rows[0]) }
    foreach ($row in $rows) {
        if ($null -eq $row) { continue }
        $text=[string]$row
        $cut=$text.IndexOf(':')
        if ($cut -gt 0) { [void]$Union.Add($text.Substring(0,$cut)) }
    }
}
function Note-RioSelectionParity([string]$name, $on, $off) {
    $union=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in @($on.dirtySuites)) { if ($id) { [void]$union.Add([string]$id) } }
    Add-RioDeferredSuiteIds $on $union
    $left=@(Get-TestSelectionSorted @($union)) -join ';'
    $right=@(Get-TestSelectionSorted @($off.dirtySuites)) -join ';'
    Note ($left -ceq $right) "parity:${name}:on=$left;off=$right"
    Note (-not $on.gateCredit -and -not $off.gateCredit) "parity-gate:${name}"
    Note ([string]$on.activation -ceq 'shadow-only' -and [string]$off.activation -ceq 'shadow-only') "parity-shadow:${name}"
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
        Put $sample 'class StaleWorkingTranscriptWatchdogTests { void Body() { int x = 1; } }'
        Put $migration 'migration'
        $baseline=Commit
        Put $sample 'class StaleWorkingTranscriptWatchdogTests { void Body() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -cnotcontains $resilience) "d1a-body-skips-resilience:$(& $shown $s)"
        Note ($s.dirtySuites -cnotcontains $serverGit) 'd1a-body-skips-server-git'
        Note ($s.dirtySuites -cnotcontains $gitinprocess) 'd1a-body-skips-git'
        Note ($s.dirtySuites -cnotcontains $antiphon) 'd1a-body-skips-antiphon'
        $deferred=DeferredRows $s
        Note ($deferred -ccontains "${resilience}:${sample}") 'd1a-resilience-deferred'
        Note ($deferred -ccontains "${serverGit}:${sample}") 'd1a-server-git-deferred'
        Note ($deferred -ccontains "${gitinprocess}:${sample}") 'd1a-git-deferred'
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
        Put 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'class LinkedHelper { void Body() { int x = 1; } }'
        $baseline=Commit
        Put 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'class LinkedHelper { void Body() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "link-resolved:$(@($s.unresolved) -join ',')"
        # The Compile edge still selects the including project. A method-body edit does not select the walkers.
        $serverGit='tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj'
        Note (SameSet $s @($resilience)) "link-selects-including:$(& $shown $s)"
        Note (Owns $s $resilience 'tests/Antiphon.Tests/TestHelpers/Linked.cs' 'Compile') 'link-compile-edge'
        Note ($s.dirtySuites -cnotcontains $serverGit) 'link-body-skips-server-git'
        Note (Lacks $s $messaging 'tests/Antiphon.Tests/TestHelpers/Linked.cs') 'link-misses-messaging'
        Note ((DeferredRows $s) -ccontains "${antiphon}:tests/Antiphon.Tests/TestHelpers/Linked.cs") 'link-antiphon-deferred'
    }
    'UnrelatedFileSelectsNothing' {
        Put 'tests/Antiphon.Tests/TestHelpers/Sibling.cs' 'changed unlinked sibling'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "sibling-resolved:$(@($s.unresolved) -join ',')"
        # A body edit of an unlinked test file selects nothing. The walkers stay deferred.
        $serverGit='tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj'
        Note ($s.dirtySuites.Count -eq 0) "sibling-selects-nothing:$(& $shown $s)"
        $siblingDeferred=DeferredRows $s
        foreach ($id in @($antiphon,$gitinprocess,$resilience,$serverGit)) {
            Note ($siblingDeferred -ccontains "${id}:tests/Antiphon.Tests/TestHelpers/Sibling.cs") "sibling-deferred:$id"
        }
        $baseline=Git @('rev-parse','HEAD')
        Put 'tests/Antiphon.Messaging.Tests/Own.cs' 'changed unrelated'; $s=Select-Manifest (Commit)
        Note ($s.unresolved.Count -eq 0) "unrelated-resolved:$(@($s.unresolved) -join ',')"
        Note ($s.dirtySuites.Count -eq 0) "unrelated-selects-nothing:$(& $shown $s)"
        Note ((DeferredRows $s) -ccontains "${messaging}:tests/Antiphon.Messaging.Tests/Own.cs") 'unrelated-messaging-deferred'
        Note ((DeferredRows $s) -ccontains "${antiphon}:tests/Antiphon.Messaging.Tests/Own.cs") 'unrelated-antiphon-deferred'
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
    'TestOnlyMethodEditSelectsNothing' {
        # A method-body edit of test source, and a fixture edit, select nothing.
        # tests/Shared stays forced full. The result names every deferred suite.
        $serverGit='tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj'
        $sibling='tests/Antiphon.Tests/TestHelpers/Sibling.cs'
        Put $sibling "class SiblingProbe { void Body() { int x = 1; } }"
        $baseline=Commit
        Put $sibling "class SiblingProbe { void Body() { int x = 2; } }"
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "test-only-selects-nothing:$(& $shown $s)"
        $deferred=DeferredRows $s
        foreach ($id in @($antiphon,$resilience,$gitinprocess,$serverGit)) {
            Note ($deferred -ccontains "${id}:${sibling}") "deferred:${id}"
        }
        $fixture='tests/Antiphon.Tests/Fixtures/probe.txt'
        $baseline=Git @('rev-parse','HEAD')
        Put $fixture 'fixture'
        $baseline=Commit
        Put $fixture 'fixture changed'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "fixture-selects-nothing:$(& $shown $s)"
        Note ((DeferredRows $s) -ccontains "${antiphon}:${fixture}") 'fixture-deferred'
        $shared='tests/Shared/Probe.cs'
        $baseline=Git @('rev-parse','HEAD')
        Put $shared "class SharedProbe { void Body() { int x = 1; } }"
        $baseline=Commit
        Put $shared "class SharedProbe { void Body() { int x = 2; } }"
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -gt 0) "shared-stays-forced:$(& $shown $s)"
        Note ($s.dirtySuites -ccontains $messaging) 'shared-dirties-messaging'
    }
    'AddedTestClassSelectsWalkers' {
        # Adding a class file selects the source walkers. The containing suite
        # stays deferred and the new class is recorded as changed-not-run.
        $serverGit='tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj'
        $path='tests/Antiphon.Tests/Application/BrandNewWalkerProbeTests.cs'
        Put $path "class BrandNewWalkerProbeTests { void Body() { int x = 1; } }"
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains $resilience) "add-selects-resilience:$(& $shown $s)"
        Note ($s.dirtySuites -ccontains $gitinprocess) 'add-selects-git'
        Note ($s.dirtySuites -ccontains $serverGit) 'add-selects-servergit'
        Note ($s.dirtySuites -cnotcontains $antiphon) "add-defers-antiphon:$(& $shown $s)"
        $notRun=ChangedNotRunRows $s
        Note ($notRun -ccontains "${antiphon}:${path}:BrandNewWalkerProbeTests") "changed-not-run:$($notRun -join ',')"
    }
    'ProductEdgeStillReachesSuite' {
        # A linked Compile edge still selects the including project when the
        # edit is only a method body. A product file still selects its readers.
        $linked='tests/Antiphon.Tests/TestHelpers/Linked.cs'
        Put $linked "class LinkedProbe { void Body() { int x = 1; } }"
        $baseline=Commit
        Put $linked "class LinkedProbe { void Body() { int x = 2; } }"
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains $resilience) "link-still-selects:$(& $shown $s)"
        Note (Owns $s $resilience $linked 'Compile') 'link-compile-edge'
        Note ($s.dirtySuites -cnotcontains $gitinprocess) "link-body-skips-walker:$(& $shown $s)"
        Note ($s.dirtySuites -cnotcontains $antiphon) 'link-body-skips-antiphon'
        $baseline=Git @('rev-parse','HEAD')
        Put 'server/App.cs' 'product edge'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains $antiphon -and $s.dirtySuites -ccontains $e2e) "product-reaches:$(& $shown $s)"
        Note (Owns $s $antiphon 'server/App.cs' 'project-tree:server/Server.csproj') 'product-project-edge'
    }
    'NoOpOwnerConflictFails' {
        # A no-op row that matches an explicit owner must not win silently.
        $path='docs/testing-and-build.md'
        $noop=@{schemaVersion=1;rows=@(
            @{path='docs/reviews/**';reason='reviews';evidence='fixture'},
            @{path=$path;reason='hides the doc reader';evidence='fixture'})}
        Put 'scripts/lib/rc-input-noop.json' ($noop | ConvertTo-Json -Depth 6)
        Put $path 'doc'
        $baseline=Commit
        Put $path 'doc changed'
        $s=Select-Manifest (Commit)
        $conflicts=ConflictRows $s
        Note ($conflicts.Count -ge 1) "noop-conflict-count:$($conflicts.Count)"
        $hit=@($conflicts | Where-Object { [string]$_ -like ('*'+$path+'*') })
        Note ($hit.Count -ge 1) "noop-conflict-names-path:$($conflicts -join ',')"
        Note ($s.dirtySuites -ccontains $antiphon) "noop-owner-wins:$(& $shown $s)"
    }
    'UnownedListAboveFortyIsVisible' {
        # Above 40 unowned paths the census result shows the count, the first
        # 40 paths, and the path of the full list file.
        foreach ($i in 1..41) { Put ("stray/file{0}.txt" -f $i) 'x' }
        $s=Select-Manifest (Commit)
        $listPath=Join-Path $root '.antiphon/rc-unowned-paths.txt'
        $lines=@()
        if (Get-Command Format-RcUnownedCensusLines -ErrorAction SilentlyContinue) {
            $lines=@(Format-RcUnownedCensusLines @($s.unowned) $listPath)
        }
        $countLine=@($lines | Where-Object { $_ -like 'UNOWNED-COUNT *' })
        $pathLines=@($lines | Where-Object { $_ -like 'UNOWNED-PATH *' })
        $listLines=@($lines | Where-Object { $_ -like 'UNOWNED-LIST *' })
        Note ($countLine.Count -eq 1 -and $countLine[0] -match '^UNOWNED-COUNT ([0-9]+)$' -and [int]$Matches[1] -gt 40) "unowned-count:$($countLine -join ',')"
        Note ($pathLines.Count -eq 40) "unowned-first-40:$($pathLines.Count)"
        Note ($listLines.Count -eq 1 -and $listLines[0] -ceq ('UNOWNED-LIST '+$listPath)) "unowned-list:$($listLines -join ',')"
    }
    'ExistingCsprojIsNotANewCsproj' {
        # new-csproj is only for a csproj nobody references that this commit adds.
        $path='tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj'
        Put $path '<Project Sdk="Microsoft.NET.Sdk" />'
        $baseline=Commit
        Put $path '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Fixture>1</Fixture></PropertyGroup></Project>'
        $s=Select-Manifest (Commit)
        $rows=UnownedRows $s $path
        Note ($rows.Count -eq 1) 'existing-csproj-unowned'
        Note ([string]$rows[0].hint -cne 'new-csproj') "existing-csproj-hint:$($rows[0].hint)"
        Note ($s.dirtySuites.Count -eq 0) "existing-csproj-selects-nothing:$(& $shown $s)"
    }
    'HelperProjectBodySelectsReferencers' {
        # Two suites ProjectReference one helper. A body edit of the helper's
        # Program.cs selects both. A body edit inside a suite's own project still defers.
        $helperProj='tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj'
        $helperSrc='tests/Antiphon.CustodyTestChild/Program.cs'
        $runner='tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj'
        Put $helperProj '<Project Sdk="Microsoft.NET.Sdk" />'
        Put $helperSrc 'class HelperProgram { void Body() { int x = 1; } }'
        Put $runner '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj" /></ItemGroup></Project>'
        Put $antiphon '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../server/Server.csproj" /><ProjectReference Include="../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj" /></ItemGroup></Project>'
        $baseline=Commit
        Put $helperSrc 'class HelperProgram { void Body() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note (SameSet $s @($antiphon,$runner)) "helper-selects-both:$(& $shown $s)"
        Note ($s.dirtySuites -cnotcontains $helperProj) "helper-own-stays-deferred:$(& $shown $s)"
        Note ((DeferredRows $s) -ccontains "${helperProj}:${helperSrc}") 'helper-own-deferred-row'
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'helper-stays-shadow'
        $own='tests/Antiphon.Tests/OwnBodyProbe.cs'
        Put $own 'class OwnBodyProbe { void Body() { int x = 1; } }'
        $baseline=Commit
        Put $own 'class OwnBodyProbe { void Body() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "own-body-still-defers:$(& $shown $s)"
        Note ((DeferredRows $s) -ccontains "${antiphon}:${own}") 'own-body-deferred-row'
    }
    'E2EAddedTestSelectsSuites' {
        # An added E2E file, and an added [Test] method on an existing E2E class,
        # select every suite. A later body edit still defers.
        $added='tests/Antiphon.E2E/AddedProbeTests.cs'
        Put $added "class AddedE2EProbeTests { [Test] void AddedMethod() { int x = 1; } }"
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-file-selects-all:$(& $shown $s)"
        Note ($s.dirtySuites -ccontains $e2e) 'e2e-file-selects-e2e'
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-file-stays-shadow'
        $existing='tests/Antiphon.E2E/ExistingProbeTests.cs'
        Put $existing 'class ExistingE2EProbeTests { void OldMethod() { int x = 1; } }'
        $baseline=Commit
        Put $existing 'class ExistingE2EProbeTests { void OldMethod() { int x = 1; } [Test] void AddedMethod() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-method-selects-all:$(& $shown $s)"
        $baseline=Git @('rev-parse','HEAD')
        Put $existing 'class ExistingE2EProbeTests { void OldMethod() { int x = 1; } [Test] void AddedMethod() { int x = 3; } }'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "e2e-body-still-defers:$(& $shown $s)"
    }
    'E2EIdentityChangeSelectsSuites' {
        # class|method|Arguments identities, not the [Test] count. A net-zero
        # rename, a same-identity move between files and an added [Arguments]
        # row select every suite. A later body edit still defers.
        $renamed='tests/Antiphon.E2E/RenameProbeTests.cs'
        Put $renamed 'class RenameE2EProbeTests { [Test] void OriginalMethod() { int x = 1; } }'
        $baseline=Commit
        Put $renamed 'class RenameE2EProbeTests { [Test] void RenamedMethod() { int x = 1; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-rename-selects-all:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-rename-stays-shadow'
        Note ([string]::IsNullOrEmpty([string]$s.creditBlockedBy)) "e2e-rename-credit:$($s.creditBlockedBy)"
        $left='tests/Antiphon.E2E/MoveLeftProbeTests.cs'
        $right='tests/Antiphon.E2E/MoveRightProbeTests.cs'
        Put $left 'class MovedE2EProbeTests { [Test] void MovedMethod() { int x = 1; } }'
        Put $right 'class MovedE2EProbeTests { void Holder() { int x = 1; } }'
        $baseline=Commit
        Put $left 'class MovedE2EProbeTests { void Holder() { int x = 1; } }'
        Put $right 'class MovedE2EProbeTests { [Test] void MovedMethod() { int x = 1; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-move-selects-all:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-move-stays-shadow'
        $rows='tests/Antiphon.E2E/ArgumentsProbeTests.cs'
        Put $rows 'class ArgumentsE2EProbeTests { [Test] void RowMethod() { int x = 1; } }'
        $baseline=Commit
        Put $rows 'class ArgumentsE2EProbeTests { [Test] [Arguments("added-row")] void RowMethod() { int x = 1; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-arguments-selects-all:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-arguments-stays-shadow'
        Note ([string]::IsNullOrEmpty([string]$s.creditBlockedBy)) "e2e-arguments-credit:$($s.creditBlockedBy)"
        $baseline=Git @('rev-parse','HEAD')
        Put $rows 'class ArgumentsE2EProbeTests { [Test] [Arguments("added-row")] void RowMethod() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites.Count -eq 0) "e2e-identity-body-still-defers:$(& $shown $s)"
        # A multi-line [Arguments] row, a new[] attribute and a generic method
        # are roster changes. Dropping the form must still select every suite.
        $multi='tests/Antiphon.E2E/MultiLineArgumentsProbeTests.cs'
        Put $multi "class MultiLineArgumentsE2EProbeTests { [Test] void RowMethod() { int x = 1; } }"
        $baseline=Commit
        Put $multi @"
class MultiLineArgumentsE2EProbeTests {
    [Test]
    [Arguments(
        "b")]
    void RowMethod() { int x = 1; }
}
"@
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-multiline-arguments-selects-all:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-multiline-arguments-stays-shadow'
        $array='tests/Antiphon.E2E/ArrayArgumentsProbeTests.cs'
        Put $array 'class ArrayArgumentsE2EProbeTests { [Test] [Arguments(new[] { "a" })] void OriginalMethod() { int x = 1; } }'
        $baseline=Commit
        Put $array 'class ArrayArgumentsE2EProbeTests { [Test] [Arguments(new[] { "a" })] void RenamedMethod() { int x = 1; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-array-arguments-rename-selects-all:$(& $shown $s)"
        $generic='tests/Antiphon.E2E/GenericProbeTests.cs'
        Put $generic 'class GenericE2EProbeTests { [Test] Task A<T>() { return Task.CompletedTask; } }'
        $baseline=Commit
        Put $generic 'class GenericE2EProbeTests { [Test] Task B<T>() { return Task.CompletedTask; } }'
        $s=Select-Manifest (Commit)
        $ids=@($s.snapshots[1].suites.id)
        Note (SameSet $s $ids) "e2e-generic-rename-selects-all:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'e2e-generic-rename-stays-shadow'
    }
    'OwnerLiteralPathsFailClosed' {
        # Every literal owner path, exceptPaths prefix and owner-glob prefix
        # must exist. Renaming the canary, the E2E directory or tests/Shared
        # fails closed and names the missing path. At base the census is absent.
        $censusRoot=Join-Path $ResultsDirectory 'owner-literal'
        function Rio-Literal([string]$Relative) {
            return Join-Path $censusRoot ($Relative -replace '/',[IO.Path]::DirectorySeparatorChar)
        }
        foreach ($dir in @('tests/Antiphon.E2E','tests/Shared','tests/Antiphon.Tests/Agents','tests/fixtures','docs/reviews')) {
            [void][IO.Directory]::CreateDirectory((Rio-Literal $dir))
        }
        $canaryRel='tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs'
        [IO.File]::WriteAllText((Rio-Literal $canaryRel),'class Canary {}')
        $mapObj=@{
            schemaVersion=1
            suiteProjects='tests/**/*.csproj'
            testOnly=@{
                enabled=$true
                paths=@('tests/**/*.cs','tests/fixtures/**')
                exceptPaths=@('tests/Shared/**')
                exceptReasons=@('forced-full-root')
                productEdgeReasons=@('runtime-reader-scan')
            }
            roots=@(
                @{path=$canaryRel; consumers=@('tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj'); reason='runtime-reader-scan'}
                @{path='tests/Antiphon.E2E/**/*.cs'; consumers=@('*'); reason='runtime-reader-unproven'; walker=$true}
                @{path='docs/**'; except=@('docs/reviews/**'); consumers=@('*'); reason='repository-reader-non-code'}
            )
        }
        $json=$mapObj | ConvertTo-Json -Depth 8
        $intact=$false
        $renamed=$false
        $prefixGone=$false
        $exceptGone=$false
        $message=''
        if (Get-Command Get-RcMissingOwnerLiteralPaths -ErrorAction SilentlyContinue) {
            $missing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $censusRoot -MapJson $json)
            $intact=($missing.Count -eq 0)
            [IO.File]::Delete((Rio-Literal $canaryRel))
            $missing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $censusRoot -MapJson $json)
            $message=$missing -join ' | '
            $renamed=@($missing | Where-Object { $_ -like "missing-literal-path:*$canaryRel*" }).Count -ge 1
            [IO.File]::WriteAllText((Rio-Literal $canaryRel),'class Canary {}')
            [IO.Directory]::Delete((Rio-Literal 'tests/Antiphon.E2E'))
            $missing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $censusRoot -MapJson $json)
            $message=$missing -join ' | '
            $prefixGone=@($missing | Where-Object { $_ -like 'missing-literal-path:*tests/Antiphon.E2E*' }).Count -ge 1
            [void][IO.Directory]::CreateDirectory((Rio-Literal 'tests/Antiphon.E2E'))
            [IO.Directory]::Delete((Rio-Literal 'tests/Shared'))
            $missing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $censusRoot -MapJson $json)
            $message=$missing -join ' | '
            $exceptGone=@($missing | Where-Object { $_ -like 'missing-literal-path:*tests/Shared*' }).Count -ge 1
        }
        Note $intact "literal-intact-not-quiet:$message"
        Note $renamed "literal-rename-not-detected:$message"
        Note $prefixGone "literal-prefix-not-detected:$message"
        Note $exceptGone "literal-except-not-detected:$message"
    }
    'RealOwnerMapLiteralPathsExist' {
        # The committed map is checked against this repository. A scratch copy
        # of those literal prefixes must go red when the canary file is renamed.
        $sourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
        $mapPath=Join-Path $sourceRoot 'scripts/lib/rc-input-owners.json'
        $json=[IO.File]::ReadAllText($mapPath)
        $live=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $sourceRoot -MapJson $json)
        Note ($live.Count -eq 0) "real-owner-map:$($live -join ' | ')"
        $census=Join-Path $sourceRoot 'scripts/rc-input-owner-path-census.ps1'
        $censusOut=@(& pwsh -NoProfile -File $census | ForEach-Object { "$_" })
        Note ($LASTEXITCODE -eq 0 -and (@($censusOut) -join "`n") -match 'missing-literal-path=0') "real-census-script:${LASTEXITCODE}:$(@($censusOut) -join ' | ')"
        $mapObj=ConvertFrom-Json -InputObject $json -AsHashtable
        $canary=@($mapObj.roots | Where-Object { [string]$_.path -like '*ClaudeDialogWatchdogCanaryTests.cs' } | ForEach-Object { [string]$_.path })
        Note ($canary.Count -eq 1) "real-canary-row:$($canary -join ',')"
        $scratch=Join-Path $ResultsDirectory 'real-map-canary'
        [void][IO.Directory]::CreateDirectory($scratch)
        $patterns=New-Object System.Collections.Generic.List[string]
        if ($mapObj.Contains('suiteProjects') -and $mapObj.suiteProjects) { $patterns.Add([string]$mapObj.suiteProjects) }
        if ($mapObj.Contains('testOnly') -and $null -ne $mapObj.testOnly) {
            foreach ($name in @('paths','exceptPaths')) {
                if (-not $mapObj.testOnly.Contains($name)) { continue }
                foreach ($item in @($mapObj.testOnly[$name])) { if ($item) { $patterns.Add([string]$item) } }
            }
        }
        foreach ($rootRow in @($mapObj.roots)) {
            if ($null -eq $rootRow -or -not $rootRow.path) { continue }
            $patterns.Add([string]$rootRow.path)
        }
        foreach ($pattern in $patterns) {
            $prefix=Get-RcLiteralPathPrefix $pattern
            if (-not $prefix) { continue }
            $full=Join-Path $scratch ($prefix -replace '/',[IO.Path]::DirectorySeparatorChar)
            if ($pattern.IndexOfAny([char[]]@('*','?')) -ge 0) {
                [void][IO.Directory]::CreateDirectory($full)
                continue
            }
            $parent=Split-Path -Parent $full
            if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
            if (-not (Test-Path -LiteralPath $full)) { [IO.File]::WriteAllText($full,'x') }
        }
        $scratchIntact=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $scratch -MapJson $json)
        Note ($scratchIntact.Count -eq 0) "scratch-map-intact:$($scratchIntact -join ' | ')"
        if ($canary.Count -eq 1) {
            $canaryFull=Join-Path $scratch ($canary[0] -replace '/',[IO.Path]::DirectorySeparatorChar)
            $renamedFull=$canaryFull+'.renamed'
            if (Test-Path -LiteralPath $canaryFull) { [IO.File]::Move($canaryFull,$renamedFull) }
            $scratchMissing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $scratch -MapJson $json)
            $hit=@($scratchMissing | Where-Object { $_ -like ('missing-literal-path:*'+$canary[0]+'*') })
            Note ($hit.Count -ge 1) "scratch-canary-rename:$($scratchMissing -join ' | ')"
        }
    }
    'CanaryEditSelectsPtySuite' {
        # A body edit of the canary file selects the Pty suite that reads it.
        # The owning Antiphon.Tests project still defers.
        $pty='tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj'
        $canary='tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs'
        Put $canary 'class ClaudeDialogWatchdogCanaryTests { void Body() { int x = 1; } }'
        $baseline=Commit
        Put $canary 'class ClaudeDialogWatchdogCanaryTests { void Body() { int x = 2; } }'
        $s=Select-Manifest (Commit)
        Note ($s.dirtySuites -ccontains $pty) "canary-selects-pty:$(& $shown $s)"
        Note (Owns $s $pty $canary 'runtime-reader-scan') 'canary-reader-reason'
        Note ($s.dirtySuites -cnotcontains $antiphon) "canary-own-stays-deferred:$(& $shown $s)"
        Note (-not $s.gateCredit -and [string]$s.activation -ceq 'shadow-only') 'canary-stays-shadow'
    }
    'EnabledFalseMatchesBaseSelection' {
        # enabled=false selects the same suites the enabled closure would have
        # selected before deferral. Selection parity, not manifest bytes.
        function Invoke-RioProbe([bool]$Enabled, [scriptblock]$Setup, [string]$Path, [string]$Before, [string]$After) {
            Put 'scripts/lib/rc-input-owners.json' (Copy-RioMap $Enabled)
            if ($Setup) { & $Setup }
            if ($null -ne $Before) { Put $Path $Before }
            [void](Git @('add','--all'))
            [void](Git @('commit','-qm','fixture','--allow-empty'))
            $base=Git @('rev-parse','HEAD')
            Put $Path $After
            return Get-RcInputManifest $root $base (Commit) $null
        }
        $helperProj='tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj'
        $helperSrc='tests/Antiphon.CustodyTestChild/Program.cs'
        $runner='tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj'
        $armHelper={
            Put $helperProj '<Project Sdk="Microsoft.NET.Sdk" />'
            Put $runner '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj" /></ItemGroup></Project>'
            Put $antiphon '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../server/Server.csproj" /><ProjectReference Include="../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj" /></ItemGroup></Project>'
        }
        $sibling='tests/Antiphon.Tests/TestHelpers/Sibling.cs'
        $on=Invoke-RioProbe $true $null $sibling 'class SiblingProbe { void Body() { int x = 1; } }' 'class SiblingProbe { void Body() { int x = 2; } }'
        $off=Invoke-RioProbe $false $null $sibling 'class SiblingProbe { void Body() { int x = 1; } }' 'class SiblingProbe { void Body() { int x = 2; } }'
        Note-RioSelectionParity 'own-body' $on $off
        Note ($on.dirtySuites.Count -eq 0) "parity-own-defers:$(& $shown $on)"
        Note ($off.dirtySuites -ccontains $antiphon) "parity-own-base-selects:$(& $shown $off)"
        $on=Invoke-RioProbe $true $null 'server/App.cs' 'server source' 'server source changed'
        $off=Invoke-RioProbe $false $null 'server/App.cs' 'server source' 'server source changed'
        Note-RioSelectionParity 'product' $on $off
        $productOn=@(Get-TestSelectionSorted @($on.dirtySuites)) -join ';'
        $productOff=@(Get-TestSelectionSorted @($off.dirtySuites)) -join ';'
        Note ($productOn -ceq $productOff) "parity-product-equal:$productOn;$productOff"
        $on=Invoke-RioProbe $true $armHelper $helperSrc 'class HelperProgram { void Body() { int x = 1; } }' 'class HelperProgram { void Body() { int x = 2; } }'
        $off=Invoke-RioProbe $false $armHelper $helperSrc 'class HelperProgram { void Body() { int x = 1; } }' 'class HelperProgram { void Body() { int x = 2; } }'
        Note-RioSelectionParity 'helper' $on $off
        Note ($off.dirtySuites -ccontains $antiphon -and $off.dirtySuites -ccontains $runner) "parity-helper-base:$(& $shown $off)"
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
