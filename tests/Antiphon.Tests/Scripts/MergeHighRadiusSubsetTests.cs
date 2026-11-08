using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1783. The Merge batch and the rc cut always run one fixed high-radius
/// subset. These witnesses fail while that subset is absent from the Merge
/// process or from scripts/impacted-classes.ps1 -MergeSubset.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MergeHighRadiusSubsetTests
{
    private const string MigrationRange =
        "ff7cceaf4f65bed039f5b7f11dd726d27e762607^..ff7cceaf4f65bed039f5b7f11dd726d27e762607";

    private const string QuietRange =
        "dcb973d896f1d12776a796b71466977b22737b79^..dcb973d896f1d12776a796b71466977b22737b79";

    private const string TestsFilter =
        "/*/*/(AppDbContextModelTests*)|(CheckpointReceiptSafetyTests*)|(HostCleanupCheckpointReceiptTests*)|(TestTimingOperationLabelTests*)/*";

    private const string E2EFilter = "/*/*/(ContractSnapshotTests*)/*";

    private const string MessagingFilter = "/*/*/(InboxConversationMigrationTests*)/*";

    // One TUnit row per selector filters entry: project, exact filter, measured minimum.
    private static readonly (string Id, string Project, string Filter, int Min)[] MergeRows =
    [
        ("CP-merge-subset-tests", "tests/Antiphon.Tests", TestsFilter, 21),
        ("CP-merge-subset-e2e", "tests/Antiphon.E2E", E2EFilter, 5),
        ("CP-merge-subset-messaging", "tests/Antiphon.Messaging.Tests", MessagingFilter, 1),
    ];

    private static readonly string[] FixedClasses =
    [
        "AppDbContextModelTests",
        "CheckpointReceiptSafetyTests",
        "ContractSnapshotTests",
        "HostCleanupCheckpointReceiptTests",
        "TestTimingOperationLabelTests",
    ];

    [Test]
    public async Task merge_process_names_the_fixed_subset_its_checkpoint_row_and_the_cut_gate()
    {
        var root = Root();
        var loop = await File.ReadAllTextAsync(Path.Combine(root, "docs", "orchestration-loop.md"));
        var guide = await File.ReadAllTextAsync(Path.Combine(root, "docs", "testing-and-build.md"));
        AssertDocs(loop, guide);
        // Deleting any of these sentences is the mutant that fails this witness.
        RejectWithout(loop, guide, "independent of what the subjects' reports name");
        RejectWithout(loop, guide, "its own CHECKPOINT row");
        RejectWithout(loop, guide, "before pushing the rc ref");
        RejectWithout(loop, guide, "does not push that ref while the row is red");
        RejectWithout(loop, guide, "CP-merge-subset");
        RejectWithout(loop, guide, TestsFilter);
        RejectWithout(loop, guide, "fixed high-radius subset");
    }

    [Test]
    public async Task merge_subset_without_a_migration_is_exactly_the_fixed_classes()
    {
        var root = Root();
        var alone = await SelectMergeAsync(root);
        AssertFixed(alone.RootElement);
        var quiet = await SelectMergeAsync(root, QuietRange);
        AssertFixed(quiet.RootElement);

        var mutant = alone.RootElement.GetRawText()
            .Replace("HostCleanupCheckpointReceiptTests", "MissingMergeSubsetTests", StringComparison.Ordinal);
        using var broken = JsonDocument.Parse(mutant);
        Should.Throw<ShouldAssertException>(() => AssertFixed(broken.RootElement));
    }

    [Test]
    public async Task merge_subset_appends_the_downgrade_selector_when_migrations_change()
    {
        var root = Root();
        var selected = await SelectMergeAsync(root, MigrationRange);
        AssertDowngrade(selected.RootElement);

        var mutant = selected.RootElement.GetRawText()
            .Replace("StandingSessionOwnershipTests", "MissingDowngradeCallerTests", StringComparison.Ordinal);
        using var broken = JsonDocument.Parse(mutant);
        Should.Throw<ShouldAssertException>(() => AssertDowngrade(broken.RootElement));
    }

    [Test]
    public async Task merge_process_rows_import_as_one_test_row_per_project_with_minimums()
    {
        var root = Root();
        var loop = await File.ReadAllTextAsync(Path.Combine(root, "docs", "orchestration-loop.md"));
        var table = MergeTable(loop);
        AssertRows(table);

        // A command row runs no tests, and a missing minimum hides zero executed tests.
        var command = table.Replace(
            "| `" + TestsFilter.Replace("|", "\\|", StringComparison.Ordinal) + "` |",
            "| scripts/impacted-classes.ps1 -MergeSubset -Range <base>..<tip> -Json |",
            StringComparison.Ordinal);
        command.ShouldNotBe(table);
        Should.Throw<ShouldAssertException>(() => AssertRows(command));
        var noMin = table.Replace("| 0 failed | 5 |", "| 0 failed | n/a |", StringComparison.Ordinal);
        noMin.ShouldNotBe(table);
        Should.Throw<ShouldAssertException>(() => AssertRows(noMin));
        var dropped = string.Join('\n', table.Split('\n')
            .Where(line => !line.Contains("CP-merge-subset-messaging", StringComparison.Ordinal)));
        dropped.ShouldNotBe(table);
        Should.Throw<ShouldAssertException>(() => AssertRows(dropped));
    }

    [Test]
    public async Task merge_subset_text_mode_prints_every_filter()
    {
        var root = Root();
        using var json = await SelectMergeAsync(root, MigrationRange);
        var ran = await RunAsync(root, "-MergeSubset", "-Range", MigrationRange);
        ran.Exit.ShouldBe(0, ran.Text);
        AssertTextFilters(ran.Stdout, json.RootElement);

        var mutant = ran.Stdout.Replace("filter=tests/Antiphon.E2E " + E2EFilter, "", StringComparison.Ordinal);
        mutant.ShouldNotBe(ran.Stdout);
        Should.Throw<ShouldAssertException>(() => AssertTextFilters(mutant, json.RootElement));
    }

    private static string MergeTable(string loop)
    {
        var start = loop.IndexOf("### Merge process (CARD-1783)", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = loop.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? loop[start..] : loop[start..end];
        var lines = section.Replace("\r\n", "\n").Split('\n').Where(line => line.TrimStart().StartsWith('|'));
        return string.Join('\n', lines);
    }

    private static void AssertRows(string table)
    {
        var imported = PlanTableImporter.ImportMarkdown("### Checkpoints\n\n" + table + "\n", "merge-process", isWindows: false);
        imported.ExitCode.ShouldBe(ExitCodes.Green, imported.Error);
        var manifest = imported.Manifest.ShouldNotBeNull();
        manifest.Checkpoints.Count.ShouldBe(MergeRows.Length);
        foreach (var expected in MergeRows)
        {
            var row = manifest.Checkpoints.SingleOrDefault(item => item.Id == expected.Id)
                ?? throw new ShouldAssertException("missing " + expected.Id);
            row.IsCommand.ShouldBeFalse(expected.Id);
            row.Filter.ShouldBe(expected.Filter, expected.Id);
            row.MinExecuted.ShouldBe(expected.Min, expected.Id);
            row.Group.ShouldBe("merge-high-radius", expected.Id);
            manifest.Builds.Single(build => build.Id == row.Build).Project.ShouldBe(expected.Project, expected.Id);
        }
    }

    private static void AssertTextFilters(string stdout, JsonElement root)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        var filters = root.GetProperty("filters").EnumerateArray().ToList();
        filters.Count.ShouldBe(3);
        foreach (var row in filters)
            lines.ShouldContain("filter=" + row.GetProperty("project").GetString() + " " + row.GetProperty("treenodeFilter").GetString());
    }

    private static void AssertDocs(string loop, string guide)
    {
        loop.ShouldContain("### Merge process (CARD-1783)", Case.Sensitive);
        loop.ShouldContain("independent of what the subjects' reports name", Case.Sensitive);
        loop.ShouldContain("its own CHECKPOINT row", Case.Sensitive);
        loop.ShouldContain("Cut the new rc line", Case.Sensitive);
        loop.ShouldContain("before pushing the rc ref", Case.Sensitive);
        loop.ShouldContain("does not push that ref while the row is red", Case.Sensitive);
        loop.ShouldContain("server/Migrations/**", Case.Sensitive);
        loop.ShouldContain("scripts/impacted-classes.ps1 -MergeSubset -Range <base>..<tip> -Json", Case.Sensitive);
        loop.ShouldContain("CP-merge-subset", Case.Sensitive);
        loop.ShouldContain("merge-high-radius", Case.Sensitive);
        loop.ShouldContain(TestsFilter, Case.Sensitive);
        loop.ShouldContain(E2EFilter, Case.Sensitive);
        foreach (var name in FixedClasses)
            loop.ShouldContain(name, Case.Sensitive, name);

        guide.ShouldContain("A Merge batch appends the union", Case.Sensitive);
        guide.ShouldContain("fixed high-radius subset", Case.Sensitive);
        guide.ShouldContain("scripts/impacted-classes.ps1 -MergeSubset", Case.Sensitive);
    }

    private static void AssertFixed(JsonElement root)
    {
        root.GetProperty("mode").GetString().ShouldBe("merge-subset");
        root.GetProperty("migrations").GetBoolean().ShouldBeFalse();
        Names(root, "fixedClasses").ShouldBe(FixedClasses, ignoreOrder: false);
        Names(root, "classes").ShouldBe(FixedClasses, ignoreOrder: false);
        Filter(root, "tests/Antiphon.Tests").ShouldBe(TestsFilter);
        Filter(root, "tests/Antiphon.E2E").ShouldBe(E2EFilter);
    }

    private static void AssertDowngrade(JsonElement root)
    {
        root.GetProperty("mode").GetString().ShouldBe("merge-subset");
        root.GetProperty("migrations").GetBoolean().ShouldBeTrue();
        var classes = Names(root, "classes");
        foreach (var name in FixedClasses)
            classes.ShouldContain(name);
        classes.ShouldContain("C1514HostLoadMigrationTests");
        classes.ShouldContain("StandingSessionOwnershipTests");
        classes.Count.ShouldBeGreaterThan(FixedClasses.Length);
        var tests = Filter(root, "tests/Antiphon.Tests");
        tests.ShouldContain("(HostCleanupCheckpointReceiptTests*)", Case.Sensitive);
        tests.ShouldContain("(StandingSessionOwnershipTests*)", Case.Sensitive);
        Filter(root, "tests/Antiphon.E2E").ShouldContain("(ContractSnapshotTests*)", Case.Sensitive);
    }

    private static void RejectWithout(string loop, string guide, string sentence)
    {
        var mutantLoop = loop.Replace(sentence, "", StringComparison.Ordinal);
        var mutantGuide = guide.Replace(sentence, "", StringComparison.Ordinal);
        (mutantLoop != loop || mutantGuide != guide).ShouldBeTrue(sentence);
        Should.Throw<ShouldAssertException>(() => AssertDocs(mutantLoop, mutantGuide));
    }

    private static string Filter(JsonElement root, string project)
    {
        foreach (var row in root.GetProperty("filters").EnumerateArray())
        {
            if (row.GetProperty("project").GetString() == project)
                return row.GetProperty("treenodeFilter").GetString() ?? "";
        }

        throw new ShouldAssertException("missing filter for " + project);
    }

    private static List<string> Names(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(row => row.GetString() ?? "").ToList();

    private static async Task<JsonDocument> SelectMergeAsync(string root, string? range = null)
    {
        var args = new List<string> { "-MergeSubset", "-Json" };
        if (range is not null)
        {
            args.Add("-Range");
            args.Add(range);
        }

        var ran = await RunAsync(root, args.ToArray());
        ran.Exit.ShouldBe(0, range + " " + ran.Text);
        return JsonDocument.Parse(ran.Stdout);
    }

    private static async Task<(int Exit, string Stdout, string Text)> RunAsync(string root, params string[] args)
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
        start.ArgumentList.Add(Path.Combine(root, "scripts", "impacted-classes.ps1"));
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stdout + stderr);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
