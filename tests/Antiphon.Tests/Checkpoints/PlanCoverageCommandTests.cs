using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Checkpoints;
[Category("Unit")]
public sealed class PlanCoverageCommandTests : CheckpointTestBase
{
    [Test]
    public void coverage_command_preserves_existing_import_contract()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var before = PlanTableImporter.ImportFile(world.Plan);
        new CoverageCommand().Run(root, world.Plan, output: new StringWriter());
        ManifestLoader.ToYaml(PlanTableImporter.ImportFile(world.Plan).Manifest!).ShouldBe(ManifestLoader.ToYaml(before.Manifest!), "coverage-import-preserved");
        PlanTableImporter.ImportFile(CheckpointFixtures.Fixture("plan-table-legacy-min.md")).ExitCode.ShouldBe(2, "coverage-legacy-import-refusal");
    }
    [Test]
    public void renders_stable_text_json_and_exit_codes()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root, "class Demo { void Check() {} }");
        var command = new CoverageCommand(); var text = new StringWriter();
        command.Run(root, world.Plan, output: text).ShouldBe(1, "coverage-exit-findings");
        var again = new StringWriter(); command.Run(root, world.Plan, output: again);
        again.ToString().ShouldBe(text.ToString(), "coverage-stable-order");
        var json = new StringWriter(); command.Run(root, world.Plan, format: "json", output: json).ShouldBe(1, "coverage-json-exit");
        json.ToString().ShouldContain("MISSING_LABEL", Case.Sensitive, "coverage-json-decisions");
        using var firstJson = JsonDocument.Parse(json.ToString());
        File.WriteAllText(world.Source, "class Demo { void Check() { x.ShouldBe(1, \"target-label\"); } }");
        command.Run(root, world.Plan, output: new StringWriter()).ShouldBe(0, "coverage-exit-clean");
        var changed = new StringWriter(); command.Run(root, world.Plan, format: "json", output: changed);
        using var nextJson = JsonDocument.Parse(changed.ToString());
        nextJson.RootElement.GetProperty("inputsSha256").GetString().ShouldNotBe(firstJson.RootElement.GetProperty("inputsSha256").GetString(), "coverage-digest-change");

        const string checklist = "{\n  \"version\": 1,\n  \"items\": []\n}\n";
        const string lfHash = "da6fa0fb6dc64a3faf1f81f830b8696ccf02b9c654135111429090fa240870f8";
        const string crlfHash = "79f2f76348e92b2903f7a7e371d28a1e951cf465a1ec68a1f1ee2221a871a200";
        const string source = "class Demo { void Check() { value.ShouldBe(1, \"target-label\"); } }\n";
        string RawHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var byteRoot = TempDir();
        var byteWorld = PlanCoverageFixture.WriteWorld(byteRoot);
        var checklistPath = Path.Combine(byteRoot, "checklist.json");
        var utf8 = new UTF8Encoding(false);
        var digests = new Dictionary<(bool Inline, bool PlanCrlf, bool SourceCrlf, bool ChecklistCrlf),
            (string Plan, string Source, string Checklist, string Inputs)>();
        foreach (var inline in new[] { true, false })
        foreach (var planCrlf in new[] { false, true })
        foreach (var sourceCrlf in new[] { false, true })
        foreach (var checklistCrlf in inline ? new[] { false } : new[] { false, true })
        {
            var planBytes = PlanCoverageFixture.Plan() + (inline ? "\n```plan-coverage-v1\n" + checklist + "```\n" : "");
            if (planCrlf) planBytes = planBytes.Replace("\n", "\r\n");
            var sourceBytes = sourceCrlf ? source.Replace("\n", "\r\n") : source;
            var checklistBytes = checklistCrlf ? checklist.Replace("\n", "\r\n") : checklist;
            File.WriteAllText(byteWorld.Plan, planBytes, utf8);
            File.WriteAllText(byteWorld.Source, sourceBytes, utf8);
            if (!inline) File.WriteAllText(checklistPath, checklistBytes, utf8);
            var output = new StringWriter();
            command.Run(byteRoot, byteWorld.Plan, format: "json", checklist: inline ? null : checklistPath, output: output)
                .ShouldBe(0, "c1013-command-exit");
            using var parsed = JsonDocument.Parse(output.ToString());
            var result = parsed.RootElement;
            result.GetProperty("invalid").GetBoolean().ShouldBeFalse("c1013-command-valid");
            result.GetProperty("schemaVersion").GetInt32().ShouldBe(1, "c1013-command-schema");
            result.GetProperty("summary").GetProperty("result").GetString().ShouldBe("clean", "c1013-command-clean");
            result.GetProperty("summary").GetProperty("matched").GetInt32().ShouldBe(2, "c1013-command-matched");
            result.GetProperty("diagnostics").GetArrayLength().ShouldBe(0, "c1013-command-diagnostics");
            var obligations = result.GetProperty("obligations").EnumerateArray().ToArray();
            obligations.Select(o => (o.GetProperty("id").GetString(), o.GetProperty("kind").GetString(), o.GetProperty("name").GetString()))
                .ShouldBe([("V-1", "method", "Demo.Check"), ("V-1", "label", "target-label")], "c1013-command-obligations");
            obligations.ShouldAllBe(o => o.GetProperty("matches").GetArrayLength() == 1, "c1013-command-matches");
            var selected = result.GetProperty("sources").EnumerateArray().Single();
            selected.GetProperty("path").GetString().ShouldBe("tests/Sample/Demo.cs", "c1013-command-source-path");
            var expectedPlanHash = RawHash(planBytes);
            var expectedSourceHash = RawHash(sourceBytes);
            var expectedChecklistHash = checklistCrlf ? crlfHash : lfHash;
            var expectedInputsHash = RawHash(expectedPlanHash + "\n" + expectedChecklistHash + "\n" + "tests/Sample/Demo.cs\0" + expectedSourceHash);
            var actualPlanHash = result.GetProperty("planSha256").GetString()!;
            var actualSourceHash = selected.GetProperty("sha256").GetString()!;
            var actualChecklistHash = result.GetProperty("checklistSha256").GetString()!;
            var actualInputsHash = result.GetProperty("inputsSha256").GetString()!;
            actualPlanHash.ShouldBe(expectedPlanHash, "c1013-command-raw-plan");
            actualSourceHash.ShouldBe(expectedSourceHash, "c1013-command-raw-source");
            actualChecklistHash.ShouldBe(expectedChecklistHash, "c1013-command-checklist");
            actualInputsHash.ShouldBe(expectedInputsHash, "c1013-command-inputs");
            digests.Add((inline, planCrlf, sourceCrlf, checklistCrlf), (actualPlanHash, actualSourceHash, actualChecklistHash, actualInputsHash));
            if (inline && !planCrlf && !sourceCrlf)
            {
                var repeated = new StringWriter();
                command.Run(byteRoot, byteWorld.Plan, format: "json", output: repeated).ShouldBe(0, "c1013-command-repeat-exit");
                repeated.ToString().ShouldBe(output.ToString(), "c1013-command-repeat-json");
            }
        }
        foreach (var (key, value) in digests)
        {
            if (key.PlanCrlf)
            {
                var lf = digests[(key.Inline, false, key.SourceCrlf, key.ChecklistCrlf)];
                value.Plan.ShouldNotBe(lf.Plan, "c1013-command-raw-plan");
                value.Inputs.ShouldNotBe(lf.Inputs, "c1013-command-inputs");
            }
            if (key.SourceCrlf)
            {
                var lf = digests[(key.Inline, key.PlanCrlf, false, key.ChecklistCrlf)];
                value.Source.ShouldNotBe(lf.Source, "c1013-command-raw-source");
                value.Inputs.ShouldNotBe(lf.Inputs, "c1013-command-inputs");
            }
            if (key.ChecklistCrlf)
            {
                var lf = digests[(key.Inline, key.PlanCrlf, key.SourceCrlf, false)];
                value.Checklist.ShouldNotBe(lf.Checklist, "c1013-command-checklist");
                value.Inputs.ShouldNotBe(lf.Inputs, "c1013-command-inputs");
            }
        }
    }
    [Test]
    public void invalid_inputs_cannot_produce_clean_summary()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var output = new StringWriter();
        new CoverageCommand().Run(root, "missing.md", output: output).ShouldBe(2, "coverage-invalid-never-clean");
        output.ToString().ShouldContain("result=invalid", Case.Sensitive, "coverage-invalid-footer");
        output.ToString().ShouldContain("testPath=\"missing.md\"", Case.Sensitive, "coverage-read-error-path");
        File.WriteAllText(world.Source, "class Demo { void Check( {");
        new CoverageCommand().Run(root, world.Plan, output: new StringWriter()).ShouldBe(2, "coverage-invalid-syntax");
        new CoverageCommand().Run(root, world.Plan, format: "yaml", output: new StringWriter()).ShouldBe(2, "coverage-invalid-format");
    }
    [Test]
    public async Task coverage_never_starts_driver_or_writes_run_state()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();
        var driver = new FakeDriver(); driver.When(_ => true, (_, _) => throw new InvalidOperationException("coverage launched driver"));
        var runtime = new CheckpointApp.Runtime { Driver = driver, Output = new StringWriter() };
        var exit = await Antiphon.Checkpoints.Program.RunAsync(["coverage", "--repo-root", root, "--plan", world.Plan], runtime);
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().SequenceEqual(before).ShouldBeTrue("coverage-no-side-effects");
        driver.Count(_ => true).ShouldBe(0, "coverage-driver-zero");
        exit.ShouldBe(0, "coverage-public-cli");
    }

    [Test]
    public async Task outside_project_fifo_is_refused_without_opening()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("coverage outside-project FIFO read sentinel requires Linux mkfifo");
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var outside = TempDir(); var fifo = Path.Combine(outside, "sentinel.csproj");
        MkFifo(fifo, 0x180).ShouldBe(0, "coverage-fifo-created");
        var link = Path.Combine(Path.GetDirectoryName(world.Source)!, "Probe.csproj");
        try
        {
            CreateProjectLink(link, fifo);
            // An open reader consumes the XML, then hangs until this writer closes.
            // Always close it and join the command, including on the red implementation.
            var writer = new FileStream(fifo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var output = new StringWriter();
            Task<int>? run = null; var refusedWithoutWaiting = false;
            try
            {
                writer.Write(System.Text.Encoding.UTF8.GetBytes("<Project />")); writer.Flush();
                run = Task.Run(() => new CoverageCommand().Run(root, world.Plan, output: output));
                refusedWithoutWaiting = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))) == run;
            }
            finally
            {
                writer.Dispose();
                if (run is not null) await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            refusedWithoutWaiting.ShouldBeTrue("coverage-outside-project-never-opened");
            run!.Result.ShouldBe(2, "coverage-outside-project-fifo-refused");
            output.ToString().ShouldContain("missing or unconfined selected path", Case.Sensitive, "coverage-fifo-confinement-verdict");
        }
        finally
        {
            File.Delete(link);
            File.Delete(fifo);
        }
    }

    [Test]
    public void existing_outside_project_is_refused_by_root_boundary()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var outside = TempDir(); var project = Path.Combine(outside, "existing.csproj");
        File.WriteAllText(project, "<Project />");
        var link = Path.Combine(Path.GetDirectoryName(world.Source)!, "Probe.csproj");
        try
        {
            CreateProjectLink(link, project);
            var output = new StringWriter();
            new CoverageCommand().Run(root, world.Plan, output: output).ShouldBe(2, "coverage-existing-outside-project-refused");
            output.ToString().ShouldContain("missing or unconfined selected path", Case.Sensitive, "coverage-existing-outside-root-boundary");
            // Also guard direct selection with an existing escape, independently of project loading.
            var source = Path.Combine(outside, "Outside.cs");
            File.WriteAllText(source, File.ReadAllText(world.Source));
            new CoverageCommand().Run(root, world.Plan, tests: [source], output: new StringWriter())
                .ShouldBe(2, "coverage-existing-outside-source-refused");
        }
        finally { File.Delete(link); }
    }

    private static void CreateProjectLink(string link, string target)
    {
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { Skip.Test($"coverage project symlink creation unavailable: {ex.GetType().Name}: {ex.Message}"); }
    }

    [Test]
    public void frozen_checklist_preserves_all_72_obligations()
    {
        var root = TempDir(); var plan = WriteFrozenWorld(root);
        var output = new StringWriter();
        new CoverageCommand().Run(root, plan, format: "json", output: output).ShouldBe(0, "coverage-frozen-count-clean");
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("summary").GetProperty("matched").GetInt32().ShouldBe(72, "coverage-frozen-count-clean");
    }

    [Test]
    public void class_only_frozen_result_count_has_no_findings()
    {
        var root = TempDir(); var plan = WriteFrozenWorld(root);
        var lines = File.ReadAllLines(plan);
        var row = Array.FindIndex(lines, l => l.StartsWith("| R-1 |", StringComparison.Ordinal));
        lines[row + 1].ShouldBeEmpty("coverage-class-only-coordinate-preserved");
        lines[row + 1] = "| R-2 | `CheckpointManifestTests` (6 results) | Whole-class regression run. |";
        File.WriteAllLines(plan, lines);
        var output = new StringWriter();
        new CoverageCommand().Run(root, plan, format: "json", output: output).ShouldBe(0, "coverage-class-only-count-clean");
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("diagnostics").GetArrayLength().ShouldBe(0, "coverage-class-only-no-findings");
        json.RootElement.GetProperty("summary").GetProperty("matched").GetInt32().ShouldBe(72, "coverage-class-only-frozen-obligations");
    }

    [Test]
    public void dropped_frozen_method_is_a_checklist_count_finding()
    {
        var root = TempDir(); var plan = WriteFrozenWorld(root);
        // Keep every original line coordinate; only the promised method item disappears.
        var lines = File.ReadAllLines(plan);
        var item = Array.FindIndex(lines, l => l.Contains("\"name\":\"CheckpointImportTests.imports_the_card_0688_table\"", StringComparison.Ordinal));
        lines[item] = ""; File.WriteAllLines(plan, lines);
        var output = new StringWriter();
        new CoverageCommand().Run(root, plan, output: output).ShouldBe(1, "coverage-checklist-method-omitted");
        output.ToString().ShouldContain("CHECKLIST_COUNT_MISMATCH", Case.Sensitive, "coverage-checklist-method-omitted");
        output.ToString().ShouldContain("id=R-1", Case.Sensitive, "coverage-checklist-method-omitted");
        output.ToString().ShouldContain("expected=26 actual=25", Case.Sensitive, "coverage-checklist-method-omitted");
    }

    [Test]
    public void extra_frozen_method_is_a_checklist_count_finding()
    {
        var root = TempDir(); var plan = WriteFrozenWorld(root);
        var text = File.ReadAllText(plan);
        var extra = "    {\"id\":\"R-1\",\"test\":\"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract\",\"kind\":\"method\",\"name\":\"PlanCoverageCommandTests.coverage_command_preserves_existing_import_contract\",\"planLine\":178},\n";
        text = text.Replace("    {\"id\":\"R-1\",\"test\":\"CheckpointImportTests.imports_the_card_0688_table\"", extra + "    {\"id\":\"R-1\",\"test\":\"CheckpointImportTests.imports_the_card_0688_table\"", StringComparison.Ordinal);
        File.WriteAllText(plan, text);
        var output = new StringWriter();
        new CoverageCommand().Run(root, plan, output: output).ShouldBe(1, "coverage-checklist-extra-method");
        output.ToString().ShouldContain("expected=26 actual=27", Case.Sensitive, "coverage-checklist-extra-method");
        output.ToString().ShouldContain("CHECKLIST_COUNT_MISMATCH", Case.Sensitive, "coverage-checklist-extra-method");
    }

    private static string WriteFrozenWorld(string root)
    {
        var plan = Path.Combine(root, "plan.md");
        File.WriteAllText(plan, PlanCoverageFixture.Raw("c999-frozen-plan.md.txt"));
        var directory = Path.Combine(root, "tests", "Antiphon.Tests", "Checkpoints");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "PlanCoverageParserTests", "PlanCoverageAssertionTests", "PlanCoveragePcTests", "PlanCoverageGoldenTests", "PlanCoverageCommandTests", "PlanCoverageFixture", "CheckpointImportTests", "CheckpointManifestTests" })
            File.Copy(Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Tests", "Checkpoints", name + ".cs"), Path.Combine(directory, name + ".cs"));
        return plan;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
