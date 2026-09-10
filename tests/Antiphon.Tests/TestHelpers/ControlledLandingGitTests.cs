using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class ControlledLandingGitTests
{
    [Test]
    public async Task C475_UnknownCommandsAreRejected()
    {
        var git = new ControlledLandingGit();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            git.RunAsync(git.Repository, ["definitely-not-a-git-command"], CancellationToken.None));
        git.NativeProcessStarts.ShouldBe(0);
    }

    [Test]
    public async Task C475_UnsupportedWorktreeOperationsAreRejected()
    {
        var git = new ControlledLandingGit();
        var wt = new LandingProtocolHarness.ControlledWorktreeManager(git);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            wt.CreateAsync(git.Repository, "card", "master", CancellationToken.None));
    }

    [Test]
    public async Task C475_SourceTargetRemoteAndPinsAreIndependent()
    {
        var git = new ControlledLandingGit();
        var beforeTarget = git.TargetHead;
        var beforeRemote = git.RemoteTarget;
        await git.RequiredAsync(git.Source, "commit", "--allow-empty", "-m", "advance source");
        git.SourceHead.ShouldNotBe(beforeTarget);
        git.TargetHead.ShouldBe(beforeTarget);
        git.RemoteTarget.ShouldBe(beforeRemote);
        git.RemoteSource.ShouldBe(git.SeedSha);
    }

    [Test]
    public async Task C475_QueryErrorsAreNotAbsence()
    {
        var git = new ControlledLandingGit();
        git.SetShowRefExistsError(128);
        var result = await git.RunAsync(git.Repository, ["show-ref", "--exists", "refs/heads/missing"], CancellationToken.None);
        result.ExitCode.ShouldBe(128);
        result.ExitCode.ShouldNotBe(2);
    }

    [Test]
    public async Task C475_RunOwnedAwaitsTheStartedCallback()
    {
        var git = new ControlledLandingGit();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var running = git.RunOwnedAsync(git.Source, ["commit", "--allow-empty", "-m", "x"],
            async (_, _, _) => { calls++; await gate.Task; }, CancellationToken.None);
        await Task.Delay(50);
        running.IsCompleted.ShouldBeFalse();
        git.SourceHead.ShouldBe(git.SeedSha);
        calls.ShouldBe(1);
        gate.SetResult();
        (await running).Succeeded.ShouldBeTrue();
        git.SourceHead.ShouldNotBe(git.SeedSha);
    }

    [Test]
    public async Task C475_PushOwnedAwaitsTheStartedCallback()
    {
        var git = new ControlledLandingGit();
        var dest = await git.DestinationAsync(git.Repository, git.TargetRef, CancellationToken.None);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var before = git.RemoteTarget;
        var running = git.PushOwnedAsync(git.Repository, dest, git.SeedSha,
            async (_, _, _) => { calls++; await gate.Task; }, CancellationToken.None);
        await Task.Delay(50);
        running.IsCompleted.ShouldBeFalse();
        git.RemoteTarget.ShouldBe(before);
        calls.ShouldBe(1);
        gate.SetResult();
        (await running).Succeeded.ShouldBeTrue();
    }
}
