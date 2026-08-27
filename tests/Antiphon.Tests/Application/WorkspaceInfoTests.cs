using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// The workspace switcher's git lookups: porcelain parsing is pinned as a pure function; the
/// repo-root/branch/worktree resolution runs against REAL git repos because path resolution
/// (common dir vs toplevel, worktrees, subdirectories) is exactly where a fake would lie.
/// </summary>
[Category("Unit")]
public class WorktreeListParsingTests
{
    [Test]
    public void Parses_main_branch_locked_and_detached_blocks()
    {
        var porcelain =
            "worktree C:/repo\n" +
            "HEAD 1111111111111111111111111111111111111111\n" +
            "branch refs/heads/master\n" +
            "\n" +
            "worktree C:/wt/card-1\n" +
            "HEAD 2222222222222222222222222222222222222222\n" +
            "branch refs/heads/feat/card-1\n" +
            "locked because reasons\n" +
            "\n" +
            "worktree C:/wt/spike\n" +
            "HEAD 3333333333333333333333333333333333333333\n" +
            "detached\n";

        var entries = GitWorkspaceService.ParseWorktreeList(porcelain);

        entries.Count.ShouldBe(3);
        entries[0].IsMain.ShouldBeTrue();
        entries[0].Branch.ShouldBe("master");
        entries[1].IsMain.ShouldBeFalse();
        entries[1].Branch.ShouldBe("feat/card-1");
        entries[1].IsLocked.ShouldBeTrue();
        entries[2].Branch.ShouldBeNull();
        entries[2].IsDetached.ShouldBeTrue();
    }

    [Test]
    public void A_bare_main_repo_is_skipped_and_marks_no_entry_as_main()
    {
        var porcelain =
            "worktree C:/store/repo.git\n" +
            "bare\n" +
            "\n" +
            "worktree C:/wt/feature\n" +
            "HEAD 4444444444444444444444444444444444444444\n" +
            "branch refs/heads/feature\n";

        var entries = GitWorkspaceService.ParseWorktreeList(porcelain);

        entries.Count.ShouldBe(1);
        entries[0].IsMain.ShouldBeFalse();
        entries[0].Branch.ShouldBe("feature");
    }

    [Test]
    public void Empty_or_failed_output_parses_to_nothing()
    {
        GitWorkspaceService.ParseWorktreeList("").ShouldBeEmpty();
        GitWorkspaceService.ParseWorktreeList("\n\n").ShouldBeEmpty();
    }
}

[Category("GitIntegration")]
public class WorkspaceInfoGitIntegrationTests
{
    [Test]
    public async Task Main_subdir_and_worktree_all_resolve_back_to_the_same_repo_root()
    {
        await SkipIfGitUnavailableAsync();
        using var repo = new ScratchGitRepo("antiphon-wsinfo");
        await repo.CommitFileAsync("readme.md", "hello");
        var worktreePath = Path.Combine(repo.WorktreeRoot, "card-X");
        await repo.GitAsync("worktree", "add", "-b", "feat/card-X", worktreePath, "master");
        var subdir = Path.Combine(repo.Path, "client");
        Directory.CreateDirectory(subdir);

        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);

        var main = await git.GetWorkspaceInfoAsync(repo.Path, CancellationToken.None);
        main.IsGitRepository.ShouldBeTrue();
        SamePath(main.RepoRoot, repo.Path).ShouldBeTrue($"{main.RepoRoot} != {repo.Path}");
        main.Branch.ShouldBe("master");
        main.IsWorktree.ShouldBeFalse();

        var sub = await git.GetWorkspaceInfoAsync(subdir, CancellationToken.None);
        SamePath(sub.RepoRoot, repo.Path).ShouldBeTrue($"{sub.RepoRoot} != {repo.Path}");
        sub.IsWorktree.ShouldBeFalse();

        var worktree = await git.GetWorkspaceInfoAsync(worktreePath, CancellationToken.None);
        worktree.IsGitRepository.ShouldBeTrue();
        SamePath(worktree.RepoRoot, repo.Path).ShouldBeTrue($"{worktree.RepoRoot} != {repo.Path}");
        worktree.Branch.ShouldBe("feat/card-X");
        worktree.IsWorktree.ShouldBeTrue();
    }

    [Test]
    public async Task Worktree_listing_names_the_main_checkout_and_every_branch()
    {
        await SkipIfGitUnavailableAsync();
        using var repo = new ScratchGitRepo("antiphon-wslist");
        await repo.CommitFileAsync("readme.md", "hello");
        var worktreePath = Path.Combine(repo.WorktreeRoot, "card-Y");
        await repo.GitAsync("worktree", "add", "-b", "feat/card-Y", worktreePath, "master");

        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);

        // Listing from the WORKTREE must see the same picture as from the main checkout.
        foreach (var startDir in new[] { repo.Path, worktreePath })
        {
            var entries = await git.ListWorktreesAsync(startDir, CancellationToken.None);
            entries.Count.ShouldBe(2, $"from {startDir}");
            entries[0].IsMain.ShouldBeTrue();
            entries[0].Branch.ShouldBe("master");
            SamePath(entries[0].Path, repo.Path).ShouldBeTrue();
            entries[1].Branch.ShouldBe("feat/card-Y");
            SamePath(entries[1].Path, worktreePath).ShouldBeTrue();
        }
    }

    [Test]
    public async Task Batch_lookup_dedupes_paths_and_degrades_missing_dirs_to_not_a_repo()
    {
        await SkipIfGitUnavailableAsync();
        using var repo = new ScratchGitRepo("antiphon-wsbatch");
        await repo.CommitFileAsync("readme.md", "hello");

        var service = new WorkspaceInfoService(
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-gone-{Guid.NewGuid():N}");

        var infos = await service.GetWorkspacesAsync(
            [repo.Path, repo.Path + "\\", missing], CancellationToken.None);

        infos.Count.ShouldBe(2);
        infos[0].IsGitRepository.ShouldBeTrue();
        infos[0].Branch.ShouldBe("master");
        infos[1].IsGitRepository.ShouldBeFalse();
        infos[1].RepoRoot.ShouldBeNull();
    }

    [Test]
    public async Task Concurrent_cold_workspace_lookups_share_one_git_pair_and_respect_the_process_gate()
    {
        await SkipIfGitUnavailableAsync();
        using var repo = new ScratchGitRepo("antiphon-ws-gate");
        await repo.CommitFileAsync("readme.md", "hello");

        var gate = new GitProcessGate(8);
        var service = new WorkspaceInfoService(
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance, gate));
        var paths = Enumerable.Range(0, 16)
            .Select(index => Path.Combine(repo.Path, $"dir-{index}"))
            .ToList();
        foreach (var path in paths)
            Directory.CreateDirectory(path);

        var infos = await Task.WhenAll(paths.Select(path => service.GetWorkspaceAsync(path, CancellationToken.None)));

        infos.All(info => info.IsGitRepository).ShouldBeTrue();
        gate.PeakInFlight.ShouldBeLessThanOrEqualTo(8);

        service.Clear();
        var started = gate.Started;
        var concurrent = await Task.WhenAll(
            service.GetWorkspacesAsync([repo.Path], CancellationToken.None),
            service.GetWorkspacesAsync([repo.Path], CancellationToken.None));

        concurrent.SelectMany(result => result).All(info => info.IsGitRepository).ShouldBeTrue();
        (gate.Started - started).ShouldBe(2, "one rev-parse plus one branch is shared by both callers");
    }

    private static bool SamePath(string? a, string b) =>
        a is not null && string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private static async Task SkipIfGitUnavailableAsync()
    {
        var probe = await ScratchGitRepo.GitInAsync(Environment.CurrentDirectory, "--version");
        if (!probe.Ok)
            throw new SkipTestException("git is required for workspace info integration tests");
    }
}
