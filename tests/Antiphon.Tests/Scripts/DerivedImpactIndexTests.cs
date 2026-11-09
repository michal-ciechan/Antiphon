using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private static readonly Regex ClassDeclaration = new(
        @"^[ \t]*(?:(?:public|internal|file|private|protected)[ \t]+)?(?:(?:sealed|static|abstract|partial)[ \t]+)*class[ \t]+([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

        Edge(index, LeakSweepPath).ShouldContain("C1677TempFamilyRegistryTests");
        Edge(index, LandingGitPath).ShouldContain("RepositoryMutationLeaseTests");
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
        index.Edges.Any(edge => edge.Path == "shared/fixture-path.txt").ShouldBeFalse();
        var capped = index.CappedPaths.Single(row => row.Path == "shared/fixture-path.txt");
        capped.Classes.ShouldBe(26);
        capped.Reason.ShouldBe("cap");
    }

    [Test]
    public async Task solution_file_selects_no_derived_class()
    {
        using var fixture = new Fixture();
        fixture.Add("Antiphon.sln", "fixture\n");
        fixture.Add("tests/Antiphon.Tests/SolutionShapeTests.cs", """
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
        AssertEmptyIds(json.RootElement);
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

        var whole = await SelectAsync(
            fixture.Root,
            "-Path", PrecheckPath,
            "-Path", "tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs",
            "-Json");
        whole.Exit.ShouldBe(0, whole.Text);
        using var wholeJson = JsonDocument.Parse(whole.Stdout);
        Strings(wholeJson.RootElement, "filters").ShouldNotBeEmpty();
        wholeJson.RootElement.GetProperty("treenodeFilter").GetString()
            .ShouldBe("/*/*/(LandGraphEvaluationTests*)/*");
    }

    [Test]
    public async Task s1_replay_matches_declaration_and_index_edges()
    {
        var root = RepoRoot();
        var indexPath = Path.Combine(root, "tests", "derived-impact-index.json");
        File.Exists(indexPath).ShouldBeTrue(indexPath);
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in index.RootElement.GetProperty("edges").EnumerateArray())
        {
            edges[edge.GetProperty("path").GetString() ?? ""] =
                edge.GetProperty("classes").EnumerateArray().Select(item => item.GetString() ?? "")
                    .ToHashSet(StringComparer.Ordinal);
        }

        var skippedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in index.RootElement.GetProperty("cappedPaths").EnumerateArray())
            skippedPaths.Add(row.GetProperty("path").GetString() ?? "");
        foreach (var row in index.RootElement.GetProperty("allowlist").EnumerateArray())
            skippedPaths.Add(row.GetString() ?? "");

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
            var skipped = Strings(selected.RootElement, "skippedByCap").ToHashSet(StringComparer.Ordinal);
            var declared = false;
            var edged = false;
            foreach (var file in names)
            {
                if (skippedPaths.Contains(file))
                    skipped.ShouldContain(file, row.Id + " " + file);
                if (row.ClassName is null)
                    continue;
                if (file.StartsWith("tests/", StringComparison.Ordinal) && file.EndsWith(".cs", StringComparison.Ordinal))
                {
                    var full = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full) && Declares(await File.ReadAllTextAsync(full), row.ClassName))
                        declared = true;
                }

                if (!skippedPaths.Contains(file) && edges.TryGetValue(file, out var set) && set.Contains(row.ClassName))
                    edged = true;
            }

            Console.WriteLine(
                "REPLAY " + row.Id + " class=" + (row.ClassName ?? "") + " selected="
                + (row.ClassName is not null && classes.Contains(row.ClassName)) + " declared=" + declared + " edged=" + edged);
            if (row.ClassName is not null && (declared || edged))
                classes.ShouldContain(row.ClassName, "replay " + row.Id);
            if (row.Id == "10")
                skipped.ShouldContain("Antiphon.sln");
        }

        Console.WriteLine("REPLAY slowest=" + slowest.TotalSeconds.ToString("0.0") + "s");
    }

    private static List<string> Edge(DerivedImpactIndex index, string path) =>
        index.Edges.Single(edge => edge.Path == path).Classes.ToList();

    private static bool Declares(string source, string className)
    {
        foreach (Match match in ClassDeclaration.Matches(source))
        {
            if (match.Groups[1].Value == className)
                return true;
        }

        return false;
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

    private static async Task<(int Exit, string Stdout, string Text)> SelectAsync(string root, params string[] args)
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
        start.ArgumentList.Add(Path.Combine(RepoRoot(), "scripts", "impacted-classes.ps1"));
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
