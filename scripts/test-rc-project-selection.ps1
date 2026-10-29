#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/rc-project-selection.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$ResultsDirectory=New-C487Root $ResultsDirectory
$root=Join-Path $ResultsDirectory 'repository with spaces'
[void][IO.Directory]::CreateDirectory($root)
function Git($a) { return (Invoke-TestSelectionGit $root $a).Stdout.Trim() }
function Put($path,$text) {
    $full=Join-Path $root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full))
    [IO.File]::WriteAllText($full,$text)
}
function Commit { [void](Git @('add','--all')); [void](Git @('commit','-qm','fixture')); return Git @('rev-parse','HEAD') }
function Check($ok,$label) { Assert-C487 ([bool]$ok) "C1545 $label" }
function Same($a,$b) { return ((Get-TestSelectionSorted @($a)) -join ',') -ceq ((Get-TestSelectionSorted @($b)) -join ',') }
[void](Git @('init','-q','-b','main')); [void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
$projects=[ordered]@{
    'src/Messaging/Messaging.csproj'=@('messaging')
    'src/Server/Server.csproj'=@('server','git-operations')
    'src/Runner/Runner.csproj'=@('claude-runner','codex-runner','shared-pty-runner')
    'tests/Server/Server.csproj'=@('server','git-operations')
    'tests/Runner/Runner.csproj'=@('claude-runner','codex-runner','shared-pty-runner')
    'tests/Other/Other.csproj'=@('other')
}
Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 @{schemaVersion=1;projects=$projects;batches=@("tests/Server/Server.csproj","tests/Runner/Runner.csproj","tests/Other/Other.csproj")})
Put 'src/Messaging/Messaging.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'src/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Messaging/Messaging.csproj" /></ItemGroup></Project>'
Put 'src/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Messaging/Messaging.csproj" /></ItemGroup></Project>'
Put 'tests/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Server/Server.csproj" /></ItemGroup></Project>'
Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Runner/Runner.csproj" /><Compile Include="../Server/Linked.cs" /></ItemGroup></Project>'
Put 'tests/Other/Other.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
Put 'src/Messaging/Message.cs' 'baseline'
Put 'tests/Server/Linked.cs' 'baseline'
$b=Commit
switch ($Case) {
    'C1545_Closure' {
        Put 'src/Messaging/Message.cs' 'changed'; $a=Commit
        $script:ProjectBlobReads=0
        $script:OriginalSelectionBlob=${function:Get-TestSelectionBlob}
        function Get-TestSelectionBlob {
            param([string]$Root,[string]$Sha,[string]$Path)
            if ($Path.EndsWith('.csproj',[StringComparison]::Ordinal)) { $script:ProjectBlobReads++ }
            return & $script:OriginalSelectionBlob $Root $Sha $Path
        }
        $s=Get-RcProjectSelection $root $b $a
        $projectReads=$script:ProjectBlobReads
        ${function:Get-TestSelectionBlob}=$script:OriginalSelectionBlob
        $projectBlobs=@((Git @('ls-tree','-r',$a)).Split("`n") | Where-Object { $_.EndsWith('.csproj',[StringComparison]::Ordinal) } | ForEach-Object { ($_ -split '\s+',4)[2] } | Sort-Object -Unique)
        Check ($projectReads -eq $projectBlobs.Count) 'immutable-blob-read-once'
        Check ($s.mode -ceq 'Scoped') 'scoped'
        Check (Same $s.changedProjects @('src/Messaging/Messaging.csproj')) 'owners'
        Check (Same $s.affectedProjects @('src/Messaging/Messaging.csproj','src/Server/Server.csproj','src/Runner/Runner.csproj','tests/Server/Server.csproj','tests/Runner/Runner.csproj')) 'downstream'
        Check (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj')) 'batches'
        Check (Same $s.categories @('messaging','server','git-operations','claude-runner','codex-runner','shared-pty-runner')) 'categories'
        $g=@($s.graphs | Where-Object sha -CEQ $a)[0]
        $m=@($g.projects | Where-Object path -CEQ 'src/Messaging/Messaging.csproj')[0]
        Check (Same $m.downstream @('src/Server/Server.csproj','src/Runner/Runner.csproj','tests/Server/Server.csproj','tests/Runner/Runner.csproj')) 'manifest-closure'
        Check (($s.effectiveMode -ceq 'Full') -and ($s.gateCredit -is [bool]) -and -not $s.gateCredit -and -not $s.executionEvidence) 'no-credit'
        $again=Get-RcProjectSelection $root $b $a
        Check ((ConvertTo-TestSelectionCanonicalJson $s) -ceq (ConvertTo-TestSelectionCanonicalJson $again)) 'deterministic'
    }
    'C1545_FailClosed' {
        Put 'unowned/file.txt' 'new'; $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and ($s.fallbackReasons -contains 'unknown-path:unowned/file.txt') -and $s.batches.Count -eq 3) 'unknown-full'
        Put 'Directory.Build.props' '<Project />'; $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check ($s.fallbackReasons -contains 'shared-input:Directory.Build.props') 'shared-full'
        Put 'src/Runner/Runner.csproj' '<Project><ItemGroup><ProjectReference Include="$(Unknown)/Runner.csproj" /></ItemGroup></Project>'; $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and (@($s.fallbackReasons | Where-Object {$_ -like '*unresolved-input*'}).Count -gt 0)) 'dynamic-full'
        $projects.Remove('tests/Other/Other.csproj'); Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 @{schemaVersion=1;projects=$projects;batches=@("tests/Server/Server.csproj","tests/Runner/Runner.csproj","tests/Other/Other.csproj")}); $a=Commit
        $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and ($s.fallbackReasons -contains 'unknown-project:tests/Other/Other.csproj')) 'category-full'
    }
    'C1545_HistoricalEdges' {
        Put 'tests/Server/Linked.cs' 'changed'; $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj')) 'linked-consumer'
        Put 'src/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'; $a=Commit
        $s=Get-RcProjectSelection $root $b $a
        $oldServer=@(@($s.graphs | Where-Object sha -CEQ $b)[0].projects | Where-Object path -CEQ 'src/Server/Server.csproj')[0]
        $newServer=@(@($s.graphs | Where-Object sha -CEQ $a)[0].projects | Where-Object path -CEQ 'src/Server/Server.csproj')[0]
        Check (($s.affectedProjects -contains 'tests/Server/Server.csproj') -and ($oldServer.references -contains 'src/Messaging/Messaging.csproj') -and $newServer.references.Count -eq 0) 'old-edge'
        [IO.File]::Delete((Join-Path $root 'src/Messaging/Message.cs')); $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check ($s.affectedProjects -contains 'src/Runner/Runner.csproj') 'deleted-owner'
        Put 'src/Runner/Runner.csproj' '<Project><ItemGroup><ProjectReference Include="../Server/Server.csproj" /></ItemGroup></Project>'
        Put 'src/Server/Server.csproj' '<Project><ItemGroup><ProjectReference Include="../Runner/Runner.csproj" /></ItemGroup></Project>'
        $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and (@($s.fallbackReasons | Where-Object {$_ -like '*cycle*'}).Count -gt 0)) 'cycle-full'
    }
    'C1545_ConsumerSafety' {
        Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Runner/Runner.csproj" /><Content Include="../Server/Data.txt" /></ItemGroup></Project>'
        Put 'tests/Server/Data.txt' 'baseline'; $cb=Commit
        Put 'tests/Server/Data.txt' 'changed'; $ca=Commit; $s=Get-RcProjectSelection $root $cb $ca
        Check (($s.mode -ceq 'Scoped') -and (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj'))) 'content-consumer'
        Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Runner/Runner.csproj" /><Content Include="../Server/Fixtures/*.json" /></ItemGroup></Project>'
        Put 'tests/Server/Fixtures/example.json' '{}'; $cb=Commit
        Put 'tests/Server/Fixtures/example.json' '{"changed":true}'; $ca=Commit; $s=Get-RcProjectSelection $root $cb $ca
        $wildcardOk=($s.mode -ceq 'Scoped') -and (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj'))
        Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Runner/Runner.csproj" /><Content Include="../Server/[sample].json" /></ItemGroup></Project>'
        Put 'tests/Server/[sample].json' '{}'; $cb=Commit
        Put 'tests/Server/[sample].json' '{"changed":true}'; $ca=Commit; $s=Get-RcProjectSelection $root $cb $ca
        Check ($wildcardOk -and ($s.mode -ceq 'Scoped') -and (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj'))) 'glob-consumer'
        Put 'src/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk"><Import Project="Dependencies.props" /></Project>'
        Put 'src/Server/Dependencies.props' '<Project><ItemGroup><ProjectReference Include="../Messaging/Messaging.csproj" /></ItemGroup></Project>'
        $cb=Commit; Put 'src/Messaging/Message.cs' 'imported consumer change'; $ca=Commit; $s=Get-RcProjectSelection $root $cb $ca
        Check (($s.mode -ceq 'Scoped') -and ($s.batches.project -contains 'tests/Server/Server.csproj')) 'imported-reference'
        Put 'tests/Runner/Runner.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Runner/Runner.csproj" /><PackageReference Include="TUnit" Version="1.0" /></ItemGroup></Project>'
        Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 @{schemaVersion=1;projects=$projects;batches=@('tests/Server/Server.csproj','tests/Other/Other.csproj')})
        $cb=Commit; Put 'src/Messaging/Message.cs' 'missing batch change'; $ca=Commit; $s=Get-RcProjectSelection $root $cb $ca
        Check (($s.mode -ceq 'Full') -and ($s.fallbackReasons -contains 'unbatched-test-project:tests/Runner/Runner.csproj')) 'batch-census'
    }
    default { throw "unknown case: $Case" }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
