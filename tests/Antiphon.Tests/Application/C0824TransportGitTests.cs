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
