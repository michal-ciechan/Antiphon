using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The offline admission regression from Debug d8f70e4c (no provider or qualification run), in process since unit 18 of
/// 2026-11-22-remaining-ps1-to-csharp.md (it ran scripts/test-rc-coverage-admission.ps1). The native Linux obligations are
/// Windows rc exclusions with their owners; the two workflow obligations are method-scoped on both platforms; every other
/// declared test of the affected files stays required, and an undeclared gate keeps coverage incomplete.
/// </summary>
[Category("Integration")]
public sealed class RcCoverageAdmissionTests
{
    private static readonly (string Suite, string Class, string Method, string Owner)[] Inventory =
    [
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1614_Released_launch_failure_exits_within_ttl", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1614_Root_exit_with_live_descendants_does_not_terminate", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1614_Missing_tree_ordinary_read_stays_unknown", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1617_Explicit_seal_reads_terminate_once_after_success", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1617_Failed_termination_retries_on_capped_backoff", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1617_Bounded_release_still_finalizes", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1617_Throwing_termination_counts_as_failed_attempt", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1685_Explicit_seal_unreadable_tree_stays_unknown", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1685_Tree_repopulated_after_drain_stays_draining", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1685_Large_failure_count_keeps_sixty_second_backoff", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1685_Backoff_counts_from_attempt_end", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1681_Eio_after_root_exit_drains_to_receipt", "CARD-1614"),
        ("pty-host", "LinuxCustodyReleaseHostTests", "C1681_Eio_while_root_alive_stays_drain_failure", "CARD-1614"),
        ("agents-pty", "Card1685LinuxCustodyRunnerTests", "C1685_Direct_throwing_termination_still_backs_off", "CARD-1685"),
        ("agents-pty", "LinuxCgroupContainmentTests", "C1614_Helper_written_tree_is_read_by_production_reader", "CARD-1614"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "swapped_parent_symlink_cannot_redirect_temp_root_sweep", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "fifo_and_socket_inside_the_root_are_removed", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "symlink_inside_the_root_is_unlinked_without_following", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "backslash_name_inside_the_root_is_reclaimed_with_the_root", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "hard_link_outside_the_root_is_not_truncated", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "mounted_child_keeps_the_root_and_its_marker", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "a_leaf_that_vanishes_mid_delete_still_reclaims_the_root", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "directory_symlink_to_an_unreadable_child_does_not_abort_other_roots", "CARD-1596"),
        ("antiphon", "TempRootSweepAnchoredDeleteTests", "directory_symlink_does_not_read_or_modify_the_outside_tree", "CARD-1596"),
        ("antiphon", "LandingGitTests", "C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_UpdateRefsStdinBreakClearsTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_StrangerPipeHolderDoesNotKeepTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_UnreadableEnvironOfAnEarlierProcessDoesNotKeepTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_LateJobAssignmentWithNoMembersKeepsTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1076_JobAssignedBeforeSpawnWithNoMembersReleasesTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C448_ParentDeathGrandchildDoesNotKeepTheJournal", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C448_JournalStaysWhileTheRootLives", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C448_LateInTreeHolderIsKilledAndTheJournalIsRemoved", "CARD-0448"),
        ("antiphon", "LandingGitTests", "C1714_Status_that_cannot_open_a_directory_is_untrusted", "CARD-0448"),
        ("antiphon", "OwnedProcessTreeTests", "C448_SnapshotFailureKillsTheChildAndLeavesTheGrandchild", "CARD-0448"),
        ("antiphon", "OwnedProcessTreeTests", "C448_ProductionKillPassesTheServerPid", "CARD-0448"),
        ("antiphon", "CheckpointReceiptStallBudgetTests", "healthy_receipts_complete_with_every_pool_worker_occupied", "CARD-1601"),
        ("antiphon", "PosixHarnessToolTests", "Cutover_window_completes_without_cat_grep_or_tr_on_path", "CARD-1323"),
        ("antiphon", "AgentReportStoreBudgetTests", "Slow_git_response_does_not_spend_write_budget", "CARD-1605"),
        ("antiphon", "CardRelationshipScriptTests", "Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns", "CARD-1323"),
        ("antiphon", "C0824DesktopLegacyReleasePolicyTests", "Status_that_cannot_read_a_posix_directory_holds_it", "CARD-0824"),
    ];

    // These are workflow obligations on both platforms, rather than Linux platform exclusions.
    private static readonly (string Suite, string Class, string Method, string Owner)[] Manual =
    [
        ("antiphon", "LiveLaneIsolationTests", "StartupArgvMatchesProductionApplyOff", "CARD-1545"),
        ("antiphon", "AgentReportStoreLoadTests", "Store_samples_record_normal_and_contended_phases", "CARD-1605"),
    ];

    // Every declared public test of the affected files is enumerated, including the portable siblings.
    private static readonly string[] Paths =
    [
        "tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs",
        "tests/Antiphon.Agents.Pty.Tests/Card1685LinuxCustodyRunnerTests.cs",
        "tests/Antiphon.Agents.Pty.Tests/LinuxCgroupContainmentTests.cs",
        "tests/Antiphon.Tests/Checkpoints/TempRootSweepAnchoredDeleteTests.cs",
        "tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs",
        "tests/Antiphon.Tests/Infrastructure/OwnedProcessTreeTests.cs",
        "tests/Antiphon.Tests/Checkpoints/CheckpointReceiptStallBudgetTests.cs",
        "tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs",
        "tests/Antiphon.Tests/Application/AgentReportStoreBudgetTests.cs",
        "tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs",
        "tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs",
        "tests/Antiphon.Tests/Scripts/LiveLaneIsolationTests.cs",
        "tests/Antiphon.Tests/Application/AgentReportStoreLoadTests.cs",
    ];

    private static readonly Regex DeclaredTest = new(@"\[Test(?:\s*,[^\]]*)?\][\s\S]*?public (?:async )?(?:Task|void) (\w+)\(");

    [Test]
    public void Windows_and_linux_obligations_remain_exact()
    {
        var rows = new NightlyCaseRows();
        var policy = Policy();
        var nodes = new List<(string Suite, string Class, string Method, string Uid)>();
        foreach (var path in Paths)
        {
            var source = File.ReadAllText(Path.Combine(Repo, path));
            var cls = Path.GetFileNameWithoutExtension(path);
            var suite = path.StartsWith("tests/Antiphon.PtyHost.Tests/", StringComparison.OrdinalIgnoreCase) ? "pty-host"
                : path.StartsWith("tests/Antiphon.Agents.Pty.Tests/", StringComparison.OrdinalIgnoreCase) ? "agents-pty" : "antiphon";
            foreach (Match match in DeclaredTest.Matches(source)) nodes.Add((suite, cls, match.Groups[1].Value, cls + "." + match.Groups[1].Value));
        }
        var manualWrong = ManualWorkflowScopeErrors(policy.Object);
        rows.Check(manualWrong.Length == 0, "CoverageAdmission separate-method-workflows", string.Join(",", manualWrong));
        // Both missing-methods variants without changing the shipped policy: a class-wide row reaches the named refusal.
        var scopeFailures = new List<string>();
        foreach (var obligation in Manual)
        {
            var fixture = JsonNode.Parse(File.ReadAllText(policy.Path))!.AsObject();
            var row = fixture["profiles"]!["rc"]!["exclusions"]!.AsArray().Select(r => r!.AsObject())
                .First(r => Text(r, "suite") == obligation.Suite && Text(r, "class") == obligation.Class);
            row.Remove("methods");
            try
            {
                var rejected = ManualWorkflowScopeErrors(ExecutionPolicy.Parse(fixture.ToJsonString()));
                if (rejected.Length != 1 || rejected[0] != obligation.Class) scopeFailures.Add(obligation.Class + ":not-rejected");
            }
            catch (Exception ex) { scopeFailures.Add(obligation.Class + ":exception:" + ex.Message); }
        }
        rows.Check(scopeFailures.Count == 0, "CoverageAdmission class-scope-rejected-by-assertion", string.Join(",", scopeFailures));

        // Resolve each suite/platform once for this run, as the real planner does.
        var exclusions = new Dictionary<string, object[]>(StringComparer.Ordinal);
        foreach (var suite in nodes.Select(n => n.Suite).Distinct())
            foreach (var platform in new[] { "windows", "linux" })
                exclusions[suite + "|" + platform] = Exclusions(policy, "rc", suite, platform);
        var missing = Inventory.Concat(Manual).Where(e => nodes.Count(n => n.Suite == e.Suite && n.Class == e.Class && n.Method == e.Method) != 1)
            .Select(e => e.Class + "." + e.Method).ToArray();
        var wrong = new List<string>();
        var terminal = new List<(string Identity, string Outcome)>();
        var portable = new List<string>();
        var linuxMissing = new List<string>();
        foreach (var node in nodes)
        {
            var declared = Inventory.Where(e => e.Suite == node.Suite && e.Class == node.Class && e.Method == node.Method)
                .Concat(Manual.Where(e => e.Class == node.Class && e.Method == node.Method)).ToArray();
            var workflow = Manual.Any(e => e.Class == node.Class && e.Method == node.Method);
            var probe = new PlainObject(("ClassName", node.Class), ("Method", node.Method), ("Uid", node.Uid));
            var d = NightlyCensus.Disposition(probe, exclusions[node.Suite + "|windows"], true);
            var wantExcluded = declared.Length > 0;
            if ((Str(d, "Disposition") == "excluded") != wantExcluded) wrong.Add(node.Uid);
            if (wantExcluded && (Str(d, "Owner") != declared[0].Owner || string.IsNullOrWhiteSpace(Str(d, "Reason")))) wrong.Add(node.Uid + ":attribution");
            if (!wantExcluded) portable.Add(node.Uid);
            // Only the independently evidenced native skips and the absent Explicit consumer are simulated: an undeclared
            // gate must poison the real coverage verdict, rather than disappear from this test's roster.
            if (Str(d, "Disposition") == "required") terminal.Add((node.Uid, wantExcluded ? "NotExecuted" : "Passed"));
            var ld = NightlyCensus.Disposition(probe, exclusions[node.Suite + "|linux"], true);
            if ((Str(ld, "Disposition") == "excluded") != workflow) linuxMissing.Add(node.Uid);
        }
        rows.Check(missing.Length == 0 && nodes.Count > 50, "CoverageAdmission source-roster", string.Join(",", missing));
        rows.Check(wrong.Count == 0, "CoverageAdmission exact-windows-dispositions", string.Join(",", wrong));
        rows.Check(linuxMissing.Count == 0, "CoverageAdmission linux-retains-native", string.Join(",", linuxMissing));
        rows.Check(portable.Contains("C0824DesktopLegacyReleasePolicyTests.Status_that_cannot_read_the_whole_tree_holds_it", StringComparer.OrdinalIgnoreCase)
            && portable.Contains("CardRelationshipScriptTests.Block_fails_loudly_when_relationships_returns_404", StringComparer.OrdinalIgnoreCase) && portable.Count > 20,
            "CoverageAdmission portable-siblings", string.Join(",", portable));
        var sweep = Verdict(terminal, policy);
        rows.Check(PsValue.IsTrue(PsValue.Prop(sweep, "coverageComplete")), "CoverageAdmission coverage-sweep", string.Join(",", Strings(sweep, "reasons")));
        terminal.Add(("Required.WindowsCapability", "NotExecuted"));
        var red = Verdict(terminal, policy);
        rows.Check(!PsValue.IsTrue(PsValue.Prop(red, "coverageComplete")) && string.Join(",", Strings(red, "reasons")).Contains("Required.WindowsCapability", StringComparison.OrdinalIgnoreCase),
            "CoverageAdmission required-skips-stay-incomplete");
        rows.ShouldAllPass(8, "CoverageAdmission separate-method-workflows", "CoverageAdmission source-roster", "CoverageAdmission exact-windows-dispositions",
            "CoverageAdmission linux-retains-native", "CoverageAdmission portable-siblings", "CoverageAdmission coverage-sweep",
            "CoverageAdmission required-skips-stay-incomplete", "CoverageAdmission class-scope-rejected-by-assertion");
    }

    // The workflow obligations whose rc row is not exactly one method-scoped row for every platform.
    private static string[] ManualWorkflowScopeErrors(ExecutionPolicy policy)
    {
        var wrong = new List<string>();
        foreach (var obligation in Manual)
        {
            var rows = policy.Profiles["rc"].Exclusions.Where(r => r.Suite == obligation.Suite && r.Class == obligation.Class).ToArray();
            var methods = rows.Length == 1 ? rows[0].Methods.Where(m => m != null).ToArray() : [];
            if (rows.Length != 1 || methods.Length != 1 || methods[0] != obligation.Method || rows[0].Platform != null || rows[0].PlatformInvalid) wrong.Add(obligation.Class);
        }
        return wrong.ToArray();
    }

    private static object Verdict(IEnumerable<(string Identity, string Outcome)> rows, PolicyFile policy) =>
        NightlyChunkVerdict.CoverageVerdict(rows.Select(r => (object)new PlainObject(("Identity", r.Identity), ("Outcome", r.Outcome))).ToArray(), 0, "fixture", "fixture",
            "fixture", "fixture", policy.Hash, policy.Hash, Array.Empty<string>(), Array.Empty<object>(), false, "", null, null);

    private static string Text(JsonObject row, string name) => row[name] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";
}
