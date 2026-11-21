using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerSettlementGitDeadlineTests
{
    private static readonly TimeSpan CommandBudget = TimeSpan.FromSeconds(4);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Timeout(20_000)]
    public async Task FakeGitExitAndOutputShareDeadline(bool landingPath, bool holdPipesAfterExit, CancellationToken ct)
    {
        using var descendants = DescendantLeakScope.Create();
        Task? rootExit = null;
        void ObserveStart(Process root)
        {
            descendants.AdoptOrKill(root);
            rootExit = root.WaitForExitAsync(ct);
        }

        string expected;
        Task run;
        if (landingPath)
        {
            var git = new FakeSettlementGit(holdPipesAfterExit, descendants, ObserveStart);
            expected = "git_timeout";
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
            ConfigureFakeChild(start, holdPipesAfterExit, descendants);
            expected = "git " + string.Join(' ', start.ArgumentList);
            run = RunnerSettlementSyncTests.SyncWorld.ExecAsync(start, CommandBudget, ObserveStart);
        }

        try
        {
            if (holdPipesAfterExit)
            {
                await rootExit.ShouldNotBeNull().WaitAsync(TimeSpan.FromSeconds(3), ct);
                descendants.Alive().ShouldNotBeEmpty("a descendant still holds a redirected writer after the root exits");
            }
            var error = await Should.ThrowAsync<TimeoutException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(10), ct));
            error.Message.ShouldBe(expected);
            await rootExit.ShouldNotBeNull().WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        finally
        {
            descendants.KillAll();
            try { await run.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) when (run.IsFaulted) { /* Observe the command deadline. */ }
        }
    }

    private static void ConfigureFakeChild(ProcessStartInfo start, bool holdPipesAfterExit, DescendantLeakScope descendants)
    {
        start.CreateNoWindow = true;
        start.ArgumentList.Clear();
        // Both children have a finite fallback if the test host dies. The initial Windows ping
        // lets the test's job adopt the root before it starts the descendant.
        if (OperatingSystem.IsWindows())
        {
            start.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(holdPipesAfterExit
                ? "ping -n 2 127.0.0.1 >nul & start /b ping -n 25 127.0.0.1"
                : "ping -n 25 127.0.0.1");
        }
        else
        {
            start.FileName = "/bin/sh";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(holdPipesAfterExit ? "sleep 25 & exit 0" : "exec sleep 25");
        }
        descendants.Apply(start);
    }

    private sealed class FakeSettlementGit(bool holdPipesAfterExit, DescendantLeakScope descendants, Action<Process> started)
        : RunnerSettlementSyncTests.SettlementFixtureGit
    {
        protected override Process? StartProcess(ProcessStartInfo start)
        {
            ConfigureFakeChild(start, holdPipesAfterExit, descendants);
            var child = base.StartProcess(start);
            if (child is not null) started(child);
            return child;
        }
    }
}
