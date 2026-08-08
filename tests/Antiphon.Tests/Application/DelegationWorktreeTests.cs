using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The worktree side of a delegated task against REAL git repos: created at dispatch from the
/// merge target, rebased-and-fast-forwarded back on success, conflicts surfaced (never resolved),
/// and the target advanced even when it is checked out somewhere.
///
/// Real repos, not mocks, because every bug this can have lives in git's actual behaviour —
/// "refusing to fetch into checked-out branch" is not something a fake would ever say.
/// </summary>
[Category("Integration")]
public class DelegationWorktreeTests
{
    // ---- creation --------------------------------------------------------------------------

    [Test]
    public async Task a_worktree_is_created_from_the_merge_target_and_recorded_on_the_task()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");

        await service.CreateForTaskAsync(task, CancellationToken.None);

        task.WorktreePath.ShouldNotBeNull();
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        task.WorktreeBranch.ShouldNotBeNull();
        // Branched FROM the target, so the eventual rebase-back is linear.
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "merge-base", "--is-ancestor", "feat/parent", "HEAD"))
            .Ok.ShouldBeTrue("the task branch must start at the merge target");
    }

    [Test]
    public async Task a_leftover_worktree_from_a_previous_attempt_is_adopted_not_an_error()
    {
        // A requeued task (retry, escalation) dispatches again with the same id. Its old worktree
        // holds whatever the last attempt committed — exactly what the handoff wants preserved.
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        var firstPath = task.WorktreePath;

        task.WorktreePath = null;
        task.WorktreeBranch = null;
        await service.CreateForTaskAsync(task, CancellationToken.None);

        task.WorktreePath.ShouldBe(firstPath);
    }

    // ---- merge-back ------------------------------------------------------------------------

    [Test]
    public async Task a_clean_change_lands_on_the_target_and_the_worktree_is_removed()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);

        // The delegate leaves UNCOMMITTED work — normal, not an error; commit-all sweeps it.
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Merged);
        Directory.Exists(task.WorktreePath).ShouldBeFalse("a merged worktree is spent");
        var landed = await repo.GitReadAsync("show", "feat/parent:feature.md");
        landed.ShouldBe("the work\n");
    }

    [Test]
    public async Task the_target_advances_even_while_checked_out_in_the_main_repo()
    {
        // The common real case: the task targets the branch the parent (or the human) is sitting
        // on. `git fetch . branch:target` refuses a checked-out branch, so the fallback must
        // ff-merge inside that checkout — which also updates its working tree, on purpose.
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        var current = (await repo.GitReadAsync("rev-parse", "--abbrev-ref", "HEAD")).Trim();

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: current);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Merged);
        File.Exists(Path.Combine(repo.Path, "feature.md"))
            .ShouldBeTrue("the checked-out target's working tree must show the landed work");
    }

    [Test]
    public async Task a_conflict_aborts_cleanly_and_names_the_files()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("shared.md", "original\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);

        // Both sides rewrite the same line: the delegate in its worktree, the target underneath it.
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "delegate version\n");
        await repo.GitAsync("checkout", "feat/parent");
        await repo.CommitFileAsync("shared.md", "target version\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Conflicted);
        outcome.ConflictFiles.ShouldBe(["shared.md"]);
        // Aborted, not stranded mid-rebase: the worktree must be clean for the Merge delegate.
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim()
            .ShouldBeEmpty("an aborted rebase must leave no half-applied state");
    }

    [Test]
    public async Task no_merge_target_leaves_the_branch_for_a_human()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        Directory.Exists(task.WorktreePath).ShouldBeTrue("the branch is the deliverable here");
        // The uncommitted work was still swept into a commit — a branch of loose files is not
        // reviewable.
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim().ShouldBeEmpty();
    }

    [Test]
    public async Task a_delegate_that_changed_nothing_leaves_no_branch_behind()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.NothingToMerge);
        Directory.Exists(task.WorktreePath).ShouldBeFalse("an empty branch is only clutter");
    }

    // ---- the PreToolUse deny hook ----------------------------------------------------------

    [Test]
    public async Task arming_the_deny_hook_writes_valid_settings_into_the_worktree()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);

        (await service.ArmDenyHookAsync(task, CancellationToken.None)).ShouldBeTrue();

        var settingsPath = Path.Combine(task.WorktreePath!, ".claude", "settings.local.json");
        File.Exists(settingsPath).ShouldBeTrue();
        // Malformed JSON would make Claude Code ignore the file SILENTLY — the guardrail would
        // just not exist, which is the worst failure mode a guardrail can have.
        var parsed = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        parsed.RootElement.GetProperty("hooks").GetProperty("PreToolUse")[0]
            .GetProperty("matcher").GetString().ShouldContain("Edit");
    }

    [Test]
    public async Task the_hook_file_never_reaches_the_merge_target()
    {
        // The merge-back sweeps the worktree with `git add -A`. Without the git exclude, the
        // settings file would land on the parent's branch — a sandbox file escaping its sandbox.
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        (await service.ArmDenyHookAsync(task, CancellationToken.None)).ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Merged);
        (await ScratchGitRepo.GitInAsync(repo.Path, "show", "feat/parent:.claude/settings.local.json"))
            .Ok.ShouldBeFalse("the hook must stay out of the branch history");
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
    }

    [Test]
    public async Task an_existing_settings_file_is_never_clobbered()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);

        var settingsPath = Path.Combine(task.WorktreePath!, ".claude", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "{ \"theirs\": true }");

        (await service.ArmDenyHookAsync(task, CancellationToken.None))
            .ShouldBeFalse("whatever put that file there outranks the hook");
        (await File.ReadAllTextAsync(settingsPath)).ShouldContain("theirs");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static AgentTask NewTask(string repoPath, string? mergeTarget) => new()
    {
        Id = Guid.NewGuid(),
        RootTaskId = Guid.NewGuid(),
        Title = "worktree test task",
        Goal = "test",
        Workspace = WorkspaceMode.Worktree,
        WorkingDirectory = repoPath,
        RepoPath = repoPath,
        MergeTargetRef = mergeTarget,
        CreatedAt = DateTime.UtcNow,
    };

    private static (DelegationWorktreeService Service, WorktreeManager Manager) CreateService(ScratchGitRepo repo)
    {
        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = repo.WorktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var service = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance);
        return (service, manager);
    }

}
