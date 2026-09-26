using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
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
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public partial class DelegationWorktreeTests
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
    public async Task a_running_sibling_is_never_a_base()
    {
        // CARD-0215: a still-running sibling (Queued, live worktree) is not a base. S1 cuts from
        // the default branch, not from the sibling tip. Settled-sibling chaining is deferred (S3).
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
        var secondHead = (await ScratchGitRepo.GitInAsync(second.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();
        secondHead.ShouldBe(masterHead);
        (await ScratchGitRepo.GitInAsync(
            second.WorktreePath!, "merge-base", "--is-ancestor", firstCommit, "HEAD"))
            .Ok.ShouldBeFalse("a running sibling is never a base; the new branch starts at the default tip");
        second.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        second.WorktreeBaseRef.ShouldBe("master");
        second.WorktreeBaseTaskId.ShouldBeNull();
    }

    [Test]
    public async Task unowned_locked_missing_registration_requires_recovery_evidence()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        var (branch, worktreePath) = ExpectedCoordinates(repo, task);

        await repo.GitAsync("worktree", "add", "--lock", "-b", branch, worktreePath);
        DeleteTree(worktreePath);
        Directory.Exists(worktreePath).ShouldBeFalse();

        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => service.CreateForTaskAsync(task, CancellationToken.None));
        Directory.Exists(worktreePath).ShouldBeFalse();
        (await repo.GitReadAsync("show-ref", "--verify", "--hash", "refs/heads/" + branch)).Trim()
            .ShouldBe((await repo.GitReadAsync("rev-parse", "HEAD")).Trim());
        (await manager.ListAsync(repo.Path, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task unowned_unlocked_missing_registration_requires_recovery_evidence()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        var (branch, worktreePath) = ExpectedCoordinates(repo, task);

        await repo.GitAsync("worktree", "add", "-b", branch, worktreePath);
        DeleteTree(worktreePath);
        Directory.Exists(worktreePath).ShouldBeFalse();

        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => service.CreateForTaskAsync(task, CancellationToken.None));
        Directory.Exists(worktreePath).ShouldBeFalse();
        (await repo.GitReadAsync("show-ref", "--verify", "--hash", "refs/heads/" + branch)).Trim()
            .ShouldBe((await repo.GitReadAsync("rev-parse", "HEAD")).Trim());
        (await manager.ListAsync(repo.Path, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task healing_re_attaches_the_task_branch_and_keeps_its_commits(bool stagedResolution)
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

        if (stagedResolution)
        {
            await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "kept.md"), "staged resolution\n");
            (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "kept.md")).Ok.ShouldBeTrue();
        }
        var indexBefore = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "write-tree")).StdOut.Trim();

        DeleteTree(task.WorktreePath!);
        task.WorktreePath = null;
        task.WorktreeBranch = null;

        await service.CreateForTaskAsync(task, CancellationToken.None);

        var after = (await ScratchGitRepo.GitInAsync(
            task.WorktreePath!, "rev-list", "--count", "feat/parent..HEAD")).StdOut.Trim();
        after.ShouldBe(before);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "show", "HEAD:kept.md"))
            .StdOut.ReplaceLineEndings("\n").ShouldBe("keep me\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "write-tree")).StdOut.Trim().ShouldBe(indexBefore);
        (await File.ReadAllTextAsync(Path.Combine(task.WorktreePath!, "kept.md"))).ReplaceLineEndings("\n")
            .ShouldBe(stagedResolution ? "staged resolution\n" : "keep me\n");
    }

    [Test]
    [Arguments("unknown.txt")]
    [Arguments("bin-private/unknown.txt")]
    public async Task failed_creation_preserves_unknown_hook_content(string relative)
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.CommitFileAsync(".gitignore", "bin-private/\n");
        var hooks = Path.Combine(repo.Path, ".git-hooks-fail");
        Directory.CreateDirectory(hooks);
        await WriteExecutableHookAsync(Path.Combine(hooks, "post-checkout"),
            "#!/bin/sh\nmkdir -p bin-private\nprintf 'valuable' > " + relative + "\nexit 1\n");
        await repo.GitAsync("config", "core.hooksPath", hooks);
        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, null);
        var (branch, path) = ExpectedCoordinates(repo, task);
        await Should.ThrowAsync<InvalidOperationException>(() => service.CreateForTaskAsync(task, CancellationToken.None));
        File.Exists(Path.Combine(path, relative)).ShouldBeTrue("failed creation must preserve unknown hook bytes before any ordinary removal");
        (await File.ReadAllTextAsync(Path.Combine(path, relative))).ShouldBe("valuable");
        (await repo.GitReadAsync("show-ref", "--verify", "--hash", "refs/heads/" + branch)).Trim()
            .ShouldBe((await repo.GitReadAsync("rev-parse", "HEAD")).Trim());
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_failed_worktree_add_leaves_no_registration_branch_or_directory(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var hooks = Path.Combine(repo.Path, ".git-hooks-fail");
        Directory.CreateDirectory(hooks);
        await WriteExecutableHookAsync(Path.Combine(hooks, "post-checkout"), "#!/bin/sh\nexit 1\n");
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
        await WriteExecutableHookAsync(Path.Combine(hooks, "post-checkout"), "#!/bin/sh\nsleep 30\nexit 0\n");
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
    public async Task C508_ReuseKeepsRecordedBase()
    {
        using var repo = new ScratchGitRepo("c508-reuse");
        await repo.CommitFileAsync("README.md", "B\n");
        var masterM = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (service, manager) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        var recordedSha = task.WorktreeBaseSha;
        recordedSha.ShouldBe(masterM);
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "work.md"), "task work\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "work.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "task work")).Ok.ShouldBeTrue();
        var taskTip = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();

        await repo.CommitFileAsync("later.md", "advanced M\n");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.WorktreeBaseSha.ShouldBe(recordedSha);
        task.WorktreeBaseRef.ShouldBe("master");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(taskTip);

        var path = task.WorktreePath;
        task.WorktreePath = null;
        task.WorktreeBranch = null;
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.WorktreePath.ShouldBe(path);
        task.WorktreeBaseSha.ShouldBe(recordedSha);

        var adversarial = NewTask(repo.Path, mergeTarget: null);
        adversarial.WorktreeBaseRef = "explicit-E";
        adversarial.WorktreeBaseSource = WorktreeBaseSource.Explicit;
        adversarial.WorktreeBaseRequestedRef = null;
        await service.CreateForTaskAsync(adversarial, CancellationToken.None);
        (await ScratchGitRepo.GitInAsync(adversarial.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe((await repo.GitReadAsync("rev-parse", "master")).Trim());
        adversarial.WorktreeBaseRef.ShouldBe("explicit-E");
        _ = manager;
    }

    [Test]
    public async Task C508_RepairStartShaWins()
    {
        using var repo = new ScratchGitRepo("c508-repair");
        await repo.CommitFileAsync("README.md", "B\n");
        await repo.CommitFileAsync("master.txt", "M\n");
        var master = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "owner");
        await repo.CommitFileAsync("owner.txt", "repair\n");
        var repairSha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        repairSha.ShouldNotBe(master);

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "master");
        task.RepairSourceTaskId = Guid.NewGuid();
        await service.CreateForTaskAsync(task, CancellationToken.None, startAtSha: repairSha);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(repairSha);
        task.WorktreeBaseRef.ShouldBe(repairSha);
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Repair);
        task.WorktreeBaseTaskId.ShouldBe(task.RepairSourceTaskId);
        task.WorktreeBaseSha.ShouldBe(repairSha);
        task.MergeTargetRef.ShouldBe("master");
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

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "feature.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");
        var movedBase = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);
        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed);
        operation.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        operation.Phase.ShouldBe(LandPhase.Complete);
        operation.RemoteConfirmedAt.ShouldNotBeNull();
        operation.PushStartedAt.ShouldNotBeNull();
        operation.PushExitCode.ShouldBe(0);
        operation.LastReason.ShouldBeNull();
        operation.RebasedSourceSha.ShouldNotBe(sourceSha);
        operation.VerifiedSourceSha.ShouldBe(operation.RebasedSourceSha);
        var remoteSha = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        remoteSha.ShouldBe(operation.VerifiedSourceSha);
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(remoteSha);
        await RequiredGitAsync(remote.Path, "merge-base", "--is-ancestor", movedBase, "master");
        (await RequiredGitAsync(remote.Path, "show", "master:feature.md")).ShouldBe("land me\n");
        (await RequiredGitAsync(remote.Path, "show", "master:README.md")).ShouldBe("base advanced\n");
        operation.CanonicalAdvancedAt.ShouldNotBeNull();
        operation.CanonicalAdvanceReason.ShouldBeNull();
        operation.LocalTargetAfterSha.ShouldBe(operation.VerifiedSourceSha);
        Directory.Exists(sourcePath).ShouldBeFalse();
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet", sourceFullRef)).Ok.ShouldBeFalse();
        operation.DirectoryRemoved.ShouldBeTrue();
        operation.RegistrationRemoved.ShouldBeTrue();
        operation.BranchRemoved.ShouldBeTrue();
        (await AssertTerminalAsync(db, task.Id, request.RequestId, operation.Id, AgentTaskEventType.Landed))
            .Detail.ShouldContain($"remote={remoteSha}");
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

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "feature.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "feature");
        var sourceSha = (await RequiredGitAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim();
        var sourceFullRef = $"refs/heads/{task.WorktreeBranch}";
        await RequiredGitAsync(task.WorktreePath!, "push", "-u", "origin", $"HEAD:{sourceFullRef}");
        (await RequiredGitAsync(task.WorktreePath!, "rev-parse", "@{upstream}")).Trim().ShouldBe(sourceSha);
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");
        var movedBase = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var (request, publishedSha, sourcePath, _, publishedRef) = await PublishAndRequestAsync(land, task);
        publishedSha.ShouldBe(sourceSha);
        publishedRef.ShouldBe(sourceFullRef);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);
        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed);
        operation.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        operation.Phase.ShouldBe(LandPhase.Complete);
        operation.RemoteConfirmedAt.ShouldNotBeNull();
        operation.PushStartedAt.ShouldNotBeNull();
        operation.PushExitCode.ShouldBe(0);
        operation.LastReason.ShouldBeNull();
        operation.RebasedSourceSha.ShouldNotBe(sourceSha);
        operation.VerifiedSourceSha.ShouldBe(operation.RebasedSourceSha);
        var remoteSha = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        remoteSha.ShouldBe(operation.VerifiedSourceSha);
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(remoteSha);
        await RequiredGitAsync(remote.Path, "merge-base", "--is-ancestor", movedBase, "master");
        (await RequiredGitAsync(remote.Path, "show", "master:feature.md")).ShouldBe("land me\n");
        (await RequiredGitAsync(remote.Path, "show", "master:README.md")).ShouldBe("base advanced\n");
        (await RequiredGitAsync(remote.Path, "rev-parse", sourceFullRef)).Trim().ShouldBe(sourceSha);
        operation.CanonicalAdvancedAt.ShouldNotBeNull();
        operation.CanonicalAdvanceReason.ShouldBeNull();
        operation.LocalTargetAfterSha.ShouldBe(operation.VerifiedSourceSha);
        Directory.Exists(sourcePath).ShouldBeFalse();
        (await ScratchGitRepo.GitInAsync(repo.Path, "show-ref", "--verify", "--quiet", sourceFullRef))
            .Ok.ShouldBeFalse("the source branch must be deleted even when its upstream is behind");
        operation.DirectoryRemoved.ShouldBeTrue();
        operation.RegistrationRemoved.ShouldBeTrue();
        operation.BranchRemoved.ShouldBeTrue();
        (await AssertTerminalAsync(db, task.Id, request.RequestId, operation.Id, AgentTaskEventType.Landed))
            .Detail.ShouldContain($"remote={remoteSha}");
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

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        var localBefore = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var remoteBefore = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);
        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_conflict");
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.Publication.ShouldBe(LandPublicationOutcome.Unconfirmed);
        operation.RemoteConfirmedAt.ShouldBeNull();
        operation.PushStartedAt.ShouldBeNull();
        operation.Cleanup.ShouldBe(LandCleanupStatus.NotStarted);
        operation.LocalTargetAfterSha.ShouldBeNull();
        var finalRequest = await AssertRequestAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        finalRequest.State.ShouldBe(LandRequestState.NeedsResolution);
        finalRequest.IsPending.ShouldBeTrue();
        finalRequest.LandingOperationId.ShouldBe(operation.Id);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
        var conflict = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == task.Id
            && e.LandRequestId == request.RequestId && e.Type == AgentTaskEventType.Conflicted);
        conflict.LandingOperationId.ShouldBe(operation.Id);
        conflict.Detail.ShouldContain("shared.md");
        await AssertUnchangedSourceAsync(sourcePath, sourceFullRef, sourceSha, "shared.md", "task version\n");
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(localBefore);
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(remoteBefore);
        (await RequiredGitAsync(remote.Path, "show", "master:shared.md")).ShouldBe("target version\n");
        operation.LandWorktreePath.ShouldNotBeNull();
        (await RequiredGitAsync(operation.LandWorktreePath!, "status", "--porcelain")).Trim().ShouldBeEmpty();
        (await RebaseStateDirectoryAsync(operation.LandWorktreePath!)).ShouldBeNull();
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent
                || e.Type == AgentTaskEventType.LandedWithResidue))).ShouldBe(0);
    }

    [Test]
    public async Task legacy_prepare_refuses_and_preserves_interrupted_rebase_resolution()
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

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "operator resolution\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "shared.md");
        var indexBefore = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "write-tree")).StdOut.Trim();
        var prepared = await service.PrepareLandAsync(task, CancellationToken.None);
        prepared.Succeeded.ShouldBeFalse();
        prepared.Detail.ShouldBe("durable_landing_protocol_required");
        (await RebaseStateDirectoryAsync(task.WorktreePath!)).ShouldNotBeNull();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "write-tree")).StdOut.Trim().ShouldBe(indexBefore);
        (await File.ReadAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"))).ShouldBe("operator resolution\n");
    }

    [Test]
    public async Task land_local_target_ahead_of_origin_is_refused_without_publication()
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
        await worktrees.CreateForTaskAsync(task, CancellationToken.None);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "feature.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "feature");
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge", "--ff-only", task.WorktreeBranch!))
            .Ok.ShouldBeTrue("ff-merge into local master without pushing");

        var localBefore = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var remoteBefore = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);
        localBefore.ShouldBe(sourceSha);
        remoteBefore.ShouldNotBe(localBefore);

        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Complete);
        var finalRequest = await AssertRequestAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        finalRequest.SourceRefusalReason.ShouldBe("target_local_ahead");
        finalRequest.LandingOperationId.ShouldBeNull();
        (await db.AgentTaskLandings.AsNoTracking().CountAsync(o => o.TaskId == task.Id)).ShouldBe(0);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).ActiveLandingId.ShouldBeNull();
        (await AssertTerminalAsync(db, task.Id, request.RequestId, null, AgentTaskEventType.LandRefused))
            .Detail.ShouldContain("target_local_ahead");
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(localBefore);
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(remoteBefore);
        (await RequiredGitAsync(remote.Path, "rev-parse", sourceFullRef)).Trim().ShouldBe(sourceSha);
        await AssertUnchangedSourceAsync(sourcePath, sourceFullRef, sourceSha, "feature.md", "land me\n");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent
                || e.Type == AgentTaskEventType.LandedWithResidue))).ShouldBe(0);
        Directory.EnumerateDirectories(repo.WorktreeRoot, "*land*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task land_push_rejection_keeps_the_source_branch_and_worktree()
    {
        using var repo = new ScratchGitRepo("antiphon-land-push-reject");
        using var remote = new TemporaryDirectory("antiphon-land-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "feature.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);
        await WriteExecutableHookAsync(Path.Combine(remote.Path, "hooks", "pre-receive"), "#!/bin/sh\nexit 1\n");
        var localBefore = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var remoteBefore = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);
        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("push_rejected");
        operation.Phase.ShouldBe(LandPhase.PushStarted);
        operation.PushStartedAt.ShouldNotBeNull();
        operation.PushExitCode.ShouldNotBeNull();
        operation.PushExitCode.ShouldNotBe(0);
        operation.Publication.ShouldBe(LandPublicationOutcome.Unconfirmed);
        operation.RemoteConfirmedAt.ShouldBeNull();
        operation.Cleanup.ShouldBe(LandCleanupStatus.NotStarted);
        operation.DirectoryRemoved.ShouldBeFalse();
        operation.RegistrationRemoved.ShouldBeFalse();
        operation.BranchRemoved.ShouldBeFalse();
        operation.RebasedSourceSha.ShouldNotBe(sourceSha);
        operation.VerifiedSourceSha.ShouldBe(operation.RebasedSourceSha);
        operation.PreparedPinned.ShouldBeTrue();
        (await RequiredGitAsync(repo.Path, "rev-parse", $"{operation.RecoveryRefPrefix}/prepared")).Trim()
            .ShouldBe(operation.RebasedSourceSha);
        operation.LandWorktreePath.ShouldNotBeNull();
        (await RequiredGitAsync(operation.LandWorktreePath!, "rev-parse", "HEAD")).Trim()
            .ShouldBe(operation.RebasedSourceSha);
        (await RequiredGitAsync(operation.LandWorktreePath!, "status", "--porcelain")).Trim().ShouldBeEmpty();
        operation.LocalTargetAfterSha.ShouldBeNull();
        operation.CanonicalAdvanceStartedAt.ShouldBeNull();
        operation.CanonicalAdvancedAt.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(localBefore);
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(remoteBefore);
        await AssertUnchangedSourceAsync(sourcePath, sourceFullRef, sourceSha, "feature.md", "land me\n");
        (await RequiredGitAsync(remote.Path, "rev-parse", sourceFullRef)).Trim().ShouldBe(sourceSha);
        (await AssertTerminalAsync(db, task.Id, request.RequestId, operation.Id, AgentTaskEventType.LandRefused))
            .Detail.ShouldContain("push_rejected");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent
                || e.Type == AgentTaskEventType.LandedWithResidue))).ShouldBe(0);
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
    public async Task a_worktree_with_a_force_added_ignored_file_is_left_for_review_naming_it()
    {
        using var repo = new ScratchGitRepo();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "*.secret\n");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (service, _) = CreateService(repo);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        var parentBefore = (await repo.GitReadAsync("rev-parse", "feat/parent")).Trim();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "a.secret"), "secret\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "-f", "a.secret")).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "a.secret"), "changed\n");

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        outcome.Detail.ShouldContain("a.secret");
        outcome.Detail.ShouldContain("*.secret");
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await repo.GitReadAsync("rev-parse", "feat/parent")).Trim().ShouldBe(parentBefore);
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
    public async Task a_self_removed_worktree_without_receipt_cannot_claim_merge_success()
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

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed);
        outcome.Detail.ShouldBe("source_registration_unknown");
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
    }

    [Test]
    public async Task an_unregistered_leftover_directory_is_preserved_without_merge_success()
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

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed);
        outcome.Detail.ShouldBe("source_registration_unknown");
        (await File.ReadAllTextAsync(Path.Combine(task.WorktreePath!, "dangling.md"))).ShouldBe("left on disk\n");
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

    /// <summary>
    /// V-6 / G-17: base resolution and creation happen only under a lease this service's own
    /// provider minted and still owns. An occupied lease refuses; a foreign lease is refused by
    /// the service BEFORE the manager is called (the recording decorator's count stays zero, so
    /// the service fence is observed independently of the manager's own guard); and the successful
    /// arm cuts from the tip that was moved before the legitimate lease was acquired.
    /// </summary>
    [Test]
    public async Task C508_BaseResolutionRequiresOwnedLease()
    {
        using var repo = new ScratchGitRepo("c508-lease");
        await repo.CommitFileAsync("README.md", "B\n");
        var settings = new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = "master",
        };

        var git = new LandingGit();
        var leases = new RepositoryMutationLease(git);
        var guarded = new GuardedWorktreeRemoval(git, leases, new NullRemovalEvidence());
        var real = new WorktreeManager(Options.Create(settings), TimeProvider.System,
            NullLogger<WorktreeManager>.Instance, guarded, leases, git);
        var recording = new RecordingWorktreeManager(real);
        var service = new DelegationWorktreeService(
            recording,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            leases, git, gitSettings: Options.Create(settings));

        // 1. Occupied: someone else holds the genuine common-directory lease.
        var occupied = NewTask(repo.Path, mergeTarget: null);
        await using (var held = await leases.TryAcquireAsync(repo.Path, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var busy = await Should.ThrowAsync<ConflictException>(
                () => service.CreateForTaskAsync(occupied, CancellationToken.None));
            busy.Message.ShouldBe("repository_busy");
        }

        occupied.WorktreePath.ShouldBeNull();
        occupied.WorktreeBaseRef.ShouldBeNull();
        occupied.WorktreeBaseSha.ShouldBeNull();
        occupied.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Unset);
        recording.CreateCalls.ShouldBe(0);

        // 2. Foreign: a live lease minted by a DIFFERENT provider over the same directory.
        var foreignProvider = new RepositoryMutationLease(git);
        var foreignTask = NewTask(repo.Path, mergeTarget: null);
        await using (var foreign = await foreignProvider.TryAcquireAsync(repo.Path, CancellationToken.None))
        {
            foreign.ShouldNotBeNull();
            var refused = await Should.ThrowAsync<ConflictException>(
                () => service.CreateForTaskAsync(foreignTask, foreign, CancellationToken.None));
            refused.Message.ShouldBe("repository_lease_required");
        }

        foreignTask.WorktreePath.ShouldBeNull();
        foreignTask.WorktreeBaseRef.ShouldBeNull();
        foreignTask.WorktreeBaseSha.ShouldBeNull();
        foreignTask.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Unset);
        recording.CreateCalls.ShouldBe(0);

        // 3. The default tip moves before the legitimate lease is taken; the creation must use
        //    the ref resolved and observed under that lease, not a tip read earlier.
        await repo.CommitFileAsync("later.md", "advanced M\n");
        var movedTip = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var granted = NewTask(repo.Path, mergeTarget: null);
        var decision = await service.CreateForTaskAsync(granted, CancellationToken.None);
        decision.ShouldNotBeNull();
        decision.NewlyRecorded.ShouldBeTrue();
        decision.Resolved.Source.ShouldBe(WorktreeBaseSource.DefaultBranch);
        decision.Resolved.Ref.ShouldBe("master");
        granted.WorktreeBaseRef.ShouldBe("master");
        granted.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        granted.WorktreeBaseSha.ShouldBe(movedTip);
        (await ScratchGitRepo.GitInAsync(granted.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(movedTip);
        recording.CreateCalls.ShouldBe(1);
    }

    private sealed class NullRemovalEvidence : IWorktreeRemovalEvidence
    {
        public Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct) =>
            Task.FromResult<AgentTaskLanding?>(null);
    }

    /// <summary>
    /// Counts creation calls so the service's own lease fence can be asserted separately from
    /// the manager's. PC-17 removes only the service throw; the count must still be zero.
    /// </summary>
    private sealed class RecordingWorktreeManager(IWorktreeManager inner) : IWorktreeManager
    {
        public int CreateCalls { get; private set; }

        public Task<Antiphon.Server.Application.Dtos.WorktreeInfo> CreateAsync(
            string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            CreateCalls++;
            return inner.CreateAsync(repoPath, cardId, baseRef, ct);
        }

        public Task<Antiphon.Server.Application.Dtos.WorktreeInfo> CreateAsync(
            string repoPath, string cardId, string baseRef, RepositoryLease lease, CancellationToken ct)
        {
            CreateCalls++;
            return inner.CreateAsync(repoPath, cardId, baseRef, lease, ct);
        }

        public Task<Antiphon.Server.Application.Dtos.WorktreeInfo> CreateVerificationAsync(
            string repoPath, string identifier, string sha, RepositoryLease lease, CancellationToken ct)
        {
            CreateCalls++;
            return inner.CreateVerificationAsync(repoPath, identifier, sha, lease, ct);
        }

        public Task<IReadOnlyList<Antiphon.Server.Application.Dtos.WorktreeInfo>> ListAsync(
            string repoPath, CancellationToken ct) => inner.ListAsync(repoPath, ct);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            inner.RemoveAsync(repoPath, worktreePath, ct);

        public Task TouchAsync(string worktreePath, CancellationToken ct) => inner.TouchAsync(worktreePath, ct);

        public Task<int> PruneStaleAsync(CancellationToken ct) => inner.PruneStaleAsync(ct);
    }

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
        var graph = DelegationTestServices.CreateGitGraph(new GitSettings { WorktreeBasePath = repo.WorktreeRoot }, db);
        var worktrees = graph.Worktrees;
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
            NullLogger<AgentTaskLandService>.Instance,
            new AgentTaskLandingProtocol(db, graph.Git, graph.Leases, graph.Manager, new LandingSafetyHarness.ControlledVerifier(), TimeProvider.System, graph.Journal,
                gitSettings: Options.Create(new GitSettings { WorktreeBasePath = repo.WorktreeRoot })),
            graph.Leases, graph.Git);
        return (land, worktrees);
    }

    private static async Task<string> RequiredGitAsync(string path, params string[] args)
    {
        var result = await ScratchGitRepo.GitInAsync(path, args);
        result.Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed: {result.StdErr}");
        return result.StdOut;
    }

    private static async Task<(LandRequestResult Request, string SourceSha, string SourcePath, string SourceBranch, string SourceFullRef)>
        PublishAndRequestAsync(AgentTaskLandService land, AgentTask task)
    {
        var sourcePath = task.WorktreePath!;
        var sourceBranch = task.WorktreeBranch!;
        var sourceFullRef = $"refs/heads/{sourceBranch}";
        var sourceSha = (await RequiredGitAsync(sourcePath, "rev-parse", "HEAD")).Trim();
        var pushUrl = (await RequiredGitAsync(sourcePath, "remote", "get-url", "--push", "origin")).Trim();
        await RequiredGitAsync(sourcePath, "push", pushUrl, $"HEAD:{sourceFullRef}");
        var request = await land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: sourceSha), CancellationToken.None);
        return (request, sourceSha, sourcePath, sourceBranch, sourceFullRef);
    }

    private static async Task<AgentTaskLandRequest> AssertRequestAsync(AppDbContext db, Guid taskId,
        Guid requestId, string sourceSha, string sourceFullRef)
    {
        var request = await db.AgentTaskLandRequests.AsNoTracking()
            .SingleAsync(r => r.TaskId == taskId && r.Id == requestId);
        request.SchemaVersion.ShouldBe(2);
        request.ExpectedSourceSha.ShouldBe(sourceSha);
        request.SourceFullRefSnapshot.ShouldBe(sourceFullRef);
        return request;
    }

    private static async Task<AgentTaskLanding> AssertBoundOperationAsync(AppDbContext db, Guid taskId,
        Guid requestId, string sourceSha, string sourceFullRef)
    {
        var request = await AssertRequestAsync(db, taskId, requestId, sourceSha, sourceFullRef);
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == taskId);
        operation.SchemaVersion.ShouldBe(3);
        operation.ApprovalLandRequestId.ShouldBe(requestId);
        operation.OriginalSourceSha.ShouldBe(sourceSha);
        operation.ReviewedSourceSha.ShouldBe(sourceSha);
        operation.SourceLocalSha.ShouldBe(sourceSha);
        operation.SourceRemoteSha.ShouldBe(sourceSha);
        request.LandingOperationId.ShouldBe(operation.Id);
        request.SourceRefusalReason.ShouldBeNull();
        return operation;
    }

    private static async Task<AgentTaskEvent> AssertTerminalAsync(AppDbContext db, Guid taskId,
        Guid requestId, Guid? operationId, AgentTaskEventType type)
    {
        var request = await db.AgentTaskLandRequests.AsNoTracking()
            .SingleAsync(r => r.TaskId == taskId && r.Id == requestId);
        request.State.ShouldBe(LandRequestState.Completed);
        request.IsPending.ShouldBeFalse();
        request.TerminalEventId.ShouldNotBeNull();
        var terminal = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.Id == request.TerminalEventId && e.AgentTaskId == taskId);
        terminal.LandRequestId.ShouldBe(requestId);
        terminal.LandingOperationId.ShouldBe(operationId);
        terminal.Type.ShouldBe(type);
        return terminal;
    }

    private static async Task AssertUnchangedSourceAsync(string sourcePath, string sourceFullRef,
        string sourceSha, string file, string content)
    {
        Directory.Exists(sourcePath).ShouldBeTrue();
        (await RequiredGitAsync(sourcePath, "rev-parse", "HEAD")).Trim().ShouldBe(sourceSha);
        (await RequiredGitAsync(sourcePath, "rev-parse", sourceFullRef)).Trim().ShouldBe(sourceSha);
        (await File.ReadAllTextAsync(Path.Combine(sourcePath, file))).ShouldBe(content);
        (await RequiredGitAsync(sourcePath, "status", "--porcelain")).Trim().ShouldBeEmpty();
        (await RebaseStateDirectoryAsync(sourcePath)).ShouldBeNull();
    }

    private static async Task WriteExecutableHookAsync(string path, string script)
    {
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static (DelegationWorktreeService Service, WorktreeManager Manager) CreateService(
        ScratchGitRepo repo,
        int? worktreeAddTimeoutSeconds = null,
        GitWorkspaceService? workspaceGit = null)
    {
        var graph = DelegationTestServices.CreateGitGraph(new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot, WorktreeStaleAfterDays = 7,
            WorktreeJanitorIntervalHours = 24, WorktreeAddTimeoutSeconds = worktreeAddTimeoutSeconds ?? 180,
            DefaultBranch = "master",
        }, workspaceGit: workspaceGit);
        return (graph.Worktrees, graph.Manager);
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
