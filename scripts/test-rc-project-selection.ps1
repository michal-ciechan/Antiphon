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
Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 @{schemaVersion=1;projects=$projects})
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
        $s=Get-RcProjectSelection $root $b $a
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
        $projects.Remove('tests/Other/Other.csproj'); Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 @{schemaVersion=1;projects=$projects}); $a=Commit
        $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and ($s.fallbackReasons -contains 'unknown-project:tests/Other/Other.csproj')) 'category-full'
    }
    'C1545_HistoricalEdges' {
        Put 'tests/Server/Linked.cs' 'changed'; $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (Same $s.batches.project @('tests/Server/Server.csproj','tests/Runner/Runner.csproj')) 'linked-consumer'
        Put 'src/Server/Server.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'; $a=Commit
        $s=Get-RcProjectSelection $root $b $a
        Check ($s.affectedProjects -contains 'tests/Server/Server.csproj') 'old-edge'
        [IO.File]::Delete((Join-Path $root 'src/Messaging/Message.cs')); $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check ($s.affectedProjects -contains 'src/Runner/Runner.csproj') 'deleted-owner'
        Put 'src/Runner/Runner.csproj' '<Project><ItemGroup><ProjectReference Include="../Server/Server.csproj" /></ItemGroup></Project>'
        Put 'src/Server/Server.csproj' '<Project><ItemGroup><ProjectReference Include="../Runner/Runner.csproj" /></ItemGroup></Project>'
        $a=Commit; $s=Get-RcProjectSelection $root $b $a
        Check (($s.mode -ceq 'Full') -and (@($s.fallbackReasons | Where-Object {$_ -like '*cycle*'}).Count -gt 0)) 'cycle-full'
    }
    default { throw "unknown case: $Case" }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
