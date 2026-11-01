using System.Text.Json;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePlatformTests
{
    [Test]
    public Task C1323_PlatformPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformPolicy", 20,
            "C1323 windows-retains-69-and-siblings", "C1323 linux-excludes-exactly-69",
            "C1323 schema4-selector-admitted", "C1323 pinned-authority-validates-windows",
            "C1323 complete-synthetic-ledger-validates", "C1323 missing-windows-uid-refuses");

    [Test]
    public Task C1323_PlatformExecution() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformExecution", 12,
            "C1323 linux-launches-only-portable-cases", "C1323 windows-retains-delivery-cases",
            "C1323 linux-summary-is-diagnostic", "C1323 linux-state-is-diagnostic",
            "C1323 linux-leaves-green-files-identical", "C1323 unknown-platform-launches-nothing");

    [Test]
    public Task C1415_SkipExclusions() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1415_SkipExclusions", 10,
            "C1323 c1415-two-method-scoped-rows", "C1323 c1415-excluded-methods-exist",
            "C1323 c1415-windows-excludes-exactly", "C1323 c1415-windows-launches-only-required",
            "C1323 c1415-windows-census-complete", "C1323 c1415-linux-excludes-exactly",
            "C1323 c1425-pack-methods-required", "C1323 c1415-launched-skips-stay-incomplete");

    [Test]
    public Task LinuxOnlyWindowsExclusionPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "LinuxOnlyWindowsExclusionPolicy", 6,
            "C1323 every-new-row-has-linux-gate", "C1323 document-classes-are-excluded-or-decided",
            "C1323 decision-classes-are-not-class-excluded", "C1323 linux-only-rows-leave-census-complete",
            "C1323 decision-gaps-stay-incomplete", "C1323 linux-still-requires-the-rows");

    // S-A census contract: compiled S/T/O identities, minus the Windows confinement sibling, stay required.
    [Test]
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
                    "C1323 c1199-roster-35", "C1323 c1199-windows-required", "C1323 c1199-linux-required",
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
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.imported_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.directory_build_props_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.conditional_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.transitive_import_build_preserves_the_tracked_dependency",
        "Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.unevaluable_import_is_refused_before_the_build",
    };
}
