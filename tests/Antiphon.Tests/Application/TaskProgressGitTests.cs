using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class TaskProgressGitTests
{
    [Test]
    [Arguments("present")]
    [Arguments("missing")]
    [Arguments("not-configured")]
    public async Task C499_V32_ObserveExactRefAtCapturedEndpoint(string kind)
    {
        using var repo = new ScratchGitRepo("c499-v32");
        await repo.CommitFileAsync("README.md", "base\n");
        var remote = Path.Combine(repo.WorktreeRoot, "remote.git");
        Directory.CreateDirectory(remote);
        (await ScratchGitRepo.GitInAsync(remote, "init", "--bare")).Ok.ShouldBeTrue();
        await repo.GitAsync("remote", "add", "origin", remote);
        await repo.GitAsync("push", "origin", "master");
        var git = new ControlledTaskProgressGit();
        var full = "refs/heads/master";
        if (kind == "missing")
            (await ScratchGitRepo.GitInAsync(remote, "update-ref", "-d", full)).Ok.ShouldBeTrue();
        if (kind == "not-configured")
            await repo.GitAsync("remote", "remove", "origin");
        var observed = await git.ObserveExactRefAsync(repo.Path, full, null, Guid.NewGuid(), default);
        if (kind == "present")
        {
            var sha = (await ScratchGitRepo.GitInAsync(remote, "rev-parse", full)).StdOut.Trim();
            observed.State.ShouldBe(ProgressRemoteState.Present);
            observed.Sha.ShouldBe(sha);
        }
        else if (kind == "missing")
            observed.State.ShouldBe(ProgressRemoteState.Missing);
        else
            observed.State.ShouldBe(ProgressRemoteState.NotConfigured);
        git.Trace.ShouldContain(a => a.Length >= 4 && a[0] == "ls-remote" && a.Contains("--refs") && a.Contains("--exit-code"));
        git.Trace.ShouldNotContain(a => a.Any(x => x.Contains("origin/", StringComparison.Ordinal)));
    }

    [Test]
    public async Task C499_V35_StrictStatusAndLogResultsSurfaceFailures()
    {
        var broken = Directory.CreateTempSubdirectory("c499-v35").FullName;
        await File.WriteAllTextAsync(Path.Combine(broken, ".git"), "gitdir: " + Path.Combine(broken, "missing.git") + "\n");
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);
        var status = await git.TryGetChangesAsync(broken, default);
        var log = await git.TryGetRecentCommitsAsync(broken, 50, default);
        status.Succeeded.ShouldBeFalse();
        log.Succeeded.ShouldBeFalse();
        (await git.GetChangesAsync(broken, default)).ShouldBeEmpty();
        (await git.GetRecentCommitsAsync(broken, 50, default)).ShouldBeEmpty();
        Directory.Delete(broken, true);
    }

    [Test]
    public async Task C499_R09b_UnreadableRemoteIsUnavailableNotMissing()
    {
        using var repo = new ScratchGitRepo("c499-r09b");
        await repo.CommitFileAsync("README.md", "base\n");
        var remote = Path.Combine(repo.WorktreeRoot, "remote.git");
        Directory.CreateDirectory(remote);
        (await ScratchGitRepo.GitInAsync(remote, "init", "--bare")).Ok.ShouldBeTrue();
        await repo.GitAsync("remote", "add", "origin", remote);
        await repo.GitAsync("push", "origin", "master");
        await repo.GitAsync("remote", "set-url", "origin", Path.Combine(repo.WorktreeRoot, "does-not-exist.git"));
        var git = new ControlledTaskProgressGit();
        var observed = await git.ObserveExactRefAsync(repo.Path, "refs/heads/master", null, Guid.NewGuid(), default);
        observed.State.ShouldBe(ProgressRemoteState.Unavailable);
        observed.Reason.ShouldBe("source_remote_unreadable");
    }

    [Test]
    public async Task C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch()
    {
        using var repo = new ScratchGitRepo("c499-r16");
        await repo.CommitFileAsync("README.md", "base\n");
        var remote = Path.Combine(repo.WorktreeRoot, "remote.git");
        Directory.CreateDirectory(remote);
        (await ScratchGitRepo.GitInAsync(remote, "init", "--bare")).Ok.ShouldBeTrue();
        await repo.GitAsync("remote", "add", "origin", remote);
        await repo.GitAsync("branch", "feat/x");
        await repo.GitAsync("push", "origin", "feat/x");
        var leases = new RepositoryMutationLease(new LandingGit());
        await using var held = await leases.TryAcquireAsync(repo.Path, default);
        held.ShouldNotBeNull();
        var git = new ControlledTaskProgressGit(leases);
        // Force a fetch by using a SHA the local repo does not have: push a new commit only to remote via clone.
        var clone = Path.Combine(repo.WorktreeRoot, "c");
        (await ScratchGitRepo.GitInAsync(repo.WorktreeRoot, "clone", remote, clone)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(clone, "n.md"), "n\n");
        (await ScratchGitRepo.GitInAsync(clone, "add", "n.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "commit", "-m", "n")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "push", "origin", "feat/x")).Ok.ShouldBeTrue();
        var observed = await git.ObserveExactRefAsync(repo.Path, "refs/heads/feat/x", null, Guid.NewGuid(), default);
        observed.State.ShouldBe(ProgressRemoteState.Unavailable);
        observed.Reason.ShouldBe("repository_lease_busy");
        git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "fetch");
        git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "update-ref");
    }
}
