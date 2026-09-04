using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        task.WorktreeBaseSha.ShouldNotBeNull();
        task.WorktreeBaseSha!.Length.ShouldBeGreaterThanOrEqualTo(7);
        // Branched FROM the target, so the eventual rebase-back is linear.
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "merge-base", "--is-ancestor", "feat/parent", "HEAD"))
            .Ok.ShouldBeTrue("the task branch must start at the merge target");
    }

    [Test]
    public async Task two_top_level_worktree_tasks_on_one_card_both_branch_from_repo_head()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        var masterHead = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        var (service, _) = CreateService(repo);
        var cardId = Guid.NewGuid();
        var first = NewTask(repo.Path, mergeTarget: null);
        first.CardId = cardId;
        var second = NewTask(repo.Path, mergeTarget: null);
        second.CardId = cardId;

        await service.CreateForTaskAsync(first, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "plan.md"), "the plan\n");
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "add", "plan.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "commit", "-m", "docs(plan): CARD-0215"))
            .Ok.ShouldBeTrue();
        var firstCommit = (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();
        firstCommit.ShouldNotBe(masterHead);

        await service.CreateForTaskAsync(second, CancellationToken.None);
        (await ScratchGitRepo.GitInAsync(second.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(masterHead);
        (await ScratchGitRepo.GitInAsync(
            second.WorktreePath!, "merge-base", "--is-ancestor", firstCommit, "HEAD"))
            .Ok.ShouldBeFalse("a sibling Worktree task branches from HEAD, never from the first task's commit");
    }

    [Test]
    public async Task a_locked_registration_whose_directory_is_gone_is_healed_and_the_task_dispatches()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        var (branch, worktreePath) = ExpectedCoordinates(repo, task);

        await repo.GitAsync("worktree", "add", "--lock", "-b", branch, worktreePath);
        DeleteTree(worktreePath);
        Directory.Exists(worktreePath).ShouldBeFalse();

        await service.CreateForTaskAsync(task, CancellationToken.None);

        task.WorktreePath.ShouldBe(worktreePath);
        task.WorktreeBranch.ShouldBe(branch);
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await manager.ListAsync(repo.Path, CancellationToken.None)).Count.ShouldBe(1);
        var ours = WorktreeManager.ParseWorktreeList(await repo.GitReadAsync("worktree", "list", "--porcelain"))
            .Where(e => PathsEqual(e.Path, worktreePath))
            .ToList();
        ours.ShouldHaveSingleItem();
        ours[0].Locked.ShouldBeFalse();
    }

    [Test]
    public async Task an_unlocked_registration_whose_directory_is_gone_is_healed_too()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        var (branch, worktreePath) = ExpectedCoordinates(repo, task);

        await repo.GitAsync("worktree", "add", "-b", branch, worktreePath);
        DeleteTree(worktreePath);
        Directory.Exists(worktreePath).ShouldBeFalse();

        await service.CreateForTaskAsync(task, CancellationToken.None);

        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await manager.ListAsync(repo.Path, CancellationToken.None)).Count.ShouldBe(1);
        var ours = WorktreeManager.ParseWorktreeList(await repo.GitReadAsync("worktree", "list", "--porcelain"))
            .Where(e => PathsEqual(e.Path, worktreePath))
            .ToList();
        ours.ShouldHaveSingleItem();
        ours[0].Locked.ShouldBeFalse();
    }

    [Test]
    public async Task healing_re_attaches_the_task_branch_and_keeps_its_commits()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "kept.md"), "keep me\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "kept.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "keep")).Ok.ShouldBeTrue();
        var before = (await ScratchGitRepo.GitInAsync(
            task.WorktreePath!, "rev-list", "--count", "feat/parent..HEAD")).StdOut.Trim();
        before.ShouldBe("1");

        DeleteTree(task.WorktreePath!);
        task.WorktreePath = null;
        task.WorktreeBranch = null;

        await service.CreateForTaskAsync(task, CancellationToken.None);

        var after = (await ScratchGitRepo.GitInAsync(
            task.WorktreePath!, "rev-list", "--count", "feat/parent..HEAD")).StdOut.Trim();
        after.ShouldBe(before);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "show", "HEAD:kept.md"))
            .StdOut.ReplaceLineEndings("\n").ShouldBe("keep me\n");
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_failed_worktree_add_leaves_no_registration_branch_or_directory(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var hooks = Path.Combine(repo.Path, ".git-hooks-fail");
        Directory.CreateDirectory(hooks);
        await File.WriteAllTextAsync(Path.Combine(hooks, "post-checkout"), "#!/bin/sh\nexit 1\n");
        await repo.GitAsync("config", "core.hooksPath", hooks);

        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        var (branch, worktreePath) = ExpectedCoordinates(repo, task);

        var failed = await Should.ThrowAsync<Exception>(
            () => service.CreateForTaskAsync(task, ct));
        failed.ShouldNotBeOfType<TimeoutException>();

        Directory.Exists(worktreePath).ShouldBeFalse();
        (await manager.ListAsync(repo.Path, CancellationToken.None)).ShouldBeEmpty();
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{branch}"))
            .Ok.ShouldBeFalse("the task branch must not survive a failed add");

        // Timeout arm: the same hook with sleep, a 1 s add budget, TimeoutException (not OCE),
        // and the same clean post-state.
        await File.WriteAllTextAsync(Path.Combine(hooks, "post-checkout"), "#!/bin/sh\nsleep 30\nexit 0\n");
        var (timeoutService, timeoutManager) = CreateService(repo, worktreeAddTimeoutSeconds: 1);
        var timeoutTask = NewTask(repo.Path, mergeTarget: null);
        var (timeoutBranch, timeoutPath) = ExpectedCoordinates(repo, timeoutTask);

        var timedOut = await Should.ThrowAsync<TimeoutException>(
            () => timeoutService.CreateForTaskAsync(timeoutTask, ct));
        timedOut.ShouldNotBeOfType<OperationCanceledException>();

        Directory.Exists(timeoutPath).ShouldBeFalse();
        (await timeoutManager.ListAsync(repo.Path, CancellationToken.None)).ShouldBeEmpty();
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{timeoutBranch}"))
            .Ok.ShouldBeFalse("a timed-out add must not leave the branch");
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

    // ---- explicit land preparation/finalization (CARD-0258 S1) ----------------------------

    [Test]
    public async Task land_happy_path_rebases_a_moved_base_and_pushes_the_fast_forward()
    {
        using var repo = new ScratchGitRepo("antiphon-land-happy");
        using var remote = new TemporaryDirectory("antiphon-land-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);
        prepared.Succeeded.ShouldBeTrue();
        prepared.BaseMoved.ShouldBeTrue();
        var finalized = await service.FinalizeLandAsync(task, prepared.Target!, CancellationToken.None);

        finalized.Pushed.ShouldBeTrue(finalized.Detail);
        finalized.Residue.ShouldBeNull();
        (await ScratchGitRepo.GitInAsync(remote.Path, "show", "master:feature.md")).StdOut.ShouldBe("land me\n");
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
    }

    [Test]
    public async Task land_with_upstream_set_deletes_the_branch()
    {
        using var repo = new ScratchGitRepo("antiphon-land-upstream");
        using var remote = new TemporaryDirectory("antiphon-land-upstream-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "push", "-u", "origin", task.WorktreeBranch!);
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);
        prepared.Succeeded.ShouldBeTrue();
        var finalized = await service.FinalizeLandAsync(task, prepared.Target!, CancellationToken.None);

        finalized.Pushed.ShouldBeTrue(finalized.Detail);
        finalized.Residue.ShouldBeNull();
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{task.WorktreeBranch}"))
            .Ok.ShouldBeFalse("the rebased branch must be deleted even when its upstream is behind");
    }

    [Test]
    public async Task land_conflict_is_reported_and_the_worktree_is_left_for_the_merge_delegate()
    {
        using var repo = new ScratchGitRepo("antiphon-land-conflict");
        using var remote = new TemporaryDirectory("antiphon-land-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "shared.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");

        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);

        prepared.Conflicted.ShouldBeTrue();
        prepared.ConflictFiles.ShouldContain("shared.md");
        Directory.Exists(task.WorktreePath).ShouldBeTrue("a merge delegate must receive the original worktree");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim().ShouldBeEmpty(
            "the rebase is aborted before the Merge delegate starts it again");
    }

    [Test]
    public async Task prepare_land_aborts_an_interrupted_rebase_first()
    {
        using var repo = new ScratchGitRepo("antiphon-land-rebase-heal");
        using var remote = new TemporaryDirectory("antiphon-land-rebase-heal-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "shared.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");

        var interrupted = await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rebase", "master");
        interrupted.Ok.ShouldBeFalse("the hand-run rebase must stop on the conflict");
        (await RebaseStateDirectoryAsync(task.WorktreePath!)).ShouldNotBeNull("the worktree must be left mid-rebase");

        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);

        prepared.Conflicted.ShouldBeTrue(prepared.Detail);
        prepared.Succeeded.ShouldBeFalse();
        prepared.ConflictFiles.ShouldContain("shared.md");
        prepared.Detail.ShouldNotBeNull();
        prepared.Detail.ShouldContain("aborted an interrupted rebase");
        prepared.Detail.ShouldNotContain("Rebase onto master failed");
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain")).StdOut.Trim()
            .ShouldBeEmpty("the healed rebase still aborts cleanly for the Merge delegate");
    }

    [Test]
    public async Task already_landed_arm_pushes_a_target_that_is_ahead_of_origin()
    {
        using var repo = new ScratchGitRepo("antiphon-land-ahead");
        using var remote = new TemporaryDirectory("antiphon-land-ahead-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, worktrees) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        task.LandRequestedAt = DateTime.UtcNow;
        await worktrees.CreateForTaskAsync(task, CancellationToken.None);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge", "--ff-only", task.WorktreeBranch!))
            .Ok.ShouldBeTrue("ff-merge into local master without pushing");

        var localSha = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var originBefore = (await ScratchGitRepo.GitInAsync(remote.Path, "rev-parse", "master")).StdOut.Trim();
        originBefore.ShouldNotBe(localSha);

        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Complete);
        var originAfter = (await ScratchGitRepo.GitInAsync(remote.Path, "rev-parse", "master")).StdOut.Trim();
        originAfter.ShouldBe(localSha);
        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldContain($"origin/master={originAfter}");
        landed.Detail.ShouldContain(originAfter);
    }

    [Test]
    public async Task land_push_rejection_keeps_the_rebased_branch_and_worktree()
    {
        using var repo = new ScratchGitRepo("antiphon-land-push-reject");
        using var remote = new TemporaryDirectory("antiphon-land-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);
        prepared.Succeeded.ShouldBeTrue();

        using var rival = new TemporaryDirectory("antiphon-land-rival");
        await ScratchGitRepo.GitInAsync(rival.Path, "clone", remote.Path, ".");
        await ScratchGitRepo.GitInAsync(rival.Path, "config", "user.email", "test@antiphon.local");
        await ScratchGitRepo.GitInAsync(rival.Path, "config", "user.name", "Rival");
        await File.WriteAllTextAsync(Path.Combine(rival.Path, "rival.md"), "remote moved\n");
        await ScratchGitRepo.GitInAsync(rival.Path, "add", "rival.md");
        await ScratchGitRepo.GitInAsync(rival.Path, "commit", "-m", "remote advance");
        await ScratchGitRepo.GitInAsync(rival.Path, "push", "origin", "master");

        var finalized = await service.FinalizeLandAsync(task, prepared.Target!, CancellationToken.None);

        finalized.Pushed.ShouldBeFalse();
        finalized.Detail.ShouldContain("push");
        Directory.Exists(task.WorktreePath).ShouldBeTrue("a push rejection must not clean up recoverable work");
    }

    [Test]
    public async Task land_verify_failure_keeps_the_rebased_worktree_for_a_follow_up()
    {
        using var worktree = new TemporaryDirectory("antiphon-land-red");

        var verification = await AgentTaskLandService.VerifyAsync(worktree.Path, null, CancellationToken.None);

        verification.Ok.ShouldBeFalse();
        verification.Step.ShouldBe("build");
        Directory.Exists(worktree.Path).ShouldBeTrue("verification itself must never remove the worktree");
    }

    [Test]
    public void land_shared_writer_hold_uses_the_same_repo_not_the_worktree_path()
    {
        var repo = Path.Combine(Path.GetTempPath(), "antiphon-land-scope");
        var landing = NewTask(repo, mergeTarget: null);
        landing.WorkingDirectory = Path.Combine(repo, "trees", "task");
        var runningShared = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "shared writer",
            Workspace = WorkspaceMode.Shared, Role = AgentTaskRole.Code,
            RepoPath = repo, WorkingDirectory = repo, Status = AgentTaskStatus.Working,
        };

        AgentTaskLandService.IsHeldBehindSharedWriter(landing, [runningShared]).ShouldBeTrue();
    }

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

    // ---- CARD-0149: self-cleaned worktree is not "NOT merged" ------------------------------

    [Test]
    public async Task a_self_removed_worktree_is_already_cleaned_up_not_a_merge_failure()
    {
        // The dispatched agent rebase/ff-merged itself, then `git worktree remove --force --force`
        // and `git branch -D`. Merge-back used to run `git status --porcelain` in the now-gone
        // path and report "NOT merged".
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "the work")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge", "--ff-only", task.WorktreeBranch!)).Ok.ShouldBeTrue();

        var removed = await ScratchGitRepo.GitInAsync(
            repo.Path, "worktree", "remove", "--force", "--force", task.WorktreePath!);
        removed.Ok.ShouldBeTrue($"worktree remove must succeed: {removed.StdErr}");
        await ScratchGitRepo.GitInAsync(repo.Path, "branch", "-D", task.WorktreeBranch!);

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.AlreadyCleanedUp);
        outcome.Detail.ShouldBe("worktree already cleaned up by the task");
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
    }

    [Test]
    public async Task an_unregistered_leftover_directory_is_already_cleaned_up_not_a_merge_failure()
    {
        // Windows shape of the CARD-0149 false alarm: `git worktree remove` unregisters (gitdir
        // gone) but leaves the directory, so Directory.Exists is true and `git status --porcelain`
        // exits 128 "not a git repository".
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "dangling.md"), "left on disk\n");

        await UnregisterWorktreeLeavingDirectoryAsync(repo.Path, task.WorktreePath!);
        Directory.Exists(task.WorktreePath).ShouldBeTrue("the leftover directory is the whole point");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "status", "--porcelain"))
            .Ok.ShouldBeFalse("git status in the leftover must fail the way production did");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.AlreadyCleanedUp);
        outcome.Detail.ShouldBe("worktree already cleaned up by the task");
    }

    [Test]
    public async Task a_still_registered_dirty_worktree_is_still_swept_and_merged()
    {
        // Positive control: the worktree is still ours and still dirty. Skip would mask a real
        // mid-cleanup break; the safety-net commit must still run.
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Merged);
        outcome.Detail.ShouldNotBe("worktree already cleaned up by the task");
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
    }

    [Test]
    public async Task a_commit_all_failure_on_a_live_worktree_still_fails()
    {
        // Positive control: a registered worktree whose safety-net commit cannot run is still a
        // real merge failure, not "already cleaned up".
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");

        var gitDir = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "--git-dir")).StdOut.Trim();
        if (!Path.IsPathRooted(gitDir))
            gitDir = Path.GetFullPath(Path.Combine(task.WorktreePath!, gitDir));
        await File.WriteAllTextAsync(Path.Combine(gitDir, "index.lock"), "held\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed);
        outcome.Detail.ShouldNotBeNull();
        outcome.Detail.ShouldContain("Committing the delegate's work failed");
        Directory.Exists(task.WorktreePath).ShouldBeTrue("a failed merge-back must keep the worktree");
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

    private static (AgentTaskLandService Land, DelegationWorktreeService Worktrees) CreateLand(
        AppDbContext db, ScratchGitRepo repo)
    {
        var (worktrees, _) = CreateService(repo);
        var tasks = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
        var land = new AgentTaskLandService(
            db,
            worktrees,
            tasks,
            new AgentTaskLandQueue(),
            null!,
            new MockEventBus(),
            TimeProvider.System,
            Options.Create(new DelegationSettings()),
            NullLogger<AgentTaskLandService>.Instance);
        return (land, worktrees);
    }

    private static (DelegationWorktreeService Service, WorktreeManager Manager) CreateService(
        ScratchGitRepo repo,
        int? worktreeAddTimeoutSeconds = null)
    {
        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = repo.WorktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
                WorktreeAddTimeoutSeconds = worktreeAddTimeoutSeconds ?? 180,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var service = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        return (service, manager);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory().FullName;

        public TemporaryDirectory(string prefix)
        {
            Directory.Delete(Path);
            Path = Directory.CreateTempSubdirectory(prefix).FullName;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<string?> RebaseStateDirectoryAsync(string worktree)
    {
        foreach (var name in new[] { "rebase-merge", "rebase-apply" })
        {
            var parsed = await ScratchGitRepo.GitInAsync(worktree, "rev-parse", "--git-path", name);
            var path = parsed.StdOut.Trim();
            if (path.Length == 0)
                continue;
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(worktree, path));
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }

    private static (string Branch, string WorktreePath) ExpectedCoordinates(ScratchGitRepo repo, AgentTask task)
    {
        var identifier = $"task-{DelegationReportFormatter.Short(task.Id)}";
        return (
            WorktreeManager.BuildBranchName(identifier),
            Path.GetFullPath(Path.Combine(repo.WorktreeRoot, WorktreeManager.BuildDirectoryName(identifier))));
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
    }

    /// <summary>
    /// Drop the worktree's gitdir and prune, leaving the directory on disk — the Windows leftover
    /// after <c>git worktree remove</c> unregisters but cannot delete files.
    /// </summary>
    private static async Task UnregisterWorktreeLeavingDirectoryAsync(string repoPath, string worktreePath)
    {
        var gitFile = Path.Combine(worktreePath, ".git");
        File.Exists(gitFile).ShouldBeTrue("a linked worktree has a .git file, not a directory");

        string? gitdir = null;
        foreach (var raw in (await File.ReadAllTextAsync(gitFile)).Split(['\r', '\n'], StringSplitOptions.None))
        {
            var line = raw.Trim();
            const string prefix = "gitdir:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            gitdir = line[prefix.Length..].Trim();
            if (!Path.IsPathRooted(gitdir))
                gitdir = Path.GetFullPath(Path.Combine(worktreePath, gitdir));
            break;
        }

        gitdir.ShouldNotBeNull("the .git file must point at a gitdir");
        DeleteTree(gitdir);
        (await ScratchGitRepo.GitInAsync(repoPath, "worktree", "prune")).Ok.ShouldBeTrue();
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
            return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

}
