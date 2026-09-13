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
        if (kind != "not-configured")
        {
            git.Trace.Any(a => a.Length >= 4 && a[0] == "ls-remote" && a.Contains("--refs") && a.Contains("--exit-code"))
                .ShouldBeTrue();
        }
        git.Trace.Any(a => a.Any(x => x.Contains("origin/", StringComparison.Ordinal))).ShouldBeFalse();
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
        (await ScratchGitRepo.GitInAsync(repo.WorktreeRoot, "clone", "--branch", "feat/x", remote, clone)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(clone, "n.md"), "n\n");
        (await ScratchGitRepo.GitInAsync(clone, "add", "n.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "commit", "-m", "n")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "push", "origin", "HEAD")).Ok.ShouldBeTrue();
        var observed = await git.ObserveExactRefAsync(repo.Path, "refs/heads/feat/x", null, Guid.NewGuid(), default);
        observed.State.ShouldBe(ProgressRemoteState.Unavailable);
        observed.Reason.ShouldBe("repository_lease_busy");
        git.Trace.Any(a => a.Length > 0 && a[0] == "fetch").ShouldBeFalse();
        git.Trace.Any(a => a.Length > 0 && a[0] == "update-ref").ShouldBeFalse();
    }

    [Test]
    public async Task C499_V33_FetchIntoTaskObservationRefAndAnswerAncestry()
    {
        using var repo = new ScratchGitRepo("c499-v33");
        await repo.CommitFileAsync("README.md", "base\n");
        var remote = Path.Combine(repo.WorktreeRoot, "remote.git");
        Directory.CreateDirectory(remote);
        (await ScratchGitRepo.GitInAsync(remote, "init", "--bare")).Ok.ShouldBeTrue();
        await repo.GitAsync("remote", "add", "origin", remote);
        await repo.GitAsync("branch", "feat/x");
        await repo.GitAsync("push", "origin", "feat/x");
        var bl = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "refs/heads/feat/x")).StdOut.Trim();
        var taskId = Guid.NewGuid();
        var leases = new RepositoryMutationLease(new LandingGit());
        var git = new ControlledTaskProgressGit(leases);
        (await git.PinBaselineAsync(repo.Path, taskId, "local", bl, default)).Succeeded.ShouldBeTrue();
        (await git.PinBaselineAsync(repo.Path, taskId, "remote", bl, default)).Succeeded.ShouldBeTrue();
        var fetchHeadBefore = File.Exists(Path.Combine(repo.Path, ".git", "FETCH_HEAD"))
            ? await File.ReadAllTextAsync(Path.Combine(repo.Path, ".git", "FETCH_HEAD")) : "";
        var headsBefore = (await ScratchGitRepo.GitInAsync(repo.Path, "for-each-ref", "refs/heads")).StdOut;

        var clone = Path.Combine(repo.WorktreeRoot, "c");
        (await ScratchGitRepo.GitInAsync(repo.WorktreeRoot, "clone", "--branch", "feat/x", remote, clone)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(clone, "n.md"), "n\n");
        (await ScratchGitRepo.GitInAsync(clone, "add", "n.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "commit", "-m", "n")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "push", "origin", "HEAD")).Ok.ShouldBeTrue();
        var c = (await ScratchGitRepo.GitInAsync(clone, "rev-parse", "HEAD")).StdOut.Trim();

        var observed = await git.ObserveExactRefAsync(repo.Path, "refs/heads/feat/x", null, taskId, default);
        observed.State.ShouldBe(ProgressRemoteState.Present);
        observed.Sha.ShouldBe(c);
        (await git.IsAncestorAsync(repo.Path, c, c, default)).ShouldBe(true);
        (await git.IsAncestorAsync(repo.Path, c, bl, default)).ShouldBe(false);
        git.Trace.Any(a => a.Length >= 3 && a[0] == "fetch" && a.Contains("--no-tags") && a.Contains("--no-write-fetch-head")
            && a.Any(x => x.Contains($"{TaskProgressGit.ProgressRefPrefix}{taskId:N}/", StringComparison.Ordinal)))
            .ShouldBeTrue();
        var fetchHeadAfter = File.Exists(Path.Combine(repo.Path, ".git", "FETCH_HEAD"))
            ? await File.ReadAllTextAsync(Path.Combine(repo.Path, ".git", "FETCH_HEAD")) : "";
        fetchHeadAfter.ShouldBe(fetchHeadBefore);
        (await ScratchGitRepo.GitInAsync(repo.Path, "for-each-ref", "refs/heads")).StdOut.ShouldBe(headsBefore);
        var pins = await git.ListProgressPinsAsync(repo.Path, taskId, default);
        pins.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    [Arguments("once")]
    [Arguments("always")]
    public async Task C499_V34_ObservationRaceIsRetriedThenIndeterminate(string mode)
    {
        using var repo = new ScratchGitRepo("c499-v34");
        await repo.CommitFileAsync("README.md", "base\n");
        var remote = Path.Combine(repo.WorktreeRoot, "remote.git");
        Directory.CreateDirectory(remote);
        (await ScratchGitRepo.GitInAsync(remote, "init", "--bare")).Ok.ShouldBeTrue();
        await repo.GitAsync("remote", "add", "origin", remote);
        await repo.GitAsync("branch", "feat/x");
        await repo.GitAsync("push", "origin", "feat/x");
        var clone = Path.Combine(repo.WorktreeRoot, "c");
        (await ScratchGitRepo.GitInAsync(repo.WorktreeRoot, "clone", "--branch", "feat/x", remote, clone)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(clone, "n.md"), "n\n");
        (await ScratchGitRepo.GitInAsync(clone, "add", "n.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "commit", "-m", "n")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "push", "origin", "HEAD")).Ok.ShouldBeTrue();

        var fetches = 0;
        var git = new ControlledTaskProgressGit(new RepositoryMutationLease(new LandingGit()));
        git.BeforeCommand = (_, args) =>
        {
            if (args.Count == 0 || args[0] != "fetch")
                return Task.FromResult<LandingGitResult?>(null);
            fetches++;
            if (mode == "always" || fetches == 1)
                return Task.FromResult<LandingGitResult?>(new(0, "", ""));
            return Task.FromResult<LandingGitResult?>(null);
        };
        var observed = await git.ObserveExactRefAsync(repo.Path, "refs/heads/feat/x", null, Guid.NewGuid(), default);
        if (mode == "once")
            observed.State.ShouldBe(ProgressRemoteState.Present);
        else
        {
            observed.State.ShouldBe(ProgressRemoteState.Unavailable);
            observed.Reason.ShouldBe("changed_during_confirmation");
            fetches.ShouldBe(3);
        }
    }
}
