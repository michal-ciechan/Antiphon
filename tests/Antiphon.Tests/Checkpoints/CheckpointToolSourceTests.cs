using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointToolSourceTests : CheckpointTestBase
{
    [Test]
    public async Task tiny_source_is_really_copied_to_the_launched_image()
    {
        var repo = TempDir();
        var source = TinyToolDirectory();
        LaunchRequest? observed = null;
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }],
        };
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = source,
            Launch = request => { observed = request; return Environment.ProcessId; },
        };
        var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), repo, TextWriter.Null, runtime);
        started.ExitCode.ShouldBe(0);
        observed.ShouldNotBeNull();
        var copiedDll = observed.Arguments[0];
        copiedDll.ShouldBe(Path.Combine(started.RunDirectory, "tool", "Antiphon.Checkpoints.dll"));
        copiedDll.ShouldNotBe(Path.Combine(source, "Antiphon.Checkpoints.dll"));
        File.ReadAllBytes(copiedDll).ShouldBe(File.ReadAllBytes(Path.Combine(source, "Antiphon.Checkpoints.dll")));
        File.ReadAllText(Path.Combine(started.RunDirectory, "tool", "sentinel.txt")).ShouldBe("tiny-tool-source");
        Directory.EnumerateFiles(source).Sum(path => new FileInfo(path).Length).ShouldBeLessThan(65536);
    }

    [Test]
    public void default_source_is_the_production_tool_directory()
    {
        CheckpointApp.ToolSource().ShouldBe(AppContext.BaseDirectory);
        CheckpointApp.ToolSource(new CheckpointApp.Runtime()).ShouldBe(AppContext.BaseDirectory);
    }
}
