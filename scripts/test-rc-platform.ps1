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
    Assert-C1323 ($shipped.SchemaVersion -eq 4 -and $shipRows.Count -eq 3 -and @($shipRows | Where-Object { $_.platform -cne 'linux' -or $_.suite -cne 'e2e' }).Count -eq 0 -and (Same-C1323Set $shipRows.class $scoped.Class)) 'shipped-three-linux-rows'
    $c1614 = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1614')
    $inbox = @($c1614 | Where-Object { [string]$_.class -ceq 'HostCustodyTests' -and [string]$_.platform -ceq 'linux' })
    $life = @($c1614 | Where-Object { [string]$_.class -ceq 'LinuxRunnerCustodyLifecycleTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' })
    $boot = @($c1614 | Where-Object { [string]$_.class -ceq 'CustodyNamespaceBootstrapTests' -and [string]$_.platform -ceq 'windows' -and [string]$_.reason -like 'Linux-only by construction:*' })
    $inboxMethods = @($inbox | ForEach-Object { $_.methods })
    Assert-C1323 ($c1614.Count -eq 3 -and $inbox.Count -eq 1 -and @($inboxMethods) -contains 'Inbox_is_unsupported_and_never_becomes_no_start_proof' -and [string]$inbox[0].reason -notlike 'Linux-only by construction:*' -and $life.Count -eq 1 -and $boot.Count -eq 1 -and $shipped.Hash -ceq [string]$shipped.Object.policyHash) 'card-1614-mixed-platform-rows'
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

function Test-LinuxOnlyWindowsExclusionPolicy {
    # Windows rc exclusions for skips that are Linux-only by construction. Gap classes stay required.
    # CARD-1613: compiled NativeLinuxPolicyWitnessTests owns the marker proof. This case checks shipped rows only.
    $prefix = 'Linux-only by construction:'
    $inventory = @(
        @{ s='antiphon'; c='PhoneHomeMirrorJoinTests'; o='CARD-1032'; m=@() }
        @{ s='antiphon'; c='UnixDelegateLaunchArgvTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='CheckpointBoundedReaderTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='CheckpointSourceGitBoundsTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='CheckpointVerifierBoundsTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='CheckpointFixtureProcessTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='DetachedLauncherTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='SafeDeleteProcessBoundaryTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='GrokSha256PinTests'; o='CARD-1210'; m=@() }
        @{ s='antiphon'; c='GithubCredentialHelperTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='GitMinimumVersionGuardTests'; o='CARD-1260'; m=@() }
        @{ s='antiphon'; c='RetiredTempContainerHostTests'; o='CARD-1050'; m=@() }
        @{ s='antiphon'; c='HostJqPrerequisiteScriptTests'; o='CARD-1210'; m=@() }
        @{ s='antiphon'; c='ScriptHarnessLinuxOwnershipTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='RefreshGithubTokenRemoteCommandTests'; o='CARD-1245'; m=@() }
        @{ s='antiphon'; c='NightlyProbeSupervisorTests'; o='CARD-1375'; m=@() }
        @{ s='antiphon'; c='RetiredTempContainerDockerTests'; o='CARD-1050'; m=@() }
        @{ s='antiphon'; c='RollingVolumeRecycleDockerTests'; o='CARD-1050'; m=@() }
        @{ s='antiphon'; c='RollingVolumeRecycleCensusChurnTests'; o='CARD-1050'; m=@() }
        @{ s='antiphon'; c='RollingProductionMountTests'; o='CARD-1050'; m=@() }
        @{ s='antiphon'; c='CheckpointRecoveryLinuxTests'; o='CARD-0590'; m=@() }
        @{ s='antiphon'; c='CheckpointFixtureRoutingTests'; o='CARD-1233'; m=@() }
        @{ s='antiphon'; c='CheckpointInheritedFileTests'; o='CARD-0590'; m=@() }
        @{ s='pty-host'; c='LinuxCgroupCustodyTests'; o='CARD-0590'; m=@() }
        @{ s='agents-pty'; c='UnixPtyArgvTests'; o='CARD-0590'; m=@() }
        @{ s='session-runner'; c='LinuxRunnerCustodyLifecycleTests'; o='CARD-1614'; m=@() }
        @{ s='session-runner'; c='CustodyNamespaceBootstrapTests'; o='CARD-1614'; m=@() }
        @{ s='antiphon'; c='PhoneHomeCompletedCardTransportTests'; o='CARD-1032'; m=@('C1032_TransportQueueRecipientReplay') }
        @{ s='antiphon'; c='LandGraphEvaluationTests'; o='CARD-1483'; m=@('C1483_S1_UnreadableLinkComponentDenies') }
        @{ s='antiphon'; c='CheckpointConfinementLedgerTests'; o='CARD-0590'; m=@('unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted') }
        @{ s='antiphon'; c='CheckpointReceiptSafetyTests'; o='CARD-0590'; m=@('linux_fifo_receipts_refuse_within_four_seconds') }
        @{ s='antiphon'; c='OutputCleanupTests'; o='CARD-0590'; m=@('external_build_payload_is_adopted_at_seal_and_removed_on_green','file_added_after_seal_is_refused_and_kept','file_modified_after_seal_is_refused_and_kept','symlink_in_a_build_output_is_refused_before_and_after_seal','tracked_file_in_an_adopted_build_output_is_refused_and_kept','adopted_build_output_over_the_entry_cap_is_unsafe_tree','hard_linked_build_file_is_not_adopted_and_outside_bytes_stay','independent_writer_after_build_exit_is_not_adopted_and_cleanup_keeps_it','seal_without_driver_observed_build_exit_adopts_nothing','hanging_git_is_refused_and_killed_within_the_bound') }
        @{ s='antiphon'; c='AnchoredPathDeleteWitnessTests'; o='CARD-0590'; m=@('linux_colon_in_a_file_name_stays_deletable','hard_link_sibling_survives_anchored_delete','ancestor_symlink_to_a_different_inode_is_refused') }
        @{ s='antiphon'; c='PlanCoverageHandleTests'; o='CARD-0590'; m=@('hardlinked_selected_file_is_refused','in_repo_fifo_project_is_refused_within_15_seconds') }
        @{ s='antiphon'; c='CodexRootExecContractTests'; o='CARD-1210'; m=@('Entrypoint_heal_chowns_the_tmp_directory_once','Entrypoint_heal_never_names_a_path_outside_tmp') }
        @{ s='antiphon'; c='FileUseCaptureTests'; o='CARD-1539'; m=@('C1539_linux_holder','C1539_absent_inputs','C1539_budget_and_caps','C1539_redaction_before_sinks','C1539_persistence_fail_safe','C1539_generation_and_ownership') }
        @{ s='antiphon'; c='JqRunnerImageContractTests'; o='CARD-1210'; m=@('Version_row_accepts_only_exact_successful_pin_without_stderr','C1054_Jq_row_rejects_home_shadow','C1054_Jq_row_accepts_canonical_file_and_alias','C1054_Jq_row_rejects_canonical_leaf_symlink','C1054_Jq_row_records_found_and_resolved_paths') }
        @{ s='antiphon'; c='RollingVolumeRecycleScriptTests'; o='CARD-1050'; m=@('C1008_Option_manifest_is_strict','C1008_Documentation_and_transport_pins_match','C1008_Refusal_receipts_do_not_leak_secrets','C1008_Retired_absent_null_is_accepted','C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt','C1594_Cross_sha_cleanup_image_binds_the_retired_temp','C1594_Older_retirement_receipt_is_ignored','C1594_Disagreeing_cross_sha_images_refuse','C1594_Antiphon_evidence_gitdir','C1008_Preview_helper_removal_is_owned') }
        @{ s='antiphon'; c='RetiredTempContainerScriptTests'; o='CARD-1050'; m=@('C994_Host_strict_volume_admission_remains_independent','C994_Host_resume_never_runs_container_cleanup','C994_Host_bridge_transport_preserves_literal_context') }
        @{ s='antiphon'; c='BuildSlotScriptTests'; o='CARD-0590'; m=@('C800_WrapperInterruptKillsChildAndReleasesLease') }
        @{ s='antiphon'; c='C1364BoundLandWaitProofTests'; o='CARD-1050'; m=@('C1364_Host_recycle_admission_reports_waited','C1364_Host_recycle_post_removal_reports_waited','C1364_Host_cleanup_reports_waited') }
        @{ s='antiphon'; c='RemoteScriptContractTests'; o='CARD-1050'; m=@('C1374_Host_census_recovers_transient_reads','C1087_Host_census_filters_and_names_cause','C1008_Recycle_exact_default_volumes','C1008_Recycle_refuses_references_and_unknown_census','C1008_Recycle_audits_work_as_1654','C1008_Recycle_refuses_unpublished_and_dirty_work','C1008_Recycle_refuses_uninspectable_git','C1008_Recycle_reaps_dirty_scratch','C1008_Recycle_audits_promisor_checkout','C1105_Git_audit_volume_scale','C1105_Jq_argv_limit','C1008_Recycle_preserves_tmp_copyup','C1105_Verify_tmp_accepts_an_absent_pty_host','C1008_Recycle_resume_requires_matching_receipt','C1008_Recycle_receipt_records_disk_and_partial_failure','C1008_Retire_temp_rechecks_absence_and_retirement','C1008_Retire_temp_reclaims_below_cache_disk_gate','C1008_Recycle_dry_run_never_mutates','C1338_Retire_temp_dry_run_lists_exactly_the_temp_volumes','C1105_Redeploy_accepts_previous_generation_and_requires_new_bind','C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout','C1105_V218_dry_run_reaches_the_generation_line','C1105_Relative_dry_run_refuses_before_removal','C1105_Generation_identity_refuses_mismatch','C1105_Main_state_init_without_session_runner_refuses','C1105_Compose_cmp_refuses_before_removal','C1105_Resume_identity_refusals','C1105_Resume_state_init_image_follows_the_journal','C1105_Resume_foreign_leftover_is_not_an_image_mismatch','C1105_Resume_digest_pins_previous_sha12','C1105_Resume_post_removal_census_tracks_open_work','C849_Deploy_prepares_and_verifies_before_acceptance','C1105_Git_audit_recovery_tips','C1105_Git_audit_seed_content','C1105_Git_audit_index_content','C1105_Git_audit_object_completeness','C1105_Git_audit_overall_timeout','C1105_Git_audit_resume_preserves_proof','C1105_Git_audit_hidden_locations','C1335_Mirror_owner_stamp_in_linked_admin') }
        @{ s='antiphon'; c='CheckpointStampSafetyTests'; o='CARD-0590'; m=@('tracked_stamp_shapes_are_bounded_and_leave_results_in_place') }
        @{ s='antiphon'; c='CheckpointTempRootSweepTests'; o='CARD-0590'; m=@('fifo_or_socket_root_is_reclaimed') }
        @{ s='antiphon'; c='PlanCoverageCommandTests'; o='CARD-0590'; m=@('outside_project_fifo_is_refused_without_opening') }
        @{ s='session-runner'; c='CompletedCardWorkspaceRemovalTests'; o='CARD-1032'; m=@('C1032_InspectionR3Mode','C1032_RemovalLinuxIgnoredFifoRefuses') }
        @{ s='session-runner'; c='HostStatsProbeLiveTests'; o='CARD-0590'; m=@('Linux_probe_reads_live_proc') }
    )
    $decisionClasses = @('ClaudeDialogWatchdogCanaryTests','CheckpointOwnedOutputTests','CheckpointScriptReaderDeadlineTests','CheckpointShapeConflictTests','CheckpointTrackedBaselineTests','CheckpointOutputConfinementTests','RunnerWorkspaceBindingTests')
    $decisionMethods = @(
        'OutputCleanupTests/parent_symlink_does_not_delete_a_tracked_stamp'
        'CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes'
        'PlanCoverageCommandTests/existing_outside_project_is_refused_by_root_boundary'
        'RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused'
    )
    $documentClasses = @(
        'ClaudeDialogWatchdogCanaryTests','LandGraphEvaluationTests','PhoneHomeCompletedCardTransportTests','PhoneHomeMirrorJoinTests','UnixDelegateLaunchArgvTests','CheckpointConfinementLedgerTests','CheckpointReceiptSafetyTests','CheckpointBoundedReaderTests','OutputCleanupTests','AnchoredPathDeleteWitnessTests','CheckpointOwnedOutputTests','CheckpointScriptReaderDeadlineTests','CheckpointSourceGitBoundsTests','CheckpointVerifierBoundsTests','PlanCoverageHandleTests','CheckpointFixtureProcessTests','DetachedLauncherTests','SafeDeleteProcessBoundaryTests','CodexRootExecContractTests','FileUseCaptureTests','GrokSha256PinTests','JqRunnerImageContractTests','GithubCredentialHelperTests','GitMinimumVersionGuardTests','RollingVolumeRecycleScriptTests','RetiredTempContainerScriptTests','RetiredTempContainerHostTests','BuildSlotScriptTests','HostJqPrerequisiteScriptTests','ScriptHarnessLinuxOwnershipTests','C1364BoundLandWaitProofTests','RollingProductionMountTests','RefreshGithubTokenRemoteCommandTests','NightlyProbeSupervisorTests','RetiredTempContainerDockerTests','RollingVolumeRecycleDockerTests','RollingVolumeRecycleCensusChurnTests','RemoteScriptContractTests','CompletedCardWorkspaceRemovalTests','RunnerWorkspaceBindingTests','HostStatsProbeLiveTests','LinuxCgroupCustodyTests','UnixPtyArgvTests','LinuxRunnerCustodyLifecycleTests','CustodyNamespaceBootstrapTests','CheckpointShapeConflictTests','CheckpointTrackedBaselineTests','CheckpointRecoveryLinuxTests','CheckpointStampSafetyTests','CheckpointOutputConfinementTests','CheckpointFixtureRoutingTests','CheckpointInheritedFileTests','CheckpointTempRootSweepTests','PlanCoverageCommandTests'
    )
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $shippedRows = @($shipped.Object.profiles.rc.exclusions | Where-Object { [string]$_.reason -like ($prefix + '*') })
    $expectedKeys = @()
    foreach ($row in $inventory) {
        $methods = @($row.m)
        $expectedKeys += ($row.s + '|' + $row.c + '|' + ((@($methods) | Sort-Object) -join ','))
    }
    $actualKeys = @()
    $rowProblems = @()
    foreach ($row in $shippedRows) {
        $methods = @($row.methods | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        $key = ([string]$row.suite) + '|' + ([string]$row.class) + '|' + ((@($methods) | Sort-Object) -join ',')
        $actualKeys += $key
        if ([string]$row.platform -cne 'windows') { $rowProblems += "$key platform" }
        $want = @($inventory | Where-Object { $_.s -ceq [string]$row.suite -and $_.c -ceq [string]$row.class })
        if ($want.Count -ne 1 -or [string]$want[0].o -cne [string]$row.owner) { $rowProblems += "$key owner" }
    }
    $rowsMatch = (Same-C1323Set $actualKeys $expectedKeys) -and $rowProblems.Count -eq 0
    Assert-C1323 $rowsMatch 'every-new-row-has-linux-gate' (($rowProblems + @('actual=' + $actualKeys.Count + ' expected=' + $expectedKeys.Count)) -join '; ')
    $excludedClasses = @($shippedRows | ForEach-Object { [string]$_.class })
    $missingDoc = @($documentClasses | Where-Object { $excludedClasses -cnotcontains $_ -and $decisionClasses -cnotcontains $_ })
    $extraDoc = @($excludedClasses | Where-Object { $documentClasses -cnotcontains $_ })
    Assert-C1323 ($missingDoc.Count -eq 0 -and $extraDoc.Count -eq 0) 'document-classes-are-excluded-or-decided' (('missing=' + ($missingDoc -join ',')) + ' extra=' + ($extraDoc -join ','))
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
        @{ s='antiphon'; c='CheckpointShapeConflictTests'; m='shape_conflict_rule' }
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
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_directory_on_file'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_file_on_directory'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_symlink_on_file'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_untracked_descendant'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_ignored_descendant'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_modified_tracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_tracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_regular_user_move'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_untracked'
        'Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_changed_untracked'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed'
        'Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency'
        'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build'
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
