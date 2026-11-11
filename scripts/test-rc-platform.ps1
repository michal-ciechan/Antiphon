#requires -Version 7.0
# CARD-1323: offline policy and frozen Windows authority witnesses. No publication.
param([string]$Case, [string]$ResultsDirectory, [string]$CompiledRosterPath)
$ErrorActionPreference = 'Stop'
foreach ($name in @('nightly-common', 'nightly-policy', 'nightly-coverage', 'release-gate', 'release-authority', 'test-selection', 'c487-harness')) {
    . "$PSScriptRoot/lib/$name.ps1"
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function Assert-C1323($Condition, $Name, $Detail = '') {
    Assert-C487 -Cond $Condition -Name "C1323 $Name" -Detail $Detail
}
function Copy-C1323Object($Object) { return ($Object | ConvertTo-Json -Depth 40 | ConvertFrom-Json) }
function Same-C1323Set($Actual, $Expected) {
    return ((@($Actual | Sort-Object -CaseSensitive) -join "`n") -ceq (@($Expected | Sort-Object -CaseSensitive) -join "`n"))
}
function Write-C1323Policy($Object, $Path) {
    $Object.policyHash = Get-NightlyPolicyHash $Object
    [IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 40))
}
function Invoke-C1323Git($Root, $Arguments) { return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim() }
function Write-C1323File($Root, $Path, $Text) {
    $file = Join-Path $Root $Path
    [void](New-Item -ItemType Directory (Split-Path $file) -Force)
    [IO.File]::WriteAllText($file, $Text)
}
function New-C1323Nodes {
    $nodes = @()
    foreach ($pair in @(@('AgentTaskLandDeliveryE2ETests', 52), @('DispatchBaseWarningDeliveryE2ETests', 12), @('WorktreeRetirementDeliveryE2ETests', 5))) {
        for ($i = 1; $i -le $pair[1]; $i++) {
            $nodes += [pscustomobject]@{ uid = "$($pair[0])-$i"; class = "Antiphon.E2E.$($pair[0])"; method = 'Expanded'; categories = @('OptIn') }
        }
    }
    $nodes += [pscustomobject]@{ uid = 'unclassified'; class = 'Antiphon.E2E.NewOptInTests'; method = 'Runs'; categories = @('OptIn') }
    $nodes += [pscustomobject]@{ uid = 'legacy-live'; class = 'Antiphon.E2E.MixedTests'; method = 'Live'; categories = @() }
    $nodes += [pscustomobject]@{ uid = 'legacy-sibling'; class = 'Antiphon.E2E.MixedTests'; method = 'Offline'; categories = @() }
    return $nodes
}
function Get-C1323Required($Nodes, $Exclusions) {
    return @($Nodes | Where-Object {
        $n = [pscustomobject]@{ ClassName = $_.class; Type = $_.class; Method = $_.method; Categories = $_.categories }
        (Get-NightlyDiscoveryDisposition $n $Exclusions -ProfileAware).Disposition -eq 'required'
    } | ForEach-Object uid)
}

function Test-C1323_PlatformPolicy {
    $policy = Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw | ConvertFrom-Json
    # Supply the new contract even at the pre-fix commit: this must expose the
    # global exclusion defect before the shipped schema is updated.
    $policy.schemaVersion = 4
    $policy.profiles.rc.exclusions = @($policy.profiles.rc.exclusions | Where-Object owner -ne 'CARD-1323')
    foreach ($class in @('AgentTaskLandDeliveryE2ETests', 'DispatchBaseWarningDeliveryE2ETests', 'WorktreeRetirementDeliveryE2ETests')) {
        $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = $class; platform = 'linux'; reason = 'Requires Windows ConPTY/native FakeGrok'; owner = 'CARD-1323' }
    }
    $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = 'MixedTests'; methods = @('Live'); reason = 'legacy live method'; owner = 'fixture' }
    $nodes = @(New-C1323Nodes)
    $expectedWindows = @($nodes.uid | Where-Object { $_ -ne 'legacy-live' })
    $windows = @(Get-NightlyProfileExclusions $policy 'rc' 'e2e' -Platform windows)
    $actualWindows = @(Get-C1323Required $nodes $windows)
    $retained = Same-C1323Set $actualWindows $expectedWindows
    Assert-C1323 $retained 'windows-retains-69-and-siblings' ($actualWindows -join ',')
    if (-not $retained) { return }
    $linux = @(Get-NightlyProfileExclusions $policy 'rc' 'e2e' -Platform linux)
    Assert-C1323 (Same-C1323Set (Get-C1323Required $nodes $linux) @('unclassified', 'legacy-sibling')) 'linux-excludes-exactly-69'
    $scoped = @($linux | Where-Object Owner -eq 'CARD-1323')
    Assert-C1323 ($scoped.Count -eq 3 -and @($scoped | Where-Object { -not $_.Reason -or -not $_.Owner }).Count -eq 0) 'exclusion-reasons-and-owners'
    Assert-C1323 (Same-C1323Set (Get-C1323Required $nodes @(Get-NightlyProfileExclusions $policy 'rc' 'e2e')) $expectedWindows) 'default-is-windows'
    $path = Join-Path $ResultsDirectory 'policy.json'
    Write-C1323Policy $policy $path
    $loaded = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
    Assert-C1323 ($loaded.SchemaVersion -eq 4) 'schema4-loads'
    $bytes = [IO.File]::ReadAllBytes($path)
    $authorityPolicy = ConvertFrom-ReleaseAuthorityPolicyBytes $bytes
    $expectedSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e', 'markdown-pdf')
    if ($policy.suites.observability) { $expectedSuites += 'observability' }
    if ($policy.suites.PSObject.Properties.Name -contains 'server-git') { $expectedSuites += 'server-git' }
    if ($policy.suites.PSObject.Properties.Name -contains 'resilience') { $expectedSuites += 'resilience' }
    if ($policy.suites.PSObject.Properties.Name -contains 'git-in-process') { $expectedSuites += 'git-in-process' }
    $observabilityRequired = $true
    if ($policy.suites.observability) {
        $missing = Copy-C1323Object $policy
        $missing.profiles.rc.requiredSuites = @($missing.profiles.rc.requiredSuites | Where-Object { $_ -cne 'observability' })
        $missing.policyHash = Get-NightlyPolicyHash $missing
        $message = ''
        try { [void](ConvertFrom-ReleaseAuthorityPolicyBytes ([Text.Encoding]::UTF8.GetBytes(($missing | ConvertTo-Json -Depth 40)))) } catch { $message = $_.Exception.Message }
        $observabilityRequired = $message -ceq 'policy-rc-suite-missing:observability'
    }
    Assert-C1323 ((Same-C1323Set $authorityPolicy.RequiredSuites $expectedSuites) -and $observabilityRequired) 'nine-suite-authority'
    Assert-C1323 (Same-C1323Set (Get-ReleaseAuthorityRequiredUids $nodes $authorityPolicy.Exclusions 'e2e') $expectedWindows) 'authority-resolves-windows'

    $invalid = @('macos', '', 'Windows', 1, $true, $null, @('linux'), @{ os = 'linux' })
    $missed = @()
    for ($i = 0; $i -lt $invalid.Count; $i++) {
        $bad = Copy-C1323Object $policy
        $row = @($bad.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0]
        $row.platform = $invalid[$i]
        Write-C1323Policy $bad $path
        $read = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
        $message = ''
        try { [void](Get-NightlyProfileExclusions $read.Object 'rc' 'antiphon' -Platform windows) } catch { $message = $_.Exception.Message }
        if ($message -notmatch 'exclusion.*platform') { $missed += "$i=$message" }
    }
    Assert-C1323 ($missed.Count -eq 0) 'invalid-platforms-refuse-before-filtering' ($missed -join ';')
    $missed = @()
    foreach ($field in @('reason', 'owner')) {
        $bad = Copy-C1323Object $policy
        @($bad.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0].$field = ''
        $message = ''
        try { [void](Get-NightlyProfileExclusions $bad 'rc' 'antiphon' -Platform windows) } catch { $message = $_.Exception.Message }
        if ($message -notmatch 'reason and owner') { $missed += $field }
    }
    Assert-C1323 ($missed.Count -eq 0) 'invalid-attribution-refuses-before-filtering' ($missed -join ',')
    $changed = Copy-C1323Object $policy
    @($changed.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0].platform = 'windows'
    Assert-C1323 ((Get-NightlyPolicyHash $changed) -cne (Get-NightlyPolicyHash $policy)) 'platform-only-hash-changes'
    [IO.File]::WriteAllText($path, ($changed | ConvertTo-Json -Depth 40))
    $message = ''; try { [void](Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path) } catch { $message = $_.Exception.Message }
    Assert-C1323 ($message -match 'stale policy hash') 'stale-platform-hash-refuses'
    $message = ''; try { [void](ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes($path))) } catch { $message = $_.Exception.Message }
    Assert-C1323 ($message -eq 'policy-hash-stale') 'authority-stale-hash-refuses'
    $historical = Copy-C1323Object $policy
    $historical.schemaVersion = 3
    $historical.profiles.rc.exclusions = @($historical.profiles.rc.exclusions | Where-Object owner -ne 'CARD-1323')
    Write-C1323Policy $historical $path
    $old = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
    $oldAuthority = ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes($path))
    Assert-C1323 ($old.SchemaVersion -eq 3 -and (Same-C1323Set $oldAuthority.RequiredSuites $expectedSuites) -and (Same-C1323Set (Get-C1323Required $nodes @(Get-NightlyProfileExclusions $old.Object 'rc' 'e2e' -Platform linux)) $expectedWindows)) 'schema3-compatibility'
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $shipRows = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')
    $ownedKeys = @($shipRows | ForEach-Object { $_.suite + '|' + $_.class + '|' + $_.platform })
    $expectedOwnedKeys = @(
        'e2e|AgentTaskLandDeliveryE2ETests|linux', 'e2e|DispatchBaseWarningDeliveryE2ETests|linux',
        'e2e|WorktreeRetirementDeliveryE2ETests|linux',
        'antiphon|PosixHarnessToolTests|windows', 'antiphon|CardRelationshipScriptTests|windows'
    )
    $linuxShipRows = @($shipRows | Where-Object { $_.platform -ceq 'linux' })
    Assert-C1323 ($shipped.SchemaVersion -eq 4 -and (Same-C1323Set $ownedKeys $expectedOwnedKeys) -and $linuxShipRows.Count -eq 3 -and @($linuxShipRows | Where-Object { $_.suite -cne 'e2e' }).Count -eq 0 -and (Same-C1323Set $linuxShipRows.class $scoped.Class)) 'shipped-three-linux-rows'
    $c1614 = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1614')
    $inbox = @($c1614 | Where-Object { [string]$_.class -ceq 'HostCustodyTests' -and [string]$_.platform -ceq 'linux' })
    $life = @($c1614 | Where-Object { [string]$_.class -ceq 'LinuxRunnerCustodyLifecycleTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' })
    $boot = @($c1614 | Where-Object { [string]$_.class -ceq 'CustodyNamespaceBootstrapTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' })
    $nativeHost = @($c1614 | Where-Object { [string]$_.suite -ceq 'pty-host' -and [string]$_.class -ceq 'LinuxCustodyReleaseHostTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' -and @($_.methods | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -eq 0 })
    $nativeHelper = @($c1614 | Where-Object { [string]$_.suite -ceq 'agents-pty' -and [string]$_.class -ceq 'LinuxCgroupContainmentTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' -and (Same-C1323Set @($_.methods) @('C1614_Helper_written_tree_is_read_by_production_reader')) })
    $inboxMethods = @($inbox | ForEach-Object { $_.methods })
    Assert-C1323 ($c1614.Count -eq 5 -and $nativeHost.Count -eq 1 -and $nativeHelper.Count -eq 1 -and $inbox.Count -eq 1 -and @($inboxMethods) -contains 'Inbox_is_unsupported_and_never_becomes_no_start_proof' -and @($inboxMethods) -contains 'C1614_Windows_tracked_linger_releases_job' -and [string]$inbox[0].reason -notlike 'Linux-only by construction:*' -and $life.Count -eq 1 -and $boot.Count -eq 1 -and $shipped.Hash -ceq [string]$shipped.Object.policyHash) 'card-1614-mixed-platform-rows'
    $shippedCensus = Test-NightlyPolicyProjectUniverse -RepoRoot $repoRoot -PolicyObject $shipped.Object
    Assert-C1323 ($shippedCensus.Ok -and $shipped.Object.selectionCatalog.path -ceq 'tests/test-selection-policy.json' -and @($shipped.Object.profiles.rc.exclusions | Where-Object class -eq 'ChannelOutboundIsolatedTests').Count -eq 1 -and @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1313').Count -gt 0) 'master-inventory-preserved' $shippedCensus.Message

    $root = Join-Path $ResultsDirectory 'git-policy'
    [void](New-Item -ItemType Directory $root)
    [void](Invoke-C1323Git $root @('init', '--quiet'))
    [void](Invoke-C1323Git $root @('config', 'user.name', 'C1323 fixture'))
    [void](Invoke-C1323Git $root @('config', 'user.email', 'c1323@example.invalid'))
    [void](Invoke-C1323Git $root @('config', 'core.autocrlf', 'false'))
    [void](Invoke-C1323Git $root @('remote', 'add', 'origin', 'https://github.com/fixture/c1323'))
    foreach ($file in @('tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1', 'scripts/lib/test-selection.ps1')) {
        Write-C1323File $root $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))
    }
    Write-C1323File $root 'tests/test-execution-policy.json' ([Text.Encoding]::UTF8.GetString($bytes))
    $catalog = Read-TestSelectionCatalog "$root/tests/test-selection-policy.json" $policy.selectionCatalog.hash
    foreach ($input in @(Get-TestSelectionSorted @($catalog.sets.sourceInputs + $catalog.sets.members.source))) {
        if ($input) { Write-C1323File $root $input 'fixture source' }
    }
    [void](Invoke-C1323Git $root @('add', '--all'))
    [void](Invoke-C1323Git $root @('commit', '--quiet', '-m', 'fixture'))
    $sha = Invoke-C1323Git $root @('rev-parse', 'HEAD')
    $selection = Get-TestSelection $root $sha $sha 'rc'
    Assert-C1323 ($null -ne $selection -and $selection.candidateSha -ceq $sha) 'schema4-selector-admitted'
    Test-C1323FrozenLedger $root $sha $nodes $expectedWindows
}

function Test-C1323FrozenLedger($Root, $Sha, $Nodes, $ExpectedWindows) {
    $candidate = Join-Path $ResultsDirectory 'candidate'
    $identity = @{ RepositoryRoot = $Root; CandidateRoot = $candidate; Sha = $Sha; Repository = 'fixture/c1323'; CandidateId = 'fixture'; CandidateRef = 'fixture-ref' }
    $authority = New-ReleaseGateAuthority @identity -IntentId 'fixture-intent' -ReleaseCardId '13231323-1323-1323-1323-132313231323' -CreatedUtc ([datetime]'2026-01-01T00:00:00Z')
    $pinned = Test-ReleaseGatePinnedAuthority @identity
    Assert-C1323 ($pinned.Ok -and (Same-C1323Set (Get-ReleaseAuthorityRequiredUids $Nodes $authority.exclusions 'e2e') $ExpectedWindows)) 'pinned-authority-validates-windows' ($pinned.Reasons -join ',')
    $discovery = @{}
    foreach ($suite in $authority.requiredSuites) {
        if ($suite -in @('scripts', 'client')) { continue }
        $discovery[$suite] = @([pscustomobject]@{ uid = "$suite-ordinary"; class = 'OfflineTests'; method = 'Runs' })
    }
    $discovery['e2e'] = $Nodes
    $plan = New-ReleaseGateExecutionPlan $candidate $discovery @{ scripts = @('script'); client = @('client') }
    $dir = Get-ReleaseAuthorityDir $candidate
    $planPath = Join-Path $dir 'execution-plan.json'
    $ledgerPath = Join-Path $dir 'ledger.json'
    $start = '2026-01-01T00:01:00Z'; $end = '2026-01-01T00:02:00Z'
    $ledger = [ordered]@{
        intentId = $authority.intentId; candidateId = $authority.candidateId; candidateRef = $authority.candidateRef; sha = $Sha; runId = 'fixture-run'
        authorityDigest = $pinned.Digest; executionPlanDigest = Get-NightlyFileSha256 $planPath
        teardownSucceeded = $true; seamed = $false; noReport = $false; diagnostic = $false; selection = 'full'
        startedAt = $start; completedAt = $end; chunks = @(); rosters = @()
        prerequisites = @(@{ id = 'build'; exitCode = 0; succeeded = $true }, @{ id = 'client-bundle'; exitCode = 0; succeeded = $true }, @{ id = 'script-census'; exitCode = 0; succeeded = $true })
        prepublicationReceipt = @{
            kind = $script:ReleaseAuthorityReceiptKind; recipient = @{ kind = $script:ReleaseAuthorityRecipientKind; cardId = $authority.releaseCardId; revision = 1 }
            correlation = @{ intentId = $authority.intentId; candidateId = $authority.candidateId; sha = $Sha; runId = 'fixture-run' }; acceptedAt = $end
        }
    }
    foreach ($suite in $plan.suites) {
        $common = @{ intentId = $authority.intentId; candidateId = $authority.candidateId; sha = $Sha; runId = 'fixture-run'; startedAt = $start; completedAt = $end; exitCode = 0; suite = $suite.id }
        if ($suite.kind -eq 'roster') {
            $common.entries = @($suite.entries | ForEach-Object { @{ id = $_; outcome = 'passed' } })
            $ledger.rosters += $common
            continue
        }
        foreach ($chunk in $suite.chunks) {
            $row = $common.Clone(); $row.chunk = $chunk.id; $row.trx = "$($chunk.id).trx"
            $trx = Join-Path $candidate $row.trx
            Write-C487Trx $trx @($chunk.uids | ForEach-Object { @{ Id = $_; ClassName = $chunk.classes[0]; MethodName = 'Expanded'; Outcome = 'Passed' } })
            $row.trxSha256 = Get-NightlyFileSha256 $trx
            $row.executed = $chunk.uids.Count; $row.passed = $chunk.uids.Count; $row.failed = 0; $row.skipped = 0
            $ledger.chunks += $row
        }
    }
    # Only card I/O is supplied here; every authority, membership, digest and
    # credit decision remains production code. No HTTP or publisher is invoked.
    function Read-ReleaseGateReleaseCard {
        param($BaseUrl, $CardId)
        $description = "$script:ReleaseAuthorityCorrelationPrefix intentId=$($authority.intentId) candidateId=$($authority.candidateId) sha=$Sha runId=fixture-run"
        return @{ Ok = $true; Card = [pscustomobject]@{ id = $CardId; revisionCount = 1; description = $description } }
    }
    Write-NightlyAtomicJson $ledgerPath $ledger
    $green = [pscustomobject]@{ runId = 'fixture-run' }
    $now = [datetime]'2026-01-01T00:03:00Z'
    $valid = Test-ReleaseGateExecutionLedger $candidate $pinned $green $now
    Assert-C1323 $valid.Ok 'complete-synthetic-ledger-validates' ($valid.Reasons -join ',')
    $missing = 'AgentTaskLandDeliveryE2ETests-1'
    $chunk = @($plan.suites | Where-Object id -eq 'e2e')[0].chunks[0]
    $chunk.uids = @($chunk.uids | Where-Object { $_ -cne $missing })
    Write-NightlyAtomicJson $planPath $plan
    $ledger.executionPlanDigest = Get-NightlyFileSha256 $planPath
    $row = @($ledger.chunks | Where-Object chunk -eq $chunk.id)[0]
    $trx = Join-Path $candidate $row.trx
    Write-C487Trx $trx @($chunk.uids | ForEach-Object { @{ Id = $_; ClassName = $chunk.classes[0]; MethodName = 'Expanded'; Outcome = 'Passed' } })
    $row.trxSha256 = Get-NightlyFileSha256 $trx; $row.executed--; $row.passed--
    Write-NightlyAtomicJson $ledgerPath $ledger
    $invalid = Test-ReleaseGateExecutionLedger $candidate $pinned $green $now
    Assert-C1323 (-not $invalid.Ok -and (Same-C1323Set $invalid.Reasons @("plan-uid-missing:$missing", "uid-missing:$missing"))) 'missing-windows-uid-refuses' ($invalid.Reasons -join ',')
}

function New-C1323ExecutionFixture {
    $root = Join-Path $ResultsDirectory 'execution'
    [void][IO.Directory]::CreateDirectory($root)
    $clone = New-C487OwnedClone $root 'https://github.com/fixture/c1323'
    $policy = Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw | ConvertFrom-Json
    # A one-suite offline policy keeps the executor bounded; V-1 pins the real nine.
    $policy.suites = [pscustomobject]@{ e2e = $policy.suites.e2e }
    $policy.profiles.rc.requiredSuites = @('e2e')
    $policy.projects = @('tests/Antiphon.E2E/Antiphon.E2E.csproj')
    $policy.projectDispositions = @([pscustomobject]@{ project = $policy.projects[0]; role = 'primary'; reason = 'offline fixture' })
    $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = 'MixedTests'; methods = @('Live'); reason = 'legacy live method'; owner = 'fixture' }
    Write-C1323File $clone 'tests/Antiphon.E2E/Antiphon.E2E.csproj' '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
    foreach ($file in @('global.json', 'tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1')) {
        Write-C1323File $clone $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))
    }
    Write-C1323Policy $policy "$clone/tests/test-execution-policy.json"
    [void](Invoke-C1323Git $clone @('init', '--quiet'))
    [void](Invoke-C1323Git $clone @('config', 'user.name', 'C1323 fixture'))
    [void](Invoke-C1323Git $clone @('config', 'user.email', 'c1323@example.invalid'))
    [void](Invoke-C1323Git $clone @('config', 'core.autocrlf', 'false'))
    [void](Invoke-C1323Git $clone @('add', '--all'))
    [void](Invoke-C1323Git $clone @('commit', '--quiet', '-m', 'offline execution fixture'))
    $sha = Invoke-C1323Git $clone @('rev-parse', 'HEAD')
    $nodes = @(New-C1323Nodes | ForEach-Object {
        @{ uid = $_.uid; type = ($_.class -replace '^Antiphon.E2E.', ''); className = $_.class; namespace = 'Antiphon.E2E'; method = $_.method; categories = $_.categories; state = 'Discovered' }
    })
    Write-NightlyAtomicJson "$root/nodes.json" $nodes
    # These are synthetic nodes with seamed execution, not the costly native
    # delivery cases sharing their names. Use the standard fallback node model.
    Write-NightlyAtomicJson "$root/class-costs.json" @{ format = 'antiphon-nightly-class-costs-v1'; classes = @{} }
    Write-C1323File $root 'commands/npm.cmd' 'fixture launcher, never executed'
    return @{ Root = $root; Clone = $clone; Sha = $sha; Nodes = $nodes }
}

function Write-C1323ExecutionSeams($Fixture, $Platform) {
    $path = Join-Path $Fixture.Root "$Platform.ps1"
    $template = @'
$script:NightlyClassCostPath = '__ROOT__/class-costs.json'
$script:NightlyClassCostTable = $null
$env:PATH = '__ROOT__/commands' + [IO.Path]::PathSeparator + $env:PATH
$NightlySeams = @{
    HostPlatform = { return '__PLATFORM__' }
    E2EPrerequisites = { return @{ Ok = $true; Rows = @(@{ name = 'offline-browser'; exitCode = 0 }) } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        __CHILD_WRITERS__
        Add-Content -LiteralPath '__ROOT__/__PLATFORM__-commands.log' -Value ($FilePath + ' ' + ($ArgumentList -join ' '))
        $argv = @($ArgumentList)
        $nodes = @(Get-Content '__ROOT__/nodes.json' -Raw | ConvertFrom-Json)
        $diagIndex = [array]::IndexOf($argv, '--diagnostic-output-directory')
        if ($argv -contains '--list-tests') {
            Write-C487Discovery (Join-Path $argv[$diagIndex + 1] 'log.diag') $nodes
        } elseif ($argv -contains '--report-trx-filename') {
            $filterIndex = [array]::IndexOf($argv, '--treenode-filter')
            if ($filterIndex -ge 0) {
                $parts = $argv[$filterIndex + 1].Split('/')
                # CARD-1569: emulate each TUnit OR operand, retaining its
                # trailing wildcard. PowerShell -like has no OR grammar.
                function Test-C1323FilterPart($Value, $Pattern) {
                    foreach ($operand in $Pattern.Split('|')) {
                        if ($Value -like $operand.Trim('(', ')')) { return $true }
                    }
                    return $false
                }
                $nodes = @($nodes | Where-Object {
                    (Test-C1323FilterPart $_.namespace $parts[2]) -and
                    (Test-C1323FilterPart $_.type $parts[3]) -and
                    (Test-C1323FilterPart $_.method $parts[4])
                })
            }
            foreach ($node in $nodes) { Add-Content '__ROOT__/__PLATFORM__-launched.txt' $node.uid }
            $trx = Join-Path $argv[[array]::IndexOf($argv, '--results-directory') + 1] $argv[[array]::IndexOf($argv, '--report-trx-filename') + 1]
            Write-C487Trx $trx @($nodes | ForEach-Object { @{ Id = $_.uid; ClassName = $_.className; MethodName = $_.method; Outcome = 'Passed' } })
            Write-C487Execution (Join-Path $argv[$diagIndex + 1] 'log.diag') @($nodes | ForEach-Object { @{ uid = $_.uid; type = $_.type; className = $_.className; method = $_.method; state = 'Passed' } })
            # This is child discovery output, never a precomputed census or verdict.
            Write-C487Discovery ([IO.Path]::ChangeExtension($trx, '.discovery.json')) @(Get-Content '__ROOT__/nodes.json' -Raw | ConvertFrom-Json)
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; LogText = 'offline child completed' }
    }
}
'@
    # Import-NightlySeams has a local scope. Keep only the child artifact writers
    # inside the callback so a real process hop can resolve them on every call.
    $writers = @('Write-C487Trx', 'Write-C487Execution', 'Write-C487Discovery') | ForEach-Object {
        'function ' + $_ + " {`n" + (Get-Command $_ -CommandType Function).Definition + "`n}"
    }
    $text = $template.Replace('__CHILD_WRITERS__', ($writers -join "`n")).Replace('__ROOT__', $Fixture.Root.Replace("'", "''")).Replace('__PLATFORM__', $Platform)
    [IO.File]::WriteAllText($path, $text)
    return $path
}

function Test-C1323_PlatformExecution {
    foreach ($name in @('nightly-tests-impl', 'nightly-run-impl')) { . "$PSScriptRoot/lib/$name.ps1" }
    $fx = New-C1323ExecutionFixture
    $linuxSeams = Write-C1323ExecutionSeams $fx 'linux'
    $linux = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/linux-direct" -Profile rc -Sha $fx.Sha -SeamsPath $linuxSeams
    $launched = @(Get-Content "$($fx.Root)/linux-launched.txt")
    $portable = Same-C1323Set $launched @('unclassified', 'legacy-sibling')
    Assert-C1323 $portable 'linux-launches-only-portable-cases' ($launched -join ',')
    if (-not $portable) { return } # red witness: no dependent assertions obscure it
    $summary = Get-Content $linux.SummaryPath -Raw | ConvertFrom-Json
    Assert-C1323 ($summary.testsPassed -and -not $summary.coverageComplete -and $summary.coverageScope -ceq 'PlatformDiagnostic' -and $summary.platform -ceq 'linux' -and $summary.reasons -contains 'windows-rc-obligations-outstanding') 'linux-summary-is-diagnostic'
    $census = @($summary.suites | Where-Object { $_.requiredUidCount -eq 2 -and $_.excludedUidCount -eq 70 })
    Assert-C1323 ($census.Count -gt 0 -and @($census[0].exclusions | Where-Object owner -eq 'CARD-1323').Count -eq 69) 'linux-census-retains-exclusion-attribution'
    Assert-C1323 ($launched -contains 'unclassified' -and $launched -contains 'legacy-sibling' -and $launched -notcontains 'legacy-live') 'optin-and-legacy-method-semantics'

    $windowsSeams = Write-C1323ExecutionSeams $fx 'windows'
    $windows = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/windows-direct" -Profile rc -Sha $fx.Sha -SeamsPath $windowsSeams
    $launched = @(Get-Content "$($fx.Root)/windows-launched.txt")
    Assert-C1323 (Same-C1323Set $launched @($fx.Nodes.uid | Where-Object { $_ -ne 'legacy-live' })) 'windows-retains-delivery-cases' ($launched -join ',')
    $summary = Get-Content $windows.SummaryPath -Raw | ConvertFrom-Json
    Assert-C1323 ($windows.ExitCode -eq 0 -and $summary.testsPassed -and $summary.coverageComplete -and $summary.coverageScope -ceq 'Full' -and $summary.platform -ceq 'windows' -and $summary.reasons.Count -eq 0) 'windows-summary-remains-full' ($summary | ConvertTo-Json -Depth 8 -Compress)

    $releaseRoot = "$($fx.Root)/releases"
    $candidateId = (Test-ReleaseGateCandidateRef -Ref 'rc/2026-10-20' -Sha $fx.Sha).CandidateId
    $green = "$releaseRoot/candidates/$candidateId/complete-green.json"
    $masterGreen = "$($fx.Root)/master/last-complete-green.json"
    Write-C1323File $fx.Root "releases/candidates/$candidateId/complete-green.json" 'candidate sentinel'
    Write-C1323File $fx.Root 'master/last-complete-green.json' 'master sentinel'
    foreach ($platform in @('linux', 'windows')) {
        $runId = "offline-$platform"
        $stateRoot = "$($fx.Root)/state-$platform"
        $request = @{ RepositoryRoot = $fx.Clone; CandidateSha = $fx.Sha; CandidateRef = 'rc/2026-10-20'; Repository = 'fixture/c1323'; Profile = 'rc'; ReleaseRoot = $releaseRoot; CandidateId = $candidateId; RunId = $runId; SelectionMode = 'Full'; Attempt = 1 }
        $bound = New-ReleaseSelectionAttemptCore $request
        $run = Invoke-AntiphonNightlyRunCore -NoSync -CheckoutRoot $fx.Clone -StateRoot $stateRoot -LogRoot "$($fx.Root)/run-$platform" -RemoteUrl 'https://github.com/fixture/c1323' -Ref 'rc/2026-10-20' -Profile rc -Trigger rc -ExpectedSha $fx.Sha -CandidateId $candidateId -Repository 'fixture/c1323' -ReleaseRoot $releaseRoot -CoordinationRoot "$($fx.Root)/coordination" -RunId $runId -SeamsPath "$($fx.Root)/$platform.ps1" -SelectionAttemptPath $bound.SelectionAttemptPath -SelectionAttemptDigest $bound.SelectionAttemptDigest -SelectionPath $bound.SelectionPath -SelectionDigest $bound.SelectionDigest
        $state = Get-Content "$stateRoot/last-run.json" -Raw | ConvertFrom-Json
        if ($platform -eq 'linux') {
            $record = ConvertTo-NightlyResultRecord $run
            Assert-C1323 ($state.testsPassed -and -not $state.coverageComplete -and $state.coverageScope -ceq 'PlatformDiagnostic' -and $state.platform -ceq 'linux' -and $state.reasons -contains 'windows-rc-obligations-outstanding' -and $record.coverageScope -ceq 'PlatformDiagnostic' -and $record.reasons -contains 'windows-rc-obligations-outstanding') 'linux-state-is-diagnostic' ($state | ConvertTo-Json -Depth 4 -Compress)
            Assert-C1323 ($run.CreditKind -eq '' -and (Get-ReleaseGateCreditVerdict $state).Reasons -contains 'full-credit-scope-required') 'linux-credit-refuses'
            Assert-C1323 ([IO.File]::ReadAllText($green) -ceq 'candidate sentinel' -and [IO.File]::ReadAllText($masterGreen) -ceq 'master sentinel') 'linux-leaves-green-files-identical'
        } else {
            Assert-C1323 ($run.ExitCode -eq 0 -and $state.coverageComplete -and $state.coverageScope -ceq 'Full' -and $state.platform -ceq 'windows' -and $run.CreditKind -ceq 'rc-release' -and [IO.File]::ReadAllText($green) -cne 'candidate sentinel') 'windows-state-preserves-full-behavior' ($state | ConvertTo-Json -Depth 4 -Compress)
        }
    }
    $unknownSeams = Write-C1323ExecutionSeams $fx 'unknown'
    $unknown = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/unknown" -Profile rc -Sha $fx.Sha -SeamsPath $unknownSeams
    $refusalRecord = ConvertTo-Json (ConvertTo-NightlyResultRecord $unknown) -Compress
    $legacyRefusal = '{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}'
    Assert-C1323 ($unknown.ExitCode -eq 3 -and $unknown.Refusal -ceq 'unsupported-host-platform' -and -not (Test-Path "$($fx.Root)/unknown-commands.log") -and $refusalRecord -ceq $legacyRefusal) 'unknown-platform-launches-nothing'
    Assert-C1323 ($linux.coverageScope -ceq 'PlatformDiagnostic' -and $linux.reasons -contains 'windows-rc-obligations-outstanding') 'diagnostic-return-value'
}

function Test-C1415_SkipExclusions {
    # CARD-1415: the shipped rc policy excludes exactly the by-design skipped identities, so a
    # Windows rc launch never runs them and its census stays complete; siblings stay required.
    # CARD-1425: the CARD-1281 producer-pack cases run from an in-test fixture, so they are
    # required on every platform and only the two Windows-only rows remain.
    . "$PSScriptRoot/lib/nightly-tests-impl.ps1"
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $ns = 'Antiphon.Tests.Application'
    $methods = [ordered]@{
        AdvisoryChangePolicyTests = @('C1303_R12_LandRefusesUnavailableCorrectedEvidence', 'C1281_ReceiptCorrection', 'C1281_IncompleteNeverClean', 'C1281_AdvisoryDoesNotRerun')
        PipelineSpeedIntegrationTests = @('C1281_B1Authorization', 'C1281_B1ReceiptImport', 'C1281_B1CorrectionTransaction', 'C1281_WiredRoundtrip', 'C1281_WiredRoundtripDelivery')
        AgentTaskDispatchBaseGuardTests = @('C1213_RemoteSourceLandingKeepsLegacyIdentity', 'C1213_SourceLandingKeepsLegacyIdentity')
        AgentTaskDispatchFailureTests = @('a_git_timeout_fails_one_task_not_the_tick', 'A_failing_dispatch_does_not_block_the_queue')
    }
    $nodes = @()
    foreach ($class in $methods.Keys) {
        foreach ($m in $methods[$class]) {
            # C1303 R12 is argument-expanded: three UIDs, one method segment.
            $cases = if ($m -ceq 'C1303_R12_LandRefusesUnavailableCorrectedEvidence') { @('missing-blob', 'missing-bundle', 'changed-blob') } else { @('') }
            foreach ($c in $cases) {
                $nodes += [pscustomobject]@{ Uid = "$class.$m($c)"; ClassName = "$ns.$class"; Type = $class; Method = $m; Categories = @('Integration'); State = 'Discovered' }
            }
        }
    }
    $pack = @($nodes | Where-Object { $_.Method -cin @('C1303_R12_LandRefusesUnavailableCorrectedEvidence', 'C1281_ReceiptCorrection', 'C1281_IncompleteNeverClean', 'C1281_B1Authorization', 'C1281_B1ReceiptImport', 'C1281_B1CorrectionTransaction', 'C1281_WiredRoundtrip') } | ForEach-Object Uid)
    $windowsOnly = @($nodes | Where-Object { $_.Method -cin @('C1213_RemoteSourceLandingKeepsLegacyIdentity', 'a_git_timeout_fails_one_task_not_the_tick') } | ForEach-Object Uid)
    $rows = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -ceq 'CARD-1415')
    Assert-C1323 ($rows.Count -eq 2 -and @($rows | Where-Object platform -cne 'windows').Count -eq 0 -and @($rows | Where-Object { [string]::IsNullOrWhiteSpace($_.reason) -or $_.suite -cne 'antiphon' -or @($_.methods).Count -eq 0 }).Count -eq 0) 'c1415-two-method-scoped-rows' ($rows | ConvertTo-Json -Depth 4 -Compress)
    $missing = @()
    foreach ($row in $rows) {
        foreach ($m in @($row.methods)) {
            $hit = @(Get-ChildItem -Path "$repoRoot/tests/Antiphon.Tests/Application" -Filter "$($row.class)*.cs" -File | Select-String -SimpleMatch "Task $m(")
            if ($hit.Count -eq 0) { $missing += "$($row.class).$m" }
        }
    }
    Assert-C1323 ($missing.Count -eq 0) 'c1415-excluded-methods-exist' ($missing -join ',')
    $packLaunched = @()
    foreach ($platform in @('windows', 'linux')) {
        $exclusions = @(Get-NightlyProfileExclusions $shipped.Object 'rc' 'antiphon' -Platform $platform)
        $expected = if ($platform -eq 'windows') { $windowsOnly } else { @() }
        $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware
        $excluded = @($census.Excluded | ForEach-Object { $_.uid })
        Assert-C1323 ((Same-C1323Set $excluded $expected) -and @($census.Excluded | Where-Object owner -cne 'CARD-1415').Count -eq 0) "c1415-$platform-excludes-exactly" ($excluded -join ',')
        $plan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $launched = @()
        foreach ($group in $plan) {
            if ([string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
                $launched += @($nodes | Where-Object { @($group.classes) -ccontains $_.ClassName } | ForEach-Object Uid)
            } else { $launched += @($group.nodes | ForEach-Object Uid) }
        }
        $required = @($nodes.Uid | Where-Object { $expected -cnotcontains $_ })
        Assert-C1323 (Same-C1323Set $launched $required) "c1415-$platform-launches-only-required" ($launched -join ',')
        $packLaunched += @($pack | Where-Object { $launched -ccontains $_ } | ForEach-Object { "$platform/$_" })
        # The executor reports only launched cases; all passing leaves no skipped row.
        $terminal = @($launched | ForEach-Object { [pscustomobject]@{ Identity = $_; Outcome = 'Passed' } })
        $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $terminal -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
        Assert-C1323 ($verdict.coverageComplete -and $verdict.testsPassed -and $verdict.skipped -eq 0) "c1415-$platform-census-complete" ($verdict | ConvertTo-Json -Compress)
    }
    # CARD-1425: all nine producer-pack cases are launched on both platforms.
    Assert-C1323 ($pack.Count -eq 9 -and $packLaunched.Count -eq 18) 'c1425-pack-methods-required' ($packLaunched -join ',')
    # Control: the same identities run and skipped (no exclusion) leave the census incomplete.
    $skipped = @($nodes | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = $(if ($windowsOnly -ccontains $_.Uid) { 'Skipped' } else { 'Passed' }) } })
    $control = ConvertTo-NightlyCoverageVerdict -TerminalRows $skipped -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
    Assert-C1323 (-not $control.coverageComplete -and $control.skipped -eq 2) 'c1415-launched-skips-stay-incomplete' ($control | ConvertTo-Json -Compress)
}

function Test-LinuxSessionRunnerAdmission {
    . "$PSScriptRoot/lib/nightly-tests-impl.ps1"
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    # Independent expanded roster; check it against source rather than deriving expectations
    # from the exclusions under test. These are Linux checkpoint obligations, not Windows ones.
    $roster = [ordered]@{
        UnixPtyArgvAdmissionTests = [ordered]@{
            Malformed_inputs_reach_existing_runtime_validation = 1
            Null_exe_keeps_existing_validation_message_on_launch_routes = 2
            Nul_request_is_refused_before_effects = 4
            Nul_refusal_is_named_on_both_launch_routes = 2
            Phone_home_nul_refusal_is_named = 2
            Platform_and_backend_boundaries_keep_existing_verdicts = 4
        }
        AgentPinPublicationTests = [ordered]@{
            V34_PublicationAdmission = 1; V14_PublicationReplay = 1; V14_PublicationFences = 1
            V13_PublicationCustody = 1; V15_PublicationCleanup = 1; V14_PublicationSerialization = 1
        }
        AgentPinWorkspaceStoreTests = [ordered]@{
            V32_InspectPosixPaths = 1; V13_InspectPathSafety = 1; V34_InspectProtocolAdmission = 1
        }
    }
    $nodes = @()
    foreach ($class in $roster.Keys) {
        $source = [IO.File]::ReadAllText((Join-Path $repoRoot "tests/Antiphon.SessionRunner.Tests/$class.cs"))
        $methods = @([regex]::Matches($source, '(?s)\[Test\]\s*(?<attrs>(?:\[Arguments\([^\r\n]*\)\]\s*)*)public async Task (?<method>\w+)\('))
        $actual = @($methods | ForEach-Object { $_.Groups['method'].Value })
        $countsMatch = $true
        foreach ($method in $methods) {
            $name = $method.Groups['method'].Value
            $count = [Math]::Max(1, [regex]::Matches($method.Groups['attrs'].Value, '\[Arguments\(').Count)
            if (-not $roster[$class].Contains($name) -or $roster[$class][$name] -ne $count) { $countsMatch = $false }
        }
        Assert-C1323 ((Same-C1323Set $actual @($roster[$class].Keys)) -and $countsMatch) "linux-runner-source-roster-$class"
        foreach ($method in $roster[$class].Keys) {
            for ($i = 0; $i -lt $roster[$class][$method]; $i++) {
                $nodes += [pscustomobject]@{ Uid = "$class.$method/$i"; ClassName = "Antiphon.SessionRunner.Tests.$class"; Type = $class; Method = $method; Categories = @('Integration'); State = 'Discovered' }
            }
        }
    }
    $linuxUids = @($nodes.Uid)
    $nodes += [pscustomobject]@{ Uid = 'portable-sibling'; ClassName = 'Antiphon.SessionRunner.Tests.PortableSiblingTests'; Type = 'PortableSiblingTests'; Method = 'Runs'; Categories = @('Integration'); State = 'Discovered' }
    $rows = @($shipped.Object.profiles.rc.exclusions | Where-Object { $_.suite -ceq 'session-runner' -and $_.class -cin @($roster.Keys) })
    Assert-C1323 ($rows.Count -eq 3 -and (Same-C1323Set $rows.class @($roster.Keys)) -and @($rows | Where-Object { $_.platform -cne 'windows' -or @($_.methods | Where-Object { $_ }).Count -gt 0 -or -not $_.reason -or -not $_.owner }).Count -eq 0) 'linux-runner-three-windows-rows'
    foreach ($platform in @('windows', 'linux')) {
        $exclusions = @(Get-NightlyProfileExclusions $shipped.Object 'rc' 'session-runner' -Platform $platform)
        $expectedRequired = if ($platform -ceq 'windows') { @('portable-sibling') } else { @($nodes.Uid) }
        $expectedExcluded = if ($platform -ceq 'windows') { $linuxUids } else { @() }
        $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware
        Assert-C1323 ($census.Ok -and (Same-C1323Set @($census.Required.uid) $expectedRequired) -and (Same-C1323Set @($census.Excluded.uid) $expectedExcluded)) "linux-runner-$platform-census" "required=$($census.RequiredCount) excluded=$($census.ExcludedCount)"
        Assert-C1323 (@($census.Excluded | Where-Object { -not $_.reason -or -not $_.owner }).Count -eq 0) "linux-runner-$platform-attribution"
        $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $launched = @()
        foreach ($chunk in $chunks) {
            if ([string]::IsNullOrWhiteSpace([string]$chunk.exactFilter)) {
                $launched += @($nodes | Where-Object { @($chunk.classes) -ccontains $_.ClassName } | ForEach-Object Uid)
            } else { $launched += @($chunk.nodes | ForEach-Object Uid) }
        }
        Assert-C1323 (Same-C1323Set $launched $expectedRequired) "linux-runner-$platform-launch-plan" ($launched -join ',')
    }
    # Frozen RC authority always resolves Windows, including on a Linux coordinator.
    $authority = ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes((Join-Path $repoRoot 'tests/test-execution-policy.json')))
    $authorityNodes = @($nodes | ForEach-Object { [pscustomobject]@{ uid = $_.Uid; class = $_.ClassName; method = $_.Method } })
    Assert-C1323 (Same-C1323Set @(Get-ReleaseAuthorityRequiredUids $authorityNodes $authority.Exclusions 'session-runner') @('portable-sibling')) 'linux-runner-frozen-windows-authority'
    # A skip alone is insufficient: the coverage verdict deliberately keeps it incomplete.
    $skipped = @($nodes | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = $(if ($linuxUids -ccontains $_.Uid) { 'Skipped' } else { 'Passed' }) } })
    $control = ConvertTo-NightlyCoverageVerdict -TerminalRows $skipped -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
    Assert-C1323 (-not $control.coverageComplete -and $control.skipped -eq 24) 'linux-runner-skip-alone-incomplete'
    Assert-C1323 ($linuxUids.Count -eq 24 -and $shipped.Object.profiles.rc.requiredSuites -ccontains 'session-runner') 'linux-runner-24-obligations-retained'
}

function Test-C1323RawHoleLost([string]$Token, [int]$Dollars, [int]$Quotes, [regex]$Literal) {
    # CARD-1711: the census ends a raw interpolated string at its first closing quote run, which is the real end unless
    # that run lies inside a hole (a nested raw string). True when it does, or when a hole holds a comment, a nested
    # verbatim or raw string, or a literal $Literal cannot skip, so that the brace count cannot be trusted.
    $depth = -1
    $skip = $Dollars + $Quotes
    foreach ($e in [regex]::Matches($Token, '\{+|\}+|\$*@\$*"|\$+"{3,}|\$+"|"+|''|/[/*]')) {
        if ($e.Index -lt $skip) { continue }
        $v = $e.Value
        if ($v[0] -ceq '{') {
            if ($depth -ge 0) { $depth += $v.Length } elseif ($v.Length -ge $Dollars) { $depth = 0 }
        } elseif ($v[0] -ceq '}') {
            if ($depth -ge 0) { $depth = if ($v.Length -le $depth) { $depth - $v.Length } else { -1 } }
        } elseif ($depth -lt 0 -or $v -ceq '""') {
            continue
        } elseif ($v -cmatch '^\$*"$' -or $v -ceq "'") {
            $m = $Literal.Match($Token, $e.Index)
            if (-not $m.Success) { return $true }
            $skip = $e.Index + $m.Length
        } else {
            return $true
        }
    }
    return $depth -ge 0
}

function Get-C1323LineStates([string]$Text) {
    # CARD-1707: the C# lexical state at the start of each line, so a line that starts with * is a comment only inside an
    # open block comment. Comments, strings and char literals are consumed in source order so a /* inside a literal opens
    # nothing; verbatim and raw strings may span lines. Returns 'code', 'string' or 'block' per line, and whether the open
    # block comment's /* was the first text on its line. $Text has LF line ends only (the census folds the others).
    # CARD-1711 A-1: a misread interpolation hole does not only fail closed: a quoted key in a multi-line verbatim hole
    # ($@"{d["k"]}) ended the string early, a /* at the start of its next line opened a phantom comment, and a later
    # *-led code line that named the token was skipped silently. Holes of regular and verbatim interpolated strings are
    # now lexed (code with balanced braces and plain string and char literals). A hole the walk cannot lex (a comment or
    # a nested verbatim, raw or interpolated string) stops the string at its {, and a raw interpolated string whose
    # closing quote run lies inside a hole is detected; either way LostFrom names the first line whose state is unknown.
    # CARD-1711 D-1: hole code may hold no bare backslash. The walk does not know format clauses, and in a regular
    # interpolated string the \" of $"{x:\"}" is an escaped quote that the walk would read as a string opener, flipping
    # its string parity; a hole with a backslash outside its string and char literals is unlexable instead.
    # CARD-1711 A-3: every $ run is anchored at its first $ and consumed atomically. An atomic group alone still retried
    # the run from each of its positions (64k: 9.5 s); the lookbehind makes the walk linear.
    $hole = '\{(?>(?:[^{}"''/@$\\]|/(?![/*])|"(?:[^"\\\n]|\\[^\n])*"|''(?:[^''\\\n]|\\[^\n])+''|(?<d>\{)|(?<-d>\}))*)(?(d)(?!))\}'
    $tokens = '//[^\n]*|(?<block>/\*[\s\S]*?(?:\*/|\z))|(?<str>(?:\$@|@\$)"(?>(?:[^"{]|""|\{\{|' + $hole + ')*)(?:"|\z|(?<lost>))' +
        '|@"(?:[^"]|"")*(?:"|\z)|(?<!\$)(?<raw>(?>\$*))(?<q>"{3,})[\s\S]*?(?:\k<q>|\z)' +
        '|(?<!\$)(?>\$+)"(?>(?:[^"\\\n{]|\\[^\n]|\{\{|' + $hole + ')*)(?:"|(?<lost>(?=\{))|))|"(?:[^"\\\n]|\\[^\n])*"?|''(?:[^''\\\n]|\\[^\n])*''?'
    $literal = [regex]('\G(?:(?>\$+)"(?>(?:[^"\\\n{]|\\[^\n]|\{\{|' + $hole + ')*)"|"(?:[^"\\\n]|\\[^\n])*"|''(?:[^''\\\n]|\\[^\n])+'')')
    $starts = [Collections.Generic.List[int]]::new()
    $starts.Add(0)
    for ($k = $Text.IndexOf([char]10); $k -ge 0; $k = $Text.IndexOf([char]10, $k + 1)) { $starts.Add($k + 1) }
    $modes = [string[]]::new($starts.Count)
    $atStart = [bool[]]::new($starts.Count)
    for ($k = 0; $k -lt $modes.Length; $k++) { $modes[$k] = 'code' }
    $lost = -1
    $line = 0
    foreach ($m in [regex]::Matches($Text, $tokens)) {
        $end = $m.Index + $m.Length
        while ($line -lt $starts.Count -and $starts[$line] -le $m.Index) { $line++ }
        if ($lost -lt 0 -and ($m.Groups['lost'].Success -or ($m.Groups['raw'].Length -gt 0 -and (Test-C1323RawHoleLost $m.Value $m.Groups['raw'].Length $m.Groups['q'].Length $literal)))) { $lost = $line - 1 }
        if ($Text.IndexOf([char]10, $m.Index, $m.Length) -lt 0) { continue }
        $mode = if ($m.Groups['block'].Success) { 'block' } elseif ($m.Groups['str'].Success) { 'string' } else { continue }
        $lead = $Text.LastIndexOf([char]10, [Math]::Max(0, $m.Index - 1)) + 1
        $first = $m.Index -eq $lead -or $Text.Substring($lead, $m.Index - $lead).Trim().Length -eq 0
        while ($line -lt $starts.Count -and $starts[$line] -lt $end) { $modes[$line] = $mode; $atStart[$line] = $first; $line++ }
    }
    return [pscustomobject]@{ Modes = $modes; AtStart = $atStart; LostFrom = $lost }
}

function Get-C1323LinuxOnlyCensus {
    # CARD-1686: the policy rows are the only list. Each prefixed row needs a [NativeLinuxOnly] marker under
    # its suite's project directory, and each marker needs a row. A marker file that a selection rule names
    # must route to the census set; an unnamed file already falls back to a full selection.
    param([string]$Root, $Policy, $Catalog, [string]$CensusSet = 'release-gate.platform',
        [string[]]$FixtureProjects = @('tests/Antiphon.NativeLinuxPolicy.Probe/'),
        # These files name the type without marking a test: the definition (class and constructor declarations, an
        # error string) and the witnesses that read, reflect over or construct it (typeof, new, a resource-name string).
        # Only those reference forms are exempt; any other use in these files fails closed like anywhere else.
        [string[]]$ExemptFiles = @(
            'tests/Shared/NativeLinuxOnlyAttribute.cs'
            'tests/Shared/NativeLinuxPolicyWitness.cs'
            'tests/Shared/NativeLinuxPolicyWitnessTests.cs'
            'tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs'
            'tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs'
        ),
        [string[]]$RoutedPaths = @('tests/test-execution-policy.json', 'scripts/test-rc-platform.ps1'))
    $prefix = 'Linux-only by construction:'
    $problems = [Collections.Generic.List[string]]::new()
    $suiteDirs = [ordered]@{}
    foreach ($suite in $Policy.suites.PSObject.Properties) {
        $project = [string]$suite.Value.project
        if ($project) { $suiteDirs[$suite.Name] = ($project.Substring(0, $project.LastIndexOf('/') + 1)) }
    }
    $rowKeys = @()
    foreach ($row in @($Policy.profiles.rc.exclusions | Where-Object { [string]$_.reason -like ($prefix + '*') })) {
        $methods = @($row.methods | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { [string]$_ } | Sort-Object -CaseSensitive -Unique)
        $key = ([string]$row.suite) + '|' + ([string]$row.class) + '|' + ($methods -join ',')
        $rowKeys += $key
        if ([string]$row.platform -cne 'windows') { $problems.Add("row-platform:$key") }
        if ([string]$row.owner -cnotmatch '^CARD-[0-9]{4}$') { $problems.Add("row-owner:$key") }
        if (-not $suiteDirs.Contains([string]$row.suite)) { $problems.Add("row-suite:$key") }
    }
    $files = @()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push((Join-Path $Root 'tests'))
    while ($pending.Count) {
        $dir = $pending.Pop()
        foreach ($child in [IO.Directory]::EnumerateDirectories($dir)) {
            if (@('bin', 'obj', 'node_modules', '.git') -cnotcontains [IO.Path]::GetFileName($child)) { $pending.Push($child) }
        }
        foreach ($file in [IO.Directory]::EnumerateFiles($dir, '*.cs')) { $files += $file }
    }
    $classMarkers = [ordered]@{}
    $methodMarkers = [ordered]@{}
    $markerFiles = @()
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('/', '\').Length + 1
    foreach ($file in ($files | Sort-Object -CaseSensitive)) {
        $text = [IO.File]::ReadAllText($file)
        if (-not $text.Contains('NativeLinuxOnly')) { continue }
        $rel = [IO.Path]::GetFullPath($file).Substring($rootPrefix).Replace('\', '/')
        if (@($FixtureProjects | Where-Object { $rel.StartsWith($_, [StringComparison]::Ordinal) }).Count) { continue }
        # Line-based and fail closed. A line whose trimmed text starts with [NativeLinuxOnly( is a canonical marker; a line
        # that starts with // is a comment line, and so is one that starts with /* unless a */ precedes the token (the
        # comment closed and code follows). CARD-1707: the block-comment state is carried across lines (strings and char
        # literals skipped), so a line that starts with * is a comment line only inside a block comment that opened at
        # the start of its line; any other line inside an open block comment, or inside a multi-line string, that names
        # the token fails closed instead of counting. Any other line (and any second use on a marker line) that names
        # the whole-word token fails closed; an exempt file strips its reference forms first. CARD-1711 A-2: lines are C#
        # lines, ended by CR LF, CR, LF, U+2028, U+2029 or U+0085 (line numbers count those ends too). A line from one whose
        # lexical state the walk lost (an interpolation hole it cannot lex) that names the token fails closed.
        $lines = [regex]::Split($text, "`r`n|[`r`n`u{2028}`u{2029}`u{0085}]")
        $exempt = $ExemptFiles -ccontains $rel
        $token = 'NativeLinuxOnly(?:Attribute)?'
        $qualified = '(?:global::)?(?:\w+\.)*' + $token
        $references = '\btypeof\(\s*' + $qualified + '\s*\)|\bnew\s+' + $qualified + '\s*\(|\bclass\s+' + $token + '\b|\bpublic\s+' + $token + '\s*\(|"[^"\\()\[\],/*]*\b' + $token + '\b[^"\\()\[\],/*]*"'
        $hits = @()
        $states = Get-C1323LineStates ($lines -join "`n")
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $trimmed = $lines[$i].Trim()
            $scan = $lines[$i]
            if ($states.LostFrom -ge 0 -and $i -ge $states.LostFrom) {
                if ([regex]::IsMatch($lines[$i], '\b' + $token + '\b')) { $problems.Add("marker-noncanonical:${rel}:$($i + 1)") }
                continue
            }
            if ($states.Modes[$i] -ceq 'block') {
                $close = $lines[$i].IndexOf('*/', [StringComparison]::Ordinal)
                if ($close -lt 0 -or -not [regex]::IsMatch($lines[$i].Substring($close + 2), '\b' + $token + '\b')) {
                    if ([regex]::IsMatch($lines[$i], '\b' + $token + '\b') -and -not ($states.AtStart[$i] -and ($trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')))) {
                        $problems.Add("marker-noncanonical:${rel}:$($i + 1)")
                    }
                    continue
                }
                if ($exempt) { $scan = [regex]::Replace($lines[$i], $references, '') }
            } elseif ($states.Modes[$i] -cne 'code') {
                if ($exempt) { $scan = [regex]::Replace($lines[$i], $references, '') }
            } elseif ($trimmed.StartsWith('[NativeLinuxOnly(', [StringComparison]::Ordinal)) {
                $hits += $i
                $scan = [regex]::Replace($lines[$i], '^\s*\[NativeLinuxOnly\((?:"(?:[^"\\]|\\.)*"|[^)"])*\)\]', '')
                if ($scan -ceq $lines[$i]) { $scan = $trimmed.Substring('[NativeLinuxOnly'.Length) }
            } elseif ($trimmed.StartsWith('//') -or ($trimmed.StartsWith('/*') -and -not [regex]::IsMatch($trimmed, '\*/.*\b' + $token + '\b'))) {
                continue
            } elseif ($exempt) {
                $scan = [regex]::Replace($lines[$i], $references, '')
            }
            if ([regex]::IsMatch($scan, '\bNativeLinuxOnly(?:Attribute)?\b')) { $problems.Add("marker-noncanonical:${rel}:$($i + 1)") }
        }
        if ($hits.Count -eq 0) { continue }
        $suite = @($suiteDirs.Keys | Where-Object { $rel.StartsWith($suiteDirs[$_], [StringComparison]::Ordinal) })
        if ($suite.Count -ne 1) { $problems.Add("marker-outside-suite:$rel"); continue }
        $markerFiles += $rel
        foreach ($i in $hits) {
            $indent = ([regex]::Match($lines[$i], '^\s*')).Length
            $rest = [regex]::Replace($lines[$i], '^\s*\[NativeLinuxOnly\((?:"(?:[^"\\]|\\.)*"|[^)"])*\)\]', '')
            $j = $i
            $decl = $rest
            while ($true) {
                $decl = [regex]::Replace($decl, '^\s*(?:\[(?:[^\[\]]|\[[^\[\]]*\])*\]\s*)*', '')
                if ($decl -and -not $decl.StartsWith('//')) { break }
                $j++
                if ($j -ge $lines.Count) { $decl = ''; break }
                $decl = $lines[$j]
            }
            $class = [regex]::Match($decl, '^(?:(?:public|internal|private|protected|sealed|static|abstract|partial|file)\s+)*(?:class|record|struct)\s+(\w+)')
            $method = [regex]::Match($decl, '^(?:(?:public|internal|private|protected|static|async|override|virtual|new)\s+)+[\w<>\[\],.? ]+?\s(\w+)\s*(?:<[^>]*>)?\s*\(')
            if ($class.Success) {
                $classMarkers[$suite[0] + '|' + $class.Groups[1].Value] = $true
            } elseif ($method.Success) {
                $owner = $null
                for ($k = $i - 1; $k -ge 0; $k--) {
                    $c = [regex]::Match($lines[$k], '^(\s*)(?:(?:public|internal|private|protected|sealed|static|abstract|partial|file)\s+)*(?:class|record|struct)\s+(\w+)')
                    if ($c.Success -and $c.Groups[1].Length -lt $indent) { $owner = $c.Groups[2].Value; break }
                }
                if (-not $owner) { $problems.Add("marker-unparsed:${rel}:$($i + 1)"); continue }
                $id = $suite[0] + '|' + $owner
                if (-not $methodMarkers.Contains($id)) { $methodMarkers[$id] = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal) }
                [void]$methodMarkers[$id].Add($method.Groups[1].Value)
            } else {
                $problems.Add("marker-unparsed:${rel}:$($i + 1)")
            }
        }
    }
    # A class row is satisfied by a class marker or by method markers: the compiled witness then proves every
    # case of the class is marked. A method row must name exactly the marked methods of a class with no class marker.
    $markerKeys = @($classMarkers.Keys | ForEach-Object { $_ + '|' })
    foreach ($id in $methodMarkers.Keys) { if (-not $classMarkers.Contains($id)) { $markerKeys += ($id + '|' + (@($methodMarkers[$id]) -join ',')) } }
    foreach ($key in ($markerKeys | Sort-Object -CaseSensitive)) {
        $classKey = $key.Substring(0, $key.LastIndexOf('|') + 1)
        if ($rowKeys -cnotcontains $key -and $rowKeys -cnotcontains $classKey) { $problems.Add("marker-without-row:$key") }
    }
    foreach ($key in ($rowKeys | Sort-Object -CaseSensitive)) {
        $id = $key.Substring(0, $key.LastIndexOf('|'))
        $found = if ($key.EndsWith('|')) { $classMarkers.Contains($id) -or $methodMarkers.Contains($id) } else { $markerKeys -ccontains $key }
        if (-not $found) { $problems.Add("row-without-marker:$key") }
    }
    if ($null -ne $Catalog) {
        foreach ($path in @($RoutedPaths + $markerFiles)) {
            $sets = @(); $full = $false; $named = $false
            foreach ($rule in @($Catalog.rules)) {
                $match = @($rule.paths | Where-Object { $_ -ceq $path }).Count -gt 0
                if (-not $match -and $rule.pattern) { $match = [regex]::IsMatch($path, [string]$rule.pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant) }
                if ($match) { $named = $true; $sets += @($rule.sets); if ($rule.full) { $full = $true } }
            }
            foreach ($edge in @($Catalog.edges)) { if ([string]$edge.path -ceq $path) { $named = $true; $sets += @($edge.sets) } }
            $explicit = $sets -ccontains $CensusSet
            $ok = if ($RoutedPaths -ccontains $path) { $explicit } else { $explicit -or $full -or -not $named }
            if (-not $ok) { $problems.Add("marker-file-unrouted:$path") }
        }
    }
    return [pscustomobject]@{ Ok = ($problems.Count -eq 0); Problems = @($problems); RowKeys = @($rowKeys); MarkerKeys = @($markerKeys); MarkerFiles = @($markerFiles) }
}

function Test-LinuxOnlyWindowsExclusionPolicy {
    # Windows rc exclusions for skips that are Linux-only by construction. Gap classes stay required.
    # CARD-1613: compiled NativeLinuxPolicyWitnessTests owns the reflection proof in each suite.
    # CARD-1686: the census is derived from the shipped rows and the source markers; there is no second list.
    $prefix = 'Linux-only by construction:'
    $decisionClasses = @('ClaudeDialogWatchdogCanaryTests','CheckpointOwnedOutputTests','CheckpointScriptReaderDeadlineTests','CheckpointShapeConflictTests','CheckpointTrackedBaselineTests','CheckpointOutputConfinementTests','RunnerWorkspaceBindingTests')
    $decisionMethods = @(
        'OutputCleanupTests/parent_symlink_does_not_delete_a_tracked_stamp'
        'CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes'
        'PlanCoverageCommandTests/existing_outside_project_is_refused_by_root_boundary'
        'RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused'
    )
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $catalog = Read-TestSelectionCatalog "$repoRoot/tests/test-selection-policy.json" $shipped.Object.selectionCatalog.hash
    $shippedRows = @($shipped.Object.profiles.rc.exclusions | Where-Object { [string]$_.reason -like ($prefix + '*') })
    $inventory = @($shippedRows | ForEach-Object {
        @{ s = [string]$_.suite; c = [string]$_.class; o = [string]$_.owner; m = @($_.methods | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) }
    })
    $census = Get-C1323LinuxOnlyCensus -Root $repoRoot -Policy $shipped.Object -Catalog $catalog
    $rowProblems = @($census.Problems | Where-Object { $_ -like 'row-platform:*' -or $_ -like 'row-owner:*' -or $_ -like 'row-suite:*' })
    Assert-C1323 ($inventory.Count -gt 0 -and $rowProblems.Count -eq 0) 'every-new-row-has-linux-gate' (($rowProblems + @('rows=' + $inventory.Count)) -join '; ')
    $markerProblems = @($census.Problems | Where-Object { $rowProblems -cnotcontains $_ })
    $decisionMarked = @($decisionClasses | Where-Object { $name = $_; @($census.MarkerKeys | Where-Object { $_.Split('|')[1] -ceq $name }).Count -gt 0 })
    Assert-C1323 ($markerProblems.Count -eq 0 -and $decisionMarked.Count -eq 0 -and $census.MarkerKeys.Count -gt 0) 'markers-and-rows-agree' (($markerProblems + @('decision-marked=' + ($decisionMarked -join ','), 'markers=' + $census.MarkerKeys.Count)) -join '; ')
    $decisionHits = @()
    foreach ($name in $decisionClasses) {
        $hit = @($shipped.Object.profiles.rc.exclusions | Where-Object { [string]$_.class -ceq $name })
        if ($hit.Count -gt 0) { $decisionHits += $name }
    }
    foreach ($pair in $decisionMethods) {
        $class, $method = $pair.Split('/')
        foreach ($row in @($shipped.Object.profiles.rc.exclusions | Where-Object { [string]$_.class -ceq $class })) {
            $methods = @($row.methods | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
            if ($methods.Count -eq 0 -or $methods -ccontains $method) { $decisionHits += $pair }
        }
    }
    Assert-C1323 ($decisionHits.Count -eq 0) 'decision-classes-are-not-class-excluded' ($decisionHits -join ',')
    $nodes = @()
    foreach ($row in $inventory) {
        $methods = @($row.m)
        if ($methods.Count -eq 0) { $methods = @('LinuxOnlyCase') }
        foreach ($m in $methods) {
            $nodes += [pscustomobject]@{ Suite = $row.s; Class = $row.c; Method = $m; Uid = ($row.c + '.' + $m); Gap = $false }
        }
    }
    $gaps = @(
        @{ s='antiphon'; c='ClaudeDialogWatchdogCanaryTests'; m='C680_LiveNudgeRefusesPermissionAndQuestionDialogs' }
        @{ s='antiphon'; c='OutputCleanupTests'; m='parent_symlink_does_not_delete_a_tracked_stamp' }
        @{ s='antiphon'; c='CheckpointOwnedOutputTests'; m='changed_inventory_is_refused' }
        @{ s='antiphon'; c='CheckpointScriptReaderDeadlineTests'; m='script_reader_re_arms_its_stall_window_and_the_stamp_reports_unreadable_apart_from_tampered' }
        @{ s='session-runner'; c='CompletedCardWorkspaceRemovalTests'; m='C1032_InspectionHoldShapes' }
        @{ s='antiphon'; c='CheckpointShapeConflictTests'; m='shape_directory_on_file' }
        @{ s='antiphon'; c='CheckpointTrackedBaselineTests'; m='local_output_property_tampering_is_reported' }
        @{ s='antiphon'; c='CheckpointOutputConfinementTests'; m='imported_props_build_preserves_the_tracked_dependency' }
        @{ s='session-runner'; c='RunnerWorkspaceBindingTests'; m='C1032_StoreIdentityRefused' }
    )
    foreach ($g in $gaps) { $nodes += [pscustomobject]@{ Suite = $g.s; Class = $g.c; Method = $g.m; Uid = ($g.c + '.' + $g.m); Gap = $true } }
    $nodes += [pscustomobject]@{ Suite = 'antiphon'; Class = 'PortableCensusSiblingTests'; Method = 'Runs'; Uid = 'PortableCensusSiblingTests.Runs'; Gap = $false }
    $cache = @{}
    function Test-ExcludedNode($node, [string]$platform) {
        $key = $node.Suite + '|' + $platform
        if (-not $cache.ContainsKey($key)) { $cache[$key] = @(Get-NightlyProfileExclusions $shipped.Object 'rc' $node.Suite -Platform $platform) }
        $probe = [pscustomobject]@{ ClassName = $node.Class; Type = $node.Class; Method = $node.Method }
        return (Test-NightlyProfileNodeExcluded $probe $cache[$key])
    }
    $linuxOnly = @($nodes | Where-Object { -not $_.Gap -and $_.Class -cne 'PortableCensusSiblingTests' })
    $windowsLaunched = @($linuxOnly | Where-Object { -not (Test-ExcludedNode $_ 'windows') })
    $siblingLaunched = @($nodes | Where-Object { $_.Class -ceq 'PortableCensusSiblingTests' -and -not (Test-ExcludedNode $_ 'windows') })
    $terminal = @($siblingLaunched | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = 'Passed' } })
    $complete = ConvertTo-NightlyCoverageVerdict -TerminalRows $terminal -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
    $completeReason = @($complete.reasons) -join ','
    Assert-C1323 ($windowsLaunched.Count -eq 0 -and $siblingLaunched.Count -eq 1 -and $complete.coverageComplete -and $completeReason -notlike '*unexecuted-outcome*') 'linux-only-rows-leave-census-complete' ("launched=" + $windowsLaunched.Count + ' ' + $completeReason)
    $gapNodes = @($nodes | Where-Object Gap)
    $gapLaunched = @($gapNodes | Where-Object { -not (Test-ExcludedNode $_ 'windows') })
    $gapTerminal = @($siblingLaunched | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = 'Passed' } })
    $gapTerminal += @($gapLaunched | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = 'NotExecuted' } })
    $incomplete = ConvertTo-NightlyCoverageVerdict -TerminalRows $gapTerminal -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
    $incompleteReason = @($incomplete.reasons) -join ','
    $gapsNamed = $true
    foreach ($g in $gapLaunched) { if ($incompleteReason -notlike ('*' + $g.Uid + '*')) { $gapsNamed = $false } }
    $inventoryNamed = $false
    foreach ($n in $linuxOnly) { if ($incompleteReason -like ('*' + $n.Uid + '*')) { $inventoryNamed = $true } }
    Assert-C1323 ($gapLaunched.Count -eq $gapNodes.Count -and -not $incomplete.coverageComplete -and $gapsNamed -and -not $inventoryNamed -and $incompleteReason -like '*unexecuted-outcome*') 'decision-gaps-stay-incomplete' $incompleteReason
    $linuxLaunched = @($linuxOnly | Where-Object { -not (Test-ExcludedNode $_ 'linux') })
    Assert-C1323 ($linuxLaunched.Count -eq $linuxOnly.Count -and $linuxOnly.Count -gt 0) 'linux-still-requires-the-rows' ("linux=" + $linuxLaunched.Count + ' inventory=' + $linuxOnly.Count)
}

function New-C1686Root($Name, $Files) {
    $root = Join-Path $ResultsDirectory "c1686-$Name"
    foreach ($path in $Files.Keys) { Write-C1323File $root $path $Files[$path] }
    return $root
}

function Test-C1686_LinuxOnlyCensus {
    # Fixture trees and policies: the census must fail for a marker without a row and a row without a marker.
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $prefix = 'Linux-only by construction: fixture.'
    $alpha = "namespace Fixture;`n`n[NativeLinuxOnly(`"needs a FIFO (see [man 7 fifo])`")]`n[Category(`"Unit`")]`npublic sealed class AlphaTests`n{`n    [Test]`n    public void Runs() { }`n}`n"
    $beta = "namespace Fixture;`n`npublic sealed partial class BetaTests`n{`n    private sealed class Rig`n    {`n    }`n`n    [NativeLinuxOnly(`"procfs, see https://man7.org/linux/man-pages/man5/proc.5.html`")]`n    [Test]`n    [Arguments(`"a`", Categories = [`"c-a`"])]`n    public async Task First(string mode) { }`n`n    // [Test, NativeLinuxOnly(`"a comment is not a marker`")]`n    [Test]`n    public void Portable() { }`n`n    [NativeLinuxOnly(`"setsid`")] [Test] public Task Second() => Task.CompletedTask;`n}`n"
    $probe = "[NativeLinuxOnly(`"probe fixture`")]`npublic sealed class DirectClassProbe { }`n"
    $files = [ordered]@{
        'tests/Antiphon.Tests/Scripts/AlphaTests.cs' = $alpha
        'tests/Antiphon.SessionRunner.Tests/BetaTests.cs' = $beta
        'tests/Antiphon.NativeLinuxPolicy.Probe/ProbeTests.cs' = $probe
        'tests/Antiphon.Tests/bin/Debug/Copied.cs' = "[NativeLinuxOnly(`"build output`")]`npublic sealed class CopiedTests { }`n"
        'tests/Antiphon.Tests/PortableTests.cs' = "public sealed class PortableTests { }`n"
        'tests/Shared/NativeLinuxOnlyAttribute.cs' = "public sealed class NativeLinuxOnlyAttribute : SkipAttribute`n{`n    public NativeLinuxOnlyAttribute(string reason) : base(reason) { }`n    internal static object Pin() => new Antiphon.TestSupport.NativeLinuxOnlyAttribute(`"pin`");`n}`n"
    }
    function New-C1686Policy($Rows) {
        $policy = Copy-C1323Object $shipped.Object
        $policy.profiles.rc.exclusions = @(@{ suite = 'antiphon'; class = 'LegacyTests'; reason = 'legacy row'; owner = 'CARD-1313' }) + @($Rows)
        return (Copy-C1323Object $policy)
    }
    $rows = @(
        @{ suite = 'antiphon'; class = 'AlphaTests'; platform = 'windows'; reason = $prefix; owner = 'CARD-1686' }
        @{ suite = 'session-runner'; class = 'BetaTests'; methods = @('Second', 'First'); platform = 'windows'; reason = $prefix; owner = 'CARD-1686' }
    )
    $catalog = @{ rules = @(@{ id = 'R-CENSUS'; paths = @('tests/test-execution-policy.json', 'scripts/test-rc-platform.ps1'); sets = @('release-gate.platform'); full = $false }); edges = @() }
    $good = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'good' $files) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 ($good.Ok -and (Same-C1323Set $good.MarkerKeys @('antiphon|AlphaTests|', 'session-runner|BetaTests|First,Second')) -and (Same-C1323Set $good.MarkerFiles @('tests/Antiphon.SessionRunner.Tests/BetaTests.cs', 'tests/Antiphon.Tests/Scripts/AlphaTests.cs'))) 'c1686-consistent-census-passes' (($good.Problems + $good.MarkerKeys) -join '; ')

    $extra = [ordered]@{} + $files
    $extra['tests/Antiphon.Tests/Checkpoints/GammaTests.cs'] = "public sealed class Holder`n{`n}`n`n[NativeLinuxOnly(`"statx`")]`ninternal sealed class GammaTests { }`n"
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'class-marker' $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 (Same-C1323Set $r.Problems @('marker-without-row:antiphon|GammaTests|')) 'c1686-class-marker-without-row-fails' ($r.Problems -join '; ')

    $extra = [ordered]@{} + $files
    $extra['tests/Antiphon.SessionRunner.Tests/BetaTests.cs'] = $beta.Replace("    public void Portable() { }", "    public void Portable() { }`n`n    [NativeLinuxOnly(`"unshare`")]`n    [Test]`n    public static async Task<int> Third() => 0;")
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'method-marker' $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 (Same-C1323Set $r.Problems @('marker-without-row:session-runner|BetaTests|First,Second,Third', 'row-without-marker:session-runner|BetaTests|First,Second')) 'c1686-method-marker-without-row-fails' ($r.Problems -join '; ')

    $more = @($rows) + @(@{ suite = 'pty-host'; class = 'DeltaTests'; platform = 'windows'; reason = $prefix; owner = 'CARD-1686' }, @{ suite = 'antiphon'; class = 'PortableTests'; methods = @('Runs'); platform = 'windows'; reason = $prefix; owner = 'CARD-1686' })
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'row' $files) -Policy (New-C1686Policy $more) -Catalog $catalog
    Assert-C1323 (Same-C1323Set $r.Problems @('row-without-marker:pty-host|DeltaTests|', 'row-without-marker:antiphon|PortableTests|Runs')) 'c1686-row-without-marker-fails' ($r.Problems -join '; ')

    $bad = @(
        @{ suite = 'antiphon'; class = 'AlphaTests'; platform = 'linux'; reason = $prefix; owner = '' }
        @{ suite = 'session-runner'; class = 'BetaTests'; methods = @('First', 'Second'); platform = 'windows'; reason = $prefix; owner = 'CARD-1686' }
    )
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'attribution' $files) -Policy (New-C1686Policy $bad) -Catalog $catalog
    Assert-C1323 (Same-C1323Set $r.Problems @('row-platform:antiphon|AlphaTests|', 'row-owner:antiphon|AlphaTests|')) 'c1686-row-attribution-fails' ($r.Problems -join '; ')

    $extra = [ordered]@{} + $files
    $extra['tests/New.Fixture/EpsilonTests.cs'] = "[NativeLinuxOnly(`"fifo`")]`npublic sealed class EpsilonTests { }`n"
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'outside' $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 (Same-C1323Set $r.Problems @('marker-outside-suite:tests/New.Fixture/EpsilonTests.cs')) 'c1686-marker-outside-suite-fails' ($r.Problems -join '; ')

    $root = New-C1686Root 'route' $files
    $narrow = Copy-C1323Object $catalog
    $narrow.rules += [pscustomobject]@{ id = 'R-ALPHA'; paths = @('tests/Antiphon.Tests/Scripts/AlphaTests.cs'); sets = @('main.remainder'); full = $false }
    $narrowed = Get-C1323LinuxOnlyCensus -Root $root -Policy (New-C1686Policy $rows) -Catalog $narrow
    $narrow.rules[1].sets += 'release-gate.platform'
    $routed = Get-C1323LinuxOnlyCensus -Root $root -Policy (New-C1686Policy $rows) -Catalog $narrow
    $unrouted = Copy-C1323Object $catalog
    $unrouted.rules[0].paths = @('scripts/test-rc-platform.ps1')
    $unrouted.rules += [pscustomobject]@{ id = 'R-AUTHORITY'; pattern = '^tests/test-(execution|selection)-policy\.json$'; sets = @(); full = $true }
    $policyRoute = Get-C1323LinuxOnlyCensus -Root $root -Policy (New-C1686Policy $rows) -Catalog $unrouted
    Assert-C1323 ((Same-C1323Set $narrowed.Problems @('marker-file-unrouted:tests/Antiphon.Tests/Scripts/AlphaTests.cs')) -and $routed.Ok -and (Same-C1323Set $policyRoute.Problems @('marker-file-unrouted:tests/test-execution-policy.json'))) 'c1686-routes-name-the-census-set' ((@($narrowed.Problems) + @($routed.Problems) + @($policyRoute.Problems)) -join '; ')

    # A marker spelling the census does not parse fails closed; it is never skipped or accepted silently.
    $spellings = [ordered]@{
        'stacked-method' = @('tests/Antiphon.SessionRunner.Tests/BetaTests.cs', $beta.Replace("    [Test]`n    public void Portable()", "    [Test, NativeLinuxOnly(`"stacked`")]`n    public void Portable()"), 15)
        'stacked-class' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[Category(`"Unit`"), NativeLinuxOnly(`"stacked`")]`npublic sealed class ZetaTests { }`n", 3)
        'suffix' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[NativeLinuxOnlyAttribute(`"suffix`")]`npublic sealed class ZetaTests { }`n", 3)
        'qualified' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[Antiphon.TestSupport.NativeLinuxOnly(`"qualified`")]`npublic sealed class ZetaTests { }`n", 3)
        'global' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[global::Antiphon.TestSupport.NativeLinuxOnly(`"global`")]`npublic sealed class ZetaTests { }`n", 3)
        'leading-space' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[ NativeLinuxOnly(`"spaced`")]`npublic sealed class ZetaTests { }`n", 3)
        'paren-space' = @('tests/Antiphon.Tests/ZetaTests.cs', "namespace Fixture;`n`n[NativeLinuxOnly (`"spaced`")]`npublic sealed class ZetaTests { }`n", 3)
    }
    foreach ($name in $spellings.Keys) {
        $path, $text, $line = $spellings[$name]
        $extra = [ordered]@{} + $files
        $extra[$path] = $text
        $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root "noncanonical-$name" $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
        Assert-C1323 (Same-C1323Set $r.Problems @("marker-noncanonical:${path}:$line")) "c1686-noncanonical-$name-fails" ($r.Problems -join '; ')
    }

    # The census is line-based: it never lexes C# strings or comments. Every use of the token outside a canonical
    # marker line, a line that starts as a comment and an exempt file fails closed, even where C# would ignore it.
    $z = 'tests/Antiphon.Tests/ZetaTests.cs'
    $failClosed = [ordered]@{
        'verbatim-interpolated-glob' = @($z, "namespace Fixture;`n`npublic sealed class ZTests`n{`n    static string P(string r) => @`$`"{r}\`" + `"**/*.cs`";`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n    const string H = `"a/**/b`";`n}`n", @('marker-without-row:antiphon|ZTests|M'))
        'split-line' = @($z, "namespace Fixture;`n`n[NativeLinuxOnly`n    (`"x`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:3"))
        'split-line-comment' = @($z, "namespace Fixture;`n`n[NativeLinuxOnly // why`n(`"x`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:3"))
        'using-alias' = @($z, "using Lin = Antiphon.TestSupport.NativeLinuxOnlyAttribute;`n`nnamespace Fixture;`n`n[Lin(`"x`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:1"))
        'string-literal' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    const string Name = `"NativeLinuxOnly`";`n}`n", @("marker-noncanonical:${z}:5"))
        'trailing-comment' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    [Test] // [NativeLinuxOnlyAttribute(`"trailing comment`")]`n    public void Runs() { }`n}`n", @("marker-noncanonical:${z}:5"))
        'second-use-on-marker-line' = @($z, "namespace Fixture;`n`n[NativeLinuxOnly(`"x`")] [Category(`"Unit`"), NativeLinuxOnly(`"y`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:3", 'marker-without-row:antiphon|ZetaTests|'))
        # CARD-1707: inside an open block comment only a line that starts with * or /* reads as a comment line, and only
        # when the comment opened at the start of its line. Any other line carrying the token is not a marker (C# ignores
        # it) but still fails closed, so a misread comment state can never hide a marker or count a commented-out one.
        'block-comment-interior' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /*`n    [NativeLinuxOnly(`"block comment`")]`n    public void Old() { }`n    [Test, NativeLinuxOnly(`"block comment`")] */`n}`n", @("marker-noncanonical:${z}:6", "marker-noncanonical:${z}:8"))
        'block-comment-star-interior' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /*`n     * [NativeLinuxOnly(`"block comment`")]`n     */`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @('marker-without-row:antiphon|ZetaTests|M'))
        # CARD-1707: a block comment's closing line reads its own state. A starred closing line is a comment line (an
        # empty expectation: no problem); a canonical-looking closing line is inside the comment, so it fails closed.
        'block-comment-star-close' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /*`n     * [NativeLinuxOnly(`"old`")] */`n    public void M() { }`n}`n", @())
        'block-comment-canonical-close' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /*`n    [NativeLinuxOnly(`"old`")] */`n    public void M() { }`n}`n", @("marker-noncanonical:${z}:6"))
        'block-comment-mid-line-open' =@($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    int a = 1; /* note`n     * [NativeLinuxOnly(`"y`")]`n     */`n}`n", @("marker-noncanonical:${z}:6"))
        # CARD-1707: a line that starts with * outside a block comment is code (a multiplication split across lines).
        'split-multiplication' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    int x = 2`n        * 3; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:6"))
        'nested-block-comment' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /* outer /* inner */`n     * [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:6"))
        # CARD-1707: a /* inside a string or char literal opens no comment. The canonical marker on the next line counts;
        # a misread literal would open a comment that hides it.
        'string-literal-opener' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int Y = `"/*`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void N() { }`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @("marker-noncanonical:${z}:6", 'marker-without-row:antiphon|ZetaTests|M'))
        'char-literal-quote' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int Y = '`"' + `"/*`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void N() { }`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @("marker-noncanonical:${z}:6", 'marker-without-row:antiphon|ZetaTests|M'))
        'char-literal-quote-before-string' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static readonly char Q = '`"'; static readonly string S = `"/*`";`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @('marker-without-row:antiphon|ZetaTests|M'))
        'verbatim-multi-line-opener' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int V = @`"a `"`" b`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:7"))
        'verbatim-string-marker-line' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    const string V = @`"`n    [NativeLinuxOnly(`"`"x`"`")]`n    public void M() { }`";`n}`n", @("marker-noncanonical:${z}:6"))
        'raw-string-opener' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    const string R = `"`"`"`n        /* not a comment`n        `"`"`";`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @('marker-without-row:antiphon|ZetaTests|M'))
        # CARD-1711: an interpolation hole is lexed, so the /* in its string literal opens nothing and the marker counts
        # (CARD-1707 read it as an opener mid-line and failed the marker closed).
        'interpolation-hole-opener' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static string S(string x) => `$`"{x ?? `"/*`"}`";`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @('marker-without-row:antiphon|ZetaTests|M'))
        # CARD-1711 A-1: interpolation holes are lexed, so a quoted key inside a multi-line verbatim hole leaves the string
        # open and the next line's /* opens nothing; a hole the walk cannot lex (a nested verbatim, raw or interpolated
        # string, or a comment) marks every later line unknown, and a raw string whose closing quotes fall inside a hole
        # does too. An unknown line that names the token fails closed, even a canonical marker.
        'verbatim-interpolation-hole-quote' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int V = `$@`"{D[`"k`"]}`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:7"))
        'interpolation-hole-unlexable' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int V = `$@`"{D[@`"k`"]}`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n    [NativeLinuxOnly(`"x`")]`n    public void N() { }`n}`n", @("marker-noncanonical:${z}:7", "marker-noncanonical:${z}:8"))
        'raw-interpolation-hole-close' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static int R = `$`"`"`"{G(`"`"`"`n/* c`n`"`"`")}`"`"`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:8"))
        'interpolation-hole-quotes-in-sync' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static string S(bool b) => `$`"{(b ? `"a`" : `"b`")}`" + `$@`"{(b ? `"c`" : `"d`")}`";`n    static string T(string x) => `$`$`"`"`"{{string.Join(`" `", x)}} {`"`"`";`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n", @('marker-without-row:antiphon|ZetaTests|M'))
        # CARD-1711 D-1: in a regular interpolated string the text after a hole's colon is a format clause, where \" is an
        # escaped quote, not a string opener. A backslash inside a hole is not lexed: the string stops at its { and every
        # later line is unknown, so a *-led marker line after a verbatim string with a /* line fails closed.
        'interpolation-format-escaped-quote' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static string S(int x) { return `$`"{x:\`"}`"; } static int T = @`"`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:7"))
        'interpolation-format-escaped-quote-verbatim-interpolated' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static string S(int x) { return `$`"{x:\`"}`"; } static int T = `$@`"`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:7"))
        'interpolation-format-escaped-quote-pair' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    static string S(int x) { return `$`"{x:\`"\`"}`"; } static int T = @`"`n/* c`".Length`n        * 2; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:7"))
        # CARD-1711 A-2: C# ends a line (and a // comment) at CR, U+2028, U+2029 and U+0085 as well as LF, so the census
        # reads those as line ends: a marker after one on a // line counts, and a string opened after one is lexed.
        'line-comment-ends-at-u2028' = @($z, "namespace Fixture;`n`n// note`u{2028}[NativeLinuxOnly(`"x`")]`npublic sealed class ZetaTests { }`n", @('marker-without-row:antiphon|ZetaTests|'))
        'line-comment-ends-at-u2029' = @($z, "namespace Fixture;`n`n// note`u{2029}[NativeLinuxOnly(`"x`")]`npublic sealed class ZetaTests { }`n", @('marker-without-row:antiphon|ZetaTests|'))
        'line-comment-ends-at-u0085' = @($z, "namespace Fixture;`n`n// note`u{0085}[NativeLinuxOnly(`"x`")]`npublic sealed class ZetaTests { }`n", @('marker-without-row:antiphon|ZetaTests|'))
        'line-comment-ends-at-lone-cr' = @($z, "namespace Fixture;`n`n// note`r[NativeLinuxOnly(`"x`")]`npublic sealed class ZetaTests { }`n", @('marker-without-row:antiphon|ZetaTests|'))
        'line-separator-opens-string' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    // note`u{2028}const string S = @`"`n/* in string`n`";`n    static int Y = 2`n        * 3; [NativeLinuxOnly(`"y`")] public void M() { }`n}`n", @("marker-noncanonical:${z}:10"))
        'inactive-if' = @($z, "namespace Fixture;`n`n#if false`n[NativeLinuxOnly(`"inactive`")]`npublic sealed class ZetaTests { }`n#endif`n", @('marker-without-row:antiphon|ZetaTests|'))
        # CARD-1694 D-1: a line that starts as a comment but closes it before the token is code, not a comment line.
        'block-comment-prefix' = @($z, "namespace Fixture;`n`n/* x */ [NativeLinuxOnly(`"y`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:3"))
        'empty-block-comment-prefix' = @($z, "namespace Fixture;`n`n/**/[NativeLinuxOnly(`"y`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:3"))
        'block-comment-close-prefix' = @($z, "namespace Fixture;`n`n/*`n * old`n */ [NativeLinuxOnly(`"y`")]`npublic sealed class ZetaTests { }`n", @("marker-noncanonical:${z}:5"))
        'indented-block-comment-prefix' = @($z, "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    /* c */ [NativeLinuxOnly(`"y`")]`n    [Test]`n    public void Runs() { }`n}`n", @("marker-noncanonical:${z}:5"))
        # CARD-1694 D-2: an exempt file is exempt only for its known reference forms; an applied attribute still fails.
        'exempt-file-stacked-marker' = @('tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs', "namespace Fixture;`n`npublic sealed class WindowsUnitPlatformAdmissionTests`n{`n    [Test, NativeLinuxOnly(`"y`")]`n    public void Runs() { }`n}`n", @('marker-noncanonical:tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs:5'))
        'exempt-file-attribute-list-line' = @('tests/Shared/NativeLinuxPolicyWitness.cs', "namespace Fixture;`n`npublic sealed class WitnessTests`n{`n    [`n        Test,`n        NativeLinuxOnly(`"y`")`n    ]`n    public void Runs() { }`n}`n", @('marker-noncanonical:tests/Shared/NativeLinuxPolicyWitness.cs:7'))
        'exempt-file-string-before-marker' = @('tests/Shared/NativeLinuxOnlyAttribute.cs', "namespace Fixture;`n`npublic sealed class WitnessTests`n{`n    [Test(Description = `"a`"), NativeLinuxOnly(`"y`")]`n    public void Runs() { }`n}`n", @('marker-noncanonical:tests/Shared/NativeLinuxOnlyAttribute.cs:5'))
        'exempt-file-block-comment-prefix' = @('tests/Shared/NativeLinuxPolicyWitnessTests.cs', "namespace Fixture;`n`npublic sealed class WitnessTests`n{`n    /* c */ [Test, NativeLinuxOnly(`"y`")]`n    public void Runs() { }`n}`n", @('marker-noncanonical:tests/Shared/NativeLinuxPolicyWitnessTests.cs:5'))
        # CARD-1707: an exempt file strips its reference forms after a block comment closes on the line and inside a
        # multi-line string too (empty expectation: no problem).
        'exempt-file-block-close-reference' = @('tests/Shared/NativeLinuxPolicyWitness.cs', "internal static class NativeLinuxPolicyWitness`n{`n    /*`n     * doc`n     */ static Type A => typeof(NativeLinuxOnlyAttribute);`n}`n", @())
        'exempt-file-multi-line-string-reference' = @('tests/Shared/NativeLinuxPolicyWitness.cs', "internal static class NativeLinuxPolicyWitness`n{`n    const string V = @`"first`n    typeof(NativeLinuxOnlyAttribute)`n    last`";`n}`n", @())
        'reflection-outside-exempt-file' =@('tests/Shared/OtherWitness.cs', "internal static class OtherWitness`n{`n    internal static object Pin() => new Antiphon.TestSupport.NativeLinuxOnlyAttribute(`"pin`");`n    internal static Type Kind => typeof(NativeLinuxOnlyAttribute);`n}`n", @('marker-noncanonical:tests/Shared/OtherWitness.cs:3', 'marker-noncanonical:tests/Shared/OtherWitness.cs:4'))
    }
    foreach ($name in $failClosed.Keys) {
        $path, $text, $expected = $failClosed[$name]
        $extra = [ordered]@{} + $files
        $extra[$path] = $text
        $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root "fail-closed-$name" $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
        Assert-C1323 (Same-C1323Set $r.Problems $expected) "c1686-fail-closed-$name" ($r.Problems -join '; ')
    }

    # CARD-1711 A-3: a long run of $ outside a string is walked in linear time. A 64k run took 12.9 s when every
    # position retried the run (measured at base on server2), so this 128k run takes about four times that; the fixed
    # walk takes milliseconds, so 5 s is generous.
    $extra = [ordered]@{} + $files
    $extra[$z] = "namespace Fixture;`n`npublic sealed class ZetaTests`n{`n    " + ('$' * 131072) + "`n    [NativeLinuxOnly(`"x`")]`n    public void M() { }`n}`n"
    $root = New-C1686Root 'dollar-run' $extra
    $policy = New-C1686Policy $rows
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $r = Get-C1323LinuxOnlyCensus -Root $root -Policy $policy -Catalog $catalog
    $clock.Stop()
    Assert-C1323 ((Same-C1323Set $r.Problems @('marker-without-row:antiphon|ZetaTests|M')) -and $clock.Elapsed.TotalSeconds -lt 5) 'c1686-dollar-run-is-linear' ("seconds=$([Math]::Round($clock.Elapsed.TotalSeconds, 2)); " + ($r.Problems -join '; '))

    $extra = [ordered]@{} + $files
    $extra['tests/Antiphon.Tests/PortableTests.cs'] = "public sealed class PortableTests`n{`n    /*`n     * [NativeLinuxOnly(`"block comment`")]`n    /* [Test, NativeLinuxOnly(`"block comment`")] */`n    // [ NativeLinuxOnly(`"line comment`")]`n    /// <see cref=`"NativeLinuxOnlyAttribute`"/> is not a marker.`n    [Test]`n    public void Runs() { }`n}`n"
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'comments' $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 ($r.Ok -and (Same-C1323Set $r.MarkerKeys $good.MarkerKeys)) 'c1686-comments-are-not-markers' (($r.Problems + $r.MarkerKeys) -join '; ')

    # CARD-1694 D-2: the reference forms an exempt file needs (declaration, constructor, typeof, new, a plain string) pass.
    $extra = [ordered]@{} + $files
    $extra['tests/Shared/NativeLinuxPolicyWitness.cs'] = "internal static class NativeLinuxPolicyWitness`n{`n    public const string AttributeResourceName = `"Antiphon.TestSupport.NativeLinuxOnlyAttribute.cs`";`n    static Type A => typeof(NativeLinuxOnlyAttribute);`n    static bool B(Type t) => t.GetCustomAttributes(typeof(Antiphon.TestSupport.NativeLinuxOnlyAttribute), inherit: false).Length > 0;`n    static object C() => new NativeLinuxOnlyAttribute(`"pin`");`n    static void D() => throw new ArgumentException(`"A NativeLinuxOnly reason must be nonempty.`", nameof(D));`n}`n"
    $r = Get-C1323LinuxOnlyCensus -Root (New-C1686Root 'exempt-references' $extra) -Policy (New-C1686Policy $rows) -Catalog $catalog
    Assert-C1323 ($r.Ok -and (Same-C1323Set $r.MarkerKeys $good.MarkerKeys)) 'c1686-exempt-file-references-pass' (($r.Problems + $r.MarkerKeys) -join '; ')
}

function Test-C1199_WindowsRequiredRoster {
    # The wrapper passes compiled S/T/O identities. Do not scan test source for that roster.
    $expected = @(
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_directory_on_file'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_file_on_directory'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_symlink_on_file'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_untracked_descendant'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_ignored_descendant'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_staged_untracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_modified_tracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_tracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_clean_build'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_regular_user_move'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_untracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_changed_untracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_stamp'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_compiler_collision'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_directory'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_symlink'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.missing_receipt_is_not_negative_success'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build'
    )
    if ([string]::IsNullOrWhiteSpace($CompiledRosterPath) -or -not (Test-Path -LiteralPath $CompiledRosterPath -PathType Leaf)) {
        foreach ($name in @('c1199-roster-35', 'c1199-windows-required', 'c1199-linux-required', 'c1199-each-gap-incomplete', 'c1199-all-passed-complete', 'c1199-residual-skips-exact')) {
            Assert-C1323 $false $name 'compiled roster path missing'
        }
        return
    }
    $parsed = Get-Content -LiteralPath $CompiledRosterPath -Raw | ConvertFrom-Json
    $roster = @($parsed | ForEach-Object { [string]$_ })
    $groups = @($roster | Group-Object -CaseSensitive)
    $rosterOk = ($roster.Count -eq 35 -and $groups.Count -eq 35 -and (Same-C1323Set $roster $expected))
    $legacy = @($roster | Where-Object { $_ -like '*.shape_conflict_rule' }).Count
    Assert-C1323 $rosterOk 'c1199-roster-35' ("count=" + $roster.Count + " unique=" + $groups.Count + " shape_conflict_rule=" + $legacy)

    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $exclusionCache = @{}
    function Test-C1199Excluded([string]$ClassName, [string]$Method, [string]$Platform) {
        $key = 'antiphon|' + $Platform
        if (-not $exclusionCache.ContainsKey($key)) {
            $exclusionCache[$key] = @(Get-NightlyProfileExclusions $shipped.Object 'rc' 'antiphon' -Platform $Platform)
        }
        $probe = [pscustomobject]@{ ClassName = $ClassName; Type = $ClassName; Method = $Method }
        return (Test-NightlyProfileNodeExcluded $probe $exclusionCache[$key])
    }
    $windowsMiss = @()
    $linuxMiss = @()
    foreach ($id in $expected) {
        $dot = $id.LastIndexOf('.')
        $class = $id.Substring(0, $dot)
        $method = $id.Substring($dot + 1)
        if (Test-C1199Excluded $class $method 'windows') { $windowsMiss += $id }
        if (Test-C1199Excluded $class $method 'linux') { $linuxMiss += $id }
    }
    Assert-C1323 ($windowsMiss.Count -eq 0) 'c1199-windows-required' ($windowsMiss -join ',')
    Assert-C1323 ($linuxMiss.Count -eq 0) 'c1199-linux-required' ($linuxMiss -join ',')

    function New-C1199Verdict([object[]]$Rows) {
        return (ConvertTo-NightlyCoverageVerdict -TerminalRows $Rows -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash)
    }
    $gapMiss = @()
    foreach ($id in $expected) {
        $rows = @($expected | ForEach-Object {
            [pscustomobject]@{ Identity = $_; Outcome = $(if ($_ -ceq $id) { 'NotExecuted' } else { 'Passed' }) }
        })
        $verdict = New-C1199Verdict $rows
        $reason = @($verdict.reasons) -join ','
        if ($verdict.coverageComplete -or $reason -notlike ('*' + $id + '*')) { $gapMiss += $id }
    }
    Assert-C1323 ($gapMiss.Count -eq 0) 'c1199-each-gap-incomplete' ($gapMiss -join ',')
    $passed = @($expected | ForEach-Object { [pscustomobject]@{ Identity = $_; Outcome = 'Passed' } })
    $complete = New-C1199Verdict $passed
    $completeReason = @($complete.reasons) -join ','
    Assert-C1323 ($complete.coverageComplete -and $complete.passed -eq 35 -and $completeReason -notlike '*unexecuted-outcome*') 'c1199-all-passed-complete' $completeReason

    . "$PSScriptRoot/lib/checkpoint-usage.ps1"
    $census = Get-NamespaceCensus
    $expectedWindows = @(
        'Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.*'
        'Antiphon.Tests.Checkpoints.DetachedLauncherTests.executor_survives_its_starter'
        'Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.*'
        'Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests.*'
        'Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.*'
        'Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted'
    )
    $expectedLinux = @(
        'Antiphon.Tests.Checkpoints.EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open'
        'Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.*'
        'Antiphon.Tests.Checkpoints.TimeoutTests.windows_quick_row_finishes_beside_a_slow_row'
        'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_arguments_round_trip_intact'
        'Antiphon.Tests.Checkpoints.TimeoutTests.windows_chatty_row_drains_interleaved_stdout_and_stderr'
        'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset'
    )
    $windowsOk = Same-C1323Set @($census.windowsSkips) $expectedWindows
    $linuxOk = Same-C1323Set @($census.linuxSkips) $expectedLinux
    Assert-C1323 ($windowsOk -and $linuxOk) 'c1199-residual-skips-exact' ("windows=" + @($census.windowsSkips).Count + " linux=" + @($census.linuxSkips).Count)
}

try {
    switch ($Case) {
        'C1323_PlatformPolicy' { Test-C1323_PlatformPolicy }
        'C1323_PlatformExecution' { Test-C1323_PlatformExecution }
        'C1415_SkipExclusions' { Test-C1415_SkipExclusions }
        'LinuxSessionRunnerAdmission' { Test-LinuxSessionRunnerAdmission }
        'LinuxOnlyWindowsExclusions' { throw 'LinuxOnlyWindowsExclusions was retired by CARD-1613. Use -Case LinuxOnlyWindowsExclusionPolicy for the shipped rows. The compiled witness is NativeLinuxPolicyWitnessTests.' }
        'LinuxOnlyWindowsExclusionPolicy' { Test-LinuxOnlyWindowsExclusionPolicy }
        'C1686_LinuxOnlyCensus' { Test-C1686_LinuxOnlyCensus }
        'C1199_WindowsRequiredRoster' { Test-C1199_WindowsRequiredRoster }
        default { throw "unknown case: $Case" }
    }
} catch {
    Write-C487Fail "C1323 unexpected exception" $_.ToString()
}
finally {
    foreach ($relative in @('git-policy/.git/objects', 'execution/checkout/.git/objects')) {
        $objects = Join-Path $ResultsDirectory $relative
        if (Test-Path -LiteralPath $objects) {
            Get-ChildItem -LiteralPath $objects -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        }
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
