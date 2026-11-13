using System.Reflection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class C1722GitTimeoutBudgetTests
{
    // CARD-1722: five complete Linux class samples at base 1038d07e66a54b630a1bc1d394923bc7049b73dd.
    // Per-method p95 below is the largest nearest-rank p95 across its argument cases (n=5 => max),
    // rounded up to milliseconds. Min/median/p95/max and host/load: the task plan timing table.
    // The 60s Git floor is the commissioned loaded-Windows startup exception (rc 472c7495);
    // Linux timings are provisional for Windows. Collect Windows timings in the next CARD-1722 rc.
    [Test]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_deletes_local_and_tracking_refs_at_expected_sha), 0.152)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_absent_refs_are_idempotent_success), 0.165)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_checked_out_branch_refuses_and_keeps_refs), 0.216)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_unpublished_local_tip_refuses), 0.523)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_ancestor_tip_is_deleted), 0.162)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_non_delegate_branch_refused), 1.603)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_ref_moved_between_read_and_delete_refuses), 0.157)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_missing_expected_object_refuses), 0.136)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_missing_repository_refuses_unavailable), 0.143)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_non_repository_directory_refuses_unavailable), 0.141)]
    [Arguments(typeof(C0824RunnerBranchRefsTests), nameof(C0824RunnerBranchRefsTests.C0824_branch_with_trailing_newline_refused), 0.146)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_ColdLeaseWipesNativeOutput), 2.063)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_OperatorColdResetFreeSlotWipes), 0.684)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_LeaseWithoutColdKeepsNative), 0.622)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_ColdRequestOnLeasedSlotPendsUntilRelease), 1.096)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_WeeklyBoundaryMarksOnce), 1.999)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_ScheduleOffMarksNone), 0.337)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_MissedBoundaryRunsOnceAtStart), 0.326)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_ColdResetRemovesDeletedProjectResidue), 0.880)]
    [Arguments(typeof(C0824WorkspaceSlotColdResetTests), nameof(C0824WorkspaceSlotColdResetTests.C0824_ColdResetQuarantinesWhatALeaseWould), 1.319)]
    [Arguments(typeof(C0824WorkspaceSlotNativeRehomeTests), nameof(C0824WorkspaceSlotNativeRehomeTests.C0824_Real0555DirectoryRehomes), 0.699)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_AllLeasedGrowsOverflowSlot), 0.891)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_ReleasingSlotWaitsBeforeGrowing), 1.392)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_OverflowCapRefuses), 1.220)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_DiskBudgetRefuses), 3.298)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_IdleOverflowRetiredGuarded), 2.151)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_LeasedRecentOrQuarantinedNeverRetired), 2.184)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved), 1.279)]
    [Arguments(typeof(C0824WorkspaceSlotOverflowTests), nameof(C0824WorkspaceSlotOverflowTests.C0824_SlotNumberNeverReusedWhileDirectoryExists), 2.846)]
    [Arguments(typeof(C0824WorkspaceSlotProcessScanTests), nameof(C0824WorkspaceSlotProcessScanTests.C0824_CwdHolderDefersThenQuarantines), 1.469)]
    [Arguments(typeof(C0824WorkspaceSlotProcessScanTests), nameof(C0824WorkspaceSlotProcessScanTests.C0824_OpenFdHolderDefersThenQuarantines), 4.109)]
    [Arguments(typeof(C0824WorkspaceSlotProcessScanTests), nameof(C0824WorkspaceSlotProcessScanTests.C0824_HolderExitsBeforeRetryReleasesNormally), 3.253)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_InjectedFsFailureQuarantinesAndRehomes), 2.665)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_IdentityBrokenDirectoryRehomes), 1.006)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_SharingViolationViaFileSystemSeam), 0.450)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_ReplacementFailureMarksOnlySlotFailed), 0.774)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_RehomeLoopGuardRetiresSlot), 1.727)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_QuarantinedDirectoryBoundHoldsPool), 2.523)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_QuarantineReleaseDispositions), 0.627)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_QuarantineReleaseUnknownDispositionRefused), 1.540)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_UnpushedCheckFailureQuarantinesNotResets), 0.597)]
    [Arguments(typeof(C0824WorkspaceSlotRehomeTests), nameof(C0824WorkspaceSlotRehomeTests.C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit), 1.372)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_LeaseResetsToExactShaFromUnrelatedState), 0.843)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_VerifyQuarantinesSilentCheckout), 1.182)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_WarmResetWipesEveryNonKeepListClass), 2.884)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_WarmResetKeepsNativeOutputByteAndMtime), 0.967)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs), 0.529)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_EvidenceRetainedBeforeWipe), 0.592)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_ResetNeverTouchesOutsideSlot), 0.737)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_PoolBoundedAcrossSequentialAndConcurrentLeases), 5.200)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_ReviewLeaseIsExactTreeNeverSubjectBranch), 0.451)]
    [Arguments(typeof(C0824WorkspaceSlotResetTests), nameof(C0824WorkspaceSlotResetTests.C0824_FlagOffRunnerKeepsTaskMirror), 1.018)]
    [Arguments(typeof(C1677RetentionDispatchTests), nameof(C1677RetentionDispatchTests.Recent_use_by_this_runner_keeps_the_tree), 1.543)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Clean_pushed_old_mirror_is_removed_without_force_or_prune), 0.514)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Confinement_refuses_outside_scope), 0.265)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Slot_paths_are_refused), 0.313)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Unreadable_slot_ledger_keeps_a_removable_mirror), 0.267)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Ownership_gates_refuse), 0.379)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Repository_and_branch_must_match), 0.429)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Dirty_tree_is_kept), 1.388)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Unpushed_commit_is_kept), 0.342)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Live_origin_must_hold_the_branch), 0.997)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Ls_remote_is_bounded), 2.037)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Holder_keeps_the_tree), 1.145)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Dry_run_reports_would_remove_and_mutates_nothing), 0.390)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Execute_rechecks_under_the_lock_before_the_move), 0.723)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Phase_b_refusal_keeps_the_moved_tree_registered), 0.599)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Move_failure_leaves_the_tree_in_place), 0.489)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Retiring_residue_is_retried_by_phase_b_only), 0.797)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Residue_retry_keeps_a_residue_a_live_process_holds), 0.434)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Dry_run_on_a_clean_unheld_residue_removes_nothing), 0.298)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Residue_retry_never_takes_a_foreign_residue_that_sorts_first), 1.559)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Residue_retry_keeps_a_legitimate_residue_beside_a_stray_entry), 0.412)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Residue_retry_keeps_an_upper_case_hex_residue), 0.882)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Unrecognised_mode_is_kept_before_any_git), 0.463)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Tree_not_registered_at_its_path_is_kept), 0.306)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Cross_device_retiring_root_moves_nothing), 0.363)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Retiring_root_inside_the_worktree_root_is_refused), 0.281)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Ignored_nested_repo_goes_with_the_tree), 0.431)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Absent_path_is_already_absent_without_prune), 0.284)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Ignored_secret_files_survive_admission_on_dry_run_and_execute), 0.655)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Ignored_nested_repository_secret_keeps_bytes_and_registration), 0.506)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree), 0.358)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Phase_b_keeps_an_ignored_secret_planted_after_the_move), 0.387)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Residue_retry_keeps_an_ignored_secret), 0.264)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Unavailable_ignored_list_keeps_the_tree), 0.346)]
    [Arguments(typeof(C1677RetentionEngineTests), nameof(C1677RetentionEngineTests.Appsettings_secret_survives_every_retention_boundary), 0.389)]
    [Arguments(typeof(C1677RetentionInventoryTests), nameof(C1677RetentionInventoryTests.Inventory_lists_root_and_retiring_entries), 0.348)]
    [Arguments(typeof(C1677RetentionInventoryTests), nameof(C1677RetentionInventoryTests.Inventory_is_read_only), 1.080)]
    [Arguments(typeof(C1677RetentionInventoryTests), nameof(C1677RetentionInventoryTests.Unmatched_name_is_refused_in_execute), 0.366)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.Cwd_holder_keeps_the_tree), 1.238)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.Open_fd_holder_keeps_the_tree), 1.034)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.Tree_is_removed_after_the_holder_exits), 2.261)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.Unreadable_proc_root_is_unknown), 0.344)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.End_suffixed_task_leaf_is_kept), 0.891)]
    [Arguments(typeof(C1677RetentionNativeProcessTests), nameof(C1677RetentionNativeProcessTests.End_suffixed_retiring_residue_is_kept), 0.841)]
    public void Git_budget_covers_measured_method(Type testClass, string methodName, double p95Seconds)
    {
        var method = testClass.GetMethod(methodName).ShouldNotBeNull();
        var timeout = (method.GetCustomAttribute<TimeoutAttribute>()
            ?? testClass.GetCustomAttribute<TimeoutAttribute>()).ShouldNotBeNull().Timeout;
        timeout.TotalSeconds.ShouldBeGreaterThanOrEqualTo(Math.Max(60, 2 * p95Seconds),
            testClass.Name + "." + methodName + " requires max(60s Git floor, 2x measured p95)");
    }

    [Test]
    public void Budget_table_covers_every_git_method()
    {
        Type[] classes =
        [
            typeof(C0824RunnerBranchRefsTests),
            typeof(C0824WorkspaceSlotColdResetTests),
            typeof(C0824WorkspaceSlotNativeRehomeTests),
            typeof(C0824WorkspaceSlotOverflowTests),
            typeof(C0824WorkspaceSlotProcessScanTests),
            typeof(C0824WorkspaceSlotRehomeTests),
            typeof(C0824WorkspaceSlotResetTests),
            typeof(C1677RetentionDispatchTests),
            typeof(C1677RetentionEngineTests),
            typeof(C1677RetentionInventoryTests),
            typeof(C1677RetentionNativeProcessTests),
        ];
        var pure = PureMethods();
        var actual = classes.SelectMany(type => type.GetMethods()
                .Where(method => method.IsDefined(typeof(TestAttribute)))
                .Select(method => type.Name + "." + method.Name))
            .Except(pure, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var recorded = typeof(C1722GitTimeoutBudgetTests)
            .GetMethod(nameof(Git_budget_covers_measured_method))!
            .GetCustomAttributes<ArgumentsAttribute>()
            .Select(row => ((Type)row.Values[0]!).Name + "." + row.Values[1])
            .Order(StringComparer.Ordinal).ToArray();

        recorded.ShouldBe(actual, "every Git-spawning method needs recorded measurements");
        recorded.Distinct(StringComparer.Ordinal).Count().ShouldBe(recorded.Length);
    }

    [Test]
    public void Pure_retention_methods_keep_their_original_budget()
    {
        foreach (var qualifiedName in PureMethods())
        {
            var names = qualifiedName.Split('.');
            var type = typeof(C1722GitTimeoutBudgetTests).Assembly
                .GetType(typeof(C1722GitTimeoutBudgetTests).Namespace + "." + names[0]).ShouldNotBeNull();
            var method = type.GetMethod(names[1]).ShouldNotBeNull();
            var timeout = (method.GetCustomAttribute<TimeoutAttribute>()
                ?? type.GetCustomAttribute<TimeoutAttribute>()).ShouldNotBeNull().Timeout;
            timeout.TotalMilliseconds.ShouldBe(15_000, qualifiedName);
        }
    }

    private static string[] PureMethods() =>
    [
        // CARD-1892: fake retention gates and an injected clock; neither method starts Git.
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Phase_a_does_not_block_input),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Mutation_lock_reports_slow_owner_wait_and_hold),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Phase_b_runs_outside_the_mutation_lock),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Phase_a_waits_for_the_mutation_lock),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Phase_b_runs_only_after_a_move),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Capability_is_advertised_only_on_linux),
        nameof(C1677RetentionDispatchTests) + "." + nameof(C1677RetentionDispatchTests.Use_in_a_subdirectory_of_the_tree_covers_it),
        nameof(C1677RetentionEngineTests) + "." + nameof(C1677RetentionEngineTests.Ignored_content_with_an_end_suffix_is_never_disposable),
        nameof(C1677RetentionEngineTests) + "." + nameof(C1677RetentionEngineTests.Ignored_content_with_an_embedded_control_character_is_never_disposable),
    ];
}
