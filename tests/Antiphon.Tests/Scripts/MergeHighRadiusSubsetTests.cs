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

    // CARD-1801: a batch diff that touches the remote deploy scripts selects the remote-scripts method row.
    private const string RemoteScriptsRange =
        "a54a6d4f755fa139b41fd7dc8aef456d3d2c0975^..a54a6d4f755fa139b41fd7dc8aef456d3d2c0975";

    // The CARD-1783 classes and ReceiptScanDestinationIntegrationTests. CARD-1801 D-1: the eight
    // land classes leave this row, four at a time in ordinal order, so no row warns at import.
    private const string TestsFilter =
        "/*/*/(AppDbContextModelTests*)|(CheckpointReceiptSafetyTests*)|(HostCleanupCheckpointReceiptTests*)"
        + "|(ReceiptScanDestinationIntegrationTests*)|(TestTimingOperationLabelTests*)/*";

    private const string LandFilter1 =
        "/*/*/(AgentTaskLandHeldNotificationTests*)|(AgentTaskLandNotificationParkFollowUpTests*)"
        + "|(AgentTaskLandNotificationPersistenceTests*)|(AgentTaskLandNotificationReconcileLoadTests*)/*";

    private const string LandFilter2 =
        "/*/*/(AgentTaskLandNotificationRecoveryTests*)|(AgentTaskLandNotificationRescanTests*)"
        + "|(AgentTaskLandReceiptWatermarkSafetyTests*)|(AgentTaskLandReceiptWatermarkTests*)/*";

    // CARD-1801: the A5 pair, the two Linux-only census cases of ReleaseGatePlatformTests.
    private const string CensusPairFilter =
        "/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*)";

    private const string E2EFilter = "/*/*/(ContractSnapshotTests*)/*";

    private const string MessagingFilter = "/*/*/(InboxConversationMigrationTests*)/*";

    // CARD-1795: the E2E fixture keeps the check-interpreter directory in its own workspace, so the
    // CARD-1783 Environment workaround on the E2E row must not come back.
    private const string CheckInterpreterWorkaround =
        "Delegation__CheckInterpreterWorkingDirectory=/tmp/antiphon-merge-e2e-check-interpreter";

    // One TUnit row per selector filters entry: project, exact filter, measured minimum.
    private static readonly (string Id, string Project, string Filter, int Min)[] MergeRows =
    [
        ("CP-merge-subset-tests", "tests/Antiphon.Tests", TestsFilter, MeasuredTestsMin),
        ("CP-merge-subset-tests-land-1", "tests/Antiphon.Tests", LandFilter1, MeasuredLand1Min),
        ("CP-merge-subset-tests-land-2", "tests/Antiphon.Tests", LandFilter2, MeasuredLand2Min),
        ("CP-merge-subset-ReleaseGatePlatformTests", "tests/Antiphon.Tests", CensusPairFilter, 2),
        ("CP-merge-subset-e2e", "tests/Antiphon.E2E", E2EFilter, 5),
        ("CP-merge-subset-messaging", "tests/Antiphon.Messaging.Tests", MessagingFilter, 1),
    ];

    // The fixed Antiphon.Tests class rows executed on server2 Linux (CARD-1801 D-1 counting runs,
    // one leased run each): 44 in 2m02s, 34 in 1m18s and 104 in 4m06s, the 182 of the unsplit row.
    private const int MeasuredTestsMin = 44;
    private const int MeasuredLand1Min = 34;
    private const int MeasuredLand2Min = 104;

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

    private static readonly string[] FixedMethods =
    [
        "ReleaseGatePlatformTests/C1686_LinuxOnlyCensus",
        "ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy",
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
        RejectWithout(loop, guide, LandFilter1);
        RejectWithout(loop, guide, LandFilter2);
        RejectWithout(loop, guide, "more than four land classes");
        RejectWithout(loop, guide, "fixed high-radius subset");
        // CARD-1801: the table is executed before a land, and a Windows row is owed, never passed on Linux.
        RejectWithout(loop, guide, "runs every impacted-classes row the batch diff selects");
        RejectWithout(loop, guide, "windowsFilters");
        RejectWithout(loop, guide, "owed on the native Windows lane");
        RejectWithout(loop, guide, CensusPairFilter);
    }

    [Test]
    public async Task merge_subset_runs_every_impacted_row_the_batch_diff_selects()
    {
        var root = Root();
        using var remote = await SelectMergeAsync(root, RemoteScriptsRange);
        AssertImpactedMethods(remote.RootElement);
        var dropped = remote.RootElement.GetRawText()
            .Replace("CP-merge-subset-RemoteScriptContractTests", "CP-merge-subset-dropped", StringComparison.Ordinal);
        using (var broken = JsonDocument.Parse(dropped))
            Should.Throw<ShouldAssertException>(() => AssertImpactedMethods(broken.RootElement));

        var ran = await RunAsync(root, "-MergeSubset", "-Path", "server/Infrastructure/Git/ServerGitIdentity.cs", "-Json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var lane = JsonDocument.Parse(ran.Stdout);
        AssertWindowsLane(lane.RootElement);
        var linuxOnly = lane.RootElement.GetRawText()
            .Replace("CP-merge-windows-tests", "CP-merge-linux-tests", StringComparison.Ordinal);
        using (var broken = JsonDocument.Parse(linuxOnly))
            Should.Throw<ShouldAssertException>(() => AssertWindowsLane(broken.RootElement));
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

        // A fifth land class in one row is the import warning of CARD-1801 D-1.
        var joined = selected.RootElement.GetRawText().Replace(
            "(LandRequestWriteDiagnosticTests*)", "(LandRequestWriteDiagnosticTests*)|(AgentTaskLandJoinedTests*)", StringComparison.Ordinal);
        using var crowded = JsonDocument.Parse(joined);
        Should.Throw<ShouldAssertException>(() => AssertLandRowsSplit(crowded.RootElement));
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
        var e2eRow = table.Split('\n').Single(line => line.Contains("| CP-merge-subset-e2e |", StringComparison.Ordinal));
        e2eRow.ShouldEndWith("| true | n/a |");
        var withEnvironment = table.Replace(
            e2eRow,
            e2eRow[..^"| n/a |".Length] + "| " + CheckInterpreterWorkaround + " |",
            StringComparison.Ordinal);
        withEnvironment.ShouldNotBe(table);
        Should.Throw<ShouldAssertException>(() => AssertRows(withEnvironment));
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
        // CARD-1801 D-1: a land row of more than four land classes warns; the documented rows import clean.
        imported.Warnings.ShouldBeEmpty(string.Join("; ", imported.Warnings));
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
            var environment = row.Environment.Select(pair => pair.Key + "=" + pair.Value).ToArray();
            environment.ShouldBeEmpty(expected.Id);
            manifest.Builds.Single(build => build.Id == row.Build).Project.ShouldBe(expected.Project, expected.Id);
        }
    }

    private static void AssertTextFilters(string stdout, JsonElement root)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        var filters = root.GetProperty("filters").EnumerateArray().ToList();
        filters.Count.ShouldBe(7);
        foreach (var row in filters)
            lines.ShouldContain("filter=" + row.GetProperty("project").GetString() + " " + row.GetProperty("treenodeFilter").GetString());
        foreach (var row in root.GetProperty("windowsFilters").EnumerateArray())
            lines.ShouldContain("windowsFilter=" + row.GetProperty("project").GetString() + " " + row.GetProperty("treenodeFilter").GetString());
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
        loop.ShouldContain(LandFilter1, Case.Sensitive);
        loop.ShouldContain(LandFilter2, Case.Sensitive);
        loop.ShouldContain("more than four land classes", Case.Sensitive);
        loop.ShouldContain(E2EFilter, Case.Sensitive);
        foreach (var name in FixedClasses)
            loop.ShouldContain(name, Case.Sensitive, name);
        loop.ShouldContain("runs every impacted-classes row the batch diff selects", Case.Sensitive);
        loop.ShouldContain("windowsFilters", Case.Sensitive);
        loop.ShouldContain("owed on the native Windows lane", Case.Sensitive);
        loop.ShouldContain(CensusPairFilter, Case.Sensitive);

        guide.ShouldContain("A Merge batch appends the union", Case.Sensitive);
        guide.ShouldContain("fixed high-radius subset", Case.Sensitive);
        guide.ShouldContain("scripts/impacted-classes.ps1 -MergeSubset", Case.Sensitive);
    }

    private static void AssertFixed(JsonElement root)
    {
        root.GetProperty("mode").GetString().ShouldBe("merge-subset");
        root.GetProperty("migrations").GetBoolean().ShouldBeFalse();
        Names(root, "fixedClasses").ShouldBe(FixedClasses, ignoreOrder: false);
        Names(root, "fixedMethods").ShouldBe(FixedMethods, ignoreOrder: false);
        Names(root, "classes").ShouldBe(
            FixedClasses.Append("ReleaseGatePlatformTests").Order(StringComparer.Ordinal).ToArray(), ignoreOrder: false);
        Ids(root, "filters").ShouldBe(
            [
                "CP-merge-subset-e2e", "CP-merge-subset-tests", "CP-merge-subset-tests-land-1",
                "CP-merge-subset-tests-land-2", "CP-merge-subset-ReleaseGatePlatformTests",
            ],
            ignoreOrder: false);
        Filter(root, "CP-merge-subset-tests").ShouldBe(TestsFilter);
        Filter(root, "CP-merge-subset-tests-land-1").ShouldBe(LandFilter1);
        Filter(root, "CP-merge-subset-tests-land-2").ShouldBe(LandFilter2);
        Filter(root, "CP-merge-subset-ReleaseGatePlatformTests").ShouldBe(CensusPairFilter);
        Filter(root, "CP-merge-subset-e2e").ShouldBe(E2EFilter);
        Project(root, "CP-merge-subset-ReleaseGatePlatformTests").ShouldBe("tests/Antiphon.Tests");
        root.GetProperty("windowsFilters").GetArrayLength().ShouldBe(0);
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
        var tests = Filter(root, "CP-merge-subset-tests");
        tests.ShouldContain("(HostCleanupCheckpointReceiptTests*)", Case.Sensitive);
        tests.ShouldContain("(StandingSessionOwnershipTests*)", Case.Sensitive);
        Filter(root, "CP-merge-subset-e2e").ShouldContain("(ContractSnapshotTests*)", Case.Sensitive);
        Filter(root, "CP-merge-subset-ReleaseGatePlatformTests").ShouldBe(CensusPairFilter);
        // A batch land class starts a new row; the fixed land rows keep their filters and floors.
        Filter(root, "CP-merge-subset-tests-land-1").ShouldBe(LandFilter1);
        Filter(root, "CP-merge-subset-tests-land-2").ShouldBe(LandFilter2);
        Filter(root, "CP-merge-subset-tests-land-3").ShouldContain("(LandRequestWriteDiagnosticTests*)", Case.Sensitive);
        AssertLandRowsSplit(root);
    }

    // The importer counts a filter class name containing "Land"; more than four in one row warns.
    private static void AssertLandRowsSplit(JsonElement root)
    {
        foreach (var row in root.GetProperty("filters").EnumerateArray())
        {
            var filter = row.GetProperty("treenodeFilter").GetString() ?? "";
            var names = filter.Split('(', ')', '|', '/').Where(name => name.Contains("Land", StringComparison.Ordinal));
            names.Count().ShouldBeLessThanOrEqualTo(4, filter);
        }
    }

    private static void AssertImpactedMethods(JsonElement root)
    {
        Names(root, "kinds").ShouldContain("remote-scripts");
        Filter(root, "CP-merge-subset-tests").ShouldBe(TestsFilter);
        var methods = Filter(root, "CP-merge-subset-RemoteScriptContractTests");
        methods.ShouldStartWith("/*/*/RemoteScriptContractTests/(", Case.Sensitive);
        methods.ShouldContain("(C849_Deploy_ordering_contract_is_pinned*)", Case.Sensitive);
        Project(root, "CP-merge-subset-RemoteScriptContractTests").ShouldBe("tests/Antiphon.Tests");
    }

    private static void AssertWindowsLane(JsonElement root)
    {
        Names(root, "kinds").ShouldContain("server-image-push-credentials");
        Filter(root, "CP-merge-subset-tests").ShouldContain("(ServerImagePushCredentialTests*)", Case.Sensitive);
        root.GetProperty("windowsFilters").GetArrayLength().ShouldBe(1);
        Filter(root, "CP-merge-windows-tests", "windowsFilters").ShouldBe("/*/*/(ServerImagePushCredentialTests*)/*");
        Project(root, "CP-merge-windows-tests", "windowsFilters").ShouldBe("tests/Antiphon.Tests");
    }

    private static void RejectWithout(string loop, string guide, string sentence)
    {
        var mutantLoop = loop.Replace(sentence, "", StringComparison.Ordinal);
        var mutantGuide = guide.Replace(sentence, "", StringComparison.Ordinal);
        (mutantLoop != loop || mutantGuide != guide).ShouldBeTrue(sentence);
        Should.Throw<ShouldAssertException>(() => AssertDocs(mutantLoop, mutantGuide));
    }

    private static string Filter(JsonElement root, string id, string property = "filters") =>
        Row(root, id, property).GetProperty("treenodeFilter").GetString() ?? "";

    private static string Project(JsonElement root, string id, string property = "filters") =>
        Row(root, id, property).GetProperty("project").GetString() ?? "";

    private static JsonElement Row(JsonElement root, string id, string property)
    {
        foreach (var row in root.GetProperty(property).EnumerateArray())
        {
            if (row.TryGetProperty("id", out var rowId) && rowId.GetString() == id)
                return row;
        }

        throw new ShouldAssertException("missing " + property + " row " + id);
    }

    private static List<string> Ids(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray()
            .Select(row => row.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "")
            .ToList();

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
