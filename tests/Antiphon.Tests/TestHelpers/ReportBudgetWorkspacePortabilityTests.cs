using System.Diagnostics;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReportBudgetWorkspacePortabilityTests
{
    [Test]
    public void Git_launch_uses_the_installed_PATH_command()
    {
        // A resolved Linux absolute path can pass every budget test here while breaking Windows.
        // Require the same executable contract as CheckpointFixtureProcess.Git, then launch it.
        var start = new ProcessStartInfo(ReportBudgetWorkspace.ResolveGitExecutable());
        start.FileName.ShouldBe("git", "the fixture must let the host resolve Git from PATH");
        start.ArgumentList.Add("--version");
        var output = CheckpointFixtureProcess.Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stderr);
        output.Stdout.ShouldStartWith("git version ");
    }
}
