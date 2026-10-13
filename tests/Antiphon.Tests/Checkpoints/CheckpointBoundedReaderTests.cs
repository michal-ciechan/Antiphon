using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointBoundedReaderTests : CheckpointTestBase
{
    [Test]
    [Arguments("project", "ordinary")]
    [Arguments("project", "held")]
    [Arguments("tool-git", "ordinary")]
    [Arguments("tool-git", "held")]
    [Arguments("script-git", "ordinary")]
    [Arguments("script-git", "held")]
    [Arguments("tool-eval", "ordinary")]
    [Arguments("tool-eval", "held")]
    [Arguments("script-eval", "ordinary")]
    [Arguments("script-eval", "held")]
    public async Task changed_readers_bound_fifo_and_exited_child_pipes(string mode, string shape)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux FIFO and process-group qualification");
        var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-bounded-readers.mjs"),
            CheckpointFixtures.RepoRoot, typeof(Antiphon.Checkpoints.Program).Assembly.Location, mode, shape }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); // The owned supervisor enforces and asserts the exact hard bound.
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
    }
}
