using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1782. The path-glob table and scripts/impacted-classes.ps1 select the test
/// classes a high-blast-radius diff puts at risk. These witnesses are the ordinary
/// proof: they fail while the selector is absent or stale, and they name the classes
/// the 2026-11-08 rc misses actually broke.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class ImpactedClassesSelectorTests
{
    private const string BatchSpan =
        "ff7cceaf4f65bed039f5b7f11dd726d27e762607^..9e7ff34988cd8c273d94db3c002975d93ea26f9e";

    private const string RepairFilesCommit = "517b705036e1496388a562835c03d3e15b522370";

    private static readonly string[] SeedKinds =
    [
        "migrations", "contracts", "timing-labels", "remote-scripts", "statement-budgets", "checkpoint-tool",
    ];

    [GeneratedRegex(@"MigrateAsync\s*\(\s*[^)\s]", RegexOptions.CultureInvariant)]
    private static partial Regex MigrateTarget();

    [GeneratedRegex(@"\bIMigrator\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigratorType();

    [GeneratedRegex(@"\bMigrationTestDatabase\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationDatabase();

    [GeneratedRegex(@"\.Down\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DownCall();

    [Test]
    public async Task seed_kinds_globs_and_owner_sentences_stay_present()
    {
        var root = Root();
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        foreach (var kind in SeedKinds)
            table.ShouldContain($"\"id\": \"{kind}\"", Case.Sensitive, kind);
        table.ShouldContain("\"cache\": \"none\"", Case.Sensitive, "no selector cache");

        var audit = await RunAsync(root, "-Audit", "-Json");
        audit.Exit.ShouldBe(0, audit.Text);
        using var json = JsonDocument.Parse(audit.Stdout);
        json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue(audit.Text);
        json.RootElement.GetProperty("emptyGlobs").GetArrayLength().ShouldBe(0, audit.Text);
        json.RootElement.GetProperty("missingClasses").GetArrayLength().ShouldBe(0, audit.Text);

        foreach (var relative in new[] { "server/Bundles/stage-code.md", "server/Bundles/stage-review.md" })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(root, relative));
            text.ShouldContain(
                "impacted-classes rows whose globs match git diff --name-only base..HEAD",
                Case.Sensitive,
                relative);
        }

        var guide = await File.ReadAllTextAsync(Path.Combine(root, "docs", "testing-and-build.md"));
        guide.ShouldContain("scripts/impacted-classes.ps1", Case.Sensitive, "owner usage");
        guide.ShouldContain("A Merge batch appends the union", Case.Sensitive, "merge usage");
        guide.ShouldContain("maximum staleness is zero", Case.Sensitive, "staleness");
    }

    [Test]
    public async Task downgrade_call_sites_are_exactly_the_derived_migration_files()
    {
        var root = Root();
        var audit = await RunAsync(root, "-Audit", "-Json");
        audit.Exit.ShouldBe(0, audit.Text);
        using var json = JsonDocument.Parse(audit.Stdout);
        var derived = json.RootElement.GetProperty("derivedFiles").EnumerateArray()
            .Select(row => row.GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        var scanned = ScanDowngradeFiles(root);
        var missing = scanned.Where(file => !derived.Contains(file)).Order(StringComparer.Ordinal).ToArray();
        var extra = derived.Where(file => !scanned.Contains(file)).Order(StringComparer.Ordinal).ToArray();
        missing.ShouldBeEmpty("MigrateAsync(<target>), IMigrator, MigrationTestDatabase or .Down( files outside the derived list: "
            + string.Join(", ", missing));
        extra.ShouldBeEmpty("derived list names files the downgrade scan does not: " + string.Join(", ", extra));
        scanned.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task audit_fails_when_a_named_class_is_missing()
    {
        var root = Root();
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        table.ShouldContain("AppDbContextModelTests", Case.Sensitive);
        var broken = table.Replace("AppDbContextModelTests", "NoSuchImpactedClassTests", StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-missing-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, broken);
        try
        {
            var audit = await RunAsync(root, "-Audit", "-Json", "-Table", path);
            audit.Exit.ShouldBe(1, audit.Text);
            audit.Stdout.ShouldContain("NoSuchImpactedClassTests", Case.Sensitive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task audit_fails_when_a_glob_matches_nothing()
    {
        var root = Root();
        var broken = """
            {
              "schema": 1,
              "cache": "none",
              "kinds": [
                {
                  "id": "empty-glob",
                  "globs": ["scripts/lib/*.sh"],
                  "classes": ["ContractSnapshotTests"]
                }
              ]
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-empty-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, broken);
        try
        {
            var audit = await RunAsync(root, "-Audit", "-Json", "-Table", path);
            audit.Exit.ShouldBe(1, audit.Text);
            audit.Stdout.ShouldContain("scripts/lib/*.sh", Case.Sensitive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task telemetry_s1_span_selects_the_downgrade_files_and_the_contract_and_label_rows()
    {
        var root = Root();
        var selected = await SelectAsync(root, BatchSpan);
        Names(selected, "kinds").ShouldBe(["contracts", "migrations", "timing-labels"], ignoreOrder: false);
        var files = Names(selected, "files").ToHashSet(StringComparer.Ordinal);
        var repair = Git(root, "diff-tree", "--no-commit-id", "--name-only", "-r", RepairFilesCommit);
        repair.Count.ShouldBe(37, "the host-load downgrade repair edited 37 files");
        var absent = repair.Where(file => !files.Contains(file)).ToArray();
        absent.ShouldBeEmpty("batch span did not list repaired downgrade files: " + string.Join(", ", absent));
        var classes = string.Join("\n", Names(selected, "classes"));
        foreach (var name in new[]
                 {
                     "AppDbContextModelTests", "C1514HostLoadMigrationTests", "StandingSessionOwnershipTests",
                     "CardFilePrivacyMigrationTests", "LandRequestWriteDiagnosticTests", "TranscriptHotPathMigrationTests",
                     "ContractSnapshotTests", "TestTimingOperationLabelTests",
                 })
            classes.ShouldContain(name, Case.Sensitive, name);
    }

    [Test]
    public async Task contract_snapshot_ranges_select_contract_snapshot_tests()
    {
        var root = Root();
        foreach (var sha in new[]
                 {
                     "f194ff221a7645bbcaecf9d44b9e65d5595dbbbf",
                     "ebd31b2498c814813ebe9180e73e61216b52f9bd",
                 })
        {
            var selected = await SelectAsync(root, sha + "^.." + sha);
            Names(selected, "kinds").ShouldContain("contracts", sha);
            var rendered = selected.RootElement.GetRawText();
            rendered.ShouldContain("ContractSnapshotTests", Case.Sensitive, sha);
            rendered.ShouldContain("tests/Antiphon.E2E", Case.Sensitive, sha);
        }
    }

    [Test]
    public async Task label_remote_budget_and_checkpoint_ranges_select_their_rows()
    {
        var root = Root();
        await AssertKinds(root, "dcb973d896f1d12776a796b71466977b22737b79", ["timing-labels"], "TestTimingOperationLabelTests");
        await AssertKinds(root, "c6eee1a0713a0c43238a23993acad8e20c0242f2", ["timing-labels"], "TestTimingOperationLabelTests");
        await AssertKinds(
            root,
            "a54a6d4f755fa139b41fd7dc8aef456d3d2c0975",
            ["remote-scripts"],
            "C849_Deploy_temp_names_saved_import_when_seed_is_missing",
            "C0995_Deploy_temp_checks_host_absence_before_checkout",
            "C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle",
            "C849_Deploy_ordering_contract_is_pinned",
            "C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo");
        await AssertKinds(
            root,
            "43d819da7189349c548b07ebd2677e6be086d95b",
            ["statement-budgets"],
            "C1149_C1150_Statement_budgets");
        await AssertKinds(
            root,
            "ceda26b31ef344bc07efd590fa9bdb59bae0f23f",
            ["checkpoint-tool"],
            "CheckpointReceiptSafetyTests",
            "CheckpointTerminalReceiptTests",
            "HostCleanupCheckpointReceiptTests");
    }

    private static async Task AssertKinds(string root, string sha, string[] kinds, params string[] needles)
    {
        var selected = await SelectAsync(root, sha + "^.." + sha);
        Names(selected, "kinds").ShouldBe(kinds, ignoreOrder: false, customMessage: sha);
        var rendered = selected.RootElement.GetRawText();
        foreach (var needle in needles)
            rendered.ShouldContain(needle, Case.Sensitive, sha + " " + needle);
    }

    private static HashSet<string> ScanDowngradeFiles(string root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var tests = Path.Combine(root, "tests");
        foreach (var file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            if (MigrateTarget().IsMatch(text) || MigratorType().IsMatch(text)
                || MigrationDatabase().IsMatch(text) || DownCall().IsMatch(text))
                found.Add(relative);
        }

        return found;
    }

    private static async Task<JsonDocument> SelectAsync(string root, string range)
    {
        var ran = await RunAsync(root, "-Range", range, "-Json");
        ran.Exit.ShouldBe(0, range + " " + ran.Text);
        return JsonDocument.Parse(ran.Stdout);
    }

    private static List<string> Names(JsonDocument json, string property) =>
        json.RootElement.GetProperty(property).EnumerateArray()
            .Select(row => row.GetString() ?? "")
            .ToList();

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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
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
