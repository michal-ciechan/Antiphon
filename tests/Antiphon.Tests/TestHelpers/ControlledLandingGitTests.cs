using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class ControlledLandingGitTests
{
    [Test]
    [Arguments("")]
    [Arguments("stash")]
    [Arguments("stash pop --unexpected")]
    [Arguments("config --global user.name changed")]
    [Arguments("branch -D master")]
    [Arguments("commit-tree HEAD")]
    [Arguments("check-ref-format --unexpected refs/heads/master")]
    [Arguments("remote --unexpected get-url origin")]
    [Arguments("ls-remote --unexpected origin")]
    [Arguments("rev-parse --unexpected HEAD")]
    [Arguments("rev-parse --absolute-git-dir extra")]
    [Arguments("rev-parse --verify")]
    [Arguments("symbolic-ref --unexpected HEAD")]
    [Arguments("show-ref --exists refs/heads/master extra")]
    [Arguments("status --unexpected")]
    [Arguments("status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none extra")]
    [Arguments("ls-files --unexpected")]
    [Arguments("worktree list --porcelain -z extra")]
    [Arguments("worktree remove --force -- SOURCE")]
    [Arguments("worktree lock SOURCE extra")]
    [Arguments("worktree lock OTHER")]
    [Arguments("fetch --force ENDPOINT refs/heads/master:refs/test/pin")]
    [Arguments("fetch --no-tags --no-write-fetch-head ENDPOINT refs/heads/master:refs/test/pin extra")]
    [Arguments("fetch --no-tags --no-write-fetch-head ENDPOINT refs/heads/master:--bad")]
    [Arguments("merge-base --unexpected --is-ancestor OID OID")]
    [Arguments("update-ref refs/heads/master OID OID extra")]
    [Arguments("update-ref --no-deref -d refs/heads/master OID extra")]
    [Arguments("rebase --abort extra")]
    [Arguments("-c rebase.autoStash=true -c rebase.updateRefs=false rebase OID")]
    [Arguments("nonsense rebase OID")]
    [Arguments("-c merge.autoStash=false merge --ff-only OID extra")]
    [Arguments("nonsense merge --ff-only OID")]
    [Arguments("diff --cached --unexpected")]
    [Arguments("commit --amend -m changed")]
    [Arguments("commit --allow-empty -m changed extra")]
    [Arguments("checkout --force -b changed")]
    [Arguments("add --unexpected keep.txt")]
    [Arguments("add ../outside.txt")]
    [Arguments("push --force ENDPOINT OID:refs/heads/master")]
    [Arguments("push ENDPOINT missing-colon")]
    public async Task C475_UnsupportedArgumentsCannotSucceedOrMutate(string command)
    {
        var git = new ControlledLandingGit();
        try
        {
            var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Replace("SOURCE", git.Source).Replace("OTHER", git.Repository)
                    .Replace("ENDPOINT", git.Remote.Replace('\\', '/')).Replace("OID", git.SeedSha)).ToArray();
            var callbacks = 0;
            git.BeforeCommand = (_, _) =>
            {
                callbacks++;
                return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(new(0, "", ""));
            };
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                git.RunOwnedAsync(git.Repository, args,
                    (_, _, _) => { callbacks++; return Task.CompletedTask; }, CancellationToken.None));
            error.Message.ShouldStartWith("unsupported controlled git command:");
            callbacks.ShouldBe(0);
            git.Trace.ShouldBeEmpty();
            git.OwnedTrace.ShouldBeEmpty();
            git.SourceHead.ShouldBe(git.SeedSha);
            git.TargetHead.ShouldBe(git.SeedSha);
            git.RemoteTarget.ShouldBe(git.SeedSha);
            Directory.Exists(git.Source).ShouldBeTrue();
            git.NativeProcessStarts.ShouldBe(0);
        }
        finally { Directory.Delete(git.Root, true); }
    }

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
