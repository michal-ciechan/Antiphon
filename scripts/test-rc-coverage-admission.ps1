#requires -Version 7.0
# Offline admission regression from Debug d8f70e4c. No provider or qualification run.
param([string]$ResultsDirectory, [string]$Case)
$ErrorActionPreference = 'Stop'
foreach ($name in @('nightly-common','nightly-policy','nightly-coverage','c487-harness')) { . "$PSScriptRoot/lib/$name.ps1" }
$repo = Split-Path $PSScriptRoot -Parent
$ResultsDirectory = New-C487Root $ResultsDirectory
$policy = Read-NightlyExecutionPolicy -RepoRoot $repo
$inventory = @(
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1614_Released_launch_failure_exits_within_ttl'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1614_Root_exit_with_live_descendants_does_not_terminate'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1614_Missing_tree_ordinary_read_stays_unknown'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1617_Explicit_seal_reads_terminate_once_after_success'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1617_Failed_termination_retries_on_capped_backoff'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1617_Bounded_release_still_finalizes'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1617_Throwing_termination_counts_as_failed_attempt'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1685_Explicit_seal_unreadable_tree_stays_unknown'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1685_Tree_repopulated_after_drain_stays_draining'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1685_Large_failure_count_keeps_sixty_second_backoff'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1685_Backoff_counts_from_attempt_end'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1681_Eio_after_root_exit_drains_to_receipt'; owner='CARD-1614' }
    @{ s='pty-host'; c='LinuxCustodyReleaseHostTests'; m='C1681_Eio_while_root_alive_stays_drain_failure'; owner='CARD-1614' }
    @{ s='agents-pty'; c='Card1685LinuxCustodyRunnerTests'; m='C1685_Direct_throwing_termination_still_backs_off'; owner='CARD-1685' }
    @{ s='agents-pty'; c='LinuxCgroupContainmentTests'; m='C1614_Helper_written_tree_is_read_by_production_reader'; owner='CARD-1614' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='swapped_parent_symlink_cannot_redirect_temp_root_sweep'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='fifo_and_socket_inside_the_root_are_removed'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='symlink_inside_the_root_is_unlinked_without_following'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='backslash_name_inside_the_root_is_reclaimed_with_the_root'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='hard_link_outside_the_root_is_not_truncated'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='mounted_child_keeps_the_root_and_its_marker'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='a_leaf_that_vanishes_mid_delete_still_reclaims_the_root'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='directory_symlink_to_an_unreadable_child_does_not_abort_other_roots'; owner='CARD-1596' }
    @{ s='antiphon'; c='TempRootSweepAnchoredDeleteTests'; m='directory_symlink_does_not_read_or_modify_the_outside_tree'; owner='CARD-1596' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_UpdateRefsStdinBreakClearsTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_StrangerPipeHolderDoesNotKeepTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_UnreadableEnvironOfAnEarlierProcessDoesNotKeepTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_LateJobAssignmentWithNoMembersKeepsTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1076_JobAssignedBeforeSpawnWithNoMembersReleasesTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C448_ParentDeathGrandchildDoesNotKeepTheJournal'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C448_JournalStaysWhileTheRootLives'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C448_LateInTreeHolderIsKilledAndTheJournalIsRemoved'; owner='CARD-0448' }
    @{ s='antiphon'; c='LandingGitTests'; m='C1714_Status_that_cannot_open_a_directory_is_untrusted'; owner='CARD-0448' }
    @{ s='antiphon'; c='OwnedProcessTreeTests'; m='C448_SnapshotFailureKillsTheChildAndLeavesTheGrandchild'; owner='CARD-0448' }
    @{ s='antiphon'; c='OwnedProcessTreeTests'; m='C448_ProductionKillPassesTheServerPid'; owner='CARD-0448' }
    @{ s='antiphon'; c='CheckpointReceiptStallBudgetTests'; m='healthy_receipts_complete_with_every_pool_worker_occupied'; owner='CARD-1601' }
    @{ s='antiphon'; c='PosixHarnessToolTests'; m='Capture_identity_resolves_git_sh_instead_of_hard_coded_bin_sh'; owner='CARD-1323' }
    @{ s='antiphon'; c='PosixHarnessToolTests'; m='Cutover_window_completes_without_cat_grep_or_tr_on_path'; owner='CARD-1323' }
    @{ s='antiphon'; c='AgentReportStoreBudgetTests'; m='Slow_git_response_does_not_spend_write_budget'; owner='CARD-1605' }
    @{ s='antiphon'; c='CardRelationshipScriptTests'; m='Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns'; owner='CARD-1323' }
    @{ s='antiphon'; c='C0824DesktopLegacyReleasePolicyTests'; m='Status_that_cannot_read_a_posix_directory_holds_it'; owner='CARD-0824' }
)
# These are workflow obligations on both platforms, rather than Linux platform exclusions.
$manual = @(
    @{ s='antiphon'; c='LiveLaneIsolationTests'; m='StartupArgvMatchesProductionApplyOff'; owner='CARD-1545' }
    @{ s='antiphon'; c='AgentReportStoreLoadTests'; m='Store_samples_record_normal_and_contended_phases'; owner='CARD-1605' }
)
# Enumerate all declared public tests of the affected files, including portable siblings.
$paths = @(
    'tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs'
    'tests/Antiphon.Agents.Pty.Tests/Card1685LinuxCustodyRunnerTests.cs'
    'tests/Antiphon.Agents.Pty.Tests/LinuxCgroupContainmentTests.cs'
    'tests/Antiphon.Tests/Checkpoints/TempRootSweepAnchoredDeleteTests.cs'
    'tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs'
    'tests/Antiphon.Tests/Infrastructure/OwnedProcessTreeTests.cs'
    'tests/Antiphon.Tests/Checkpoints/CheckpointReceiptStallBudgetTests.cs'
    'tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs'
    'tests/Antiphon.Tests/Application/AgentReportStoreBudgetTests.cs'
    'tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs'
    'tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs'
    'tests/Antiphon.Tests/Scripts/LiveLaneIsolationTests.cs'
    'tests/Antiphon.Tests/Application/AgentReportStoreLoadTests.cs'
)
$nodes = @()
foreach ($path in $paths) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $path))
    $class = [IO.Path]::GetFileNameWithoutExtension($path)
    $suite = switch -Wildcard ($path) { 'tests/Antiphon.PtyHost.Tests/*' { 'pty-host' }; 'tests/Antiphon.Agents.Pty.Tests/*' { 'agents-pty' }; default { 'antiphon' } }
    foreach ($match in [regex]::Matches($source, '\[Test(?:\s*,[^\]]*)?\][\s\S]*?public (?:async )?(?:Task|void) (\w+)\(')) {
        $method = $match.Groups[1].Value
        $nodes += @{ s=$suite; c=$class; m=$method; ClassName=$class; Method=$method; Uid=($class+'.'+$method) }
    }
}
function Get-ManualWorkflowScopeErrors($PolicyObject) {
    $manualWrong = @()
    foreach ($obligation in $manual) {
        $rows = @($PolicyObject.profiles.rc.exclusions | Where-Object { $_.suite -ceq $obligation.s -and $_.class -ceq $obligation.c })
        $methods = @()
        if ($rows.Count -eq 1) { $methods = @($rows[0].methods | Where-Object { $null -ne $_ }) }
        if ($rows.Count -ne 1 -or $methods.Count -ne 1 -or $methods[0] -cne $obligation.m -or $rows[0].PSObject.Properties.Name -contains 'platform') { $manualWrong += $obligation.c }
    }
    return $manualWrong
}
$manualWrong = @(Get-ManualWorkflowScopeErrors $policy.Object)
Assert-C487 ($manualWrong.Count -eq 0) 'CoverageAdmission separate-method-workflows' ($manualWrong -join ',')
# Exercise both missing-methods variants without changing the shipped policy. A
# class-wide row must reach the named assertion, rather than throw on a null array.
$scopeFailures = @()
foreach ($obligation in $manual) {
    $fixture = $policy.Object | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $row = @($fixture.profiles.rc.exclusions | Where-Object { $_.suite -ceq $obligation.s -and $_.class -ceq $obligation.c })[0]
    $row.PSObject.Properties.Remove('methods')
    try {
        $rejected = @(Get-ManualWorkflowScopeErrors $fixture)
        if ($rejected.Count -ne 1 -or $rejected[0] -cne $obligation.c) { $scopeFailures += ($obligation.c + ':not-rejected') }
    } catch { $scopeFailures += ($obligation.c + ':exception:' + $_.Exception.Message) }
}
Assert-C487 ($scopeFailures.Count -eq 0) 'CoverageAdmission class-scope-rejected-by-assertion' ($scopeFailures -join ',')
# Resolve each suite/platform once for this run, as the real planner does.
$exclusions = @{}
foreach ($suite in @($nodes.s | Sort-Object -Unique)) {
    foreach ($platform in @('windows','linux')) {
        $exclusions["$suite|$platform"] = @(Get-NightlyProfileExclusions $policy.Object 'rc' $suite -Platform $platform)
    }
}
$missing = @(); $wrong = @(); $terminal = @(); $portable = @(); $linuxMissing = @()
foreach ($expected in @($inventory + $manual)) {
    if (@($nodes | Where-Object { $_.s -ceq $expected.s -and $_.c -ceq $expected.c -and $_.m -ceq $expected.m }).Count -ne 1) { $missing += ($expected.c+'.'+$expected.m) }
}
foreach ($node in $nodes) {
    $native = @($inventory | Where-Object { $_.s -ceq $node.s -and $_.c -ceq $node.c -and $_.m -ceq $node.m })
    $workflow = @($manual | Where-Object { $_.c -ceq $node.c -and $_.m -ceq $node.m })
    $windowsRows = $exclusions[$node.s + "|windows"]
    $d = Get-NightlyDiscoveryDisposition $node $windowsRows -ProfileAware
    $wantExcluded = ($native.Count -gt 0 -or $workflow.Count -gt 0)
    if (($d.Disposition -ceq 'excluded') -ne $wantExcluded) { $wrong += $node.Uid }
    if ($wantExcluded -and ($d.Owner -cne @($native + $workflow)[0].owner -or [string]::IsNullOrWhiteSpace($d.Reason))) { $wrong += ($node.Uid+':attribution') }
    if (-not $wantExcluded) { $portable += $node.Uid }
    # Simulate only the independently evidenced native skips / absent Explicit consumer.
    # An undeclared gate must poison the real coverage verdict, rather than disappear from this test's roster.
    if ($d.Disposition -ceq 'required') {
        $terminal += [pscustomobject]@{ Identity=$node.Uid; Outcome=$(if ($wantExcluded) { 'NotExecuted' } else { 'Passed' }) }
    }
    $linuxRows = $exclusions[$node.s + "|linux"]
    $ld = Get-NightlyDiscoveryDisposition $node $linuxRows -ProfileAware
    if (($ld.Disposition -ceq 'excluded') -ne ($workflow.Count -gt 0)) { $linuxMissing += $node.Uid }
}
Assert-C487 ($missing.Count -eq 0 -and $nodes.Count -gt 50) 'CoverageAdmission source-roster' ($missing -join ',')
Assert-C487 ($wrong.Count -eq 0) 'CoverageAdmission exact-windows-dispositions' ($wrong -join ',')
Assert-C487 ($linuxMissing.Count -eq 0) 'CoverageAdmission linux-retains-native' ($linuxMissing -join ',')
Assert-C487 ($portable -contains 'C0824DesktopLegacyReleasePolicyTests.Status_that_cannot_read_the_whole_tree_holds_it' -and $portable -contains 'CardRelationshipScriptTests.Block_fails_loudly_when_relationships_returns_404' -and $portable.Count -gt 20) 'CoverageAdmission portable-siblings' ($portable -join ',')
$verdictArgs = @{ ProcessExit=0; Sha='fixture'; ExpectedSha='fixture'; GitRef='fixture'; ExpectedRef='fixture'; PolicyHash=$policy.Hash; ExpectedPolicyHash=$policy.Hash }
$v = ConvertTo-NightlyCoverageVerdict -TerminalRows $terminal @verdictArgs
Assert-C487 $v.coverageComplete 'CoverageAdmission coverage-sweep' (@($v.reasons) -join ',')
$terminal += [pscustomobject]@{ Identity='Required.WindowsCapability'; Outcome='NotExecuted' }
$red = ConvertTo-NightlyCoverageVerdict -TerminalRows $terminal @verdictArgs
Assert-C487 (-not $red.coverageComplete -and (@($red.reasons) -join ',') -like '*Required.WindowsCapability*') 'CoverageAdmission required-skips-stay-incomplete'
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 8
