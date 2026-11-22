using System.Text.Json;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
public sealed partial class ReleaseGatePlatformTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1323_PlatformPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformPolicy", 20,
            "C1323 windows-retains-69-and-siblings", "C1323 linux-excludes-exactly-69",
            "C1323 schema4-selector-admitted", "C1323 pinned-authority-validates-windows",
            "C1323 complete-synthetic-ledger-validates", "C1323 missing-windows-uid-refuses");

    /// <summary>The tests half: NightlyTests.Run per platform on the offline e2e fixture (test-rc-platform.ps1, the C# executor on its seams).</summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1323_PlatformExecution() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformExecution", 8,
            "C1323 linux-launches-only-portable-cases", "C1323 windows-retains-delivery-cases",
            "C1323 linux-summary-is-diagnostic", "C1323 unknown-platform-launches-nothing");

    /// <summary>
    /// The run half (in-process since W2-nightly S20; it ran the PowerShell run core on the platform seams): NightlyRun.Run of a
    /// -NoSync rc run with the selection bound, its tests hop answered with what the tests half writes on each platform. A Linux
    /// run keeps the executor's PlatformDiagnostic scope and reasons in last-run.json and the completion record, earns no credit
    /// (full-credit-scope-required) and leaves both green files as they were; a Windows run stays Full and earns rc credit.
    /// </summary>
    [Test, Timeout(300_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C1323_PlatformRunCredit(CancellationToken token)
    {
        const string candidateRef = "rc/2026-10-20";
        using var chain = new Antiphon.Tests.Scripts.Nightly.NightlyRcChain("c1323-run", candidateRef).Publish();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var releaseRoot = chain.Fixture.PathOf("releases");
        var candidateId = chain.CandidateId();
        var green = Path.Combine(releaseRoot, "candidates", candidateId, "complete-green.json");
        var masterGreen = chain.Fixture.PathOf("master", "last-complete-green.json");
        Directory.CreateDirectory(Path.GetDirectoryName(green)!);
        Directory.CreateDirectory(Path.GetDirectoryName(masterGreen)!);
        File.WriteAllText(green, "candidate sentinel");
        File.WriteAllText(masterGreen, "master sentinel");
        foreach (var platform in new[] { "linux", "windows" })
        {
            var linux = platform == "linux";
            chain.TestsAnswer = options =>
            {
                Directory.CreateDirectory(options.LogRoot);
                var summary = new System.Text.Json.Nodes.JsonObject
                {
                    ["runId"] = options.RunId, ["sha"] = options.Sha, ["profile"] = options.Profile, ["testsPassed"] = true, ["coverageComplete"] = !linux,
                    ["coverageScope"] = linux ? "PlatformDiagnostic" : "Full", ["platform"] = platform,
                    ["reasons"] = linux ? new System.Text.Json.Nodes.JsonArray("windows-rc-obligations-outstanding") : new System.Text.Json.Nodes.JsonArray(),
                    ["policyHash"] = "c1323-policy", ["reportDelivered"] = true,
                };
                File.WriteAllText(Path.Combine(options.LogRoot, "summary.json"), summary.ToJsonString());
                return 0;
            };
            var state = chain.Fixture.PathOf("state-" + platform);
            var run = chain.Run(chain.Options("offline-" + platform, o =>
            {
                o.WhatIf = false; o.NoReport = false; o.CandidateId = candidateId; o.Repository = "fixture/c1323"; o.ReleaseRoot = releaseRoot;
                o.StateRoot = state; o.LogRoot = chain.Fixture.PathOf("run-" + platform); o.CoordinationRoot = chain.Fixture.PathOf("coordination");
            }));
            var last = Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Json(Path.Combine(state, "last-run.json"))!.AsObject();
            string Text(string name) => last[name]?.GetValue<string>() ?? "";
            bool Flag(string name) => last[name]?.GetValue<bool>() == true;
            var reasons = last["reasons"]?.AsArray().Select(r => r?.GetValue<string>() ?? "").ToArray() ?? [];
            if (linux)
            {
                var record = NightlyRunRecord(run);
                rows.Check(Flag("testsPassed") && !Flag("coverageComplete") && Text("coverageScope") == "PlatformDiagnostic" && Text("platform") == "linux"
                    && reasons.Contains("windows-rc-obligations-outstanding") && record.Scope == "PlatformDiagnostic" && record.Reasons.Contains("windows-rc-obligations-outstanding"),
                    "C1323 linux-state-is-diagnostic", last.ToJsonString());
                var verdict = Antiphon.Nightly.ReleaseGateCredit.Verdict(last.ToDictionary(p => p.Key, p => Plain(p.Value)));
                rows.Check(run.CreditKind == "" && verdict.Reasons.Contains("full-credit-scope-required"), "C1323 linux-credit-refuses", string.Join(",", verdict.Reasons));
                rows.Check(File.ReadAllText(green) == "candidate sentinel" && File.ReadAllText(masterGreen) == "master sentinel", "C1323 linux-leaves-green-files-identical");
            }
            else
            {
                rows.Check(run.ExitCode == 0 && Flag("coverageComplete") && Text("coverageScope") == "Full" && Text("platform") == "windows" && run.CreditKind == "rc-release"
                    && File.ReadAllText(green) != "candidate sentinel", "C1323 windows-state-preserves-full-behavior", last.ToJsonString() + " " + run.Refusal + "\n" + chain.Fixture.Out);
            }
        }
        rows.ShouldAllPass(4, "C1323 linux-state-is-diagnostic", "C1323 linux-credit-refuses", "C1323 linux-leaves-green-files-identical", "C1323 windows-state-preserves-full-behavior");
    }

    private static (string Scope, string[] Reasons) NightlyRunRecord(Antiphon.Nightly.NightlyRunResult run)
    {
        var record = Antiphon.Nightly.NightlyRun.ResultRecord(run);
        var reasons = (record["reasons"] as object[] ?? []).Select(r => Antiphon.Nightly.PsValue.Str(r)).ToArray();
        return (Antiphon.Nightly.PsValue.Str(record["coverageScope"]), reasons);
    }

    private static object? Plain(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<bool>(out var b) => b,
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<int>(out var i) => i,
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var s) => s,
        System.Text.Json.Nodes.JsonArray a => a.Select(Plain).ToArray(),
        null => null,
        _ => node.ToJsonString(),
    };

    // C1415_SkipExclusions and LinuxSessionRunnerAdmission: ReleaseGatePlatformTests.Plan.cs (in-process since W2-nightly S11).

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task LinuxOnlyWindowsExclusionPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "LinuxOnlyWindowsExclusionPolicy", 6,
            "C1323 every-new-row-has-linux-gate", "C1323 markers-and-rows-agree",
            "C1323 decision-classes-are-not-class-excluded", "C1323 linux-only-rows-leave-census-complete",
            "C1323 decision-gaps-stay-incomplete", "C1323 linux-still-requires-the-rows");

    // CARD-1686: fixture trees prove the derived census fails for a marker without a row, a row without a marker
    // and any use of the name outside a canonical marker line, a comment line or an exempt file's reference forms.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1686_LinuxOnlyCensus() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1686_LinuxOnlyCensus", 62,
            "C1323 c1686-consistent-census-passes", "C1323 c1686-class-marker-without-row-fails",
            "C1323 c1686-method-marker-without-row-fails", "C1323 c1686-row-without-marker-fails",
            "C1323 c1686-row-attribution-fails", "C1323 c1686-marker-outside-suite-fails",
            "C1323 c1686-routes-name-the-census-set", "C1323 c1686-noncanonical-stacked-method-fails",
            "C1323 c1686-noncanonical-stacked-class-fails", "C1323 c1686-noncanonical-suffix-fails",
            "C1323 c1686-noncanonical-qualified-fails", "C1323 c1686-noncanonical-global-fails",
            "C1323 c1686-noncanonical-leading-space-fails", "C1323 c1686-noncanonical-paren-space-fails",
            "C1323 c1686-fail-closed-verbatim-interpolated-glob", "C1323 c1686-fail-closed-split-line",
            "C1323 c1686-fail-closed-split-line-comment", "C1323 c1686-fail-closed-using-alias",
            "C1323 c1686-fail-closed-string-literal", "C1323 c1686-fail-closed-trailing-comment",
            "C1323 c1686-fail-closed-second-use-on-marker-line", "C1323 c1686-fail-closed-block-comment-interior",
            "C1323 c1686-fail-closed-block-comment-star-interior", "C1323 c1686-fail-closed-block-comment-mid-line-open",
            "C1323 c1686-fail-closed-split-multiplication", "C1323 c1686-fail-closed-nested-block-comment",
            "C1323 c1686-fail-closed-string-literal-opener", "C1323 c1686-fail-closed-char-literal-quote",
            "C1323 c1686-fail-closed-char-literal-quote-before-string", "C1323 c1686-fail-closed-block-comment-star-close",
            "C1323 c1686-fail-closed-block-comment-canonical-close", "C1323 c1686-fail-closed-exempt-file-block-close-reference",
            "C1323 c1686-fail-closed-exempt-file-multi-line-string-reference",
            "C1323 c1686-fail-closed-verbatim-multi-line-opener", "C1323 c1686-fail-closed-verbatim-string-marker-line",
            "C1323 c1686-fail-closed-raw-string-opener", "C1323 c1686-fail-closed-interpolation-hole-opener",
            "C1323 c1686-fail-closed-inactive-if", "C1323 c1686-fail-closed-block-comment-prefix",
            "C1323 c1686-fail-closed-empty-block-comment-prefix", "C1323 c1686-fail-closed-block-comment-close-prefix",
            "C1323 c1686-fail-closed-indented-block-comment-prefix", "C1323 c1686-fail-closed-exempt-file-stacked-marker",
            "C1323 c1686-fail-closed-exempt-file-attribute-list-line", "C1323 c1686-fail-closed-exempt-file-block-comment-prefix",
            "C1323 c1686-fail-closed-exempt-file-string-before-marker",
            "C1323 c1686-fail-closed-reflection-outside-exempt-file", "C1323 c1686-comments-are-not-markers",
            "C1323 c1686-exempt-file-references-pass", "C1323 c1686-fail-closed-verbatim-interpolation-hole-quote",
            "C1323 c1686-fail-closed-interpolation-hole-unlexable", "C1323 c1686-fail-closed-raw-interpolation-hole-close",
            "C1323 c1686-fail-closed-interpolation-hole-quotes-in-sync",
            "C1323 c1686-fail-closed-interpolation-format-escaped-quote",
            "C1323 c1686-fail-closed-interpolation-format-escaped-quote-verbatim-interpolated",
            "C1323 c1686-fail-closed-interpolation-format-escaped-quote-pair", "C1323 c1686-fail-closed-line-comment-ends-at-u2028",
            "C1323 c1686-fail-closed-line-comment-ends-at-u2029", "C1323 c1686-fail-closed-line-comment-ends-at-u0085",
            "C1323 c1686-fail-closed-line-comment-ends-at-lone-cr", "C1323 c1686-fail-closed-line-separator-opens-string",
            "C1323 c1686-dollar-run-is-linear");

    // S-A census contract: compiled S/T/O identities, minus the Windows confinement sibling, stay required.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    [Timeout(120_000)]
    public async Task C1199_WindowsRequiredRoster(CancellationToken cancellationToken)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests",
            "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests",
            "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests",
        };
        const string sibling =
            "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset";
        var selected = CheckpointRoster.CompiledCases(typeof(CheckpointShapeConflictTests).Assembly, "Antiphon.Tests.Checkpoints")
            .Where(row => classes.Contains(row.ClassName))
            .Select(row => row.ClassName + "." + row.Method)
            .Where(name => !string.Equals(name, sibling, StringComparison.Ordinal))
            .ToArray();
        var path = Path.Combine(Path.GetTempPath(), "c1199-roster-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(selected), cancellationToken);
        try
        {
            await ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1199_WindowsRequiredRoster", 6,
                new[]
                {
                    "C1323 c1199-roster-37", "C1323 c1199-windows-required", "C1323 c1199-linux-required",
                    "C1323 c1199-each-gap-incomplete", "C1323 c1199-all-passed-complete", "C1323 c1199-residual-skips-exact",
                },
                ScriptHarnessOptions.Default with { AdditionalArguments = new[] { "-CompiledRosterPath", path } },
                cancellationToken);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        selected.Length.ShouldBe(ExpectedC1199Roster.Length);
        selected.Distinct(StringComparer.Ordinal).Count().ShouldBe(ExpectedC1199Roster.Length);
        new HashSet<string>(selected, StringComparer.Ordinal).SetEquals(ExpectedC1199Roster).ShouldBeTrue(
            string.Join('\n', selected.OrderBy(name => name, StringComparer.Ordinal)));
    }

    private static readonly string[] ExpectedC1199Roster =
    {
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_directory_on_file",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_file_on_directory",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_symlink_on_file",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_untracked_descendant",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_ignored_descendant",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_staged_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_modified_tracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_tracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_clean_build",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_regular_user_move",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_deleted_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_changed_untracked",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_stamp",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_compiler_collision",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_directory",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_existing_output_symlink",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.missing_receipt_is_not_negative_success",
        "Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests.shape_fixture_sdk_build_count",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.local_output_property_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.intermediate_output_path_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.generated_target_file_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.imported_props_reference_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.transitive_reference_tampering_is_reported",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.dirty_tracked_tree_is_refused_and_unmodified",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.clean_untracked_output_build_exits_zero",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.tracked_snapshot_contains_committed_files",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_untracked_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.staged_ignored_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.index_added_file_is_kept_and_indexed",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.passing_script_requires_writable_source_receipt",
        "Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests.baseline_staged_fixture_sdk_build_count",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build",
    };
}
