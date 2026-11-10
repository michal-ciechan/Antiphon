using System.Diagnostics;
using System.Text.Json;
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

    private const string LeakSweepPath = "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs";

    private const string LandingGitPath = "server/Infrastructure/Git/LandingGit.cs";

    private const string NotificationServicePath = "server/Application/Services/AgentTaskLandNotificationService.cs";

    private const string MasterSelector = "9e050cf9600aef15b77d67957fc0cd68826d6b35";

    private const string PrecheckPath = "scripts/audit-land-verification-precheck.ps1";

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
        ("13", "6a371bc38888a77325fbdc24e55e61850ed05e68", "NightlyCoreTests"),
        ("14", "a930059d8b82f259549925120fda3abf196fa08e", "NightlyCoreTests"),
        ("15", "6a371bc38888a77325fbdc24e55e61850ed05e68", "WriteBehindFileSystemTests"),
        ("16", "fabda595738775bfc98b4a487dce5543288e7883", "TestLaneCategoryGuardTests"),
        ("17", "c8fc4a413cafc76f5e52c2262ba9419794f196ad", "TestLaneCategoryGuardTests"),
    ];

    [Test]
    public async Task committed_index_matches_the_builder()
    {
        var root = RepoRoot();
        var started = Stopwatch.StartNew();
        var index = DerivedImpactIndexBuilder.Build(root);
        started.Stop();
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), "derived index rebuild");
        index.Cap.ShouldBe(DerivedImpactIndexBuilder.Cap);
        index.Allowlist.ShouldBe(DerivedImpactIndexBuilder.Allowlist);
        index.Utf8.Length.ShouldBeLessThanOrEqualTo(DerivedImpactIndexBuilder.ByteLimit);

        var path = Path.Combine(root, "tests", "derived-impact-index.json");
        File.Exists(path).ShouldBeTrue(path);
        var committed = await File.ReadAllBytesAsync(path);
        committed.ShouldBe(index.Utf8);

        foreach (var broad in DerivedImpactIndexBuilder.Allowlist)
            index.Edges.Any(edge => edge.Path == broad).ShouldBeFalse(broad);

        committed.Any(b => b == (byte)'\r').ShouldBeFalse("committed index contains CR");

        // A capped path keeps its 25 most specific classes. The remainder is owed, not dropped.
        Capped(index, LandingGitPath);
        Capped(index, LeakSweepPath);
        Capped(index, NotificationServicePath);
        Edge(index, LandingGitPath).ShouldContain("RepositoryMutationLeaseTests");
        Edge(index, NotificationServicePath).ShouldContain("PreDispatchFailureDeliveryTests");
        Edge(index, LeakSweepPath).ShouldContain("C1677TempFamilyRegistryTests");
        Edge(index, PrecheckPath).ShouldContain("LandGraphEvaluationTests");
    }

    [Test]
    public async Task declaration_diff_selects_the_new_tests_class()
    {
        using var fixture = new Fixture();
        fixture.Add("tests/Antiphon.Tests/AddedTests.cs", "public sealed class AddedTests { public void T() { } }\n");
        fixture.Add("tests/impacted-classes.json", AnchorTable("AddedTests"));
        var ran = await SelectAsync(fixture.Root, "-Path", "tests/Antiphon.Tests/AddedTests.cs", "-Json");
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
        var ran = await SelectAsync(fixture.Root, "-Path", "Antiphon.sln", "-Json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        Strings(json.RootElement, "classes").ShouldBeEmpty();
        Strings(json.RootElement, "derivedClasses").ShouldBeEmpty();
        Strings(json.RootElement, "skippedByCap").ShouldContain("Antiphon.sln");

        var real = await SelectAsync(RepoRoot(), "-Path", "Antiphon.sln", "-Json");
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
        var ran = await SelectAsync(RepoRoot(), "-Path", MigrationPath, "-Json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        Strings(json.RootElement, "kinds").ShouldBe(["migrations"]);
        var classes = Strings(json.RootElement, "classes");
        foreach (var name in HandMigrationClasses)
            classes.ShouldContain(name);
        classes.ShouldContain("AgentTuiSecretMigratorTests");
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
            var ran = await SelectAsync(root, "-Path", MigrationPath, "-Json", "-Table", path);
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
        var ran = await SelectAsync(RepoRoot(), "-MergeSubset", "-Path", LeakSweepPath, "-Json");
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
                  "path": "scripts/audit-land-verification-precheck.ps1",
                  "classes": [ "LandGraphEvaluationTests" ]
                }
              ]
            }
            """);
        var method = await SelectAsync(fixture.Root, "-Path", PrecheckPath, "-Json");
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
            var whole = await SelectAsync(fixture.Root, "-FileList", list, "-Json");
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
            var ran = await SelectAsync(root, "-Range", range, "-Json");
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
                selectedName.ShouldBeFalse("replay 1");
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
        var lease = await SelectAsync(root, "-Path", LandingGitPath, "-Json");
        lease.Exit.ShouldBe(0, lease.Text);
        using (var json = JsonDocument.Parse(lease.Stdout))
        {
            Strings(json.RootElement, "classes").ShouldNotContain("RepositoryMutationLeaseTests");
            WholeClasses(json.RootElement, "windowsFilters").ShouldContain("RepositoryMutationLeaseTests");
            OwedCount(json.RootElement, LandingGitPath).ShouldBe(39);
        }

        var leaseText = await SelectAsync(root, "-Path", LandingGitPath);
        leaseText.Exit.ShouldBe(0, leaseText.Text);
        leaseText.Stdout.Replace("\r\n", "\n").Split('\n').ShouldContain(
            "owed/capped server/Infrastructure/Git/LandingGit.cs count=39 first=DispatchBaseNotificationTests,"
            + "RunnerSettlementSyncTests,AgentTaskLandVerifierTests,PhoneHomeTaskDispatchProjectionTests,"
            + "RepairSourceDispatchTests,RepositoryMutationLeaseDescribeTests,RunnerAlarmDeliveryTests,"
            + "RunnerStateAlarmExclusionTests");

        var service = await SelectAsync(root, "-Path", NotificationServicePath, "-Json");
        service.Exit.ShouldBe(0, service.Text);
        using (var json = JsonDocument.Parse(service.Stdout))
        {
            Strings(json.RootElement, "classes").ShouldContain("PreDispatchFailureDeliveryTests");
            OwedCount(json.RootElement, NotificationServicePath).ShouldBe(11);
        }

        var sweep = await SelectAsync(root, "-Path", LeakSweepPath, "-Json");
        sweep.Exit.ShouldBe(0, sweep.Text);
        using var sweepJson = JsonDocument.Parse(sweep.Stdout);
        Strings(sweepJson.RootElement, "classes").ShouldContain("C1677TempFamilyRegistryTests");
        OwedCount(sweepJson.RootElement, LeakSweepPath).ShouldBe(6);
    }

    [Test]
    public async Task merge_subset_matches_the_hand_selector()
    {
        var root = RepoRoot();
        var master = Path.Combine(Path.GetTempPath(), "impacted-classes-master-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(master, GitShow(root, MasterSelector + ":scripts/impacted-classes.ps1"));
        try
        {
            foreach (var sha in MergeRanges)
            {
                var args = sha is null
                    ? new[] { "-MergeSubset", "-Json" }
                    : new[] { "-MergeSubset", "-Range", sha + "^.." + sha, "-Json" };
                var current = await SelectAsync(root, args);
                var previous = await SelectScriptAsync(root, master, args);
                current.Exit.ShouldBe(0, sha + " " + current.Text);
                previous.Exit.ShouldBe(0, sha + " " + previous.Text);
                if (current.Stdout != previous.Stdout)
                {
                    var at = 0;
                    var limit = Math.Min(current.Stdout.Length, previous.Stdout.Length);
                    while (at < limit && current.Stdout[at] == previous.Stdout[at])
                        at++;
                    throw new ShouldAssertException(
                        "merge " + (sha ?? "none") + " differs at " + at
                        + " current " + current.Stdout.Length + " master " + previous.Stdout.Length);
                }
            }
        }
        finally
        {
            File.Delete(master);
        }
    }

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
            var ran = await SelectAsync(root, "-Range", sha + "^.." + sha, "-Json");
            ran.Exit.ShouldBe(0, sha + " " + ran.Text);
            using var selected = JsonDocument.Parse(ran.Stdout);
            foreach (var name in WholeClasses(selected.RootElement, "filters"))
                windowsOnly.ShouldNotContain(name, sha + " " + name);
        }
    }

    private static List<string> Edge(DerivedImpactIndex index, string path) =>
        index.Edges.Single(edge => edge.Path == path).Classes.ToList();

    private static void Capped(DerivedImpactIndex index, string path)
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

    private static async Task<JsonElement> SelectBuiltAsync(Fixture fixture, string path)
    {
        var index = DerivedImpactIndexBuilder.Build(fixture.Root);
        DerivedImpactIndexBuilder.Write(fixture.Root, index);
        var ran = await SelectAsync(fixture.Root, "-Path", path, "-Json");
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

    private static string GitShow(string root, string spec)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("show");
        start.ArgumentList.Add(spec);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout;
    }

    private static List<string> Strings(JsonElement root, string property) =>
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

    private static Task<(int Exit, string Stdout, string Text)> SelectAsync(string root, params string[] args) =>
        SelectScriptAsync(root, Path.Combine(RepoRoot(), "scripts", "impacted-classes.ps1"), args);

    private static async Task<(int Exit, string Stdout, string Text)> SelectScriptAsync(
        string root, string script, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-Repo");
        start.ArgumentList.Add(root);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("impacted-classes exceeded 120s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stdout + stderr);
    }

    private static string RepoRoot()
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
