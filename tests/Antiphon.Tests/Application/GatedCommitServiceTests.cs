using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GatedCommitServiceTests
{
    private readonly Guid _taskId = Guid.NewGuid();
    private Dictionary<string, string> Trailers => new()
    { ["antiphon"] = "true", ["antiphon-task"] = _taskId.ToString(), ["antiphon-commit"] = "gated" };

    private static async Task<ScratchGitRepo> Repo()
    {
        var repo = new ScratchGitRepo("c527-gate");
        await repo.CommitFileAsync(".gitignore", "_private/\n*.secret\n");
        await repo.CommitFileAsync("tracked.txt", "original");
        Directory.CreateDirectory(Path.Combine(repo.Path, "_private"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "_private/verbatim.txt"), "ignored");
        return repo;
    }

    private Task<GatedCommitResult> Commit(ScratchGitRepo repo, string[]? paths, RecordingGitWorkspaceService? spy = null)
    {
        var git = new LandingGit();
        return new GatedCommitService(spy ?? new(), new RepositoryMutationLease(git), git)
            .CommitAsync(repo.Path, paths, "task test: subject\n\nreport body", Trailers, default);
    }

    private static Task Write(ScratchGitRepo repo, string path, string text = "new") =>
        File.WriteAllTextAsync(Path.Combine(repo.Path, path), text);

    [Test]
    public async Task Case2_tracked_then_ignored_file_is_refused_IgnoredPathStaged()
    {
        using var repo = await Repo();
        await repo.CommitFileAsync(".gitignore", "_private/\n*.secret\ntracked.txt\n");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        await Write(repo, "tracked.txt"); await Write(repo, "new.txt");
        var result = await Commit(repo, ["tracked.txt", "new.txt"]);
        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals!.Single().ShouldBe(new GitWorkspaceService.IgnoredPath("tracked.txt", ".gitignore:3:tracked.txt"));
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    public async Task Case3_deleted_gitignore_is_refused_IgnoreRulesChanged_and_nothing_is_staged()
    {
        using var repo = await Repo(); var spy = new RecordingGitWorkspaceService();
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        File.Delete(Path.Combine(repo.Path, ".gitignore")); await Write(repo, "new.txt");
        foreach (var paths in new string[]?[] { null, ["new.txt"] })
        {
            var result = await Commit(repo, paths, spy);
            result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged);
            result.Refusals!.Select(r => r.Path).ShouldContain(".gitignore");
        }
        spy.Verbs.ShouldNotContain("add");
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    [Arguments("docs/.gitignore", true)]
    [Arguments("sub/deep/.gitignore", false)]
    [Arguments(".gitignore", true)]
    public async Task Case3b_modified_nested_or_new_gitignore_is_refused(string path, bool tracked)
    {
        using var repo = await Repo(); var spy = new RecordingGitWorkspaceService();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Path, path))!);
        if (tracked && path != ".gitignore") await repo.CommitFileAsync(path, "old\n");
        await Write(repo, path, "changed\n"); await Write(repo, "new.txt");
        var result = await Commit(repo, ["new.txt"], spy);
        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged);
        result.Refusals!.Select(r => r.Path).ShouldContain(path);
        spy.Verbs.ShouldNotContain("add");
    }

    [Test]
    public async Task Case4_force_added_ignored_file_is_refused_naming_path_and_rule()
    {
        using var repo = await Repo();
        await Write(repo, "a.secret"); await repo.GitAsync("add", "-f", "a.secret");
        await repo.GitAsync("commit", "-m", "tracked secret");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        await Write(repo, "a.secret", "modified"); await Write(repo, "new.txt");
        var result = await Commit(repo, ["a.secret", "new.txt"]);
        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals!.Single().ShouldBe(new GitWorkspaceService.IgnoredPath("a.secret", ".gitignore:2:*.secret"));
        (await repo.GitReadAsync("status", "--porcelain")).ShouldContain("?? new.txt");
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    public async Task Case1_new_file_beside_ignored_untracked_commits_only_the_new_file()
    {
        using var repo = await Repo(); await Write(repo, "_private/more.txt"); await Write(repo, "new.txt");
        var result = await Commit(repo, null);
        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        result.Files.ShouldBe(["new.txt"]);
        (await repo.GitReadAsync("ls-files")).ShouldNotContain("more.txt");
        File.Exists(Path.Combine(repo.Path, "_private/more.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task Scoped_commit_leaves_a_foreign_staged_path_staged_and_out_of_the_commit()
    {
        using var repo = await Repo(); await Write(repo, "foo.txt"); await repo.GitAsync("add", "foo.txt"); await Write(repo, "bar.txt");
        var result = await Commit(repo, ["bar.txt"]);
        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var committedPaths = result.Files!;
        committedPaths.ShouldNotContain("foo.txt"); committedPaths.ShouldBe(["bar.txt"]);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("foo.txt");
    }

    [Test]
    public async Task Held_lease_is_RepositoryBusy_and_nothing_is_staged()
    {
        using var repo = await Repo(); await Write(repo, "new.txt");
        var git = new LandingGit(); var lease = new RepositoryMutationLease(git); var spy = new RecordingGitWorkspaceService();
        await using var held = await lease.TryAcquireAsync(repo.Path, default); held.ShouldNotBeNull();
        var result = await new GatedCommitService(spy, lease, git).CommitAsync(repo.Path, ["new.txt"], "message", Trailers, default);
        result.Outcome.ShouldBe(GatedCommitOutcome.RepositoryBusy); spy.Verbs.ShouldNotContain("add");
    }

    [Test]
    public async Task Trailers_name_the_task_and_the_gate()
    {
        using var repo = await Repo(); await Write(repo, "new.txt");
        (await Commit(repo, ["new.txt"])).Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var body = await repo.GitReadAsync("log", "-1", "--format=%B");
        body.ShouldStartWith("task test: subject\n\nreport body\n");
        body.ShouldContain("antiphon: true"); body.ShouldContain($"antiphon-task: {_taskId}"); body.ShouldContain("antiphon-commit: gated");
    }

    [Test]
    public async Task Nothing_is_ever_pushed()
    {
        using var repo = await Repo(); await repo.AddBareOriginAsync(); var spy = new RecordingGitWorkspaceService();
        var upstream = await repo.GitReadAsync("rev-parse", "origin/master"); await Write(repo, "new.txt");
        (await Commit(repo, ["new.txt"], spy)).Outcome.ShouldBe(GatedCommitOutcome.Committed);
        spy.Verbs.ShouldContain("commit"); spy.Verbs.ShouldNotContain("push");
        (await repo.GitReadAsync("rev-parse", "origin/master")).ShouldBe(upstream);
    }

    [Test]
    public async Task Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index()
    {
        using var repo = await Repo(); await Write(repo, "new.txt"); await Write(repo, "late.secret");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var spy = new RecordingGitWorkspaceService { BeforeRun = async args =>
        { if (args[0] == "add") await repo.GitAsync("add", "-f", "late.secret"); } };
        var result = await Commit(repo, null, spy);
        result.Outcome.ShouldBe(GatedCommitOutcome.IgnoredPathStaged);
        result.Refusals!.Select(r => r.Path).ShouldContain("late.secret");
        var stagedAfter = await repo.GitReadAsync("diff", "--cached", "--name-only"); stagedAfter.ShouldBeEmpty();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    public async Task Scoped_commit_with_a_deletion_records_the_deletion()
    {
        using var repo = await Repo(); File.Delete(Path.Combine(repo.Path, "tracked.txt"));
        (await Commit(repo, ["tracked.txt"])).Outcome.ShouldBe(GatedCommitOutcome.Committed);
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-status", "-r", "HEAD")).Trim().ShouldBe("D\ttracked.txt");
    }

    [Test]
    public async Task Failing_hook_returns_CommitFailed_and_leaves_the_index_staged()
    {
        using var repo = await Repo(); await repo.InstallFailingPreCommitHookAsync("gate says no"); await Write(repo, "new.txt");
        var result = await Commit(repo, ["new.txt"]);
        result.Outcome.ShouldBe(GatedCommitOutcome.CommitFailed); result.Stderr.ShouldContain("gate says no");
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("new.txt");
    }

    [Test]
    public async Task Clean_tree_returns_NothingToCommit()
    {
        using var repo = await Repo(); (await Commit(repo, null)).Outcome.ShouldBe(GatedCommitOutcome.NothingToCommit);
    }
}
