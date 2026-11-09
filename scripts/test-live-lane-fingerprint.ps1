#requires -Version 7.0
# CARD-1545 S2a-1: live-lane fingerprint witnesses (V-1..V-7 of the S2a/S2b test design).
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/rc-project-selection.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$ResultsDirectory=New-C487Root $ResultsDirectory
$script:Lane='fixture-lane'
$script:Method=($Case -replace '^C1545_','')
$script:Library=Join-Path $PSScriptRoot 'lib/live-lane-fingerprint.ps1'
$script:StatusScript=Join-Path $PSScriptRoot 'live-lane-status.ps1'
$script:Pwsh=(Get-Process -Id $PID).Path

function Git([string[]]$a, [string]$At=$script:root) { return (Invoke-C487FixtureGit $At $a) }
function Put([string]$path, [string]$text) {
    $full=Join-Path $script:root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full))
    [IO.File]::WriteAllText($full,$text)
}
function Drop([string]$path) { [IO.File]::Delete((Join-Path $script:root $path)) }
function Commit { [void](Git @('add','--all')); [void](Git @('commit','-qm','fixture')); return Git @('rev-parse','HEAD') }
# Commits the index as staged; used where the working tree cannot carry the entry (mode, symlink, gitlink).
function Commit-Index { [void](Git @('commit','-qm','fixture')); return Git @('rev-parse','HEAD') }
function Reset-Fixture { [void](Git @('checkout','-q','-f','--detach',$script:base)); [void](Git @('clean','-q','-fdx')) }
function Check($ok, [string]$label, [string]$detail='') { Assert-C487 ([bool]$ok) "C1545 $($script:Method)/$label" $detail }
# Parsed project documents are keyed by Git blob id (immutable bytes), so one case shares them
# across its fingerprints; every graph, closure and inventory is still resolved per commit. A
# fixture case otherwise spawns ~18 git processes per fingerprint, the dominant cost on a loaded
# Windows host (rc run f40ad905 group-125 exceeded the 60 s budget).
$script:Documents=@{}
function Fp([string]$sha, [string]$lane=$script:Lane, [string]$at=$script:root) {
    return Get-LiveLaneFingerprint -RepositoryRoot $at -Sha $sha -Lane $lane -DocumentCache $script:Documents
}
function Canon($value) { return ConvertTo-TestSelectionCanonicalJson $value }
function Differs($a, $b, [string[]]$components=@()) {
    if (-not ($a.digest -cmatch '^[0-9a-f]{64}$') -or $a.digest -ceq $b.digest) { return $false }
    foreach ($name in $components) {
        if ($null -eq $a.components -or $null -eq $b.components) { return $false }
        if (-not $a.components[$name] -or $a.components[$name] -ceq $b.components[$name]) { return $false }
    }
    return $true
}
function Same($a, $b) { return ($a.digest -cmatch '^[0-9a-f]{64}$') -and ($a.digest -ceq $b.digest) }
function Has($fp, [string]$reason) { return @($fp.unknownReasons) -ccontains $reason }
function Unknown($fp, [string]$reason) { return ($fp.status -ceq 'unknown') -and (Has $fp $reason) }
function Paths($fp) { return @($fp.inventory | ForEach-Object { $_.path }) }
function Entry($fp, [string]$path) { return @($fp.inventory | Where-Object { $_.path -ceq $path })[0] }
function Invoke-Status([string]$sha, [string]$lane=$script:Lane) {
    $start=[Diagnostics.ProcessStartInfo]::new($script:Pwsh)
    foreach ($arg in @('-NoProfile','-NonInteractive','-File',$script:StatusScript,'-RepositoryRoot',$script:root,'-Lane',$lane,'-CandidateSha',$sha)) { $start.ArgumentList.Add($arg) }
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true; $start.UseShellExecute=$false
    $process=[Diagnostics.Process]::Start($start)
    $stderr=$process.StandardError.ReadToEndAsync()
    $stdout=$process.StandardOutput.ReadToEnd()
    if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw 'status-timeout' }
    $process.WaitForExit()
    $document=$null; try { $document=$stdout | ConvertFrom-Json -AsHashtable } catch { }
    return @{exit=$process.ExitCode;stdout=$stdout;stderr=$stderr.Result;document=$document}
}

function New-LaneDeclaration {
    return [ordered]@{
        manifest='tests/live-lanes.json'
        contractFiles=@('tests/live-lanes.json','tests/live-lane-inputs.json','scripts/lib/live-lane-*.ps1')
        minimumRoots=@('src/Agents.Pty/','src/SessionRunner/','server/','tests/Shared/LiveClaude/Fixtures/')
        runtimeRoots=@('scripts/hooks/','scripts/lib/')
        namespaceRoots=@('Antiphon.Agents.Pty','Antiphon.PtyHost','Antiphon.SessionRunner','Antiphon.Server','Antiphon.Tests','Antiphon.Agents.Pty.Tests')
        instructionPatterns=@('CLAUDE.md','CLAUDE.local.md','AGENTS.md','.claude/**','.mcp.json')
    }
}
function Write-Declaration($lanes, [string]$format='antiphon-live-lane-inputs-v1', [string]$suffix='') {
    Put 'tests/live-lane-inputs.json' ((ConvertTo-Json -Depth 10 ([ordered]@{format=$format;lanes=$lanes}))+$suffix)
}
function Write-Manifest([string]$suffix='') {
    $row={ param($project,$class) [ordered]@{project=$project;namespace="Antiphon.$project";class=$class;method='Live';arguments=@();uid="Antiphon.$project.$class.1.1.Live.1.1.0";categories=@('Integration','LiveClaude');bound=$true} }
    Put 'tests/live-lanes.json' ((ConvertTo-Json -Depth 10 ([ordered]@{format='antiphon-live-lanes-v1';core=@((& $row 'Pty.Tests' 'PtyLive'),(& $row 'Main.Tests' 'MainLive'));gates=@();additiveContract=@()}))+$suffix)
}
function Write-Catalog([string[]]$batches=@('tests/Pty.Tests/Pty.Tests.csproj','tests/Main.Tests/Main.Tests.csproj','tests/Other/Other.csproj')) {
    $projects=[ordered]@{}
    foreach ($path in @('src/Protocol/Protocol.csproj','src/Agents.Pty/Agents.Pty.csproj','src/SessionRunner/SessionRunner.csproj','server/Server.csproj',
            'tests/Pty.Tests/Pty.Tests.csproj','tests/Main.Tests/Main.Tests.csproj','src/Unrelated/Unrelated.csproj','tests/Other/Other.csproj')) { $projects[$path]=@('fixture') }
    Put 'tests/rc-project-categories.json' (ConvertTo-Json -Depth 10 ([ordered]@{schemaVersion=1;projects=$projects;batches=$batches}))
}
$script:PtyTestsProject='<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="TUnit" Version="1.0" /><ProjectReference Include="../../src/Agents.Pty/Agents.Pty.csproj" /><Compile Include="../Shared/LiveClaude/Shared.cs" /><Content Include="../Shared/Data/*.json" /></ItemGroup></Project>'
$script:MainTestsProject='<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="TUnit" Version="1.0" /><ProjectReference Include="../../server/Server.csproj" /><Compile Include="../Shared/LiveClaude/Shared.cs" /></ItemGroup></Project>'
$script:ServerProject='<Project Sdk="Microsoft.NET.Sdk"><Import Project="../build/Server.targets" /><ItemGroup><ProjectReference Include="../src/SessionRunner/SessionRunner.csproj" /></ItemGroup></Project>'
function New-FingerprintFixture([string]$name='repository with spaces') {
    $script:root=Join-Path $ResultsDirectory $name
    [void][IO.Directory]::CreateDirectory($script:root)
    [void](Git @('init','-q','-b','main')); [void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
    [void](Git @('config','core.autocrlf','false'))
    Write-Catalog
    Put 'Directory.Build.props' '<Project />'
    Put 'global.json' '{"sdk":{"version":"10.0.100"}}'
    Put 'NuGet.Config' '<configuration />'
    Put 'Directory.Packages.props' '<Project />'
    Put 'build/Server.targets' '<Project />'
    Put 'src/Protocol/Protocol.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
    Put 'src/Protocol/P.cs' "namespace Antiphon.PtyHost.Protocol;`n"
    Put 'src/Agents.Pty/Agents.Pty.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Protocol/Protocol.csproj" /></ItemGroup></Project>'
    Put 'src/Agents.Pty/A.cs' "namespace Antiphon.Agents.Pty;`n"
    Put 'src/SessionRunner/SessionRunner.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
    Put 'src/SessionRunner/S.cs' "namespace Antiphon.SessionRunner;`n"
    Put 'server/Server.csproj' $script:ServerProject
    Put 'server/Server.cs' "namespace Antiphon.Server;`n"
    Put 'tests/Pty.Tests/Pty.Tests.csproj' $script:PtyTestsProject
    Put 'tests/Pty.Tests/T.cs' "namespace Antiphon.Agents.Pty.Tests;`nclass T { const string Contract = `"scripts/lib/live-lane-contract.ps1`"; }`n"
    Put 'tests/Pty.Tests/Other.cs' "namespace Antiphon.Agents.Pty.Tests;`n// other`n"
    Put 'tests/Main.Tests/Main.Tests.csproj' $script:MainTestsProject
    Put 'tests/Main.Tests/M.cs' "namespace Antiphon.Tests;`n"
    Put 'tests/Shared/LiveClaude/Shared.cs' "namespace Antiphon.Tests.Shared;`n"
    Put 'tests/Shared/LiveClaude/Fixtures/witness.mjs' "export const witness = 1;`n"
    Put 'tests/Shared/Data/a.json' '{"a":1}'
    Put 'src/Unrelated/Unrelated.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'
    Put 'src/Unrelated/U.cs' "namespace Antiphon.Unrelated;`n"
    Put 'tests/Other/Other.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="TUnit" Version="1.0" /><ProjectReference Include="../../src/Unrelated/Unrelated.csproj" /></ItemGroup></Project>'
    Put 'tests/Other/O.cs' "namespace Antiphon.Unrelated.Tests;`n"
    Put 'scripts/hooks/hook.ps1' "# hook`n"
    Put 'scripts/lib/live-lane-contract.ps1' "# contract`n"
    Put 'docs/unrelated.md' "unrelated`n"
    Put 'CLAUDE.md' "@AGENTS.md`n"
    Put 'AGENTS.md' "# Agents`n@docs/imported.md`n"
    Put 'docs/imported.md' "imported`n"
    Put '.claude/settings.json' '{}'
    Put '.claude/skills/x/SKILL.md' "skill`n"
    Put '.mcp.json' '{}'
    Write-Manifest
    Write-Declaration ([ordered]@{'fixture-lane'=(New-LaneDeclaration)})
    $script:base=Commit
    return $script:base
}

$expectedRows=@{C1545_StableCanonicalInputs=10;C1545_ReferenceClosureInvalidates=7;C1545_TestAndFixtureInvalidates=6
    C1545_UnknownDependencyRefuses=11;C1545_RuntimeScriptsIncluded=6;C1545_RelevantNamespaceOutsideInputsRefuses=5
    C1545_LoadedInstructionsOutsideInputsRefuse=8}
if (-not $expectedRows.ContainsKey($Case)) { throw "unknown case: $Case" }
$b=New-FingerprintFixture
switch ($Case) {
    'C1545_StableCanonicalInputs' {
        Put 'docs/unrelated.md' "changed`n"; $c=Commit
        # The reader's selection is computed before the fingerprint library is loaded.
        $selectionBefore=(Get-RcProjectSelection $script:root $b $c).digest
        . $script:Library
        $selectionAfter=(Get-RcProjectSelection $script:root $b $c).digest
        $f1=Fp $b; $f2=Fp $b; $j1=Canon $f1
        Check (($j1 -ceq (Canon $f2)) -and ($f1.digest -cmatch '^[0-9a-f]{64}$')) 'deterministic'
        $fc=Fp $c; $jc=Canon $fc
        $leaks=@($b,$c,'runId',$ResultsDirectory,$script:root,(ConvertTo-Json $script:root -Compress).Trim('"'))
        Check ((Same $f1 $fc) -and -not @($leaks | Where-Object { $j1.Contains($_) -or $jc.Contains($_) }).Count) 'sha-free'
        $clone=Join-Path $ResultsDirectory 'second clone'
        [void](Git @('clone','-q',$script:root,$clone) $ResultsDirectory)
        Check (Same (Fp $b $script:Lane $clone) $f1) 'path-free'
        Put 'src/SessionRunner/S.cs' ''; $x=Commit; Drop 'src/SessionRunner/S.cs'; $y=Commit
        $fx=Fp $x; $fy=Fp $y
        Check (($fx.digest -cne $fc.digest) -and ($fy.digest -cne $fc.digest) -and ($fx.digest -cne $fy.digest) -and $fx.digest -and $fy.digest) 'absent-vs-empty'
        $lanes=[ordered]@{'fixture-lane'=(New-LaneDeclaration);'fixture-lane-2'=(New-LaneDeclaration)}
        Write-Declaration $lanes; $d=Commit
        Check (Differs (Fp $d 'fixture-lane') (Fp $d 'fixture-lane-2')) 'domain'
        $laneEntry=New-LaneDeclaration; $laneEntry.minimumRoots+=@('src/Missing/')
        Write-Declaration ([ordered]@{'fixture-lane'=$laneEntry}); $missing=Commit
        Check (Unknown (Fp $missing) 'declared-root-empty:src/Missing/') 'missing-root'
        Write-Declaration ([ordered]@{'fixture-lane'=(New-LaneDeclaration)}); $restored=Commit
        $status=Invoke-Status $restored; $doc=$status.document
        Check (($status.exit -eq 0) -and ($null -ne $doc) -and ($doc.effectiveMode -ceq 'Full') -and ($doc.activation -ceq 'shadow-only') -and
            ($doc.gateCredit -is [bool]) -and -not $doc.gateCredit -and ($doc.executionEvidence -is [bool]) -and -not $doc.executionEvidence -and
            ($doc.state -ceq 'DUE') -and ($selectionBefore -ceq $selectionAfter)) 'shadow-only' $status.stdout
        $fbefore=Fp $restored
        [void](Git @('update-index','--chmod=+x','src/Agents.Pty/A.cs')); $executable=Commit-Index
        Check (Differs (Fp $executable) $fbefore @('repositoryClosure')) 'mode'
        $repository=Split-Path -Parent $PSScriptRoot
        try {
            $head=Git @('rev-parse','HEAD') $repository
            $real1=Fp $head 'live-claude-core-windows' $repository
            $real2=Get-LiveLaneFingerprint -RepositoryRoot $repository -Sha $head -Lane 'live-claude-core-windows' -DocumentCache $script:Documents
            $declaration=(Invoke-TestSelectionGit $repository @('cat-file','blob',"${head}:tests/live-lane-inputs.json")).Stdout | ConvertFrom-Json -AsHashtable
            $real=$declaration.lanes['live-claude-core-windows']
            $paths=Paths $real1; $empty=@()
            foreach ($rootPath in @($real.minimumRoots)+@($real.runtimeRoots)) {
                if (-not @($paths | Where-Object { $_.StartsWith($rootPath,[StringComparison]::Ordinal) }).Count) { $empty+=$rootPath }
            }
            foreach ($pattern in @($real.contractFiles)) {
                $regex='^'+[regex]::Escape($pattern).Replace('\*','[^/]*')+'$'
                if (-not @($paths | Where-Object { $_ -cmatch $regex }).Count) { $empty+=$pattern }
            }
            Check (($real1.digest -cmatch '^[0-9a-f]{64}$') -and ($real1.digest -ceq $real2.digest) -and -not $empty.Count -and
                -not @($real1.unknownReasons | Where-Object { $_.StartsWith('declared-root-empty:',[StringComparison]::Ordinal) }).Count) 'real-head' ("empty=" + ($empty -join ','))
            # Review f5fcf533 follow-up (a): docker/ and the two sibling test trees are declared roots, so
            # their namespaces and scripts are hashed; the two fixture strings stay unknown on purpose.
            $sibling=@($real1.unknownReasons | Where-Object { $_ -cmatch '^(namespace-outside-inputs|undeclared-runtime):(docker/|tests/Antiphon\.SessionRunner\.Tests/|tests/Antiphon\.PtyHost\.Tests/)' })
            Check (($real1.status -ceq 'unknown') -and -not $sibling.Count -and (Has $real1 'undeclared-runtime:file.ps1') -and (Has $real1 'undeclared-runtime:outside.ps1')) 'real-declaration' ("reasons=$(@($real1.unknownReasons).Count) sibling=$($sibling.Count)")
        } catch { Check $false 'real-head' $_.Exception.Message; Check $false 'real-declaration' $_.Exception.Message }
    }
    'C1545_ReferenceClosureInvalidates' {
        . $script:Library
        $fb=Fp $b
        Put 'src/Agents.Pty/A.cs' "namespace Antiphon.Agents.Pty;`n// edit`n"; $c=Commit; $fc=Fp $c
        Check (Differs $fc $fb @('liveRoots','repositoryClosure')) 'direct-edge'
        Put 'src/Protocol/P.cs' "namespace Antiphon.PtyHost.Protocol;`n// edit`n"; $d=Commit; $fd=Fp $d
        $graph=Read-RcProjectGraph $script:root $d
        $selected=@('tests/Pty.Tests/Pty.Tests.csproj','tests/Main.Tests/Main.Tests.csproj')
        $expected=@($graph.projects.Keys | Where-Object { $down=@(Get-RcDownstream $graph.projects @($_)); @($selected | Where-Object { $down -ccontains $_ }).Count })
        Check ((Differs $fd $fc) -and ((Get-TestSelectionSorted $expected) -join ',') -ceq ((Get-TestSelectionSorted @($fd.closureProjects)) -join ',') -and $expected.Count -eq 6) 'transitive-edge' (@($fd.closureProjects) -join ',')
        Put 'tests/Pty.Tests/Pty.Tests.csproj' $script:PtyTestsProject.Replace('<ProjectReference Include="../../src/Agents.Pty/Agents.Pty.csproj" />','')
        $e=Commit; $fe=Fp $e
        $before=Entry $fd 'tests/Pty.Tests/Pty.Tests.csproj'; $after=Entry $fe 'tests/Pty.Tests/Pty.Tests.csproj'
        Check ((Differs $fe $fd) -and $before -and $after -and ($before.blob -cne $after.blob)) 'edge-removed'
        Put 'tests/Pty.Tests/Pty.Tests.csproj' $script:PtyTestsProject; $r=Commit; $fr=Fp $r
        Put 'src/Unrelated/U.cs' "namespace Antiphon.Unrelated;`n// stable`n"; $u=Commit; $fu=Fp $u
        Check (Same $fu $fr) 'unrelated-stable'
        Put 'src/Unrelated/U.cs' "namespace Antiphon.Unrelated;`n// before edge`n"; $u0=Commit; $fu0=Fp $u0
        Put 'tests/Pty.Tests/Pty.Tests.csproj' $script:PtyTestsProject.Replace('</ItemGroup>','<ProjectReference Include="../../src/Unrelated/Unrelated.csproj" /></ItemGroup>')
        $edge=Commit; $fedge=Fp $edge
        Put 'src/Unrelated/U.cs' "namespace Antiphon.Unrelated;`n// after edge`n"; $u1=Commit; $fu1=Fp $u1
        Check ((Same $fu0 $fu) -and (Differs $fedge $fu0) -and (Differs $fu1 $fedge)) 'edge-added'
        Put 'tests/Pty.Tests/Pty.Tests.csproj' $script:PtyTestsProject; $r=Commit
        $digests=@((Fp $r).digest)
        foreach ($file in @('Directory.Build.props','build/Server.targets','global.json','NuGet.Config','Directory.Packages.props')) {
            Put $file ([IO.File]::ReadAllText((Join-Path $script:root $file))+"`n"); $digests+=(Fp (Commit)).digest
        }
        Check ((@($digests | Sort-Object -Unique -CaseSensitive).Count -eq 6) -and -not @($digests | Where-Object { $_ -cnotmatch '^[0-9a-f]{64}$' }).Count) 'shared-build-inputs'
        $fm=Fp (Git @('rev-parse','HEAD'))
        [void](Git @('mv','tests/Pty.Tests/Other.cs','tests/Pty.Tests/Renamed.cs')); $mv=Commit-Index
        Check (Differs (Fp $mv) $fm) 'rename'
    }
    'C1545_TestAndFixtureInvalidates' {
        . $script:Library
        $fb=Fp $b
        Put 'tests/Pty.Tests/Other.cs' "namespace Antiphon.Agents.Pty.Tests;`n// edited`n"; $c=Commit; $fc=Fp $c
        Check (Differs $fc $fb @('testSources')) 'test-source'
        Put 'tests/Shared/LiveClaude/Shared.cs' "namespace Antiphon.Tests.Shared;`n// edited`n"; $d=Commit; $fd=Fp $d
        Check (Differs $fd $fc) 'linked-shared'
        Put 'tests/Shared/Data/a.json' '{"a":2}'; $e=Commit; $fe=Fp $e
        Put 'tests/Shared/Data/b.json' '{"b":1}'; $f=Commit; $ff=Fp $f
        Check ((Differs $fe $fd) -and (Differs $ff $fe)) 'content-glob'
        Drop 'tests/Main.Tests/M.cs'; $g=Commit; $fg=Fp $g
        Check (Differs $fg $ff) 'deletion'
        Put 'tests/Other/O.cs' "namespace Antiphon.Unrelated.Tests;`n// edited`n"; $h=Commit; $fh=Fp $h
        Check (Same $fh $fg) 'other-test-project'
        [void](Git @('mv','tests/Pty.Tests/Pty.Tests.csproj','tests/Pty.Tests/PtyTests.csproj')); $i=Commit-Index
        Check (Unknown (Fp $i) 'core-project-missing:Pty.Tests') 'core-project-map'
    }
    'C1545_UnknownDependencyRefuses' {
        . $script:Library
        function Refused([string]$sha, [scriptblock]$reason) {
            $state=Get-LiveLaneStatus -RepositoryRoot $script:root -Lane $script:Lane -CandidateSha $sha -DocumentCache $script:Documents
            $fp=$state.fingerprint
            return ($null -ne $fp) -and ($fp.status -ceq 'unknown') -and ($state.state -ceq 'DUE') -and (@($state.reasons) -ccontains 'unknown-input') -and
                @($fp.unknownReasons | Where-Object { $null -ne $_ } | Where-Object $reason).Count -gt 0 -and ($state.gateCredit -is [bool]) -and -not $state.gateCredit
        }
        function Complete([string]$sha, [scriptblock]$observation) {
            $fp=Fp $sha
            return ($fp.status -ceq 'complete') -and -not @($fp.unknownReasons).Count -and @($fp.observations | Where-Object { $null -ne $_ } | Where-Object $observation).Count -gt 0
        }
        Put 'server/Server.csproj' $script:ServerProject.Replace('</Project>','<Import Project="$(Root)/x.targets" /></Project>'); $s=Commit
        Check (Refused $s { $_.StartsWith('graph:server/Server.csproj:unresolved-input',[StringComparison]::Ordinal) }) 'dynamic-import'
        Reset-Fixture
        Put 'tests/Main.Tests/Main.Tests.csproj' $script:MainTestsProject.Replace('</ItemGroup>','<Compile Include="../Shared/Gone.cs" /></ItemGroup>'); $s=Commit
        Check (Refused $s { $_.Contains('missing-linked-input') }) 'missing-linked'
        Reset-Fixture
        Put 'src/Protocol/Protocol.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Agents.Pty/Agents.Pty.csproj" /></ItemGroup></Project>'; $s=Commit
        Check (Refused $s { $_.StartsWith('project-cycle:',[StringComparison]::Ordinal) }) 'cycle'
        Reset-Fixture
        Put 'tests/Main.Tests/Main.Tests.csproj' $script:MainTestsProject.Replace('</ItemGroup>','<Compile Include="../../../../x.cs" /></ItemGroup>'); $s=Commit
        Check (Refused $s { $_.Contains('outside-input') }) 'outside-root'
        Reset-Fixture
        $target=Join-Path $ResultsDirectory 'link-target.txt'; [IO.File]::WriteAllText($target,'A.cs')
        $blob=Git @('hash-object','-w',$target)
        [void](Git @('update-index','--add','--cacheinfo',"120000,$blob,src/Agents.Pty/link.cs")); $s=Commit-Index
        Check (Refused $s { $_ -ceq 'symlink:src/Agents.Pty/link.cs' }) 'symlink'
        Reset-Fixture
        [void](Git @('update-index','--add','--cacheinfo',"160000,$b,src/Agents.Pty/sub")); $s=Commit-Index
        Check (Refused $s { $_ -ceq 'gitlink:src/Agents.Pty/sub' }) 'submodule'
        Reset-Fixture
        Put 'tests/Copy/Pty.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk" />'; $s=Commit
        Check (Refused $s { $_ -ceq 'core-project-ambiguous:Pty.Tests' }) 'ambiguous-project'
        Reset-Fixture
        Drop 'tests/live-lanes.json'; $s=Commit
        $status=Invoke-Status $s
        Check (($status.exit -eq 2) -and ($status.document.error -ceq 'manifest-missing')) 'missing-manifest' $status.stdout
        Reset-Fixture
        $laneEntry=New-LaneDeclaration; $laneEntry.extra='x'; Write-Declaration ([ordered]@{'fixture-lane'=$laneEntry}); $s1=Commit
        Reset-Fixture
        Write-Declaration ([ordered]@{'fixture-lane'=(New-LaneDeclaration)}) 'antiphon-live-lane-inputs-v2'; $s2=Commit
        $status1=Invoke-Status $s1; $status2=Invoke-Status $s2
        Check (($status1.exit -eq 2) -and ($status1.document.error -ceq 'declaration-invalid') -and ($status2.exit -eq 2) -and ($status2.document.error -ceq 'declaration-invalid')) 'declaration-schema' ($status1.stdout+$status2.stdout)
        Reset-Fixture
        Write-Catalog @('tests/Pty.Tests/Pty.Tests.csproj','tests/Main.Tests/Main.Tests.csproj'); $s=Commit
        Check (Complete $s { $_ -ceq 'unbatched-test-project:tests/Other/Other.csproj' }) 'catalog-only'
        Reset-Fixture
        Put 'src/Unrelated/Unrelated.csproj' '<Project Sdk="Microsoft.NET.Sdk"><Import Project="$(Root)/x.targets" /></Project>'; $s=Commit
        Check (Complete $s { $_.StartsWith('graph:src/Unrelated/Unrelated.csproj:',[StringComparison]::Ordinal) }) 'fault-outside-closure'
    }
    'C1545_RuntimeScriptsIncluded' {
        . $script:Library
        $fb=Fp $b
        Put 'scripts/hooks/hook.ps1' "# hook edited`n"; $c=Commit; $fc=Fp $c
        Check (Differs $fc $fb @('runtimeInputs')) 'hook'
        Put 'scripts/lib/live-lane-contract.ps1' "# contract edited`n"; $d=Commit; $fd=Fp $d
        Check (Differs $fd $fc @('laneContract')) 'contract-scripts'
        Write-Manifest "`n"; $e=Commit; $fe=Fp $e
        Write-Declaration ([ordered]@{'fixture-lane'=(New-LaneDeclaration)}) 'antiphon-live-lane-inputs-v1' "`n"; $f=Commit; $ff=Fp $f
        Check ((Differs $fe $fd @('laneContract')) -and (Differs $ff $fe @('laneContract'))) 'manifest-and-declaration'
        Put 'tests/Shared/LiveClaude/Fixtures/witness.mjs' "export const witness = 2;`n"; $g=Commit; $fg=Fp $g
        Check (Differs $fg $ff) 'fixture-assets'
        $current=Git @('rev-parse','HEAD')
        Put 'tests/Pty.Tests/Uses.cs' "class Uses { const string Tool = `"scripts/other/tool.ps1`"; void M() => ScriptHarness.RunHarnessCaseAsync(`"tool2.ps1`", `"C1`", `"C1_X`", 1); }`n"
        $h=Commit; $fh=Fp $h
        [void](Git @('checkout','-q','-f','--detach',$current))
        Put 'tests/Pty.Tests/Uses.cs' "class Uses { const string Tool = `"scripts/lib/tool.ps1`"; void M() => ScriptHarness.RunHarnessCaseAsync(`"lib/tool2.ps1`", `"C1`", `"C1_X`", 1); }`n"
        $control=Commit; $fcontrol=Fp $control
        Check ((Unknown $fh 'undeclared-runtime:scripts/other/tool.ps1') -and (Has $fh 'undeclared-runtime:scripts/tool2.ps1') -and
            ($fcontrol.status -ceq 'complete') -and -not @($fcontrol.unknownReasons).Count) 'undeclared-runtime-literal' ((@($fh.unknownReasons)+@($fcontrol.unknownReasons)) -join ',')
        Put 'docs/unrelated.md' "unrelated edited`n"; $i=Commit
        Check (Same (Fp $i) $fcontrol) 'unrelated-file'
    }
    'C1545_RelevantNamespaceOutsideInputsRefuses' {
        . $script:Library
        $fb=Fp $b
        Put 'tools/Stray/Stray.cs' "namespace Antiphon.SessionRunner.Stray;`n"; $s=Commit
        Check (Unknown (Fp $s) 'namespace-outside-inputs:tools/Stray/Stray.cs') 'file-scoped'
        Reset-Fixture
        Put 'tools/Stray/Stray.cs' "namespace Antiphon.Agents.Pty.Extra`n{`n}`n"; $s=Commit
        Check (Unknown (Fp $s) 'namespace-outside-inputs:tools/Stray/Stray.cs') 'block-scoped'
        Reset-Fixture
        Put 'tools/T.cs' "namespace Antiphon.Tests.Helpers;`n"; $s=Commit
        Check (Unknown (Fp $s) 'namespace-outside-inputs:tools/T.cs') 'test-namespace'
        Reset-Fixture
        Put 'tools/Stray/Stray.cs' "namespace Antiphon.Tools;`n"; Put 'tools/Stray/Other.cs' "namespace Antiphon.ServerTools`n{`n}`n"; $s=Commit; $fs=Fp $s
        Check (($fs.status -ceq 'complete') -and -not @($fs.unknownReasons).Count -and ($fs.digest -cmatch '^[0-9a-f]{64}$')) 'foreign-namespace' (@($fs.unknownReasons) -join ',')
        Reset-Fixture
        Put 'src/Agents.Pty/New.cs' "namespace Antiphon.Agents.Pty;`n"; $s=Commit; $fs=Fp $s
        Check (($fs.status -ceq 'complete') -and -not @($fs.unknownReasons).Count -and (Differs $fs $fb)) 'inside-inputs'
    }
    'C1545_LoadedInstructionsOutsideInputsRefuse' {
        . $script:Library
        $fb=Fp $b
        Put 'CLAUDE.md' "@AGENTS.md`nedited`n"; $c=Commit; $fc=Fp $c
        Put 'AGENTS.md' "# Agents edited`n@docs/imported.md`n"; $d=Commit; $fd=Fp $d
        Check ((Differs $fc $fb @('instructions')) -and (Differs $fd $fc @('instructions'))) 'root-instructions'
        Put 'docs/imported.md' "imported edited`n"; $e=Commit; $fe=Fp $e
        Check (Differs $fe $fd @('instructions')) 'import-chain'
        Put 'sub/dir/CLAUDE.md' "nested`n"; $f=Commit; $ff=Fp $f
        Put '.claude/skills/x/SKILL.md' "skill edited`n"; $g=Commit; $fg=Fp $g
        Put '.mcp.json' '{"servers":{}}'; $h=Commit; $fh=Fp $h
        Check ((Differs $ff $fe) -and (Differs $fg $ff) -and (Differs $fh $fg)) 'nested-and-claude-tree'
        Reset-Fixture
        Put 'AGENTS.md' "# Agents`n@docs/imported.md`n@../outside.md`n"; $s=Commit
        Check (Unknown (Fp $s) 'instruction-import-outside:AGENTS.md') 'import-escape'
        Reset-Fixture
        Put 'AGENTS.md' "# Agents`n@docs/imported.md`n@~/.claude/notes.md`n"; $s=Commit
        Check (Unknown (Fp $s) 'instruction-import-user:AGENTS.md') 'import-home'
        Reset-Fixture
        Put 'AGENTS.md' "# Agents`n@docs/imported.md`n@docs/gone.md`n"; $s=Commit
        Check (Unknown (Fp $s) 'instruction-import-missing:docs/gone.md') 'import-missing'
        Reset-Fixture
        Put 'AGENTS.md' "# Agents`n@docs/imported.md`n@docs/a.md`n"; Put 'docs/a.md' "@b.md`n"; Put 'docs/b.md' "@a.md`n"; $s=Commit
        $watch=[Diagnostics.Stopwatch]::StartNew(); $fs=Fp $s; $watch.Stop()
        $paths=Paths $fs
        Check (($fs.status -ceq 'complete') -and ($paths -ccontains 'docs/a.md') -and ($paths -ccontains 'docs/b.md') -and $watch.Elapsed.TotalSeconds -lt 30) 'import-cycle' (@($fs.unknownReasons) -join ',')
        Reset-Fixture
        $laneEntry=New-LaneDeclaration; $laneEntry.instructionPatterns=@('CLAUDE.md','CLAUDE.local.md','AGENTS.md','.claude/**')
        Write-Declaration ([ordered]@{'fixture-lane'=$laneEntry}); $s=Commit
        Check (Unknown (Fp $s) 'instruction-outside-inputs:.mcp.json') 'undeclared-instruction'
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $expectedRows[$Case]
