using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointStampSafetyTests : CheckpointTestBase
{
    [NativeLinuxOnly("Linux source-shape qualification")]
    [Test]
    [Arguments("tool", "fifo")]
    [Arguments("script", "fifo")]
    [Arguments("tool", "symlink")]
    [Arguments("script", "symlink")]
    [Arguments("tool", "directory")]
    [Arguments("script", "directory")]
    [Arguments("tool", "dangling")]
    [Arguments("script", "dangling")]
    [Arguments("tool", "regular")]
    [Arguments("script", "regular")]
    public async Task tracked_stamp_shapes_are_bounded_and_leave_results_in_place(string entry, string shape)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux source-shape qualification");
        var root = TempDir();
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-stamp.ps1");
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-File", script, "-Root", root, "-Repository", CheckpointFixtures.RepoRoot,
            "-ToolDll", typeof(Antiphon.Checkpoints.Program).Assembly.Location, "-Entry", entry, "-Shape", shape }) psi.ArgumentList.Add(arg);
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
