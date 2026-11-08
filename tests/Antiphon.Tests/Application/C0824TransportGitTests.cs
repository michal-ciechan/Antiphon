using System.Diagnostics;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C0824TransportGitTests
{
    [Test]
    public void Scratch_git_returns_output_and_preserves_nonzero_exit()
    {
        var root = Directory.CreateTempSubdirectory("c0824-git-proof-").FullName;
        try
        {
            C0824SlotTransportWorld.Git(root, "--version").ShouldStartWith("git version ");
            var failure = Should.Throw<InvalidOperationException>(() => C0824SlotTransportWorld.Git(root, "not-a-git-command-c0824"));
            failure.Message.ShouldContain("not-a-git-command-c0824");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Scratch_git_preserves_caller_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => C0824SlotTransportWorld.RunGit(
            Path.GetTempPath(), ["--version"], cancelled.Token, C0824SlotTransportWorld.GitBudget));
    }

    [Test]
    [Timeout(20_000)]
    public void Scratch_git_accepts_a_start_past_the_retired_one_second_budget()
    {
        // The retired budget was 1s, twice a Linux cold-process maximum of 325ms.
        // Two seconds is past that budget and inside a hang bound.
        RunShell(HoldCommand(2), C0824SlotTransportWorld.GitBudget).ShouldContain("ok");
    }

    [Test]
    [Timeout(40_000)]
    public void Hung_scratch_git_fails_at_the_hang_bound()
    {
        var budget = C0824SlotTransportWorld.GitBudget;
        var watch = Stopwatch.StartNew();
        var failure = Should.Throw<TimeoutException>(() => RunShell(HoldCommand(120), budget));
        failure.Message.ShouldBe("scratch git exceeded its operation deadline");
        watch.Elapsed.ShouldBeLessThan(budget + TimeSpan.FromSeconds(8),
            "a non-returning git fails at the hang bound, inside the 80s class timeout");
        budget.ShouldBeGreaterThan(TimeSpan.FromSeconds(7.2),
            "the bound sits above the loaded Windows rig-start ceiling of 7.2s (run 4e0d1159)");
        budget.ShouldBeLessThan(TimeSpan.FromSeconds(80));
    }

    [Test]
    public void First_scratch_timeout_does_not_fail_the_next_case()
    {
        var calls = 0;
        var store = C0824SlotTransportWorld.CreateStore(() =>
        {
            calls++;
            if (calls == 1)
                throw new TimeoutException("scratch git exceeded its operation deadline");
            return C0824SlotTransportWorld.CreateScratch("recovered");
        });

        var first = store.Value;
        var second = store.Value;
        first.Base.ShouldBe("recovered");
        second.Base.ShouldBe(first.Base);
        calls.ShouldBe(2);
    }

    private static string RunShell(string command, TimeSpan budget)
    {
        var root = Directory.CreateTempSubdirectory("c0824-git-bound-").FullName;
        try
        {
            var file = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
            var args = OperatingSystem.IsWindows()
                ? new[] { "/d", "/c", command }
                : new[] { "-c", command };
            return C0824SlotTransportWorld.RunGit(root, args, CancellationToken.None, budget, file);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string HoldCommand(int seconds) =>
        OperatingSystem.IsWindows()
            ? $"ping -n {seconds + 1} 127.0.0.1 >NUL & echo ok"
            : $"sleep {seconds}; echo ok";

    [Test]
    [NativeLinuxOnly("The pipe-holder witness uses Git's shell alias and a native sleep child on Linux")]
    public void Scratch_git_deadline_includes_inherited_output_drains()
    {
        var root = Directory.CreateTempSubdirectory("c0824-git-drain-").FullName;
        try
        {
            // Direct git and its shell exit; the sleeping descendant still owns redirected pipes.
            // A deadline that only wraps WaitForExit cannot complete this observation.
            var watch = Stopwatch.StartNew();
            Should.Throw<TimeoutException>(() => C0824SlotTransportWorld.RunGit(root,
                ["-c", "alias.hold=!sleep 30 & echo $! > child.pid; exit 0", "hold"],
                CancellationToken.None, TimeSpan.FromSeconds(2)));
            File.Exists(Path.Combine(root, "child.pid")).ShouldBeTrue("the real pipe holder must have started");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the 30-second pipe holder must not escape the operation deadline");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
