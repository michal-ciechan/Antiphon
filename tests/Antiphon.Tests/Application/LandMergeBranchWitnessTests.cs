using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.DelegationWorktreeTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1637 mutation survivors (Mutation 56bf971d, docs/investigations/2026-11-04-mutation-1637.md):
/// the conflicted-file summary budget, the Merge-role list sizing, the Rebase StageOutcome call site,
/// the target..source merge scan, a failed merge scan and a failed rebase with no unmerged paths.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public class LandMergeBranchWitnessTests
{
    // M4b: the last name that fits the bare budget still leaves the 32-character suffix reserve.
    [Test]
    public void Summary_keeps_its_count_when_the_names_fill_the_budget()
    {
        var names = Enumerable.Range(0, 25).Select(i => $"n/{i:D3}-".PadRight(198, 'x')).ToList();

        var summary = AgentTaskLandingProtocol.ConflictFileSummary(names);

        summary.ShouldBe(string.Join(", ", names.Take(9)) + ", ... and 16 more");
        summary.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.ConflictListMaxLength);
    }

    // M5: the documented contract is "never longer than maxLength", even below the suffix reserve.
    [Test]
    public void Summary_is_never_longer_than_a_tiny_budget()
    {
        var names = Enumerable.Range(0, 25).Select(i => $"n/{i:D3}.cs").ToList();

        var tiny = AgentTaskLandingProtocol.ConflictFileSummary(names, 10);

        tiny.Length.ShouldBeLessThanOrEqualTo(10);
        tiny.ShouldStartWith("... and");
        AgentTaskLandingProtocol.ConflictFileSummary(names, 0).ShouldBe("");
        AgentTaskLandingProtocol.ConflictFileSummary(names, -5).ShouldBe("");
    }

    // R1: a long source ref leaves less than 2000 characters for the list; the tail sentence survives the hard cut.
    [Test]
    public void Merge_role_list_budget_leaves_room_for_the_fixed_text()
    {
        var op = new AgentTaskLanding
        {
            SourceFullRef = "refs/heads/" + new string('s', 1500),
            TargetFullRef = "refs/heads/master",
            OriginalSourceSha = new string('a', 64),
            TargetBeforeSha = new string('b', 64),
        };
        var names = Enumerable.Range(0, 80).Select(i => $"long/w1637-{i:D3}-".PadRight(4096, 'x')).ToList();

        var detail = AgentTaskLandingProtocol.MergeRoleDetail(op, true, names);

        detail.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.MergeRoleDetailMaxLength);
        detail.ShouldContain(" more. ");
        detail.ShouldContain($"git push origin {op.OriginalSourceSha}:refs/heads/master");
        detail.ShouldEndWith("No rebase resolver was spawned.");
    }

    // R1b: fixed text over the hard cut gives a negative budget; the list still keeps its count.
    [Test]
    public void Merge_role_list_keeps_its_count_when_the_fixed_text_is_over_budget()
    {
        var op = new AgentTaskLanding
        {
            SourceFullRef = "refs/heads/feat/x",
            TargetFullRef = "refs/heads/" + new string('t', 4000),
            OriginalSourceSha = new string('a', 64),
            TargetBeforeSha = new string('b', 64),
        };

        var detail = AgentTaskLandingProtocol.MergeRoleDetail(op, true,
            Enumerable.Range(0, 80).Select(i => $"f{i}.cs").ToList());

        detail.ShouldContain("conflicted in ... and 80 more. ");
        detail.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.MergeRoleDetailMaxLength);
    }

    // C1: the Rebase StageOutcome carries the bounded summary with its count, not a raw join clipped by Record.
    [Test]
    public async Task Linear_conflict_stage_outcome_records_the_bounded_summary()
    {
        var names = LongConflictNames();
        using var repo = new ScratchGitRepo("antiphon-w1637-stage");
        using var remote = new TemporaryDirectory("antiphon-w1637-stage-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await WriteAllAsync(repo.Path, names, "base\n");
        await repo.CommitFileAsync("shared.md", "base\n");
        await PublishTargetAsync(repo, remote);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = await CreateTaskAsync(db, service, repo);
        await WriteAllAsync(task.WorktreePath!, names, "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", ".");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edits");
        await WriteAllAsync(repo.Path, names, "target version\n");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_conflict");
        var outcome = await db.StageOutcomes.AsNoTracking()
            .SingleAsync(o => o.SubjectTaskId == task.Id && o.Stage == OrchestrationStage.Rebase);
        outcome.Detail.ShouldBe(AgentTaskLandingProtocol.ConflictFileSummary(names, StageOutcome.DetailMaxLength));
        outcome.Detail.Length.ShouldBeLessThanOrEqualTo(StageOutcome.DetailMaxLength);
        outcome.Detail.ShouldEndWith(", ... and 67 more");
    }

    // D3: merges on the target side (before and after the fork) are outside target..source; a linear branch
    // that conflicts still gets the rebase resolver path.
    [Test]
    public async Task Linear_branch_over_a_target_with_merges_still_gets_rebase_conflict()
    {
        using var repo = new ScratchGitRepo("antiphon-w1637-topology");
        using var remote = new TemporaryDirectory("antiphon-w1637-topology-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await MergeSideLineAsync(repo, "pre-side", "pre.md");
        await PublishTargetAsync(repo, remote);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = await CreateTaskAsync(db, service, repo);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await MergeSideLineAsync(repo, "post-side", "post.md");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        (await RequiredGitAsync(task.WorktreePath!, "rev-list", "--min-parents=2", "HEAD")).Trim().ShouldNotBeEmpty();
        (await RequiredGitAsync(task.WorktreePath!, "rev-list", "--min-parents=2", "origin/master..HEAD")).Trim().ShouldBeEmpty();
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_conflict");
        (await AssertRequestAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef)).State
            .ShouldBe(LandRequestState.NeedsResolution);
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.LandRequestId == request.RequestId && e.Type == AgentTaskEventType.Conflicted)).ShouldBe(1);
    }

    // D6 and D7b: a merge scan that fails refuses as merge_scan_failed (never reads as "no merges", which would
    // spawn a rebase resolver for a merge branch), and the rebase was already aborted when the scan ran.
    [Test]
    public async Task Failed_merge_scan_refuses_after_the_rebase_abort()
    {
        using var repo = new ScratchGitRepo("antiphon-w1637-scan");
        using var remote = new TemporaryDirectory("antiphon-w1637-scan-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await PublishTargetAsync(repo, remote);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var git = new FailingMergeScanGit();
        var (land, service) = CreateLand(db, repo, git);
        var task = await CreateTaskAsync(db, service, repo);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "merge", "--no-edit", "master")).Ok.ShouldBeFalse();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "merged version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "--no-edit");
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        git.Faults.ShouldBe(1, "the land must run the merge scan exactly once");
        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("merge_scan_failed");
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.PushStartedAt.ShouldBeNull();
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.ParentTaskId == task.Id))
            .ShouldBe(0, "a failed merge scan must never get a rebase resolver");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Conflicted)).ShouldBe(0);
        Directory.Exists(operation.LandWorktreePath).ShouldBeTrue();
        (await RebaseStateDirectoryAsync(operation.LandWorktreePath!))
            .ShouldBeNull("the rebase must be aborted before the merge scan can refuse");
    }

    // D10: a merge branch whose rebase fails with no unmerged paths (a failing prepare-commit-msg hook stops the
    // first pick) stays rebase_failed; it is not a conflict, so the Merge-role refusal does not apply.
    [Test]
    public async Task Merge_branch_rebase_failure_without_conflicts_stays_rebase_failed()
    {
        using var repo = new ScratchGitRepo("antiphon-w1637-failed");
        using var remote = new TemporaryDirectory("antiphon-w1637-failed-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await PublishTargetAsync(repo, remote);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = await CreateTaskAsync(db, service, repo);
        await RequiredGitAsync(task.WorktreePath!, "checkout", "-b", "side");
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "side.md"), "side\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "side.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "side edit");
        await RequiredGitAsync(task.WorktreePath!, "checkout", task.WorktreeBranch!);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "task.md"), "task\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "task.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await RequiredGitAsync(task.WorktreePath!, "merge", "--no-ff", "--no-edit", "side");
        await repo.CommitFileAsync("target.md", "target\n");
        await repo.GitAsync("push", "origin", "master");
        var targetSha = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, _, _, sourceFullRef) = await PublishAndRequestAsync(land, task);
        var hooks = (await RequiredGitAsync(repo.Path, "rev-parse", "--path-format=absolute", "--git-path", "hooks")).Trim();
        Directory.CreateDirectory(hooks);
        await WriteExecutableHookAsync(Path.Combine(hooks, "prepare-commit-msg"), "#!/bin/sh\nexit 1\n");

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("rebase_failed");
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.PushStartedAt.ShouldBeNull();
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.Detail.Contains("merge_branch_needs_merge_role"))).ShouldBe(0);
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(targetSha);
        (await RebaseStateDirectoryAsync(operation.LandWorktreePath!)).ShouldBeNull();
    }

    private static async Task PublishTargetAsync(ScratchGitRepo repo, TemporaryDirectory remote)
    {
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");
    }

    private static async Task MergeSideLineAsync(ScratchGitRepo repo, string branch, string file)
    {
        await repo.GitAsync("checkout", "-b", branch);
        await repo.CommitFileAsync(file, branch + "\n");
        await repo.GitAsync("checkout", "master");
        await repo.GitAsync("merge", "--no-ff", "--no-edit", branch);
    }

    private static async Task<AgentTask> CreateTaskAsync(AppDbContext db, DelegationWorktreeService service, ScratchGitRepo repo)
    {
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    /// <summary>The real landing git, except that the merge-commit scan fails as an unreadable history would.</summary>
    private sealed class FailingMergeScanGit : LandingGit
    {
        private int _faults;
        public int Faults => Volatile.Read(ref _faults);

        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments is ["rev-list", "--min-parents=2", ..])
            {
                Interlocked.Increment(ref _faults);
                return Task.FromResult(new LandingGitResult(128, "", "fatal: injected merge scan failure"));
            }
            return base.RunAsync(repository, arguments, ct);
        }
    }
}
