using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerSettlementGitDeadlineTests
{
    private static readonly TimeSpan CommandBudget = TimeSpan.FromSeconds(1);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Timeout(20_000)]
    [NativeLinuxOnly("POSIX witness for a fake Git child or a detached holder of its redirected pipes.")]
    public async Task FakeGitExitAndOutputShareDeadline(bool landingPath, bool holdPipesAfterExit, CancellationToken ct)
    {
        using var descendants = DescendantLeakScope.Create();
        // The finite fallback also bounds the lifetime if the test host itself is interrupted.
        var script = holdPipesAfterExit ? "sleep 25 & exit 0" : "exec sleep 25";
        Task run;
        if (landingPath)
        {
            var git = new FakeSettlementGit(script, descendants);
            run = git.RunAsync(Path.GetTempPath(), ["fixture-child"], new LandingGitRunOptions(CommandBudget), ct);
        }
        else
        {
            var start = new ProcessStartInfo
            {
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            ConfigureFakeChild(start, script, descendants);
            run = RunnerSettlementSyncTests.SyncWorld.ExecAsync(start, CommandBudget);
        }

        try
        {
            var error = await Should.ThrowAsync<TimeoutException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(8), ct));
            error.Message.ShouldBe(landingPath ? "git_timeout" : "git -c " + script);
        }
        finally
        {
            descendants.KillAll();
            try { await run.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) when (run.IsFaulted) { /* Observe the command deadline. */ }
        }
    }

    private static void ConfigureFakeChild(ProcessStartInfo start, string script, DescendantLeakScope descendants)
    {
        start.FileName = "/bin/sh";
        start.ArgumentList.Clear();
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        descendants.Apply(start);
    }

    private sealed class FakeSettlementGit(string script, DescendantLeakScope descendants)
        : RunnerSettlementSyncTests.SettlementFixtureGit
    {
        protected override Process? StartProcess(ProcessStartInfo start)
        {
            ConfigureFakeChild(start, script, descendants);
            return base.StartProcess(start);
        }
    }
}
