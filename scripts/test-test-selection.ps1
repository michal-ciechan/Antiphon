#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory, [string]$FilterHost)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/nightly-coverage.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
. "$PSScriptRoot/lib/release-authority.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$repoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$script:C1313GitFixtures = [Collections.Generic.List[string]]::new()

function Test-C1313_ProjectAndCatalogUniverse {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $census = Test-NightlyPolicyProjectUniverse -RepoRoot $repoRoot -PolicyObject $policy.Object
    Assert-C487 -Cond $census.Ok -Name 'C1313 project-universe' -Detail ($census | ConvertTo-Json -Compress)
    $roles = $policy.Object.projectDispositions
    $probePaths = @('scripts/fixtures/nightly/c1375-probe/Probe.csproj', 'tests/Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj')
    $probes = @($roles | Where-Object { $probePaths -ccontains $_.project })
    $ownedProbes = @($probes | Where-Object {
        $_.role -ceq 'child' -and $_.reason -and @($_.consumers).Count -eq 1 -and
        $_.consumers[0] -ceq 'tests/Antiphon.Tests/Antiphon.Tests.csproj'
    })
    $observability = 'tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj'
    $hasObservability = Test-Path (Join-Path $repoRoot $observability)
    $ownedAssembly = @($roles | Where-Object { $_.project -ceq $observability -and $_.role -ceq 'primary' -and $_.reason -and @($_.consumers).Count -eq 0 })
    $assemblyOwned = -not $hasObservability -or ($ownedAssembly.Count -eq 1 -and $policy.Object.profiles.rc.requiredSuites -ccontains 'observability' -and $policy.Object.profiles.nightly.requiredSuites -ccontains 'observability')
    $authority=ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes("$repoRoot/tests/test-execution-policy.json"))
    Assert-C487 -Cond (@($roles | Where-Object role -eq primary).Count -eq (7 + [int]$hasObservability) -and $assemblyOwned -and $ownedProbes.Count -eq 2 -and $authority.RequiredSuites.Count -eq (9 + [int]$hasObservability) -and $authority.RequiredSuites -ccontains 'markdown-pdf' -and $policy.Object.profiles.nightly.requiredSuites.Count -eq (7 + [int]$hasObservability)) -Name 'C1313 primary-and-probe-ownership'
    $catalog = Read-TestSelectionCatalog "$repoRoot/tests/test-selection-policy.json" $policy.Object.selectionCatalog.hash
    Assert-C487 -Cond (@($catalog.sets | Where-Object id -eq core).members.Count -eq 7) -Name 'C1313 seven-method-core'
    $root=Join-Path $ResultsDirectory 'universe';[void](New-Item -ItemType Directory $root)
    foreach($path in $census.Disk){$file=Join-Path $root $path;[void](New-Item -ItemType Directory (Split-Path $file) -Force);[IO.File]::WriteAllText($file,'<Project><IsTestProject>true</IsTestProject></Project>')}
    $extra=Join-Path $root 'tests/New.Fixture/New.Fixture.csproj';[void](New-Item -ItemType Directory (Split-Path $extra));[IO.File]::WriteAllText($extra,'<Project><IsTestProject>true</IsTestProject></Project>')
    $bad=Test-NightlyPolicyProjectUniverse $root $policy.Object
    Assert-C487 -Cond (-not $bad.Ok -and $bad.Missing -contains 'tests/New.Fixture/New.Fixture.csproj') -Name 'C1313 unclassified-project'
    $catalog.catalogHash='0'*64;$msg='';try{[void](Assert-TestSelectionCatalog $catalog)}catch{$msg=$_.Exception.Message}
    Assert-C487 -Cond ($msg -eq 'catalog-hash-stale') -Name 'C1313 stale-catalog-refused'
}

function Test-C1313_LiveEligibility {
    $policy=Read-NightlyExecutionPolicy $repoRoot
    $catalog=Read-TestSelectionCatalog "$repoRoot/tests/test-selection-policy.json" $policy.Object.selectionCatalog.hash
    $classes=@('ClaudeHeadedLongTests','GrokMidTurnInterruptCanaryTests','GrokSizeCaptureTests','HerdrPaneDisposalGuardedLiveTests')
    $exclusions=@($policy.Object.profiles.rc.exclusions | Where-Object {$classes -ccontains $_.class})
    Assert-C487 -Cond ($exclusions.Count -eq 4 -and @($exclusions|Where-Object {$_.owner -ne 'CARD-1313' -or -not $_.reason}).Count -eq 0) -Name 'C1313 live-obligations-visible'
    $live=@($catalog.sets|Where-Object {$_.id -in @('provider.claude.tui-live','provider.grok.tui-live','desktop.herdr')})
    Assert-C487 -Cond ($live.Count -eq 3 -and @($live|Where-Object eligibility -ne manual).Count -eq 0 -and @($live.members.class) -contains 'Antiphon.Tests.Agents.ClaudeEffortPromptCanaryTests') -Name 'C1313 live-gates-owned'
    $linux=@($catalog.sets|Where-Object id -eq 'pdf.linux')[0]
    $missingPlatformExclusions=@($linux.members|Where-Object {
        $member=$_;$simple=($member.class -split '\.')[-1]
        @($policy.Object.profiles.rc.exclusions|Where-Object {$_.suite -ceq 'markdown-pdf' -and $_.class -ceq $simple -and $_.methods -ccontains $member.method}).Count -ne 1
    })
    Assert-C487 -Cond ($linux.platforms -ceq 'linux' -and $linux.members.Count -eq 3 -and $missingPlatformExclusions.Count -eq 0) -Name 'C1313 other-os-obligation'
    $core=@($catalog.sets|Where-Object id -eq core)[0];$core.resources.api='unknown';$catalog.catalogHash=Get-TestSelectionCatalogHash $catalog
    $msg='';try{[void](Assert-TestSelectionCatalog $catalog)}catch{$msg=$_.Exception.Message}
    Assert-C487 -Cond ($msg -eq 'unknown-resource') -Name 'C1313 unknown-resource-ineligible'
}

function Test-C1313_MixedDeliveryInventory {
    $policy=Read-NightlyExecutionPolicy $repoRoot
    $exclusions=@(Get-NightlyProfileExclusions $policy.Object 'rc' 'agents-pty')
    $fake=[pscustomobject]@{Uid='fake';ClassName='Antiphon.Agents.Pty.Tests.ClaudeVerifiedDeliveryTests';Type='ClaudeVerifiedDeliveryTests';Method='Fake_evidence_appears_then_submit_completes_a_turn';Categories=@()}
    $real=[pscustomobject]@{Uid='real';ClassName=$fake.ClassName;Type=$fake.Type;Method='Real_evidence_appears_then_submit_completes_a_turn';Categories=@('OptIn','Headed')}
    Assert-C487 -Cond ((Get-NightlyDiscoveryDisposition $fake $exclusions -ProfileAware).Disposition -eq 'required' -and (Get-NightlyDiscoveryDisposition $real $exclusions -ProfileAware).Disposition -eq 'excluded') -Name 'C1313 fake-required-real-excluded'
    $catalog=Read-TestSelectionCatalog "$repoRoot/tests/test-selection-policy.json" $policy.Object.selectionCatalog.hash
    $oracles=@($catalog.sets|Where-Object {$_.id -in @('provider.codex.vendor-offline','provider.grok.vendor-offline')})
    Assert-C487 -Cond ($oracles.Count -eq 2 -and @($oracles|Where-Object {$_.resources.api -ne 'none' -or $_.resources.vendorExecutable -eq 'none' -or $_.eligibility -ne 'manual'}).Count -eq 0) -Name 'C1313 vendor-oracles-are-not-fakes'
}

function Invoke-C1313Git($Root,$Arguments){return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim()}
function Write-C1313File($Root,$Path,$Text){$file=Join-Path $Root $Path;[void](New-Item -ItemType Directory (Split-Path $file) -Force);[IO.File]::WriteAllText($file,$Text)}
function New-C1313GitFixture {
    $root=Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'));[void](New-Item -ItemType Directory $root)
    $script:C1313GitFixtures.Add($root)
    [void](Invoke-C1313Git $root @('init','--quiet'))
    [void](Invoke-C1313Git $root @('config','user.name','C1313 fixture'));[void](Invoke-C1313Git $root @('config','user.email','c1313@example.invalid'))
    [void](Invoke-C1313Git $root @('config','core.autocrlf','false'))
    foreach($file in @('tests/test-execution-policy.json','tests/test-selection-policy.json','scripts/select-test-rosters.ps1','scripts/lib/test-selection.ps1')){Write-C1313File $root $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))}
    $catalog=Get-Content "$root/tests/test-selection-policy.json" -Raw|ConvertFrom-Json -AsHashtable
    foreach($path in @(Get-TestSelectionSorted @($catalog.sets.sourceInputs + $catalog.sets.members.source))){if($path){Write-C1313File $root $path 'fixture source'}}
    [void](Invoke-C1313Git $root @('add','--all'));[void](Invoke-C1313Git $root @('commit','--quiet','-m','baseline'))
    return @{Root=$root;B=(Invoke-C1313Git $root @('rev-parse','HEAD'));Catalog=$catalog}
}
function New-C1313Candidate($Fx,[string[]]$Paths,[string]$Content='changed'){
    [void](Invoke-C1313Git $Fx.Root @('read-tree',$Fx.B))
    foreach($path in $Paths){Write-C1313File $Fx.Root $path $Content;[void](Invoke-C1313Git $Fx.Root @('add','--',$path))}
    $tree=Invoke-C1313Git $Fx.Root @('write-tree')
    return Invoke-C1313Git $Fx.Root @('commit-tree',$tree,'-p',$Fx.B,'-m','candidate')
}
function New-C1313PolicyCandidate($Fx,$Catalog){
    $Catalog.catalogHash=Get-TestSelectionCatalogHash $Catalog
    $p=Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw|ConvertFrom-Json -AsHashtable
    $p.selectionCatalog.hash=$Catalog.catalogHash;$p.policyHash=Get-NightlyPolicyHash $p
    [void](Invoke-C1313Git $Fx.Root @('read-tree',$Fx.B))
    Write-C1313File $Fx.Root 'tests/test-selection-policy.json' ($Catalog|ConvertTo-Json -Depth 30)
    Write-C1313File $Fx.Root 'tests/test-execution-policy.json' ($p|ConvertTo-Json -Depth 30)
    [void](Invoke-C1313Git $Fx.Root @('add','--','tests/test-selection-policy.json','tests/test-execution-policy.json'))
    $tree=Invoke-C1313Git $Fx.Root @('write-tree');return Invoke-C1313Git $Fx.Root @('commit-tree',$tree,'-p',$Fx.B,'-m','policy')
}
function Invoke-C1313Selection($Fx,$A,$B=$Fx.B,[string]$ScriptRoot){
    if([string]::IsNullOrWhiteSpace($ScriptRoot)){$ScriptRoot=$PSScriptRoot}
    . "$PSScriptRoot/lib/checkpoint-process-runner.ps1"
    $start=[Diagnostics.ProcessStartInfo]::new('pwsh');$start.WorkingDirectory=$repoRoot
    $start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false,$true)
    $selector=Join-Path $ScriptRoot 'select-test-rosters.ps1'
    foreach($a in @('-NoProfile','-File',$selector,'-RepositoryRoot',$Fx.Root,'-BaselineSha',$B,'-CandidateSha',$A,'-Json')){$start.ArgumentList.Add($a)}
    $run=[Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start,60000,$false)
    $doc=$null;if($run.Exit -eq 0){$doc=$run.Stdout|ConvertFrom-Json -AsHashtable}
    return @{Exit=$run.Exit;Document=$doc;Error=$run.Stderr}
}
function Get-C1313Selection($Fx,$A,$B=$Fx.B){$r=Invoke-C1313Selection $Fx $A $B;if($r.Exit -ne 0){throw "control did not reach selector: $($r.Error)"};return $r.Document}
function Same-C1313Set($Actual,$Expected){return ((@(Get-TestSelectionSorted $Actual) -join "`n") -ceq (@(Get-TestSelectionSorted $Expected) -join "`n"))}

function Test-C1313_InputRefusal {
    $fx=New-C1313GitFixture
    [void](Get-C1313Selection $fx $fx.B)
    $blob=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B):tests/test-selection-policy.json")
    $bad=@();foreach($b in @('HEAD',$fx.B.Substring(0,8),('0'*40),$blob,'--help')){$r=Invoke-C1313Selection $fx $fx.B $b;if($r.Exit -ne 2 -or $r.Document){$bad+=$b}}
    Assert-C487 ($bad.Count -eq 0) 'C1313 invalid-intent-refused' ($bad -join ',')
    $fx.B=New-C1313Candidate $fx @('notes/nested/file.txt')
    [void](Invoke-C1313Git $fx.Root @('read-tree',$fx.B));[void](Invoke-C1313Git $fx.Root @('update-index','--force-remove','--','notes/nested/file.txt'))
    $tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','remove subtree')
    [void](Get-C1313Selection $fx $a)
    $tree=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B):notes")
    # Git loose objects are read-only; this object belongs only to our disposable fixture.
    Remove-Item -LiteralPath (Join-Path $fx.Root ('.git/objects/'+$tree.Substring(0,2)+'/'+$tree.Substring(2))) -Force
    $r=Invoke-C1313Selection $fx $a
    Assert-C487 ($r.Exit -eq 2 -and -not $r.Document -and $r.Error.Contains('git-read-failed:diff')) 'C1313 git-read-refused' $r.Error
}
function Test-C1313_CoreAndUnknownFallback {
    $fx=New-C1313GitFixture;$empty=Get-C1313Selection $fx $fx.B
    $expected=@('C1313_ProjectAndCatalogUniverse','C1313_CoreAndUnknownFallback','C1044_DiscoveryReconciliation','D13_ExecutionGuards','C599_ExpandedCoverage','Caps_concurrent_process_spawning_tests_at_one','Process_spawning_classes_carry_the_limiter')
    Assert-C487 ($empty.mode -ceq 'narrow' -and (Same-C1313Set $empty.selectedSets.id @('core')) -and (Same-C1313Set $empty.selectedSets.members.method $expected) -and -not $empty.executionEvidence) 'C1313 core-seven'
    $a=New-C1313Candidate $fx @('notes/unmapped.txt');$full=Get-C1313Selection $fx $a
    Assert-C487 ($full.mode -ceq 'full' -and (Same-C1313Set $full.selectedSets.id $fx.Catalog.sets.id) -and $full.unselectedSets.Count -eq 0 -and $full.unresolvedObligations.Count -gt 0 -and $full.fallbackReasons -ccontains 'unknown-path:notes/unmapped.txt') 'C1313 unknown-full'
}
function Test-C1313_PinnedPolicy {
    $fx=New-C1313GitFixture;$a=New-C1313Candidate $fx @('client/src/App.tsx')
    Write-C1313File $fx.Root 'tests/test-selection-policy.json' 'not json';Write-C1313File $fx.Root 'tests/test-execution-policy.json' 'not json'
    $good=Get-C1313Selection $fx $a
    Assert-C487 ($good.mode -ceq 'narrow' -and $good.selectedSets.id -ccontains 'client' -and $good.catalogHash -ceq $fx.Catalog.catalogHash) 'C1313 candidate-blob'
    $p=Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw|ConvertFrom-Json -AsHashtable;$p.policyHash='0'*64
    $a=New-C1313Candidate $fx @('tests/test-execution-policy.json') ($p|ConvertTo-Json -Depth 30)
    $bad=Invoke-C1313Selection $fx $a
    Assert-C487 ($bad.Exit -eq 2 -and $bad.Error.Contains('policy-hash-stale')) 'C1313 stale-policy-refused' $bad.Error
}
function Test-C1313_PathIdentity_delete {
    $fx=New-C1313GitFixture;$path='tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs'
    $c=$fx.Catalog
    $c.rules=@($c.rules|Where-Object {$_.paths -cnotcontains $path})
    $c.rules+=@{id='P-TEST-NEW-OWNER';paths=@($path);sets=@('main.remainder');full=$false}
    [void](New-C1313PolicyCandidate $fx $c)
    [void](Invoke-C1313Git $fx.Root @('update-index','--force-remove','--',$path))
    $tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','delete')
    $r=Get-C1313Selection $fx $a
    $old=@($r.changedPaths|Where-Object {$_.path -ceq $path -and $_.role -ceq 'old'})
    Assert-C487 ($old.Count -eq 1 -and $old.sets -ccontains 'provider.codex.offline' -and $r.selectedSets.id -ccontains 'provider.codex.offline' -and $r.removedMembers.member.class -ccontains 'Antiphon.Tests.Agents.CodexResponseAnalyzerTests' -and $r.selectedSets.members.class -cnotcontains 'Antiphon.Tests.Agents.CodexResponseAnalyzerTests') 'C1313 old-new-union'
}
function Test-C1313_PathIdentity_case_rename {
    $fx=New-C1313GitFixture;$old='tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs';$new='tests/Antiphon.Tests/Agents/codexResponseAnalyzerTests.cs'
    $blob=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B):$old")
    [void](Invoke-C1313Git $fx.Root @('read-tree',$fx.B));[void](Invoke-C1313Git $fx.Root @('update-index','--force-remove','--',$old));[void](Invoke-C1313Git $fx.Root @('update-index','--add','--cacheinfo',"100644,$blob,$new"))
    $tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','case')
    $r=Get-C1313Selection $fx $a
    Assert-C487 ((Same-C1313Set $r.changedPaths.path @($old,$new)) -and $r.mode -ceq 'full') 'C1313 ordinal-paths'
}
function Test-C1313_PathIdentity_nul_paths {
    $fx=New-C1313GitFixture;$paths=@("notes/white space.txt","notes/line`nbreak.txt",'notes/uni-λ.txt')
    # Index objects make newline and case identity independent of Windows filename restrictions.
    $blob=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B):tests/test-selection-policy.json")
    [void](Invoke-C1313Git $fx.Root @('read-tree',$fx.B))
    # Git for Windows rejects control characters even for index-only entries by
    # default. Disable that checkout protection for these fixture index writes
    # only; the paths are never materialized in the Windows working tree.
    foreach($path in $paths){[void](Invoke-C1313Git $fx.Root @('-c','core.protectNTFS=false','update-index','--add','--cacheinfo',"100644,$blob,$path"))}
    [void](Invoke-C1313Git $fx.Root @('update-index','--add','--cacheinfo',"160000,$($fx.B),notes/gitlink"));$paths+='notes/gitlink'
    [void](Invoke-C1313Git $fx.Root @('update-index','--add','--cacheinfo',"120000,$blob,notes/link"));$paths+='notes/link'
    $tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','paths')
    $r=Get-C1313Selection $fx $a
    Assert-C487 ((Same-C1313Set $r.changedPaths.path $paths) -and $r.mode -ceq 'full') 'C1313 ordinal-paths' `
        (@{expected=$paths;actual=$r.changedPaths.path;mode=$r.mode}|ConvertTo-Json -Depth 5 -Compress)
}
function Test-C1313_AuthorityChanges {
    $fx=New-C1313GitFixture;$bad=@()
    foreach($path in @('scripts/lib/release-authority.ps1','Directory.Build.props')){$a=New-C1313Candidate $fx @($path);$r=Get-C1313Selection $fx $a;if($r.mode -cne 'full' -or $r.fallbackReasons -cnotcontains 'selection-authority-changed'){$bad+=$path}}
    $a=New-C1313PolicyCandidate $fx $fx.Catalog;$r=Get-C1313Selection $fx $a
    if($r.fallbackReasons -cnotcontains 'selection-authority-changed'){$bad+='catalog'}
    Assert-C487 ($bad.Count -eq 0) 'C1313 authority-full' ($bad -join ',')
    $tree=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B)^{tree}");$other=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-m','unrelated')
    $r=Get-C1313Selection $fx $fx.B $other
    Assert-C487 ($r.mode -ceq 'full' -and $r.fallbackReasons -ccontains 'baseline-not-ancestor') 'C1313 nonancestor-full'
}
function Test-C1313_DeterministicReasons {
    $fx=New-C1313GitFixture;$bad=@()
    $cases=@(
        @('server/Program.cs','P-SERVER','full','main.remainder'),@('serverish/Program.cs','P-UNKNOWN','full','core'),
        @('client/src/App.tsx','P-CLIENT','narrow','e2e.browser'),@('clientish/src/App.tsx','P-UNKNOWN','full','core'),
        @('src/Antiphon.Messaging.Service/Program.cs','P-MSG','narrow','main.outbound-broker'),@('src/Antiphon.MessagingExtra/Program.cs','P-UNKNOWN','full','core'),
        @('tools/Antiphon.MarkdownPdf/MarkdownPdfRenderer.cs','P-PDF','narrow','pdf.browser-linux'),@('tools/Antiphon.MarkdownPdfExtra/Renderer.cs','P-UNKNOWN','full','core'),
        @('scripts/deploy-server2.ps1','P-DOCKER','narrow','docker.operations'),@('scripts/deploy-server20-unknown.ps1','P-UNKNOWN','full','core'),
        @('notes/release-authority.ps1','P-UNKNOWN','full','core'),@('notes/Directory.Build.props.txt','P-UNKNOWN','full','core'))
    foreach($case in $cases){$a=New-C1313Candidate $fx @($case[0]);$r=Get-C1313Selection $fx $a;if($r.mode -cne $case[2] -or $r.changedPaths.ruleIds -cnotcontains $case[1] -or $r.selectedSets.id -cnotcontains $case[3]){$bad+=$case[0]}}
    $a=New-C1313Candidate $fx @('client/src/App.tsx');$one=Get-C1313Selection $fx $a;$two=Get-C1313Selection $fx $a
    $browser=@($one.selectedSets|Where-Object id -eq 'e2e.browser')[0]
    $names=@('AgentE2ETests','BoardE2ETests','DirectoryAutocompleteE2ETests','DispatchConcurrencySettingsE2ETests','OrchestratorE2ETests','PipelineGlanceE2ETests','ProjectDeleteE2ETests','SmokeE2ETests','WorkflowDeleteTests','WorkflowOutputTests')|ForEach-Object{"Antiphon.E2E.$_"}
    if($one.digest -cne $two.digest -or -not (Same-C1313Set $browser.members.class $names)){$bad+='digest/browser-roster'}
    Assert-C487 ($bad.Count -eq 0) 'C1313 deterministic-reasons' ($bad -join ',')
}
function Test-C1313_ProviderAndShared {
    $fx=New-C1313GitFixture;$bad=@();$sharedBad=@()
    foreach($provider in @('claude','codex','grok')){
        $title=(Get-Culture).TextInfo.ToTitleCase($provider);$a=New-C1313Candidate $fx @("src/Antiphon.Agents.Pty/${title}Detectors.cs");$r=Get-C1313Selection $fx $a
        $expected=@('core',"provider.$provider.offline","provider.$provider.fake-native","provider.$provider.tui-stub","provider.$provider.tui-live","provider.$provider.vendor-offline","provider.$provider.server-runner",'main.remainder','e2e.remainder')
        if(-not (Same-C1313Set $r.selectedSets.id $expected) -or $r.changedPaths.ruleIds -cnotcontains "P-$($provider.ToUpperInvariant())"){$bad+=$provider}
        $a=New-C1313Candidate $fx @("src/Antiphon.Agents.Pty/${title}NewShared.cs");$r=Get-C1313Selection $fx $a
        foreach($p in @('claude','codex','grok')){if($r.selectedSets.id -cnotcontains "provider.$p.tui-live"){$sharedBad+=$p}}
    }
    Assert-C487 ($bad.Count -eq 0) 'C1313 provider-exact' ($bad -join ',')
    $a=New-C1313Candidate $fx @('src/Antiphon.Agents.Pty/PtyAgentRunner.cs');$r=Get-C1313Selection $fx $a
    foreach($p in @('claude','codex','grok')){if($r.selectedSets.id -cnotcontains "provider.$p.offline"){$sharedBad+=$p}}
    $a=New-C1313Candidate $fx @('src/Antiphon.Agents.PtyExtra/Unknown.cs');$r=Get-C1313Selection $fx $a
    Assert-C487 ($sharedBad.Count -eq 0 -and $r.mode -ceq 'full') 'C1313 shared-all-providers' ($sharedBad -join ',')
    $c=Get-Content "$repoRoot/tests/test-selection-policy.json" -Raw|ConvertFrom-Json -AsHashtable
    $c.rules+=@{id='P-OVERLAP';paths=@('src/Antiphon.Agents.Pty/ClaudeDetectors.cs');sets=@('core');full=$true}
    [void](New-C1313PolicyCandidate $fx $c)
    Write-C1313File $fx.Root 'src/Antiphon.Agents.Pty/ClaudeDetectors.cs' 'changed'
    [void](Invoke-C1313Git $fx.Root @('add','--','src/Antiphon.Agents.Pty/ClaudeDetectors.cs'));$tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','overlap')
    $r=Get-C1313Selection $fx $a
    Assert-C487 ($r.mode -ceq 'full' -and $r.fallbackReasons -ccontains 'rule-full:P-OVERLAP' -and $r.changedPaths.ruleIds -ccontains 'P-CLAUDE') 'C1313 full-dominates'
}
function Test-C1313_RuntimeEdges {
    $fx=New-C1313GitFixture
    foreach($case in @(@('tests/Antiphon.Messaging.Tests/FakeSlack/FakeSlackServer.cs','linked-slack','e2e.remainder','linked-consumer'),@('tests/Antiphon.Checkpoints.FilterHost/FilterHostTests.cs','staged-filter-host','main.remainder','staged-consumer'),@('scripts/lib/nightly-common.ps1','helper-nightly','scripts','helper-consumer'),@('client/dist/index.html','bundle-client','e2e.browser','bundle-consumer'))){
        $a=New-C1313Candidate $fx @($case[0]);$r=Get-C1313Selection $fx $a
        Assert-C487 ($r.selectedSets.id -ccontains $case[2] -and $r.explanationEdges.id -ccontains $case[1]) ('C1313 '+$case[3])
    }
    # Rule boundary checks must reach the selector, not just a regex helper.
    foreach($path in @('src/Antiphon.FakeLlmApi/FakeLlmApiServer.cs','src/Antiphon.FakeOther/Program.cs','tests/UnmappedSharedHelper.cs')){
        $a=New-C1313Candidate $fx @($path);$r=Get-C1313Selection $fx $a
        if($path.Contains('FakeLlmApi')){if($r.changedPaths.ruleIds -cnotcontains 'P-FAKE'){throw 'fake consumer missing'}}elseif($r.mode -cne 'full'){throw 'unknown helper did not select Full'}
    }
}
function Test-C1313_InvalidGraph {
    $fx=New-C1313GitFixture;$graphBad=@();$ruleBad=@();$variantResults=@{}
    [void](Get-C1313Selection $fx $fx.B)
    foreach($variant in @('missing-set','cycle','missing-prerequisite','prerequisite-cycle','bad-regex','script','regex-timeout')){
        $c=Get-Content "$repoRoot/tests/test-selection-policy.json" -Raw|ConvertFrom-Json -AsHashtable
        switch($variant){
            'missing-set' {$c.sets[0].dependsOn=@('missing')}
            'cycle' {$c.sets[0].dependsOn=@($c.sets[0].id)}
            'missing-prerequisite' {$c.sets[0].prerequisites=@('missing')}
            'prerequisite-cycle' {$c.prerequisites[0].dependsOn=@($c.prerequisites[1].id);$c.prerequisites[1].dependsOn=@($c.prerequisites[0].id)}
            'bad-regex' {$c.rules+=@{id='bad';pattern='^([$';sets=@('core');full=$false}}
            'script' {$c.rules[0].command='Write-Output unsafe'}
            'regex-timeout' {$c.rules+=@{id='slow';pattern='^(a+)+$';sets=@('core');full=$false}}
        }
        $a=New-C1313PolicyCandidate $fx $c
        if($variant -eq 'regex-timeout'){
            $blob=Invoke-C1313Git $fx.Root @('rev-parse',"$($fx.B):tests/test-selection-policy.json");$path=('a'*200)+'!'
            [void](Invoke-C1313Git $fx.Root @('update-index','--add','--cacheinfo',"100644,$blob,$path"));$tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','regex')
        }
        $r=Invoke-C1313Selection $fx $a
        $reason=switch($variant){'missing-set'{'missing-dependency'};'cycle'{'dependency-cycle'};'missing-prerequisite'{'missing-prerequisite'};'prerequisite-cycle'{'dependency-cycle'};'bad-regex'{'invalid-rule'};'script'{'invalid-catalog-field:command'};'regex-timeout'{'invalid-rule-timeout'}}
        $variantResults[$variant]=($r.Exit -eq 2 -and $r.Error.Contains($reason))
        if($r.Exit -ne 2 -or -not $r.Error.Contains($reason)){if($variant -in @('bad-regex','script','regex-timeout')){$ruleBad+="$variant : $($r.Error)"}else{$graphBad+="$variant : $($r.Error)"}}
    }
    $c=Get-Content "$repoRoot/tests/test-selection-policy.json" -Raw|ConvertFrom-Json -AsHashtable
    $c.rules+=@{id='P-OVERLAP';paths=@('src/Antiphon.Agents.Pty/ClaudeDetectors.cs');sets=@('core');full=$true}
    @($c.sets|Where-Object id -eq 'provider.claude.offline')[0].dependsOn=@('provider.codex.offline')
    $a=New-C1313PolicyCandidate $fx $c
    Write-C1313File $fx.Root 'src/Antiphon.Agents.Pty/ClaudeDetectors.cs' 'changed'
    [void](Invoke-C1313Git $fx.Root @('add','--','src/Antiphon.Agents.Pty/ClaudeDetectors.cs'));$tree=Invoke-C1313Git $fx.Root @('write-tree');$a=Invoke-C1313Git $fx.Root @('commit-tree',$tree,'-p',$fx.B,'-m','overlap')
    $r=Get-C1313Selection $fx $a
    if($r.mode -cne 'full' -or $r.fallbackReasons -cnotcontains 'rule-full:P-OVERLAP' -or $r.changedPaths.ruleIds -cnotcontains 'P-CLAUDE' -or $r.explanationEdges.id -cnotcontains 'set:provider.claude.offline->provider.codex.offline'){$graphBad+='overlap/dependency-closure'}
    Assert-C487 ($graphBad.Count -eq 0) 'C1313 invalid-graph' ($graphBad -join ',')
    Assert-C487 ($ruleBad.Count -eq 0) 'C1313 invalid-rule' ($ruleBad -join ',')
    Assert-C487 $variantResults['prerequisite-cycle'] 'C1313 cycle-refused'
    Assert-C487 $variantResults['regex-timeout'] 'C1313 regex-timeout-refused'
    Assert-C487 $variantResults['script'] 'C1313 data-not-code'
}

function Invoke-C1313FilterChild($Arguments,$LogPath) {
    . "$PSScriptRoot/lib/checkpoint-process-runner.ps1"
    $start=[Diagnostics.ProcessStartInfo]::new('dotnet');$start.WorkingDirectory=$ResultsDirectory
    $start.Environment['C1313_SENTINEL']=Join-Path $ResultsDirectory 'forbidden.txt'
    foreach($arg in @($FilterHost)+$Arguments){$start.ArgumentList.Add($arg)}
    $r=[Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start,30000,$false)
    [IO.File]::WriteAllText($LogPath,$r.Stdout+$r.Stderr)
    if($r.TimedOut){throw 'owned filter child timeout'}
    return $r
}
function Get-C1313FilterDiscovery {
    $dir=Join-Path $ResultsDirectory 'discovery';[void](New-Item -ItemType Directory $dir)
    $r=Invoke-C1313FilterChild @('--list-tests','--no-progress','--no-ansi','--diagnostic','--diagnostic-output-directory',$dir,'--results-directory',$dir) (Join-Path $dir 'stdout.txt')
    if($r.Exit -ne 0){throw 'discovery child failed'}
    $parsed=ConvertFrom-NightlyDiagnosticLog (Get-NightlyLatestDiagnosticLog $dir) discovery
    return ConvertTo-NightlyDiscoveryDocument $parsed.Nodes (Get-NightlyAssemblyHash $FilterHost)
}
function Test-C1313_ExactExpandedExecution {
    . "$PSScriptRoot/lib/nightly-tests-impl.ps1"
    $discovery=Get-C1313FilterDiscovery
    $expected=@($discovery.nodes | Where-Object {$_.type -ceq 'C1313Offline' -and $_.method -cin @('One','Rows')})
    $dir=Join-Path $ResultsDirectory 'execution'
    $members=@(@{class='Antiphon.Tests.FilterHost.C1313Offline';method='One'},@{class='Antiphon.Tests.FilterHost.C1313Offline';method='Rows'})
    $actual=Invoke-NightlyExactRoster -AssemblyPath $FilterHost -Discovery $discovery -Members $members -ResultsDirectory $dir -Environment @{C1313_SENTINEL=(Join-Path $ResultsDirectory 'forbidden.txt')}
    $readback=Get-Content (Join-Path $dir 'receipt.json') -Raw|ConvertFrom-Json
    # Retain child TRX/diagnostics after ScriptHarness reclaims its owned scratch.
    $retained=Join-Path $repoRoot ('.antiphon/test-output/c1313/'+[guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory $retained)
    Copy-Item -LiteralPath $dir -Destination (Join-Path $retained 'execution') -Recurse
    Copy-Item -LiteralPath (Join-Path $ResultsDirectory 'discovery') -Destination (Join-Path $retained 'discovery') -Recurse
    Assert-C487 ($expected.Count -eq 4 -and $readback.complete -and $readback.executedUids.Count -eq 4 -and (Same-C1313Set $readback.executedUids $expected.uid)) 'C1313 exact-uid-union' ($readback.executedUids -join ',')
    Assert-C487 (-not (Test-Path (Join-Path $ResultsDirectory 'forbidden.txt'))) 'C1313 forbidden-not-launched'
}

function Test-C1313_CoverageRefusal {
    $required=@('one','row-1','row-2','row-3')
    $green=@($required|ForEach-Object {[pscustomobject]@{Uid=$_;Outcome='Passed'}})
    Assert-C487 (Test-NightlyExactCoverage $required $green).Ok 'C1313 coverage-control'
    foreach($fault in @('missing-uid','extra-uid','duplicate-uid','required-skip','zero-required')){
        $rows=@($required|ForEach-Object {[pscustomobject]@{Uid=$_;Outcome='Passed'}})
        switch($fault){
            'missing-uid' {$rows=$rows[0..2]}
            'extra-uid' {$rows += [pscustomobject]@{Uid='forbidden';Outcome='Passed'}}
            'duplicate-uid' {$rows += $rows[0]}
            'required-skip' {$rows[0].Outcome='Skipped'}
            'zero-required' {$rows=@()}
        }
        $r=Test-NightlyExactCoverage $required $rows
        Assert-C487 (-not $r.Ok -and $r.Reasons -ccontains $fault) ('C1313 '+$fault) ($r.Reasons -join ',')
    }
}
function New-C1313SelectorCandidate {
    param($Fx, [scriptblock]$Mutate)
    [void](Invoke-C1313Git $Fx.Root @('read-tree',$Fx.B))
    foreach($path in @('scripts/select-test-rosters.ps1','scripts/lib/test-selection.ps1')){
        $lf=[IO.File]::ReadAllText((Join-Path $repoRoot $path))
        if($lf.Contains("`r")){throw "live selector is not LF: $path"}
        $text=& $Mutate $lf
        $bytesPath=Join-Path $ResultsDirectory ('selector-' + [guid]::NewGuid().ToString('N'))
        [IO.File]::WriteAllBytes($bytesPath, [Text.Encoding]::UTF8.GetBytes($text))
        $blob=Invoke-C1313Git $Fx.Root @('hash-object','-w','--no-filters','--',$bytesPath)
        [void](Invoke-C1313Git $Fx.Root @('update-index','--add','--cacheinfo',"100644,$blob,$path"))
        Remove-Item -LiteralPath $bytesPath -Force
    }
    $globalBytes=Join-Path $ResultsDirectory ('global-' + [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllBytes($globalBytes, [IO.File]::ReadAllBytes((Join-Path $repoRoot 'global.json')))
    $globalBlob=Invoke-C1313Git $Fx.Root @('hash-object','-w','--no-filters','--',$globalBytes)
    [void](Invoke-C1313Git $Fx.Root @('update-index','--add','--cacheinfo',"100644,$globalBlob,global.json"))
    Remove-Item -LiteralPath $globalBytes -Force
    $tree=Invoke-C1313Git $Fx.Root @('write-tree')
    return Invoke-C1313Git $Fx.Root @('commit-tree',$tree,'-p',$Fx.B,'-m','selector bytes')
}
function Get-C1313LfSelectorHash {
    $selectors=@{}
    foreach($path in @('scripts/select-test-rosters.ps1','scripts/lib/test-selection.ps1')){
        $selectors[$path]=Get-TestSelectionDigest ([IO.File]::ReadAllText((Join-Path $repoRoot $path)))
    }
    return Get-TestSelectionDigest $selectors
}
function New-C1313CrlfScriptRoot {
    param([scriptblock]$Mutate)
    $root=Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    $scripts=Join-Path $root 'scripts'
    [void](New-Item -ItemType Directory (Join-Path $scripts 'lib') -Force)
    $pinned=@('scripts/select-test-rosters.ps1','scripts/lib/test-selection.ps1')
    # Direct dot-sources plus the lib files test-selection.ps1 loads from its own directory.
    $copied=@(
        'scripts/select-test-rosters.ps1',
        'scripts/lib/nightly-common.ps1',
        'scripts/lib/nightly-policy.ps1',
        'scripts/lib/test-selection.ps1',
        'scripts/lib/checkpoint-process-runner.ps1',
        'scripts/lib/checkpoint-file-reader.ps1'
    )
    foreach($path in $copied){
        $lf=[IO.File]::ReadAllText((Join-Path $repoRoot $path))
        if($lf.Contains("`r")){throw "live script is not LF: $path"}
        $text=$lf.Replace("`n","`r`n")
        if($Mutate -and $path -ceq 'scripts/lib/test-selection.ps1'){$text=& $Mutate $text}
        if(-not $Mutate -and ($pinned -ccontains $path) -and (ConvertTo-TestSelectionLfText $text) -cne $lf){throw "CRLF round-trip changed $path"}
        $dest=Join-Path $root ($path.Replace('/',[IO.Path]::DirectorySeparatorChar))
        [IO.File]::WriteAllBytes($dest,[Text.Encoding]::UTF8.GetBytes($text))
    }
    [IO.File]::Copy((Join-Path $repoRoot 'scripts/lib/CheckpointProcessRunner.cs'),(Join-Path $scripts 'lib/CheckpointProcessRunner.cs'))
    return $scripts
}
function Test-C1313_SelectorLineEndings {
    $fx=New-C1313GitFixture
    $crlf=New-C1313SelectorCandidate $fx { param($lf) $lf.Replace("`n","`r`n") }
    $roster=Get-TestSelectionBlob $fx.Root $crlf 'scripts/select-test-rosters.ps1'
    $selector=Get-TestSelectionBlob $fx.Root $crlf 'scripts/lib/test-selection.ps1'
    Assert-C487 ($roster.Contains("`r`n") -and $selector.Contains("`r`n")) 'C1313 selector-blob-has-cr'
    $expected=Get-C1313LfSelectorHash
    $accepted=Invoke-C1313Selection $fx $crlf
    Assert-C487 ($accepted.Exit -eq 0 -and $null -ne $accepted.Document) 'C1313 crlf-selector-accepted' $accepted.Error
    $actualHash=if($accepted.Document){[string]$accepted.Document.selectorHash}else{''}
    Assert-C487 ($actualHash -ceq $expected) 'C1313 crlf-selector-hash-is-lf' $actualHash
    . "$PSScriptRoot/lib/release-selection.ps1"
    $producer=Get-ReleaseSelectionContract $fx.Root $crlf 'rc'
    Assert-C487 ([string]$producer.selectorHash -ceq $expected) 'C1313 crlf-producer-hash-is-lf' $producer.selectorHash
    $content=New-C1313SelectorCandidate $fx { param($lf) ('X' + $lf.Replace("`n","`r`n")) }
    $contentRun=Invoke-C1313Selection $fx $content
    Assert-C487 ($contentRun.Exit -eq 2 -and $contentRun.Error.Contains('selector-source-mismatch')) 'C1313 selector-content-refused' $contentRun.Error
    $case=New-C1313SelectorCandidate $fx { param($lf) $lf.Replace('param','PARAM').Replace("`n","`r`n") }
    $caseRun=Invoke-C1313Selection $fx $case
    Assert-C487 ($caseRun.Exit -eq 2 -and $caseRun.Error.Contains('selector-source-mismatch')) 'C1313 selector-case-refused' $caseRun.Error
    $space=New-C1313SelectorCandidate $fx { param($lf) ($lf.Substring(0,1) + ' ' + $lf.Substring(1)).Replace("`n","`r`n") }
    $spaceRun=Invoke-C1313Selection $fx $space
    Assert-C487 ($spaceRun.Exit -eq 2 -and $spaceRun.Error.Contains('selector-source-mismatch')) 'C1313 selector-space-refused' $spaceRun.Error
    $embedded=New-C1313SelectorCandidate $fx { param($lf) $lf.Insert(1,"`r") }
    $embeddedRun=Invoke-C1313Selection $fx $embedded
    Assert-C487 ($embeddedRun.Exit -eq 2 -and $embeddedRun.Error.Contains('selector-source-mismatch')) 'C1313 embedded-cr-refused' $embeddedRun.Error
    $disk=New-C1313CrlfScriptRoot
    $diskRoster=[IO.File]::ReadAllText((Join-Path $disk 'select-test-rosters.ps1'))
    $diskSelector=[IO.File]::ReadAllText((Join-Path $disk 'lib/test-selection.ps1'))
    $liveRoster=[IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/select-test-rosters.ps1'))
    $liveSelector=[IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/lib/test-selection.ps1'))
    Assert-C487 ($diskRoster.Contains("`r`n") -and $diskSelector.Contains("`r`n") -and (ConvertTo-TestSelectionLfText $diskRoster) -ceq $liveRoster -and (ConvertTo-TestSelectionLfText $diskSelector) -ceq $liveSelector) 'C1313 disk-selector-has-cr'
    $diskRun=Invoke-C1313Selection $fx $fx.B $fx.B $disk
    Assert-C487 ($diskRun.Exit -eq 0 -and $null -ne $diskRun.Document) 'C1313 crlf-disk-selector-accepted' ("exit={0} {1}" -f $diskRun.Exit,$diskRun.Error)
    $diskHash=if($diskRun.Document){[string]$diskRun.Document.selectorHash}else{''}
    Assert-C487 ($diskHash -ceq $expected) 'C1313 crlf-disk-selector-hash-is-lf' $diskHash
    $edited=New-C1313CrlfScriptRoot { param($crlf) $crlf.Insert($crlf.IndexOf("`n")+1,"# c1313-disk-edit`r`n") }
    $editedRun=Invoke-C1313Selection $fx $fx.B $fx.B $edited
    Assert-C487 ($editedRun.Exit -eq 2 -and $editedRun.Error.Contains('selector-source-mismatch')) 'C1313 crlf-disk-selector-refused' $editedRun.Error
}
function Test-C1313_NewUidRemainder {
    $discovery=Get-C1313FilterDiscovery
    $members=@(@{class='Antiphon.Tests.FilterHost.C1313Offline';method='One'})
    $control=New-NightlyExactRoster $discovery $members @($discovery.nodes.uid)
    if($control.Widened -or $control.RequiredUids.Count -ne 1){throw 'valid remainder control failed'}
    $new=@($discovery.nodes|Where-Object {$_.type -ceq 'C1313Offline' -and $_.method -ceq 'Rows'})[0].uid
    $known=@($discovery.nodes.uid|Where-Object {$_ -cne $new})
    $r=New-NightlyExactRoster $discovery $members $known
    . "$PSScriptRoot/lib/nightly-tests-impl.ps1"
    $refused='';try{[void](Invoke-NightlyExactRoster -AssemblyPath $FilterHost -Discovery $discovery -Members $members -ClassifiedUids $known -ResultsDirectory (Join-Path $ResultsDirectory 'must-not-launch'))}catch{$refused=$_.Exception.Message}
    Assert-C487 ($r.Widened -and $r.RequiredUids -ccontains $new -and $r.RequiredUids.Count -eq $discovery.nodes.Count -and $refused.StartsWith('new-uid-required') -and -not (Test-Path (Join-Path $ResultsDirectory 'must-not-launch'))) 'C1313 new-uid-required' $refused
}

try { & "Test-$Case" }
finally {
    # Git loose objects in our disposable fixtures are read-only on Windows.
    # Normalize only those objects, then let ScriptHarness own deletion/retention.
    foreach ($fixture in $script:C1313GitFixtures) {
        $objects = Join-Path $fixture '.git/objects'
        if (Test-Path -LiteralPath $objects) {
            Get-ChildItem -LiteralPath $objects -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        }
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
