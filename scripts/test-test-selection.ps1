#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/nightly-coverage.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$repoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function Test-C1313_ProjectAndCatalogUniverse {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $census = Test-NightlyPolicyProjectUniverse -RepoRoot $repoRoot -PolicyObject $policy.Object
    Assert-C487 -Cond ($census.Ok -and $census.Disk.Count -eq 11) -Name 'C1313 project-universe' -Detail ($census | ConvertTo-Json -Compress)
    $roles = $policy.Object.projectDispositions
    Assert-C487 -Cond (@($roles | Where-Object role -eq primary).Count -eq 7 -and @($roles | Where-Object role -eq child).Count -eq 4) -Name 'C1313 seven-primary-four-child'
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
    Assert-C487 -Cond ($linux.platforms -ceq 'linux' -and $linux.members.Count -eq 3) -Name 'C1313 other-platform-visible'
    $core=@($catalog.sets|Where-Object id -eq core)[0];$core.resources.api='unknown';$catalog.catalogHash=Get-TestSelectionCatalogHash $catalog
    $msg='';try{[void](Assert-TestSelectionCatalog $catalog)}catch{$msg=$_.Exception.Message}
    Assert-C487 -Cond ($msg -eq 'unknown-resource') -Name 'C1313 unknown-resource'
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

function Test-C1313_ProviderAndShared {
    # Test-only legacy characterization. S2 replaces this path with the production CLI.
    $policy=Read-NightlyExecutionPolicy $repoRoot
    $actual=@(Resolve-NightlySelectedSuites $policy.Object @())
    $expected=@('core','provider.claude.offline','provider.claude.fake-native','provider.claude.tui-stub','provider.claude.tui-live','provider.claude.vendor-offline','provider.claude.server-runner','main.remainder','e2e.remainder')
    Assert-C487 -Cond (($actual|Sort-Object) -join ',' -ceq ($expected|Sort-Object) -join ',') -Name 'C1313 provider-exact' -Detail ($actual -join ',')
}

& "Test-$Case"
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
