using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S1. Declaration union and the committed derived-impact index.
/// V-1 through V-6, R-1, R-3, and R-4. Keyword and roster files stay empty lists.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(600_000)]
public sealed class DerivedImpactIndexTests
{
    private const string MigrationPath = "server/Migrations/20261108121426_Card1773TranscriptPromptStampIndex.cs";

    internal const string LeakSweepPath = "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs";

    internal const string LandingGitPath = "server/Infrastructure/Git/LandingGit.cs";

    internal const string NotificationServicePath = "server/Application/Services/AgentTaskLandNotificationService.cs";

    // The land-graph pre-check's rules: a change to the audit tool alone selects the budget method (the selector's tool-only rule).
    private const string PrecheckPath = "tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs";

    // The land-graph-audit command (unit 11 of 2026-11-22-remaining-ps1-to-csharp.md; it replaced the audit-land-verification-*.ps1).
    internal const string AuditCommandPath = "scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs";

    private static readonly string[] HandMigrationClasses =
    [
        "AppDbContextModelTests",
        "C1514HostLoadMigrationTests",
        "AgentTaskLandHeldNotificationTests",
        "AgentTaskLandNotificationPersistenceTests",
        "DispatchBaseNotificationTests",
        "RunnerDefaultMigrationTests",
        "SessionRuntimeActionRoutingTests",
        "StandingRestartEscalationTests",
        "StandingSpecialistRoutingMigrationTests",
        "WorktreeBaseSelectionTests",
    ];

    private static readonly string[] FixedClasses =
    [
        "AgentTaskLandHeldNotificationTests",
        "AgentTaskLandNotificationParkFollowUpTests",
        "AgentTaskLandNotificationPersistenceTests",
        "AgentTaskLandNotificationReconcileLoadTests",
        "AgentTaskLandNotificationRecoveryTests",
        "AgentTaskLandNotificationRescanTests",
        "AgentTaskLandReceiptWatermarkSafetyTests",
        "AgentTaskLandReceiptWatermarkTests",
        "AppDbContextModelTests",
        "CheckpointReceiptSafetyTests",
        "ContractSnapshotTests",
        "HostCleanupCheckpointReceiptTests",
        "ReceiptScanDestinationIntegrationTests",
        "TestTimingOperationLabelTests",
    ];

    private static readonly string?[] MergeRanges =
    [
        null,
        "ff7cceaf4f65bed039f5b7f11dd726d27e762607",
        "dcb973d896f1d12776a796b71466977b22737b79",
        "a54a6d4f755fa139b41fd7dc8aef456d3d2c0975",
        "ae5b545d9ac9091f42847734b88bf8e202cf2814",
        "dff78584d636fa49eca37652f2c3f14a48002a92",
    ];

    // Review 7c6f052c: the grep census of *Tests classes whose file contains OSPlatform.Windows.
    // That census is 31 classes. The report names CodexAdapterLocalShellTests, RawPtyAdapterTests,
    // AgentSessionServiceIntegrationTests, SessionMessageQueuePtyIntegrationTests,
    // GrokDelegateEndToEndTests, DelegateLaunchArgvIntegrityTests and FakeClaudeContractTests.
    private static readonly string[] ReviewerOsPlatformWindowsClasses =
    [
        "AgentSessionServiceIntegrationTests",
        "ClaudeAdapterLocalShellTests",
        "ClaudeDetectorsTests",
        "ClaudeRemoteControlLaunchArgsTests",
        "ClaudeSubmitContractTests",
        "ClaudeVerifiedDeliveryTests",
        "CodexAdapterLocalShellTests",
        "DelegateLaunchArgvIntegrityTests",
        "DelegationBriefCeilingPtyTests",
        "FakeClaudeContractTests",
        "FakeGrokContractTests",
        "GrokDelegateEndToEndTests",
        "GrokSignInRuntimeTests",
        "HostSessionPipeTests",
        "ModernPtyDa1Tests",
        "ParentDeathSpikeTests",
        "PtyAgentRunnerTests",
        "PtyBackendContractTests",
        "PtyBracketedPasteContractTests",
        "PtyHostLauncherTests",
        "PtyInputChunkingTests",
        "PtyKillProcessTreeTests",
        "PtyLargeWriteTests",
        "PtyStressTests",
        "RawPtyAdapterTests",
        "RunnerPlatformContractTests",
        "SessionMessageQueueGrokPtyIntegrationTests",
        "SessionMessageQueuePtyIntegrationTests",
        "SessionQueueReceiptPlumbingTests",
        "SlashCommandMenuReconciliationTests",
        "WaitForQuietAfterVisibleTests",
    ];

    private static readonly (string Id, string Sha, string? ClassName)[] Replay =
    [
        ("1", "4d91773c122186930a3da485dd2a3221a1e960cd", "AgentTaskLandDeliveryE2ETests"),
        ("2", "be4c317a38568a37b15d41ae985fee6f8054ef1d", "ProcessSpawnLimitTests"),
        ("3", "5e21b61a2d3b05d919edee81d71e43dd65f34751", "Card1277PtyHostFixtureTests"),
        ("4", "dff78584d636fa49eca37652f2c3f14a48002a92", "RepositoryMutationLeaseTests"),
        ("5", "57bbfc99d1810c99f2bf6e1a928427918c05d1fd", "Card1773TranscriptPromptStampMigrationTests"),
        ("6", "ae5b545d9ac9091f42847734b88bf8e202cf2814", "PreDispatchFailureDeliveryTests"),
        ("7", "de3c59991297c9d49d18bba0b7ad6d25ea270156", "C1677TempFamilyRegistryTests"),
        ("8", "86902399c084f51e2cc7021dcf1cbaa9f7353c00", "CapabilityScriptTests"),
        ("9", "bc96c71bef39761b4dbc5c05e1030312396a0e16", "LandGraphEvaluationTests"),
        ("10", "f5cc4facfacdc4e4a72a6d098d225305624dac12", null),
        ("11", "6db9130dfd4ced7a9751fe673628e4f5138bb651", "AgentTaskLandNotificationReconcileLoadTests"),
        ("12", "1d3c34ff062f6cebcffa60110baaa8dc09e3b4b4", "RemoteWorkspacePreparerTests"),
        ("13", "6a371bc38888a77325fbdc24e55e61850ed05e68", "NightlyCoreGoldenTests"),
        ("14", "a930059d8b82f259549925120fda3abf196fa08e", "NightlyCoreGoldenTests"),
        ("15", "6a371bc38888a77325fbdc24e55e61850ed05e68", "WriteBehindFileSystemTests"),
        ("16", "fabda595738775bfc98b4a487dce5543288e7883", "TestLaneCategoryGuardTests"),
        ("17", "c8fc4a413cafc76f5e52c2262ba9419794f196ad", "TestLaneCategoryGuardTests"),
    ];

    // CARD-1838. A stale committed index is rewritten by the Merge command, and a second run
    // is a no-op. The command runs in this process and starts the driver staged by the test build.
    [Test]
    public async Task regenerate_command_reports_changed_then_unchanged()
    {
        using var fixture = new Fixture();
        const string source = "tests/Antiphon.Tests/AddedTests.cs";
        fixture.Add(source, "public sealed class AddedTests { public void T() { } }\n");
        fixture.Add("tests/derived-impact-index.json", "{}\n");
        fixture.Commit();
        var head = Git(fixture.Root, "rev-parse", "HEAD").Single();
        var indexPath = IndexPath(fixture.Root);
        var original = await File.ReadAllBytesAsync(indexPath);
        var originalLink = Path.Combine(fixture.Root, "old-index.json");
        if (OperatingSystem.IsWindows())
            CreateHardLink(originalLink, indexPath, IntPtr.Zero).ShouldBeTrue(Marshal.GetLastPInvokeError().ToString());
        else
            Link(indexPath, originalLink).ShouldBe(0, Marshal.GetLastPInvokeError().ToString());

        // A system-temp staging file cannot be created here, even if system temp and the index share a volume.
        var missingTemp = Path.Combine(fixture.Root, "missing-temp");
        Directory.Exists(missingTemp).ShouldBeFalse();
        var first = await RegenAsync(fixture.Root, missingTemp);
        first.Exit.ShouldBe(0, first.Text);
        AssertChanged(first.Stdout);
        Git(fixture.Root, "rev-parse", "HEAD").Single().ShouldBe(head);
        var written = await File.ReadAllBytesAsync(indexPath);
        written.ShouldBe(DerivedImpactIndexBuilder.Build(fixture.Root).Utf8);
        written.ShouldNotBe(original);
        (await File.ReadAllBytesAsync(originalLink)).ShouldBe(original, "atomic replacement keeps the old file's bytes intact");

        var second = await RegenAsync(fixture.Root, missingTemp);
        second.Exit.ShouldBe(0, second.Text);
        AssertUnchanged(second.Stdout);
        Git(fixture.Root, "rev-parse", "HEAD").Single().ShouldBe(head);
        (await File.ReadAllBytesAsync(indexPath)).ShouldBe(written);

        // Git still lists the source, so the real builder fails before publication.
        File.Delete(Path.Combine(fixture.Root, source));
        var failed = await RegenAsync(fixture.Root, missingTemp);
        failed.Exit.ShouldBe(1, failed.Text);
        failed.Text.ShouldContain("derived-impact-index failed:", Case.Sensitive);
        failed.Stdout.ShouldNotContain("derived-impact-index changed", Case.Sensitive);
        failed.Stdout.ShouldNotContain("derived-impact-index unchanged", Case.Sensitive);
        (await File.ReadAllBytesAsync(indexPath)).ShouldBe(written);
        Directory.GetFiles(Path.GetDirectoryName(indexPath)!).ShouldBe([indexPath]);

        var mutant = first.Stdout.Replace(
            "derived-impact-index changed",
            "derived-impact-index unchanged",
            StringComparison.Ordinal);
        mutant.ShouldNotBe(first.Stdout);
        Should.Throw<ShouldAssertException>(() => AssertChanged(mutant));
    }

    [Test]
    public async Task regenerate_command_removes_staging_file_when_move_fails()
    {
        using var fixture = new Fixture();
        fixture.Add("tests/Antiphon.Tests/AddedTests.cs", "public sealed class AddedTests { public void T() { } }\n");
        fixture.Commit();
        var indexPath = IndexPath(fixture.Root);
        var indexDirectory = Path.GetDirectoryName(indexPath)!;
        // The sibling can be written, but it cannot replace an occupied directory.
        Directory.CreateDirectory(indexPath);
        var marker = Path.Combine(indexPath, "keep");
        await File.WriteAllTextAsync(marker, "existing destination");

        var failed = await RegenAsync(fixture.Root, indexDirectory);

        failed.Exit.ShouldBe(1, failed.Text);
        failed.Text.ShouldContain("derived-impact-index failed:", Case.Sensitive);
        failed.Stdout.ShouldNotContain("derived-impact-index changed", Case.Sensitive);
        failed.Stdout.ShouldNotContain("derived-impact-index unchanged", Case.Sensitive);
        (await File.ReadAllTextAsync(marker)).ShouldBe("existing destination");
        Directory.GetFileSystemEntries(indexDirectory).Order(StringComparer.Ordinal)
            .ShouldBe(new[] { Path.Combine(indexDirectory, "Antiphon.Tests"), indexPath });
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newPath, string oldPath, IntPtr reserved);

    // CARD-1838. The Merge process names the regenerate command as the last step before
    // DerivedImpactIndexTests and the push, and the Merge guard union names the two missed rows.
    [Test]
    public void merge_process_names_the_regenerate_step()
    {
        var root = RepoRoot();
        var loop = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var guide = File.ReadAllText(Path.Combine(root, "docs", "testing-and-build.md"));
        AssertRegenDocs(loop, guide);
        File.Exists(Path.Combine(root, "scripts", "regenerate-derived-impact-index.ps1")).ShouldBeFalse("the regeneration is a command (unit 41)");

        var blinded = loop.Replace(
            RegenStep,
            "The last step before the push is a hand edit of tests/derived-impact-index.json.",
            StringComparison.Ordinal);
        blinded.ShouldNotBe(loop);
        Should.Throw<ShouldAssertException>(() => AssertRegenDocs(blinded, guide));

        var guardBlind = guide.Replace(
            "A Merge brief adds DerivedImpactIndexTests and C1584_E2EExpandedBudget to that standing guard union",
            "A Merge brief keeps the standing guard union",
            StringComparison.Ordinal);
        guardBlind.ShouldNotBe(guide);
        Should.Throw<ShouldAssertException>(() => AssertRegenDocs(loop, guardBlind));
    }

    // rc run ae84478d g131. regenerate_command_reports_changed_then_unchanged runs the driver with
    // --no-build. The qualification builds only Antiphon.sln, and the Merge step builds the driver
    // itself, so only this read sees a driver that the solution build does not produce.
    [Test]
    public void solution_build_produces_the_regenerate_driver()
    {
        var solution = File.ReadAllText(Path.Combine(RepoRoot(), "Antiphon.sln"));
        AssertSolutionBuildsDriver(solution);

        var unbuilt = solution.Replace(".Debug|Any CPU.Build.0 = Debug|Any CPU", ".Debug|Any CPU.Skip = Debug|Any CPU", StringComparison.Ordinal);
        unbuilt.ShouldNotBe(solution);
        Should.Throw<ShouldAssertException>(() => AssertSolutionBuildsDriver(unbuilt));
    }

    private static void AssertSolutionBuildsDriver(string solution)
    {
        const string driver = "\"scripts\\regenerate-derived-impact-index\\Antiphon.DerivedImpactIndex.csproj\"";
        var line = solution.Split('\n').SingleOrDefault(l => l.StartsWith("Project(", StringComparison.Ordinal) && l.Contains(driver, StringComparison.Ordinal));
        line.ShouldNotBeNull("Antiphon.sln does not list the regenerate driver");
        var guid = line.TrimEnd('\r').Split(", ")[^1].Trim('"');
        solution.ShouldContain(guid + ".Debug|Any CPU.Build.0 = Debug|Any CPU", Case.Sensitive);
    }

    private static void AssertChanged(string stdout)
    {
        stdout.ShouldContain("derived-impact-index changed", Case.Sensitive);
        stdout.ShouldNotContain("derived-impact-index unchanged", Case.Sensitive);
    }

    private static void AssertUnchanged(string stdout)
    {
        stdout.ShouldContain("derived-impact-index unchanged", Case.Sensitive);
        stdout.ShouldNotContain("derived-impact-index changed", Case.Sensitive);
    }

    // Unit 41: the step is the CLI command; scripts/regenerate-derived-impact-index.ps1 is gone.
    private const string RegenStep =
        "The last step before DerivedImpactIndexTests and the push is `scripts/antiphon regenerate-derived-impact-index`";

    private static void AssertRegenDocs(string loop, string guide)
    {
        const string heading = "### Merge process (CARD-1783)";
        var start = loop.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = loop.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = end < 0 ? loop[start..] : loop[start..end];
        section.ShouldContain(RegenStep, Case.Sensitive);
        section.ShouldContain("DerivedImpactIndexBuilder.Build", Case.Sensitive);
        section.ShouldContain("DerivedImpactIndexBuilder.Write", Case.Sensitive);
        section.ShouldContain("commits nothing", Case.Sensitive);
        section.ShouldContain(
            "DerivedImpactIndexTests.committed_index_matches_the_builder keeps the full-byte compare",
            Case.Sensitive);
        section.ShouldContain("so a Merge that skips the command still fails", Case.Sensitive);

        const string guard = "A Merge brief adds DerivedImpactIndexTests and C1584_E2EExpandedBudget to that standing guard union";
        guide.ShouldContain(guard, Case.Sensitive);
        guide.ShouldContain("/*/*/C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget*", Case.Sensitive);
        guide.ShouldContain("(DerivedImpactIndexTests*)", Case.Sensitive);
    }

    [Test]
    public async Task declaration_diff_selects_the_new_tests_class()
    {
        using var fixture = new Fixture();
        fixture.Add("tests/Antiphon.Tests/AddedTests.cs", "public sealed class AddedTests { public void T() { } }\n");
        fixture.Add("tests/impacted-classes.json", AnchorTable("AddedTests"));
        var ran = await SelectAsync(fixture.Root, "--path", "tests/Antiphon.Tests/AddedTests.cs", "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        Strings(json.RootElement, "classes").ShouldContain("AddedTests");
        Strings(json.RootElement, "derivedClasses").ShouldBe(["AddedTests"]);
        AssertEmptyIds(json.RootElement);
        Strings(json.RootElement, "kinds").ShouldBeEmpty();
    }

    [Test]
    public async Task type_edge_on_a_fixture_diff_selects_the_lease_class()
    {
        using var fixture = FixtureWithLandingType("""
            [Category("Unit")]
            public sealed class RepositoryMutationLeaseTests
            {
                public void NamesTheType() { var git = new LandingGit(); }
            }
            """);
        var selected = await SelectBuiltAsync(fixture, LandingGitPath);
        Strings(selected, "classes").ShouldContain("RepositoryMutationLeaseTests");
        Strings(selected, "derivedClasses").ShouldContain("RepositoryMutationLeaseTests");
    }

    [Test]
    public async Task one_hop_path_selects_the_temp_family_class()
    {
        using var fixture = new Fixture();
        fixture.Add("src/Antiphon.SessionRunner/RunnerTempSweep.cs", """
            public sealed class RunnerTempSweep
            {
                public const string Owned = "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs";
            }
            """);
        fixture.Add("tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs", "public class PtyHostLeakSweep { }\n");
        fixture.Add("tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs", """
            [Category("Unit")]
            public sealed class C1677TempFamilyRegistryTests
            {
                public void NamesTheSweep() { var families = RunnerTempSweep.Owned; }
            }
            """);
        fixture.Add("tests/impacted-classes.json", AnchorTable("C1677TempFamilyRegistryTests"));
        fixture.Commit();
        var selected = await SelectBuiltAsync(fixture, LeakSweepPath);
        Strings(selected, "classes").ShouldContain("C1677TempFamilyRegistryTests");
        Strings(selected, "derivedClasses").ShouldContain("C1677TempFamilyRegistryTests");
    }

    [Test]
    public async Task dotted_script_name_selects_the_lease_class()
    {
        using var fixture = FixtureWithLandingType("""
            [Category("Unit")]
            public sealed class RepositoryMutationLeaseTests
            {
                public void Script()
                {
                    var script = "Antiphon.Server.Infrastructure.Git.LandingGit";
                }
            }
            """);
        var selected = await SelectBuiltAsync(fixture, LandingGitPath);
        Strings(selected, "classes").ShouldContain("RepositoryMutationLeaseTests");
        Strings(selected, "derivedClasses").ShouldContain("RepositoryMutationLeaseTests");
    }

    [Test]
    public void a_path_with_26_classes_is_capped()
    {
        using var fixture = new Fixture();
        var body = new System.Text.StringBuilder();
        for (var i = 0; i < 26; i++)
        {
            body.Append("public sealed class Cap").Append(i.ToString("00"))
                .Append("Tests { public void T() { var _ = \"shared/fixture-path.txt\"; } }\n");
        }

        fixture.Add("tests/Antiphon.Tests/CapTests.cs", body.ToString());
        fixture.Add("shared/fixture-path.txt", "fixture\n");
        fixture.Commit();
        var index = DerivedImpactIndexBuilder.Build(fixture.Root);
        var edge = index.Edges.Single(row => row.Path == "shared/fixture-path.txt");
        edge.Classes.Count.ShouldBe(DerivedImpactIndexBuilder.Cap);
        edge.Classes.ShouldBe(Enumerable.Range(0, 25).Select(i => "Cap" + i.ToString("00") + "Tests").ToArray());
        var capped = index.CappedPaths.Single(row => row.Path == "shared/fixture-path.txt");
        capped.Classes.ShouldBe(26);
        capped.Reason.ShouldBe("cap");
        capped.Owed.ShouldBe(1);
        capped.First.ShouldBe(["Cap25Tests"]);
    }

    [Test]
    public async Task solution_file_selects_no_derived_class()
    {
        using var fixture = new Fixture();
        fixture.Add("Antiphon.sln", "fixture\n");
        fixture.Add("tests/Antiphon.Tests/SolutionShapeTests.cs", """
            [Category("Unit")]
            public sealed class SolutionShapeTests
            {
                public void ReadsTheSolution() { var path = "Antiphon.sln"; }
            }
            """);
        fixture.Add("tests/impacted-classes.json", AnchorTable("SolutionShapeTests"));
        fixture.Commit();
        var index = DerivedImpactIndexBuilder.Build(fixture.Root);
        index.Edges.Any(edge => edge.Path == "Antiphon.sln").ShouldBeFalse();
        index.CappedPaths.Single(row => row.Path == "Antiphon.sln").Reason.ShouldBe("allowlist");
        DerivedImpactIndexBuilder.Write(fixture.Root, index);
        var ran = await SelectAsync(fixture.Root, "--path", "Antiphon.sln", "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        Strings(json.RootElement, "classes").ShouldBeEmpty();
        Strings(json.RootElement, "derivedClasses").ShouldBeEmpty();
        Strings(json.RootElement, "skippedByCap").ShouldContain("Antiphon.sln");

        var real = await SelectAsync(RepoRoot(), "--path", "Antiphon.sln", "--json");
        real.Exit.ShouldBe(0, real.Text);
        using var realJson = JsonDocument.Parse(real.Stdout);
        Strings(realJson.RootElement, "derivedClasses").ShouldBeEmpty();
        Strings(realJson.RootElement, "skippedByCap").ShouldContain("Antiphon.sln");
        Strings(realJson.RootElement, "classes").ShouldNotContain("LandGraphEvaluationTests");
        Strings(realJson.RootElement, "classes").ShouldNotContain("LandGraphEvaluationScriptTests");
    }

    [Test]
    public async Task migration_only_diff_keeps_hand_classes_and_the_derive_set()
    {
        var ran = await SelectAsync(RepoRoot(), "--path", MigrationPath, "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        Strings(json.RootElement, "kinds").ShouldBe(["migration-generated-sql-text", "migrations"]);
        var classes = Strings(json.RootElement, "classes");
        var windows = WholeClasses(json.RootElement, "windowsFilters");
        var windowsOnly = await CommittedWindowsOnly();
        foreach (var name in HandMigrationClasses)
        {
            // A hand class the index marks windowsOnly is still selected. It leaves the Linux list.
            if (windowsOnly.Contains(name))
            {
                classes.ShouldNotContain(name);
                windows.ShouldContain(name);
            }
            else
            {
                classes.ShouldContain(name);
            }
        }
        classes.ShouldContain("AgentTuiSecretMigratorTests");
        classes.ShouldContain("Card1773TranscriptPromptStampMigrationTests");
        classes.ShouldContain("TranscriptHotPathMigrationTests");
        AssertEmptyIds(json.RootElement);
    }

    [Test]
    public async Task schema_2_is_still_refused()
    {
        var root = RepoRoot();
        var path = Path.Combine(Path.GetTempPath(), "derived-index-schema-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, "{\"schema\":2,\"kinds\":[]}");
        try
        {
            var ran = await SelectAsync(root, "--path", MigrationPath, "--json", "--table", path);
            ran.Exit.ShouldBe(1, ran.Text);
            ran.Text.ShouldContain("schema", Case.Sensitive);
            using var json = JsonDocument.Parse(ran.Stdout);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task merge_subset_on_a_derived_hit_emits_the_fixed_classes()
    {
        var ran = await SelectAsync(RepoRoot(), "--merge-subset", "--path", LeakSweepPath, "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        json.RootElement.GetProperty("mode").GetString().ShouldBe("merge-subset");
        var classes = Strings(json.RootElement, "classes");
        foreach (var name in FixedClasses)
            classes.ShouldContain(name);
        classes.ShouldContain("ReleaseGatePlatformTests");
        json.RootElement.TryGetProperty("keywordIds", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("rosterIds", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("derivedClasses", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("skippedByCap", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("owed", out _).ShouldBeFalse();
    }

    [Test]
    public async Task tool_only_diff_selects_the_budget_method()
    {
        using var fixture = new Fixture();
        fixture.Add(
            "tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs",
            "public sealed class LandGraphEvaluationTests { public void EvaluationBudgetRefusesSlowDotnet() { } }\n");
        fixture.Add("tests/impacted-classes.json", AnchorTable("LandGraphEvaluationTests"));
        fixture.Add("tests/derived-impact-index.json", """
            {
              "schema": 1,
              "cap": 25,
              "byteLimit": 2000000,
              "allowlist": [],
              "cappedPaths": [],
              "edges": [
                {
                  "path": "tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs",
                  "classes": [ "LandGraphEvaluationTests" ]
                }
              ]
            }
            """);
        var method = await SelectAsync(fixture.Root, "--path", PrecheckPath, "--json");
        method.Exit.ShouldBe(0, method.Text);
        using var methodJson = JsonDocument.Parse(method.Stdout);
        var filter = methodJson.RootElement.GetProperty("filters")[0].GetProperty("treenodeFilter").GetString();
        filter.ShouldBe("/*/*/LandGraphEvaluationTests/(EvaluationBudgetRefusesSlowDotnet*)");
        Strings(methodJson.RootElement, "derivedClasses").ShouldContain("LandGraphEvaluationTests");

        // pwsh -File rejects a repeated -Path. -FileList is the supported multi-path form.
        var list = Path.Combine(Path.GetTempPath(), "derived-index-both-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(
                list,
                PrecheckPath + "\ntests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs\n");
            var whole = await SelectAsync(fixture.Root, "--file-list", list, "--json");
            whole.Exit.ShouldBe(0, whole.Text);
            using var wholeJson = JsonDocument.Parse(whole.Stdout);
            var wholeFilter = wholeJson.RootElement.GetProperty("filters")[0].GetProperty("treenodeFilter").GetString();
            wholeFilter.ShouldBe("/*/*/(LandGraphEvaluationTests*)/*");
            wholeJson.RootElement.GetProperty("treenodeFilter").GetString().ShouldBe(wholeFilter);
        }
        finally
        {
            File.Delete(list);
        }
    }

    [Test]
    public async Task s1_replay_matches_declaration_and_index_edges()
    {
        var root = RepoRoot();
        var indexPath = Path.Combine(root, "tests", "derived-impact-index.json");
        File.Exists(indexPath).ShouldBeTrue(indexPath);
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        var allow = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in index.RootElement.GetProperty("allowlist").EnumerateArray())
            allow.Add(row.GetString() ?? "");
        var windowsOnly = Strings(index.RootElement, "windowsOnly").ToHashSet(StringComparer.Ordinal);

        // S1 claims. Rows 13, 16, and 17 are roster catches and stay out of this slice.
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "2", "3", "4", "5", "6", "7", "8", "9", "11", "12", "14", "15",
        };

        var slowest = TimeSpan.Zero;
        foreach (var row in Replay)
        {
            var range = row.Sha + "^.." + row.Sha;
            var started = Stopwatch.StartNew();
            var ran = await SelectAsync(root, "--range", range, "--json");
            started.Stop();
            if (started.Elapsed > slowest)
                slowest = started.Elapsed;
            ran.Exit.ShouldBe(0, row.Id + " " + ran.Text);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), row.Id + " selector budget");
            using var selected = JsonDocument.Parse(ran.Stdout);
            var names = Git(root, "diff", "--name-only", range);
            var classes = Strings(selected.RootElement, "classes").ToHashSet(StringComparer.Ordinal);
            var windows = WholeClasses(selected.RootElement, "windowsFilters");
            var skipped = Strings(selected.RootElement, "skippedByCap").ToHashSet(StringComparer.Ordinal);
            foreach (var file in names)
            {
                if (allow.Contains(file))
                    skipped.ShouldContain(file, row.Id + " " + file);
            }

            foreach (var name in WholeClasses(selected.RootElement, "filters"))
                windowsOnly.ShouldNotContain(name, "replay " + row.Id + " linux " + name);

            var selectedName = row.ClassName is not null && (classes.Contains(row.ClassName) || windows.Contains(row.ClassName));
            Console.WriteLine("REPLAY " + row.Id + " class=" + (row.ClassName ?? "") + " selected=" + selectedName);
            if (required.Contains(row.Id))
                selectedName.ShouldBeTrue("replay " + row.Id + " " + row.ClassName);
            if (row.Id == "1")
            {
                // CARD-1819's e2e-land-delivery kind is windows-only. The derived index still has no Linux edge.
                classes.ShouldNotContain(row.ClassName!, "replay 1 linux");
                windows.ShouldContain(row.ClassName!, "replay 1 windows");
            }

            // Rows 2, 4, 7, 9 and 14 are Linux catches. A Windows-only landing is the round-3 hole.
            if (row.Id is "2" or "4" or "7" or "9" or "14")
            {
                WholeClasses(selected.RootElement, "filters")
                    .ShouldContain(row.ClassName!, "replay " + row.Id + " linux filters");
                if (row.Id is "2" or "4" or "7" or "14")
                    windows.ShouldNotContain(row.ClassName!, "replay " + row.Id + " windows filters");
            }

            if (row.Id == "10")
                skipped.ShouldContain("Antiphon.sln");
        }

        Console.WriteLine("REPLAY slowest=" + slowest.TotalSeconds.ToString("0.0") + "s");
    }

    [Test]
    public void index_bytes_stay_lf_when_the_newline_source_is_crlf()
    {
        var previous = DerivedImpactIndexBuilder.NewLineSource;
        try
        {
            DerivedImpactIndexBuilder.NewLineSource = static () => "\r\n";
            using var fixture = new Fixture();
            fixture.Add("tests/Antiphon.Tests/AddedTests.cs", "public sealed class AddedTests { public void T() { } }\n");
            fixture.Commit();
            var index = DerivedImpactIndexBuilder.Build(fixture.Root);
            index.Utf8.Contains((byte)'\r').ShouldBeFalse();
            index.Utf8[^1].ShouldBe((byte)'\n');
            DerivedImpactIndexBuilder.NewLineSource().ShouldBe("\r\n");
        }
        finally
        {
            DerivedImpactIndexBuilder.NewLineSource = previous;
        }
    }

    [Test]
    public async Task capped_real_paths_keep_the_specific_class()
    {
        var root = RepoRoot();
        var lease = await SelectAsync(root, "--path", LandingGitPath, "--json");
        lease.Exit.ShouldBe(0, lease.Text);
        using (var json = JsonDocument.Parse(lease.Stdout))
        {
            // RepositoryMutationLeaseTests is undeclared, so it stays on Linux. The boundary
            // class is the hand windows:only floor and stays off that filter.
            Strings(json.RootElement, "classes").ShouldContain("RepositoryMutationLeaseTests");
            Strings(json.RootElement, "classes").ShouldNotContain("RepositoryLeaseWindowsBoundaryTests");
            WholeClasses(json.RootElement, "windowsFilters").ShouldNotContain("RepositoryMutationLeaseTests");
            WholeClasses(json.RootElement, "windowsFilters").ShouldContain("RepositoryLeaseWindowsBoundaryTests");
            // LandNonCodeSkipTests subclasses LandingGit, so it leads the cap and the owed remainder is 40.
            OwedCount(json.RootElement, LandingGitPath).ShouldBe(40);
        }

        var leaseText = await SelectAsync(root, "--path", LandingGitPath);
        leaseText.Exit.ShouldBe(0, leaseText.Text);
        leaseText.Stdout.Replace("\r\n", "\n").Split('\n').ShouldContain(
            "owed/capped server/Infrastructure/Git/LandingGit.cs count=40 first=LandNonCodeSkipTests,"
            + "RunnerSettlementSyncTests,AgentTaskLandVerifierTests,PhoneHomeTaskDispatchProjectionTests,"
            + "RepairSourceDispatchTests,RepositoryMutationLeaseDescribeTests,RunnerAlarmDeliveryTests,"
            + "RunnerStateAlarmExclusionTests");

        var service = await SelectAsync(root, "--path", NotificationServicePath, "--json");
        service.Exit.ShouldBe(0, service.Text);
        using (var json = JsonDocument.Parse(service.Stdout))
        {
            Strings(json.RootElement, "classes").ShouldContain("PreDispatchFailureDeliveryTests");
            // The wake test joined the kept window, so this path's owed remainder is 17.
            OwedCount(json.RootElement, NotificationServicePath).ShouldBe(17);
        }

        var sweep = await SelectAsync(root, "--path", LeakSweepPath, "--json");
        sweep.Exit.ShouldBe(0, sweep.Text);
        using var sweepJson = JsonDocument.Parse(sweep.Stdout);
        Strings(sweepJson.RootElement, "classes").ShouldContain("C1677TempFamilyRegistryTests");
        WholeClasses(sweepJson.RootElement, "windowsFilters").ShouldNotContain("C1677TempFamilyRegistryTests");
        OwedCount(sweepJson.RootElement, LeakSweepPath).ShouldBe(7);
    }

    // CARD-1843. Every merge-subset entry runs in exactly the projects whose directories declare its class, a
    // remainderProjects directory of the selection catalog being a project of its own, and the land rows stay at four
    // land classes or fewer. Until 2026-11-22 this compared the output with a frozen PowerShell copy of the selector
    // (72fada66, before the project map); the selector is C# now (unit 4 of the remaining-ps1 plan) and the invariant
    // that comparison guarded is asserted directly.
    [Test]
    public async Task merge_subset_runs_each_entry_in_its_declaring_projects()
    {
        var root = RepoRoot();
        var declared = DeclaringProjects(root);
        var mirrored = 0;
        foreach (var sha in MergeRanges)
        {
            var args = sha is null
                ? new[] { "--merge-subset", "--json" }
                : new[] { "--merge-subset", "--range", sha + "^.." + sha, "--json" };
            var current = await SelectAsync(root, args);
            current.Exit.ShouldBe(0, sha + " " + current.Text);
            using var now = JsonDocument.Parse(current.Stdout);
            foreach (var property in new[] { "filters", "windowsFilters" })
            {
                foreach (var (entry, projects) in FilterEntries(now.RootElement, property))
                {
                    var className = entry.Split('/')[0];
                    var expected = declared.TryGetValue(className, out var found) ? found : ["tests/Antiphon.Tests"];
                    projects.Order(StringComparer.Ordinal).ToArray()
                        .ShouldBe(expected.Order(StringComparer.Ordinal).ToArray(), sha + " " + property + " " + entry);
                    if (projects.Any(project => !HistoricalProjects.Contains(project) && project != "tests/Antiphon.Tests"))
                        mirrored++;
                }

                foreach (var row in now.RootElement.GetProperty(property).EnumerateArray())
                {
                    var filter = row.GetProperty("treenodeFilter").GetString() ?? "";
                    filter.Split('(', ')', '|', '/').Count(name => name.Contains("Land", StringComparison.Ordinal))
                        .ShouldBeLessThanOrEqualTo(4, sha + " " + filter);
                }
            }
        }

        // dff78584 edits LandingGit.cs, which selects the Server.Git mirror classes.
        mirrored.ShouldBeGreaterThan(0, "no range exercised the CARD-1843 project map");
    }

    // The test projects of their own that predate the catalog's remainderProjects.
    private static readonly string[] HistoricalProjects = ["tests/Antiphon.E2E", "tests/Antiphon.Messaging.Tests", "tests/Antiphon.SessionRunner.Tests"];

    [Test]
    public async Task windows_only_classes_stay_out_of_linux_filters()
    {
        var root = RepoRoot();
        var indexPath = Path.Combine(root, "tests", "derived-impact-index.json");
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        var windowsOnly = Strings(index.RootElement, "windowsOnly").ToHashSet(StringComparer.Ordinal);
        foreach (var sha in MergeRanges)
        {
            if (sha is null)
                continue;
            var ran = await SelectAsync(root, "--range", sha + "^.." + sha, "--json");
            ran.Exit.ShouldBe(0, sha + " " + ran.Text);
            using var selected = JsonDocument.Parse(ran.Stdout);
            foreach (var name in WholeClasses(selected.RootElement, "filters"))
                windowsOnly.ShouldNotContain(name, sha + " " + name);
        }
    }

    [Test]
    public async Task reviewer_census_classes_are_windows_only()
    {
        ReviewerOsPlatformWindowsClasses.Length.ShouldBe(31);
        var windowsOnly = await CommittedWindowsOnly();
        foreach (var name in ReviewerOsPlatformWindowsClasses)
            windowsOnly.ShouldContain(name);
    }

    [Test]
    public async Task undeclared_class_with_an_os_token_stays_on_both_lanes()
    {
        var probe = await SelectDeclarationAsync("UndeclaredOsTokenTests", """
            [Category("Unit")]
            public sealed class UndeclaredOsTokenTests
            {
                [Test]
                public void Runs()
                {
                    if (OperatingSystem.IsWindows()) { }
                }
            }
            """, overrides: null);
        probe.WindowsOnly.ShouldNotContain("UndeclaredOsTokenTests");
        probe.LinuxOnly.ShouldNotContain("UndeclaredOsTokenTests");
        WholeClasses(probe.Json, "filters").ShouldContain("UndeclaredOsTokenTests");
        WholeClasses(probe.Json, "windowsFilters").ShouldNotContain("UndeclaredOsTokenTests");
    }

    [Test]
    public async Task linux_only_methods_stay_off_the_windows_filter()
    {
        var probe = await SelectDeclarationAsync("AllLinuxOnlyTests", """
            [Category("Unit")]
            public sealed class AllLinuxOnlyTests
            {
                [Test]
                [LinuxOnly("fifo")]
                public void One() { }

                [Test]
                [LinuxOnly("procfs")]
                public void Two()
                {
                    if (OperatingSystem.IsWindows()) { }
                }
            }
            """, overrides: null);
        probe.LinuxOnly.ShouldContain("AllLinuxOnlyTests");
        probe.WindowsOnly.ShouldNotContain("AllLinuxOnlyTests");
        WholeClasses(probe.Json, "filters").ShouldContain("AllLinuxOnlyTests");
        WholeClasses(probe.Json, "windowsFilters").ShouldNotContain("AllLinuxOnlyTests");
    }

    [Test]
    public async Task has_windows_specifics_stays_on_both_lanes()
    {
        var probe = await SelectDeclarationAsync("WindowsSpecificsTests", """
            [Category("Unit")]
            public sealed class WindowsSpecificsTests
            {
                [Test]
                [HasWindowsSpecifics("path")]
                public void One()
                {
                    if (OperatingSystem.IsWindows()) { }
                }
            }
            """, overrides: null);
        probe.WindowsOnly.ShouldNotContain("WindowsSpecificsTests");
        probe.LinuxOnly.ShouldNotContain("WindowsSpecificsTests");
        WholeClasses(probe.Json, "filters").ShouldContain("WindowsSpecificsTests");
        WholeClasses(probe.Json, "windowsFilters").ShouldNotContain("WindowsSpecificsTests");
    }

    [Test]
    public async Task class_level_linux_only_stays_off_the_windows_filter()
    {
        var probe = await SelectDeclarationAsync("ClassLinuxOnlyTests", """
            [LinuxOnly("class shorthand")]
            [Category("Unit")]
            public sealed class ClassLinuxOnlyTests
            {
                [Test]
                public void One()
                {
                    if (OperatingSystem.IsWindows()) { }
                }
            }
            """, overrides: null);
        probe.LinuxOnly.ShouldContain("ClassLinuxOnlyTests");
        probe.WindowsOnly.ShouldNotContain("ClassLinuxOnlyTests");
        WholeClasses(probe.Json, "filters").ShouldContain("ClassLinuxOnlyTests");
        WholeClasses(probe.Json, "windowsFilters").ShouldNotContain("ClassLinuxOnlyTests");
    }

    [Test]
    public async Task override_list_forces_an_undeclared_class_onto_windows()
    {
        var probe = await SelectDeclarationAsync("OverrideTargetTests", """
            [Category("Unit")]
            public sealed class OverrideTargetTests
            {
                [Test]
                public void One() { }
            }
            """, overrides: "OverrideTargetTests\n");
        probe.WindowsOnly.ShouldContain("OverrideTargetTests");
        probe.LinuxOnly.ShouldNotContain("OverrideTargetTests");
        WholeClasses(probe.Json, "filters").ShouldNotContain("OverrideTargetTests");
        WholeClasses(probe.Json, "windowsFilters").ShouldContain("OverrideTargetTests");
    }

    [Test]
    public void override_file_names_exist_and_are_undeclared()
    {
        var root = RepoRoot();
        var path = Path.Combine(root, DerivedImpactIndexBuilder.OverrideRelativePath);
        var text = File.ReadAllText(path);
        text.Contains('\r').ShouldBeFalse();
        text.EndsWith('\n').ShouldBeTrue();
        var names = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.ShouldBe(ReviewerOsPlatformWindowsClasses);
        names.ShouldBe(names.Order(StringComparer.Ordinal).ToArray());
        var facts = DerivedImpactIndexBuilder.InspectOverrides(root);
        facts.Select(fact => fact.Name).ShouldBe(names);
        foreach (var fact in facts)
        {
            fact.Found.ShouldBeTrue(fact.Name);
            fact.Declared.ShouldBeFalse(fact.Name);
        }
    }

    // Ordered method tuples of the five declarations, lengths 0 through 4: 781.
    // Eight class-shape cases follow. The count is the closed product the parser must match.
    [Test]
    public void parser_agrees_with_platform_taxonomy_derive()
    {
        var values = Enum.GetValues<PlatformDeclaration>();
        values.Length.ShouldBe(5);
        var compared = 0;
        foreach (var methods in DeclarationTuples(values))
        {
            var parsed = DerivedImpactIndexBuilder.ClassifyTexts(
                [("tests/Antiphon.Tests/ProbeTests.cs", RenderProbe(methods))],
                []);
            var members = new PlatformMember[methods.Length];
            for (var i = 0; i < methods.Length; i++)
                members[i] = new PlatformMember(true, methods[i]);
            Agree(parsed, members, string.Join(",", methods));
            compared++;
        }

        compared.ShouldBe(781);

        Agree(ClassifyOne("""
            [LinuxOnly("class")]
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                public void One() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.LinuxOnly)], "class shorthand");
        Agree(ClassifyOne("""
            [LinuxOnly("class")]
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                [WindowsOnly("method")]
                public void One() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.LinuxOnly)], "class wins");
        Agree(ClassifyOne("""
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                public void One() { }

                [LinuxOnly("helper")]
                public void Helper() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.Unannotated)], "helper ignored");
        Agree(ClassifyOne("""
            [WindowsOnly("class")]
            [Category("Unit")]
            public sealed class ProbeTests {
                public void Helper() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.WindowsOnly)], "class with no test");
        Agree(ClassifyOne("""
            [LinuxOnly("a")]
            [WindowsOnly("b")]
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                public void One() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.Unannotated)], "class conflict");
        Agree(ClassifyOne("""
            [HasWindowsSpecifics("class")]
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                public void One() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.HasWindowsSpecifics)], "specifics shorthand");
        Agree(ClassifyOne("""
            [Category("Unit")]
            public sealed class ProbeTests {
                [Test]
                [LinuxOnlyAttribute("suffix")]
                public void One() { }
            }
            """), [new PlatformMember(true, PlatformDeclaration.LinuxOnly)], "attribute suffix");

        var partial = DerivedImpactIndexBuilder.ClassifyTexts(
            [
                ("tests/Antiphon.Tests/A.cs", """
                    namespace N;
                    public partial class ProbeTests {
                        [Test]
                        [LinuxOnly("a")]
                        public void One() { }
                    }
                    """),
                ("tests/Antiphon.Tests/B.cs", """
                    namespace N;
                    public partial class ProbeTests {
                        [Test]
                        [LinuxOnly("b")]
                        public void Two() { }
                    }
                    """),
            ],
            []);
        Agree(partial, [
            new PlatformMember(true, PlatformDeclaration.LinuxOnly),
            new PlatformMember(true, PlatformDeclaration.LinuxOnly),
        ], "partial");
        compared += 8;

        var forced = DerivedImpactIndexBuilder.ClassifyTexts(
            [("tests/Antiphon.Tests/ProbeTests.cs", """
                [Category("Unit")]
                public sealed class ProbeTests {
                    [Test]
                    [LinuxOnly("declared")]
                    public void One() { }
                }
                """)],
            ["ProbeTests"]);
        forced.WindowsOnly.ShouldContain("ProbeTests");
        forced.LinuxOnly.ShouldNotContain("ProbeTests");
        compared.ShouldBe(789);
    }

    private static IEnumerable<PlatformDeclaration[]> DeclarationTuples(PlatformDeclaration[] values)
    {
        yield return [];
        for (var length = 1; length <= 4; length++)
        {
            var total = 1;
            for (var i = 0; i < length; i++)
                total *= values.Length;
            for (var n = 0; n < total; n++)
            {
                var row = new PlatformDeclaration[length];
                var rest = n;
                for (var i = 0; i < length; i++)
                {
                    row[i] = values[rest % values.Length];
                    rest /= values.Length;
                }

                yield return row;
            }
        }
    }

    private static string RenderProbe(PlatformDeclaration[] methods)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("public sealed class ProbeTests {\n");
        for (var i = 0; i < methods.Length; i++)
        {
            builder.Append("    [Test]\n");
            if (methods[i] != PlatformDeclaration.Unannotated)
            {
                builder.Append("    [");
                builder.Append(methods[i].ToString());
                builder.Append("(\"r\")]\n");
            }

            builder.Append("    public void M");
            builder.Append(i.ToString());
            builder.Append("() { }\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static LaneSets ClassifyOne(string source) =>
        DerivedImpactIndexBuilder.ClassifyTexts([("tests/Antiphon.Tests/ProbeTests.cs", source)], []);

    private static void Agree(LaneSets parsed, PlatformMember[] members, string label)
    {
        var expected = PlatformTaxonomy.Derive(members);
        parsed.LinuxOnly.Contains("ProbeTests").ShouldBe(expected.LinuxOnly, label);
        parsed.WindowsOnly.Contains("ProbeTests").ShouldBe(expected.WindowsOnly, label);
    }

    private static string IndexPath(string root) => Path.Combine(root, "tests", "derived-impact-index.json");

    private static async Task<HashSet<string>> CommittedWindowsOnly()
    {
        var root = RepoRoot();
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(IndexPath(root)));
        return Strings(index.RootElement, "windowsOnly").ToHashSet(StringComparer.Ordinal);
    }

    internal static List<string> Edge(DerivedImpactIndex index, string path) =>
        index.Edges.Single(edge => edge.Path == path).Classes.ToList();

    internal static void Capped(DerivedImpactIndex index, string path)
    {
        var row = index.CappedPaths.Single(item => item.Path == path);
        row.Reason.ShouldBe("cap", path);
        row.Classes.ShouldBeGreaterThan(DerivedImpactIndexBuilder.Cap, path);
    }

    private static Fixture FixtureWithLandingType(string testsSource)
    {
        var fixture = new Fixture();
        fixture.Add("server/Infrastructure/Git/LandingGit.cs", """
            namespace Antiphon.Server.Infrastructure.Git
            {
                public sealed class LandingGit
                {
                }
            }
            """);
        fixture.Add("tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs", testsSource);
        fixture.Add("tests/impacted-classes.json", AnchorTable("RepositoryMutationLeaseTests"));
        fixture.Commit();
        return fixture;
    }

    private readonly record struct LaneProbe(
        IReadOnlyList<string> WindowsOnly,
        IReadOnlyList<string> LinuxOnly,
        JsonElement Json);

    private async Task<LaneProbe> SelectDeclarationAsync(string className, string source, string? overrides)
    {
        using var fixture = new Fixture();
        fixture.Add("tests/Antiphon.Tests/" + className + ".cs", source);
        fixture.Add("tests/impacted-classes.json", AnchorTable(className));
        if (overrides is not null)
            fixture.Add("tests/derived-impact-index-known-os-gated.txt", overrides);
        fixture.Commit();
        var index = DerivedImpactIndexBuilder.Build(fixture.Root);
        DerivedImpactIndexBuilder.Write(fixture.Root, index);
        var ran = await SelectAsync(fixture.Root, "--path", "tests/Antiphon.Tests/" + className + ".cs", "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var document = JsonDocument.Parse(ran.Stdout);
        return new LaneProbe(index.WindowsOnly.ToArray(), index.LinuxOnly.ToArray(), document.RootElement.Clone());
    }

    private async Task<JsonElement> SelectBuiltAsync(Fixture fixture, string path)
    {
        var index = DerivedImpactIndexBuilder.Build(fixture.Root);
        DerivedImpactIndexBuilder.Write(fixture.Root, index);
        var ran = await SelectAsync(fixture.Root, "--path", path, "--json");
        ran.Exit.ShouldBe(0, ran.Text);
        var document = JsonDocument.Parse(ran.Stdout);
        // The caller reads the element before the method returns to the test, which disposes nothing.
        // Keep the document alive by parking it on the element via a cloned buffer.
        return document.RootElement.Clone();
    }

    private static string AnchorTable(string className) =>
        "{\"schema\":1,\"cache\":\"none\",\"kinds\":[{\"id\":\"anchor\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\""
        + className + "\"]}]}";

    private static void AssertEmptyIds(JsonElement root)
    {
        root.GetProperty("keywordIds").GetArrayLength().ShouldBe(0);
        root.GetProperty("rosterIds").GetArrayLength().ShouldBe(0);
    }

    private static int OwedCount(JsonElement root, string path)
    {
        foreach (var row in root.GetProperty("owed").EnumerateArray())
        {
            if (row.GetProperty("path").GetString() == path)
                return row.GetProperty("count").GetInt32();
        }

        throw new ShouldAssertException("owed row missing for " + path);
    }

    // Class name -> the projects whose directory holds a file declaring it: the historical test projects and every
    // remainderProjects directory of the selection catalog, the longest directory winning; tests/Antiphon.Tests otherwise.
    private static Dictionary<string, HashSet<string>> DeclaringProjects(string root)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "test-selection-policy.json")));
        var dirs = catalog.RootElement.GetProperty("sets").EnumerateArray()
            .SelectMany(set => set.GetProperty("remainderProjects").EnumerateArray())
            .Select(item => item.GetString() ?? "")
            .Where(entry => entry.StartsWith("tests/", StringComparison.Ordinal) && entry.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(entry => entry[..entry.LastIndexOf('/')])
            .Concat(HistoricalProjects)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var declaration = new System.Text.RegularExpressions.Regex(
            @"(?m)^[ \t]*(?:(?:public|internal|file|private|protected)[ \t]+)?(?:(?:sealed|static|abstract|partial)[ \t]+)*class[ \t]+([A-Za-z_][A-Za-z0-9_]*)\b");
        var projects = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            var project = dirs.Where(dir => relative.StartsWith(dir + "/", StringComparison.Ordinal))
                .OrderByDescending(dir => dir.Length).FirstOrDefault() ?? "tests/Antiphon.Tests";
            foreach (System.Text.RegularExpressions.Match match in declaration.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups[1].Value;
                if (!projects.TryGetValue(name, out var set))
                    projects[name] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(project);
            }
        }

        return projects;
    }

    // "Class" for a whole-class row, "Class/Method" for a method row, each with the projects that run it.
    private static Dictionary<string, List<string>> FilterEntries(JsonElement root, string property)
    {
        var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty(property).EnumerateArray())
        {
            var project = row.GetProperty("project").GetString() ?? "";
            var body = (row.GetProperty("treenodeFilter").GetString() ?? "")["/*/*/".Length..];
            var keys = new List<string>();
            if (body.StartsWith('('))
            {
                body.EndsWith("/*", StringComparison.Ordinal).ShouldBeTrue(body);
                keys.AddRange(body[..^2].Split('|').Select(part => part[1..^2]));
            }
            else
            {
                var slash = body.IndexOf('/');
                keys.AddRange(body[(slash + 1)..].Split('|').Select(part => body[..slash] + "/" + part[1..^2]));
            }

            foreach (var key in keys)
            {
                if (!entries.TryGetValue(key, out var list))
                    entries[key] = list = [];
                list.Add(project);
            }
        }

        return entries;
    }

    private static HashSet<string> WholeClasses(JsonElement root, string property)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(property, out var filters))
            return names;
        foreach (var filter in filters.EnumerateArray())
        {
            var body = filter.GetProperty("treenodeFilter").GetString() ?? "";
            const string prefix = "/*/*/";
            if (body.StartsWith(prefix, StringComparison.Ordinal))
                body = body[prefix.Length..];
            if (body.EndsWith("/*", StringComparison.Ordinal))
                body = body[..^2];
            foreach (var part in body.Split('|'))
            {
                if (part.StartsWith('(') && part.EndsWith("*)", StringComparison.Ordinal))
                    names.Add(part[1..^2]);
            }
        }

        return names;
    }

    internal static List<string> Strings(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(item => item.GetString() ?? "").ToList();

    private static List<string> Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    // The selector in this process on the repository given, this checkout as its source root.
    private static Task<(int Exit, string Stdout, string Text)> SelectAsync(string root, params string[] args) =>
        ImpactedClassesRun.SelectAsync(RepoRoot(), ["--repo", root, .. args]);

    // Check the command's invocation before redirecting it to the same driver's staged DLL.
    // A test-project or checkpoint build may have no output in the driver's native bin directory.
    private static async Task<(int Exit, string Stdout, string Text)> RegenAsync(string fixtureRoot, string tempDirectory)
    {
        var checkout = RepoRoot();
        var project = Antiphon.Nightly.ImpactIndexRegeneration.ProjectPath(checkout);
        var driver = Path.Combine(AppContext.BaseDirectory, "derived-impact-index", "Antiphon.DerivedImpactIndex.dll");
        File.Exists(driver).ShouldBeTrue($"The test build must stage the index driver at {driver}.");
        var options = Antiphon.Nightly.ImpactIndexRegenerationOptions.Parse(["--repo", fixtureRoot, "--no-build"], checkout, checkout, null);
        var output = new StringWriter();
        var error = new StringWriter();
        var tempOverrides = new Dictionary<string, string?> { ["TMPDIR"] = tempDirectory };
        if (OperatingSystem.IsWindows())
        {
            tempOverrides["TMP"] = tempDirectory;
            tempOverrides["TEMP"] = tempDirectory;
        }
        var environment = Antiphon.Tests.Application.LandGraphCommandHost.Environment(tempOverrides);
        var host = Antiphon.Tests.Application.LandGraphCommandHost.Create(output, error, launch =>
        {
            launch.FileName.ShouldBe("dotnet");
            launch.Arguments.ShouldBe(["run", "--project", project, "--no-build", "-p:UseAppHost=false", "--", fixtureRoot]);
            return launch with { Arguments = [driver, fixtureRoot], Environment = environment };
        });
        var exit = await Task.Run(() => Antiphon.Nightly.ImpactIndexRegeneration.Run(options, host, new System.IO.Abstractions.FileSystem()));
        var stdout = output.ToString();
        return (exit, stdout, stdout + error);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "derived-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Add(string relative, string content)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        public void Commit()
        {
            Git(Root, "init", "-q");
            Git(Root, "add", "-A");
            Git(
                Root,
                "-c", "user.name=derived",
                "-c", "user.email=derived@example.invalid",
                "-c", "commit.gpgsign=false",
                "commit", "-q", "-m", "fixture");
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
                return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
