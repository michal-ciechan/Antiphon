using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointStampSafetyTests : CheckpointTestBase
{
    [NativeLinuxOnly("Linux source-shape qualification")]
    [Test]
    [Arguments("fifo")]
    [Arguments("symlink")]
    [Arguments("directory")]
    [Arguments("dangling")]
    [Arguments("regular")]
    public async Task tracked_stamp_shapes_are_bounded_and_leave_results_in_place(string shape)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux source-shape qualification");
        var root = TempDir();
        // The staged script harness host (checkpoint-stamp) settles a hand-built baseline with the tool's confinement, in a
        // process of its own so a read that blocked on the FIFO is killed at the bound instead of holding this one. The
        // script half (run-checkpoint.ps1's own stamp) went with that script in unit 20.
        var host = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "exec", host, "checkpoint-stamp", typeof(Antiphon.Checkpoints.Program).Assembly.Location, root, shape })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true); await process.WaitForExitAsync();
            throw new ShouldAssertException("HUNG: killed and awaited at four-second hard bound " + await stdout + await stderr);
        }
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        (await stdout).ShouldContain("tool/" + shape + " PASS");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("CP-1")]
    public async Task import_and_coverage_refuse_blank_identities_without_writes(string? identity)
    {
        var root = TempDir();
        var world = PlanCoverageFixture.WriteWorld(root);
        File.WriteAllText(world.Plan, PlanCoverageFixture.Plan().Replace("| CP-1 |", "| " + identity + " |", StringComparison.Ordinal));
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var outputPath = Path.Combine(root, "import.yaml");
        var imported = await Antiphon.Checkpoints.Program.RunAsync(["import", "--repo-root", root, "--plan", world.Plan, "--out", outputPath]);
        var output = new StringWriter();
        var coverage = new CoverageCommand().Run(root, world.Plan, output: output);
        if (identity == "CP-1") { imported.ShouldBe(0); coverage.ShouldBe(0, output.ToString()); File.Exists(outputPath).ShouldBeTrue(); }
        else
        {
            imported.ShouldBe(2, "import must refuse a blank checkpoint identity");
            coverage.ShouldBe(2, output.ToString()); output.ToString().ShouldContain("identity");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ShouldBe(before.Keys.Order());
            foreach (var pair in before) File.ReadAllBytes(pair.Key).ShouldBe(pair.Value);
        }
        foreach (var outputCell in new[] { "", "  " })
        {
            var badBuild = PlanCoverageFixture.Plan().Replace("bin-demo/", outputCell, StringComparison.Ordinal);
            var result = PlanTableImporter.ImportMarkdown(badBuild);
            result.ExitCode.ShouldBe(2); result.Manifest.ShouldBeNull(); result.Error.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
