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

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
