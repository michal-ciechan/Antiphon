using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0688 V-1: the persistent, locked, detached land worktree (real git).</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandWorkspaceTests
{
    [Test]
    public async Task C688_V01a_EnsureCreatesALockedDetachedWorktreeUnderTheManagedRoot()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        Path.GetDirectoryName(path).ShouldBe(Path.Combine(s.Trees, "land"));
        Path.GetFileName(path).Length.ShouldBe(12);
        Path.GetFileName(path).ShouldAllBe(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
        s.Workspace.PathFor(s.Common).ShouldBe(path, "the path is a pure function of the common directory");

        var state = await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None);

        state.Reason.ShouldBeNull(state.Detail);
        state.Created.ShouldBeTrue();
        state.HeadSha.ShouldBe(s.F.SeedSha);
        File.Exists(Path.Combine(path, ".git")).ShouldBeTrue("a linked worktree has a .git file");
        (await s.HeadAsync(path)).ShouldBe(s.F.SeedSha);
        (await s.F.Git.RunAsync(path, ["symbolic-ref", "-q", "HEAD"], CancellationToken.None)).ExitCode.ShouldBe(1, "detached");
        File.Exists(Path.Combine(await s.AdminAsync(path), "locked")).ShouldBeTrue("git worktree prune must never take it");
        (await s.RegistrationsForAsync(path)).ShouldBe(1);
    }

    [Test]
    public async Task C688_V01b_SecondEnsureResetsInPlace()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        (await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        await s.F.RequiredAsync(s.F.Repository, "commit", "--allow-empty", "-m", "next");
        var next = (await s.F.RequiredAsync(s.F.Repository, "rev-parse", "HEAD")).Trim();

        var state = await s.Workspace.EnsureAsync(s.F.Repository, path, next, null, CancellationToken.None);

        state.Reason.ShouldBeNull(state.Detail);
        state.Created.ShouldBeFalse();
        (await s.HeadAsync(path)).ShouldBe(next);
        (await s.F.RequiredAsync(path, "status", "--porcelain=v1", "--untracked-files=all")).ShouldBeEmpty();
        (await s.RegistrationsForAsync(path)).ShouldBe(1, "a reset never adds a second registration");
    }

    [Test]
    public async Task C688_V01c_LeftoverRebaseAndEditsAreCleared()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        (await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        // A real interrupted rebase: the target changes keep.txt one way, the land worktree another.
        await File.WriteAllTextAsync(Path.Combine(s.F.Repository, "keep.txt"), "target side\n");
        await s.F.RequiredAsync(s.F.Repository, "commit", "-am", "target edit");
        var target = (await s.F.RequiredAsync(s.F.Repository, "rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(path, "keep.txt"), "land side\n");
        await s.F.RequiredAsync(path, "commit", "-am", "land edit");
        var rebase = await s.F.Git.RunAsync(path, ["rebase", target], CancellationToken.None);
        rebase.Succeeded.ShouldBeFalse("the fixture needs a conflicted rebase");
        var admin = await s.AdminAsync(path);
        Directory.Exists(Path.Combine(admin, "rebase-merge")).ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(path, "fixture-owner.txt"), "modified tracked\n");
        await File.WriteAllTextAsync(Path.Combine(path, "untracked.txt"), "untracked\n");
        Directory.CreateDirectory(Path.Combine(path, "bin-junk"));
        await File.WriteAllTextAsync(Path.Combine(path, "bin-junk", "ignored.txt"), "ignored\n");
        s.F.Git.Trace.Clear();

        var state = await s.Workspace.EnsureAsync(s.F.Repository, path, target, null, CancellationToken.None);

        state.Reason.ShouldBeNull(state.Detail);
        Directory.Exists(Path.Combine(admin, "rebase-merge")).ShouldBeFalse();
        (await s.HeadAsync(path)).ShouldBe(target);
        (await s.F.RequiredAsync(path, "status", "--porcelain=v1", "--untracked-files=all", "--ignored")).ShouldBeEmpty();
        var abort = s.F.Git.Trace.FindIndex(a => a.Contains("rebase") && a.Contains("--abort"));
        var reset = s.F.Git.Trace.FindIndex(a => a.Contains("reset") && a.Contains("--hard"));
        abort.ShouldBeGreaterThanOrEqualTo(0);
        reset.ShouldBeGreaterThan(abort, "the sequencer is aborted before the reset");
        s.F.Git.Trace.ShouldContain(a => a.Contains("clean") && a.Contains("-fdx"));
    }

    [Test]
    public async Task C688_V01d_DeletedDirectoryHealsWithoutPrune()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        (await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        Directory.Delete(path, true);
        s.F.Git.Trace.Clear();

        var state = await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None);

        state.Reason.ShouldBeNull(state.Detail);
        state.Created.ShouldBeTrue();
        (await s.HeadAsync(path)).ShouldBe(s.F.SeedSha);
        s.F.Git.Trace.ShouldContain(a => a.Length >= 6 && a[0] == "worktree" && a[1] == "add" && a.Contains("--detach")
            && a.Count(x => x == "--force") == 2);
        s.F.Git.Trace.ShouldNotContain(a => a.Contains("prune"));
        (await s.RegistrationsForAsync(path)).ShouldBe(1);
        File.Exists(Path.Combine(await s.AdminAsync(path), "locked")).ShouldBeTrue();
    }

    [Test]
    public async Task C688_V01e_ManagerListingScanAndPruneIgnoreTheLandWorktree()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        (await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        var manager = new WorktreeManager(Options.Create(s.Settings), TimeProvider.System, NullLogger<WorktreeManager>.Instance);

        (await manager.ListAsync(s.F.Repository, CancellationToken.None))
            .ShouldNotContain(w => LandingGit.PathsEqual(w.Path, path));
        (await manager.ScanDelegateWorktreesAsync(s.F.Repository, CancellationToken.None))
            .ShouldNotContain(w => w.Path.Length > 0 && LandingGit.PathsEqual(w.Path, path));
        await manager.PruneStaleAsync(CancellationToken.None);

        Directory.Exists(path).ShouldBeTrue();
        (await s.RegistrationsForAsync(path)).ShouldBe(1);
        (await s.F.Git.RunAsync(s.F.Repository, ["worktree", "prune"], CancellationToken.None)).Succeeded.ShouldBeTrue();
        (await s.RegistrationsForAsync(path)).ShouldBe(1, "a locked registration survives git worktree prune");
    }

    [Test]
    public async Task C688_V01f_LiveIndexLockRefusesWithTheLockCode()
    {
        await using var s = await Scene.CreateAsync();
        var path = s.Workspace.PathFor(s.Common);
        (await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        var lockPath = (await s.F.RequiredAsync(path, "rev-parse", "--path-format=absolute", "--git-path", "index.lock")).Trim();
        await File.WriteAllBytesAsync(lockPath, []);
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow - TimeSpan.FromHours(1));
        s.F.Git.Trace.Clear();

        var state = await s.Workspace.EnsureAsync(s.F.Repository, path, s.F.SeedSha, null, CancellationToken.None);

        state.Reason.ShouldBe(GitIndexLock.StaleCode);
        s.F.Git.Trace.ShouldNotContain(a => a.Contains("reset") || a.Contains("clean"));
        File.Exists(lockPath).ShouldBeTrue("a refusal never deletes the lock");
        File.Delete(lockPath);
    }

    private sealed class Scene : IAsyncDisposable
    {
        public LandingGitFixture F { get; } = new();
        public string Trees => Path.Combine(F.Root, "trees");
        public GitSettings Settings => new() { WorktreeBasePath = Trees };
        public LandWorkspace Workspace { get; private set; } = null!;
        public string Common { get; private set; } = "";

        public static async Task<Scene> CreateAsync()
        {
            var s = new Scene();
            await s.F.InitializeAsync();
            s.Workspace = new LandWorkspace(s.F.Git, Options.Create(s.Settings), TimeProvider.System);
            s.Common = await s.F.Git.CommonDirectoryAsync(s.F.Repository, CancellationToken.None);
            return s;
        }

        public async Task<string> HeadAsync(string path) => (await F.RequiredAsync(path, "rev-parse", "HEAD")).Trim();

        public async Task<string> AdminAsync(string path) =>
            await F.Git.CanonicalDirectoryAsync((await F.RequiredAsync(path, "rev-parse", "--absolute-git-dir")).Trim(), CancellationToken.None);

        public async Task<int> RegistrationsForAsync(string path)
        {
            var rows = LandingGit.ParseRegistrations(await F.RequiredAsync(F.Repository, "worktree", "list", "--porcelain", "-z"));
            return rows.Count(r => LandingGit.PathsEqual(r.Path, path));
        }

        public async ValueTask DisposeAsync()
        {
            var land = Path.Combine(Trees, "land");
            if (Directory.Exists(land))
                foreach (var dir in Directory.EnumerateDirectories(land))
                    await F.Git.RunAsync(F.Repository, ["worktree", "unlock", dir], CancellationToken.None);
            await F.DisposeAsync();
        }
    }
}
