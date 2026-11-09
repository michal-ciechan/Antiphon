using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1784 S1. The post-land script plans a Linux early-warning lane.
/// <see cref="PostLandEarlyWarningScript"/> names the production script function
/// <c>ResolvePostLandPlatform</c> for the optional platform seam.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandEarlyWarningTests
{
    private const string ForbiddenLetterOr =
        "(A*)|(B*)|(C*)|(D*)|(E*)|(F*)|(G*)|(H*)|(I*)|(J*)|(K*)|(L*)";

    private const string TestingSentence =
        "The post-land warning runs the shard manifest one shard at a time through the build-slot gate, skips a docs-only batch, and is not a per-task Final and not a replacement for the Windows Full run.";

    private const string OrchestratorSentence =
        "A post-land summary with new reds gets one Debug per new class under the rc run-group repair loop, before the rc cut, and the Windows qualification is left running.";

    private const string CheckpointsNamespace = "Antiphon.Tests.Checkpoints";

    private static readonly Regex ClassLine = new(
        @"^public\s+(?:(?:sealed|static|partial|abstract)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FileNamespace = new(
        @"^namespace\s+([A-Za-z0-9_.]+)\s*;",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BlockNamespace = new(
        @"^namespace\s+([A-Za-z0-9_.]+)\s*\{",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Test]
    public async Task Docs_only_range_skips()
    {
        using var fixture = Fixture.Create();
        fixture.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        fixture.WriteTable("src/Fixture.cs", "FixtureUnitTests");

        var docs = await fixture.PlanAsync(fixture.Paths("docs/owner.md", "README.md"));
        docs.Exit.ShouldBe(0, docs.Text);
        docs.State.ShouldBe("skipped-docs");
        docs.Shards.Count.ShouldBe(0);
        docs.Root.GetProperty("slot").GetBoolean().ShouldBeFalse();

        var empty = await fixture.PlanAsync(fixture.Paths());
        empty.Exit.ShouldBe(0, empty.Text);
        empty.State.ShouldBe("skipped-docs");

        var code = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"));
        code.Exit.ShouldBe(0, code.Text);
        code.State.ShouldBe("run", code.Text);
        code.State.ShouldNotBe("skipped-docs");
    }

    [Test]
    public async Task Code_range_plans_namespace_shards_and_one_lane()
    {
        using var fixture = Fixture.Create();
        fixture.WriteUnitClass("Antiphon.Tests.Alpha", "AlphaTests");
        fixture.WriteUnitClass("Antiphon.Tests.Beta", "BetaTests");
        fixture.WriteTable("src/Fixture.cs", "AlphaTests");

        var open = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"));
        open.Exit.ShouldBe(0, open.Text);
        open.State.ShouldBe("run", open.Text);
        open.Root.GetProperty("lane").GetInt32().ShouldBe(1);
        var filters = open.Shards.Select(shard => shard.GetProperty("filter").GetString()).ToArray();
        filters.ShouldBe(new[]
        {
            "/*/Antiphon.Tests.Alpha/*/*[Category=Unit]",
            "/*/Antiphon.Tests.Beta/*/*[Category=Unit]",
        });
        open.Shards.ShouldAllBe(shard => shard.GetProperty("kind").GetString() == "unit");

        fixture.WriteLock(Environment.ProcessId);
        var busy = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"));
        busy.Exit.ShouldBe(0, busy.Text);
        busy.State.ShouldBe("skipped-busy", busy.Text);
        busy.Shards.Count.ShouldBe(0, busy.Text);
        busy.Root.GetProperty("lane").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task Start_inside_sixty_minutes_is_hourly()
    {
        using var fixture = Fixture.Create();
        fixture.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        fixture.WriteTable("src/Fixture.cs", "FixtureUnitTests");
        fixture.WriteLastStart(DateTime.UtcNow.AddMinutes(-30));

        var inside = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"));
        inside.Exit.ShouldBe(0, inside.Text);
        inside.State.ShouldBe("skipped-hourly", inside.Text);
        inside.Shards.Count.ShouldBe(0);

        fixture.WriteLastStart(DateTime.UtcNow.AddMinutes(-61));
        var outside = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"));
        outside.Exit.ShouldBe(0, outside.Text);
        outside.State.ShouldBe("run", outside.Text);
    }

    [Test]
    public async Task New_reds_are_failures_absent_from_the_ancestor_receipt()
    {
        using var repo = GitRepo.Create();
        repo.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        repo.WriteTable("src/Code.cs", "FixtureUnitTests");
        var older = repo.Commit("older", "2026-11-01T00:00:00Z");
        repo.WriteFile("notes/nearer.txt", "nearer\n");
        var nearer = repo.Commit("nearer", "2026-11-02T00:00:00Z");
        repo.WriteFile("src/Code.cs", "class Code { }\n");
        var tip = repo.Commit("tip", "2026-11-03T00:00:00Z");
        var shard = "unit-ns-Antiphon.Tests.Fixture";
        repo.WriteTrx(tip, shard, Trx(
            ("Synthetic.Ancient", "Fail", "Failed"),
            ("Synthetic.Mid", "Fail", "Failed"),
            ("Synthetic.Fresh", "Fail", "Failed"),
            ("Synthetic.Mid", "Ok", "Passed")));
        repo.WriteReceipt(older, "completed", "Ancient.Fail", "Mid.Fail");
        repo.WriteReceipt(nearer, "completed", "Mid.Fail");
        repo.WriteReceipt(new string('a', 40), "completed", "Fresh.Fail");

        var compared = await repo.PlanRangeAsync(older + ".." + tip);
        compared.Exit.ShouldBe(1, compared.Text);
        Names(compared.Root, "newReds").ShouldBe(new[] { "Ancient.Fail", "Fresh.Fail" });
        compared.Root.GetProperty("previousTipSha").GetString().ShouldBe(nearer);

        using var first = GitRepo.Create();
        first.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        first.WriteTable("src/Code.cs", "FixtureUnitTests");
        first.WriteFile("src/Code.cs", "class Code { }\n");
        var only = first.Commit("only", "2026-11-03T00:00:00Z");
        var parent = new string('b', 40);
        first.WriteTrx(only, shard, Trx(
            ("Synthetic.Ancient", "Fail", "Failed"),
            ("Synthetic.Fresh", "Fail", "Failed")));
        var opened = await first.PlanAsync(first.Paths("src/Code.cs"), parent + ".." + only);
        opened.Exit.ShouldBe(1, opened.Text);
        Names(opened.Root, "newReds").ShouldBe(new[] { "Ancient.Fail", "Fresh.Fail" });
        opened.Root.GetProperty("previousTipSha").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Summary_line_names_tip_batch_and_new_reds()
    {
        const string baseline = "1111111111111111111111111111111111111111";
        const string tip = "2222222222222222222222222222222222222222";
        using var fixture = Fixture.Create();
        fixture.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        fixture.WriteTable("src/Fixture.cs", "FixtureUnitTests");
        fixture.WriteTrx(tip, "unit-ns-Antiphon.Tests.Fixture", Trx(
            ("Antiphon.Tests.Fixture.FixtureUnitTests", "Breaks", "Failed")));

        var plan = await fixture.PlanAsync(fixture.Paths("src/Widget.cs"), baseline + ".." + tip);
        plan.Exit.ShouldBe(1, plan.Text);
        var line = plan.Root.GetProperty("summaryLine").GetString();
        line.ShouldBe(
            "post-land tip=2222222222222222222222222222222222222222 batch=111111111111..222222222222 new-reds=1 FixtureUnitTests.Breaks owed-windows=0");
    }

    [Test]
    public async Task Non_linux_run_refuses_before_a_slot()
    {
        using var blocked = GitRepo.Create();
        blocked.WriteUnitClass("Antiphon.Tests.Fixture", "FixtureUnitTests");
        blocked.WriteTable("src/Code.cs", "FixtureUnitTests");
        blocked.WriteFile("src/Code.cs", "class Code { }\n");
        var tip = blocked.Commit("tip", "2026-11-04T00:00:00Z");
        var shim = Path.Combine(blocked.ReceiptRoot, "slot-shim.txt");
        var runnable = blocked.Paths("src/Code.cs");

        var absentStart = blocked.Start(plan: false, range: new string('0', 40) + ".." + new string('0', 40), fileList: runnable);
        absentStart.Environment.Remove("POST_LAND_WARNING_PLATFORM");
        absentStart.Environment["POST_LAND_WARNING_SLOT_SHIM"] = shim;
        _ = PostLandEarlyWarningScript.ResolvePostLandPlatform(absentStart);
        var absent = await blocked.WaitAsync(absentStart);
        absent.Exit.ShouldBe(2, absent.Text);
        absent.Text.ShouldContain("refused=head-mismatch", Case.Sensitive);
        absent.Text.ShouldNotContain("refused=not-linux", Case.Sensitive);
        File.Exists(shim).ShouldBeFalse();

        var emptyStart = blocked.Start(plan: false, range: new string('0', 40) + ".." + new string('0', 40), fileList: runnable);
        emptyStart.Environment["POST_LAND_WARNING_PLATFORM"] = "";
        emptyStart.Environment["POST_LAND_WARNING_SLOT_SHIM"] = shim;
        _ = PostLandEarlyWarningScript.ResolvePostLandPlatform(emptyStart);
        var empty = await blocked.WaitAsync(emptyStart);
        empty.Exit.ShouldBe(2, empty.Text);
        empty.Text.ShouldContain("refused=head-mismatch", Case.Sensitive);
        empty.Text.ShouldNotContain("refused=not-linux", Case.Sensitive);
        File.Exists(shim).ShouldBeFalse();

        var windowsStart = blocked.Start(plan: false, range: new string('c', 40) + ".." + tip, fileList: runnable);
        windowsStart.Environment["POST_LAND_WARNING_PLATFORM"] = "windows";
        windowsStart.Environment["POST_LAND_WARNING_SLOT_SHIM"] = shim;
        _ = PostLandEarlyWarningScript.ResolvePostLandPlatform(windowsStart);
        var windows = await blocked.WaitAsync(windowsStart);
        windows.Exit.ShouldBe(3, windows.Text);
        windows.Text.ShouldContain("refused=not-linux", Case.Sensitive);
        File.Exists(shim).ShouldBeFalse();
        File.Exists(Path.Combine(blocked.ReceiptRoot, tip + ".json")).ShouldBeFalse();
    }

    [Test]
    public async Task Manifest_uses_no_letter_or_and_partitions_unit_classes()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var manifestPath = Path.Combine(root, "tests", "post-land-unit-shards.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        manifest.ShouldNotContain(ForbiddenLetterOr);
        manifest.ShouldContain("/*/<namespace>/*/*[Category=Unit]", Case.Sensitive);
        manifest.ShouldContain("/*/Antiphon.Tests.Checkpoints/<Letter>*/*[Category=Unit]", Case.Sensitive);

        using var list = new Fixture(root, Path.Combine(Path.GetTempPath(), "post-land-manifest-" + Guid.NewGuid().ToString("N")), deleteRepo: false);
        var plan = await list.PlanAsync(list.Paths("src/post-land-early-warning-unmatched.cs"));
        plan.Exit.ShouldBe(0, plan.Text);
        plan.State.ShouldBe("run", plan.Text);
        var census = Census(root);
        census.Any(row => row.Namespace == CheckpointsNamespace).ShouldBeTrue("this tree has Checkpoints unit classes");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shard in plan.Shards.Where(shard => shard.GetProperty("kind").GetString() == "unit"))
        {
            var filter = shard.GetProperty("filter").GetString() ?? "";
            filter.ShouldNotContain(ForbiddenLetterOr);
            foreach (var item in shard.GetProperty("classes").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                var ns = item.GetProperty("namespace").GetString() ?? "";
                var key = ns + "\n" + name;
                seen.ContainsKey(key).ShouldBeFalse(key);
                if (ns == CheckpointsNamespace)
                    filter.ShouldBe($"/*/Antiphon.Tests.Checkpoints/{name[0]}*/*[Category=Unit]");
                else
                    filter.ShouldBe($"/*/{ns}/*/*[Category=Unit]");
                seen.Add(key, filter);
            }
        }

        var expected = census.Select(row => row.Namespace + "\n" + row.Name).ToHashSet(StringComparer.Ordinal);
        seen.Keys.ToHashSet(StringComparer.Ordinal).ShouldBe(expected);
        plan.Text.ShouldNotContain(ForbiddenLetterOr);
    }

    [Test]
    public async Task Integration_list_drops_unit_classes_and_records_windows_as_owed()
    {
        using var fixture = Fixture.Create();
        fixture.WriteUnitClass("Antiphon.Tests.Fixture", "UnitSideTests");
        for (var i = 1; i <= 9; i++)
            fixture.WriteIntegrationClass("Antiphon.Tests.Fixture", "Int" + i.ToString("00") + "Tests");
        fixture.WriteIntegrationClass("Antiphon.Tests.Fixture", "WinOnlyTests");
        var classes = new List<string> { "UnitSideTests", "WinOnlyTests" };
        for (var i = 1; i <= 9; i++)
            classes.Add("Int" + i.ToString("00") + "Tests");
        fixture.WriteWindowsTable("src/Mixed.cs", classes, "WinOnlyTests");

        var plan = await fixture.PlanAsync(fixture.Paths("src/Mixed.cs"));
        plan.Exit.ShouldBe(0, plan.Text);
        plan.State.ShouldBe("run", plan.Text);
        var integration = plan.Shards.Where(shard => shard.GetProperty("kind").GetString() == "integration").ToArray();
        integration.Length.ShouldBe(2);
        var names = integration.SelectMany(shard => shard.GetProperty("classes").EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? "")).ToArray();
        names.Length.ShouldBe(9);
        names.ShouldNotContain("UnitSideTests");
        names.ShouldNotContain("WinOnlyTests");
        integration[0].GetProperty("classes").GetArrayLength().ShouldBe(8);
        integration[1].GetProperty("classes").GetArrayLength().ShouldBe(1);
        var owed = plan.Root.GetProperty("owedWindows").EnumerateArray().ToArray();
        owed.Length.ShouldBe(1);
        owed[0].GetProperty("passed").GetBoolean().ShouldBeFalse();
        (owed[0].GetProperty("filter").GetString() ?? "").ShouldContain("WinOnlyTests", Case.Sensitive);
        plan.Root.GetProperty("summaryLine").GetString().ShouldContain("owed-windows=1", Case.Sensitive);
    }

    [Test]
    public async Task Doc_sentences_are_present_in_both_owner_files()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var testing = await File.ReadAllTextAsync(Path.Combine(root, "docs", "testing-and-build.md"));
        var loop = await File.ReadAllTextAsync(Path.Combine(root, "docs", "orchestration-loop.md"));
        testing.ShouldContain(TestingSentence, Case.Sensitive);
        loop.ShouldContain(OrchestratorSentence, Case.Sensitive);
    }

    private static string Trx(params (string ClassName, string Method, string Outcome)[] rows)
    {
        var defs = new StringBuilder();
        var results = new StringBuilder();
        for (var i = 0; i < rows.Length; i++)
        {
            var id = "id" + i.ToString();
            defs.Append("<UnitTest id=\"").Append(id).Append("\"><TestMethod className=\"")
                .Append(rows[i].ClassName).Append("\" name=\"").Append(rows[i].Method).Append("\" /></UnitTest>");
            results.Append("<UnitTestResult testId=\"").Append(id).Append("\" outcome=\"")
                .Append(rows[i].Outcome).Append("\" />");
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>"
            + defs + "</TestDefinitions><Results>" + results + "</Results></TestRun>";
    }

    private static string[] Names(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(item => item.GetString() ?? "").ToArray();

    private static List<(string Namespace, string Name)> Census(string repo)
    {
        var root = Path.Combine(repo, "tests", "Antiphon.Tests");
        var found = new List<(string Namespace, string Name)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = file.Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            var lines = File.ReadAllLines(file);
            var ns = "";
            for (var i = 0; i < lines.Length; i++)
            {
                var trim = lines[i].Trim();
                var fileNs = FileNamespace.Match(trim);
                if (fileNs.Success)
                {
                    ns = fileNs.Groups[1].Value;
                    continue;
                }

                var blockNs = BlockNamespace.Match(trim);
                if (blockNs.Success)
                {
                    ns = blockNs.Groups[1].Value;
                    continue;
                }

                var match = ClassLine.Match(trim);
                if (!match.Success)
                    continue;
                var attrs = new List<string>();
                for (var j = i - 1; j >= 0; j--)
                {
                    var earlier = lines[j].Trim();
                    if (earlier.Length == 0 || earlier.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    if (earlier.StartsWith("[", StringComparison.Ordinal))
                    {
                        attrs.Add(earlier);
                        continue;
                    }

                    break;
                }

                var unit = attrs.Any(line => line.Contains("Category(\"Unit\")", StringComparison.Ordinal));
                var integration = attrs.Any(line => line.Contains("Category(\"Integration\")", StringComparison.Ordinal));
                if (!unit || integration)
                    continue;
                var hasTest = false;
                for (var k = i + 1; k < lines.Length; k++)
                {
                    var body = lines[k].Trim();
                    if (ClassLine.IsMatch(body))
                        break;
                    if (body.Contains("[Test]", StringComparison.Ordinal)
                        || body.Contains("[Test,", StringComparison.Ordinal)
                        || body.Contains("[TestAttribute", StringComparison.Ordinal))
                    {
                        hasTest = true;
                        break;
                    }
                }

                if (!hasTest)
                    continue;
                var key = ns + "\n" + match.Groups[1].Value;
                if (seen.Add(key))
                    found.Add((ns, match.Groups[1].Value));
            }
        }

        return found;
    }

    private sealed class PostLandEarlyWarningScript
    {
        public static string ResolvePostLandPlatform(ProcessStartInfo start)
        {
            if (!start.Environment.TryGetValue("POST_LAND_WARNING_PLATFORM", out var value) || value is null)
                return "absent";
            if (value.Length == 0)
                return "empty";
            return value;
        }
    }

    private class Fixture : IDisposable
    {
        private readonly bool _deleteRepo;

        internal Fixture(string repo, string receiptRoot, bool deleteRepo)
        {
            Repo = repo;
            ReceiptRoot = receiptRoot;
            Directory.CreateDirectory(ReceiptRoot);
            _deleteRepo = deleteRepo;
        }

        internal string Repo { get; }
        internal string ReceiptRoot { get; }

        internal static Fixture Create()
        {
            var repo = Directory.CreateTempSubdirectory("post-land-repo-").FullName;
            var receipt = Directory.CreateTempSubdirectory("post-land-receipt-").FullName;
            return new Fixture(repo, receipt, deleteRepo: true);
        }

        internal string Paths(params string[] paths)
        {
            var list = Path.Combine(ReceiptRoot, "paths-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(list, string.Join('\n', paths));
            return list;
        }

        internal void WriteFile(string relative, string content)
        {
            var path = Path.Combine(Repo, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        internal void WriteUnitClass(string ns, string name) =>
            WriteClass(ns, name, "Unit");

        internal void WriteIntegrationClass(string ns, string name) =>
            WriteClass(ns, name, "Integration");

        internal void WriteTable(string glob, params string[] classes) =>
            WriteFile("tests/impacted-classes.json", Table(glob, classes));

        internal void WriteWindowsTable(string glob, IReadOnlyList<string> classes, string windowsClass)
        {
            var listed = string.Join(", ", classes.Select(name => "\"" + name + "\""));
            var json = "{\n  \"schema\": 1,\n  \"cache\": \"none\",\n  \"kinds\": [\n    {\n      \"id\": \"mixed\",\n      \"globs\": [\""
                + glob + "\"],\n      \"classes\": [" + listed + "]\n    },\n    {\n      \"id\": \"win\",\n      \"globs\": [\""
                + glob + "\"],\n      \"classes\": [\"" + windowsClass + "\"],\n      \"windows\": \"required\"\n    }\n  ]\n}\n";
            WriteFile("tests/impacted-classes.json", json);
        }

        internal void WriteLock(int pid) =>
            File.WriteAllText(Path.Combine(ReceiptRoot, "lock.json"), "{\"pid\":" + pid.ToString() + ",\"startedAt\":\"2026-11-09T00:00:00Z\"}\n");

        internal void WriteLastStart(DateTime utc) =>
            File.WriteAllText(
                Path.Combine(ReceiptRoot, "last-start.json"),
                "{\"startedAt\":\"" + utc.ToUniversalTime().ToString("o") + "\"}\n");

        internal void WriteTrx(string tip, string shard, string body)
        {
            var dir = Path.Combine(ReceiptRoot, tip);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, shard + ".trx"), body);
        }

        internal void WriteReceipt(string tip, string state, params string[] failures)
        {
            var names = string.Join(",", failures.Select(name => "\"" + name + "\""));
            File.WriteAllText(
                Path.Combine(ReceiptRoot, tip + ".json"),
                "{\"tipSha\":\"" + tip + "\",\"state\":\"" + state + "\",\"failures\":[" + names + "]}\n");
        }

        internal Task<PlanResult> PlanAsync(string fileList) =>
            PlanAsync(fileList, new string('1', 40) + ".." + new string('2', 40));

        internal Task<PlanResult> PlanAsync(string fileList, string range) =>
            WaitAsync(Start(plan: true, range: range, fileList: fileList));

        internal Task<PlanResult> PlanRangeAsync(string range) =>
            WaitAsync(Start(plan: true, range: range, fileList: null));

        internal ProcessStartInfo Start(bool plan, string range, string? fileList)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Repo,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "post-land-early-warning.ps1"));
            if (plan)
                start.ArgumentList.Add("-Plan");
            start.ArgumentList.Add("-Range");
            start.ArgumentList.Add(range);
            start.ArgumentList.Add("-Repo");
            start.ArgumentList.Add(Repo);
            start.ArgumentList.Add("-ReceiptRoot");
            start.ArgumentList.Add(ReceiptRoot);
            if (!string.IsNullOrEmpty(fileList))
            {
                start.ArgumentList.Add("-FileList");
                start.ArgumentList.Add(fileList);
            }

            var table = Path.Combine(Repo, "tests", "impacted-classes.json");
            if (File.Exists(table))
            {
                start.ArgumentList.Add("-Table");
                start.ArgumentList.Add(table);
            }

            return start;
        }

        internal async Task<PlanResult> WaitAsync(ProcessStartInfo start)
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return PlanResult.Parse(process.ExitCode, stdout, stderr);
        }

        public void Dispose()
        {
            if (_deleteRepo && Directory.Exists(Repo))
                Directory.Delete(Repo, recursive: true);
            if (Directory.Exists(ReceiptRoot) && !ReceiptRoot.StartsWith(DelegateScriptRunner.RepoRoot, StringComparison.Ordinal))
                Directory.Delete(ReceiptRoot, recursive: true);
        }

        private void WriteClass(string ns, string name, string lane)
        {
            WriteFile(
                "tests/Antiphon.Tests/" + name + ".cs",
                "namespace " + ns + ";\n\n[Category(\"" + lane + "\")]\npublic sealed class " + name + "\n{\n    [Test]\n    public void Holds() { }\n}\n");
        }

        private static string Table(string glob, string[] classes)
        {
            var listed = string.Join(", ", classes.Select(name => "\"" + name + "\""));
            return "{\n  \"schema\": 1,\n  \"cache\": \"none\",\n  \"kinds\": [\n    {\n      \"id\": \"fixture\",\n      \"globs\": [\""
                + glob + "\"],\n      \"classes\": [" + listed + "]\n    }\n  ]\n}\n";
        }
    }

    private sealed class GitRepo : Fixture
    {
        private GitRepo(string repo, string receiptRoot) : base(repo, receiptRoot, deleteRepo: true)
        {
        }

        internal static new GitRepo Create()
        {
            var repo = Directory.CreateTempSubdirectory("post-land-git-").FullName;
            var receipt = Directory.CreateTempSubdirectory("post-land-git-receipt-").FullName;
            var created = new GitRepo(repo, receipt);
            Git(repo, "init", "-b", "master");
            Git(repo, "config", "user.email", "fixture@example.com");
            Git(repo, "config", "user.name", "fixture");
            Git(repo, "config", "commit.gpgsign", "false");
            created.WriteFile("README.md", "fixture\n");
            created.Commit("init", "2026-10-01T00:00:00Z");
            return created;
        }

        internal string Commit(string message, string date)
        {
            Git(Repo, "add", "-A");
            var psi = GitStart(Repo, "commit", "--no-gpg-sign", "-m", message);
            psi.Environment["GIT_AUTHOR_DATE"] = date;
            psi.Environment["GIT_COMMITTER_DATE"] = date;
            var result = GitWait(psi);
            result.Exit.ShouldBe(0, result.Text);
            return Git(Repo, "rev-parse", "HEAD").Trim();
        }

        private static string Git(string repo, params string[] args)
        {
            var result = GitWait(GitStart(repo, args));
            result.Exit.ShouldBe(0, result.Text);
            return result.Stdout;
        }

        private static ProcessStartInfo GitStart(string repo, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            start.Environment["GIT_AUTHOR_NAME"] = "fixture";
            start.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.com";
            start.Environment["GIT_COMMITTER_NAME"] = "fixture";
            start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.com";
            return start;
        }

        private static (int Exit, string Stdout, string Text) GitWait(ProcessStartInfo start)
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout, stdout + stderr);
        }
    }

    private sealed class PlanResult
    {
        private PlanResult(int exit, string stdout, string text, JsonDocument? document)
        {
            Exit = exit;
            Stdout = stdout;
            Text = text;
            Document = document;
        }

        internal int Exit { get; }
        internal string Stdout { get; }
        internal string Text { get; }
        private JsonDocument? Document { get; }
        internal JsonElement Root => Document!.RootElement;
        internal string State => Root.GetProperty("state").GetString() ?? "";
        internal List<JsonElement> Shards => Root.GetProperty("shards").EnumerateArray().ToList();

        internal static PlanResult Parse(int exit, string stdout, string stderr)
        {
            JsonDocument? document = null;
            var body = stdout.Trim();
            var start = body.IndexOf('{');
            var end = body.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    document = JsonDocument.Parse(body[start..(end + 1)]);
                }
                catch (JsonException)
                {
                    document = null;
                }
            }

            return new PlanResult(exit, stdout, stdout + "\n" + stderr, document);
        }
    }
}
