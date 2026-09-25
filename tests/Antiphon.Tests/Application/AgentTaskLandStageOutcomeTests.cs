using Antiphon.Server.Application.Dtos;
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
/// CARD-0272 S1: the land operation writes one StageOutcome row per step it actually ran.
/// Real git, isolated schema — the rows are the thing under test, not the rebase itself
/// (that stays in <see cref="DelegationWorktreeTests"/>).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskLandStageOutcomeTests
{
    [Test]
    public async Task land_happy_path_writes_rebase_verify_cleanup_rows()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-happy");
        using var remote = new TemporaryDirectory("antiphon-land-so-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await SeedBuildableAsync(repo);
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        await RequestHeadAsync(land, task);
        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Complete);
        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome, o.Source)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean, StageOutcomeSource.Server),
            (OrchestrationStage.Verify, StageOutcomeKind.Clean, StageOutcomeSource.Server),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean, StageOutcomeSource.Server),
        ]);
        rows.Single(o => o.Stage == OrchestrationStage.Verify).Detail.ShouldContain("build OK");
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);
    }

    [Test]
    public async Task land_conflict_writes_rebase_found()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-conflict");
        using var remote = new TemporaryDirectory("antiphon-land-so-cremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "shared.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.ShouldHaveSingleItem();
        rows[0].Stage.ShouldBe(OrchestrationStage.Rebase);
        rows[0].Outcome.ShouldBe(StageOutcomeKind.Found);
        rows[0].Detail.ShouldContain("shared.md");
        rows[0].Ref.ShouldNotBeNull();
        rows[0].Ref.ShouldNotBe("merge task cap reached");
        (await db.AgentTasks.CountAsync(t => t.ParentTaskId == task.Id && t.Role == AgentTaskRole.Merge))
            .ShouldBe(1);
        var blocked = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.LandRequestedAt.ShouldNotBeNull();
        blocked.LandAttempt.ShouldBe(1);
    }

    [Test]
    public async Task unreadable_push_endpoint_refuses_before_any_stage()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-push");
        using var remote = new TemporaryDirectory("antiphon-land-so-premote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        // The push endpoint is the only endpoint landing reads (CARD-0488 D-3: the remote source is
        // observed on origin's push URL, never the fetch URL), so an unusable push URL is refused
        // by source resolution before any operation, stage row or Git mutation exists. The fetch
        // URL stays the bare remote so RequestHeadAsync can still publish the source; a rival push
        // before RunAsync would be an origin-ahead refusal instead.
        await repo.GitAsync("remote", "set-url", "--push", "origin", Path.Combine(remote.Path, "no-such-remote.git"));

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.ShouldBeEmpty("unreadable push endpoint is refused before preparation or verification");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == task.Id)).ShouldBe(0, "refused before any operation exists");
        var refused = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldContain("source_remote_unreadable");
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);
    }

    [Test]
    public async Task land_verify_failure_writes_verify_found()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-red");
        using var remote = new TemporaryDirectory("antiphon-land-so-rremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Found),
        ]);
        var verify = rows.Single(o => o.Stage == OrchestrationStage.Verify);
        verify.Detail.ShouldContain("build failed");
        verify.Ref.ShouldBe(task.WorktreePath);
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);
    }

    [Test]
    public async Task a_clean_change_lands_and_skips_verify_when_the_base_did_not_move()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-clean");
        using var remote = new TemporaryDirectory("antiphon-land-so-clremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Skipped),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean),
        ]);
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);
    }

    [Test]
    public async Task locked_source_lands_and_keeps_its_worktree_as_cleanup_residue()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-residue");
        using var remote = new TemporaryDirectory("antiphon-land-so-reresidue");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        (await ScratchGitRepo.GitInAsync(repo.Path, "worktree", "lock", task.WorktreePath!)).Ok.ShouldBeTrue();

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        // CARD-0688 D-2 / I-3: the source is the branch ref, so a locked task worktree no longer blocks publication;
        // guarded cleanup refuses the locked registration and keeps the worktree (was registration_unavailable).
        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldContain((OrchestrationStage.Cleanup, StageOutcomeKind.Failed));
        var outcome = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandedWithResidue);
        outcome.Detail.ShouldContain("registration_locked");
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(remote.Path, "show", "master:feature.md")).Ok.ShouldBeTrue();
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);

        var queued = await RequestHeadAsync(land, task);
        queued.Status.ShouldBe("queued");
    }

    [Test]
    public async Task reland_of_an_already_landed_task_runs_cleanup_only()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-reland");
        using var remote = new TemporaryDirectory("antiphon-land-so-reremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");

        await RequestHeadAsync(land, task);
        await land.RunAsync(task.Id, null, CancellationToken.None);
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
        var afterFirst = await RowsAsync(db, task.Id);
        afterFirst.Count.ShouldBe(3);
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);

        await land.RequestAsync(task.Id, new LandAgentTaskRequest(), CancellationToken.None);
        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Count.ShouldBe(4);
        rows[3].Stage.ShouldBe(OrchestrationStage.Cleanup);
        rows[3].Outcome.ShouldBe(StageOutcomeKind.Clean);
        rows[3].Detail.ShouldContain("cleanup complete");
        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldContain("mode=Fresh");
        landed.Detail.ShouldContain("cleanup=Complete");
        var cleanup = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandingCleanup)
            .OrderByDescending(e => e.At)
            .FirstAsync();
        cleanup.Detail.ShouldContain("mode=CleanupRetry");
        cleanup.Detail.ShouldContain("cleanup=Complete");
        cleanup.LandingMode.ShouldBe(LandOperationMode.CleanupRetry);
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
        await AssertPendingClearedAsync(db, task.Id, attempt: 1);
    }

    [Test]
    public async Task land_warns_when_a_same_card_kept_branch_is_not_an_ancestor()
    {
        using var repo = new ScratchGitRepo("antiphon-land-sib-warn");
        using var remote = new TemporaryDirectory("antiphon-land-sib-wremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var card = await SeedCardAsync(db);
        var sibling = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await File.WriteAllTextAsync(Path.Combine(sibling.WorktreePath!, "plan.md"), "the plan\n");
        await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "add", "plan.md");
        await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "commit", "-m", "docs(plan): CARD-0215");

        var build = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await File.WriteAllTextAsync(Path.Combine(build.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(build.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(build.WorktreePath!, "commit", "-m", "feature");

        await RequestHeadAsync(land, build);
        await land.RunAsync(build.Id, null, CancellationToken.None);

        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldContain(
            $"unlanded-sibling={DelegationReportFormatter.Short(sibling.Id)}:{sibling.WorktreeBranch}");
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
        warning.Detail.ShouldContain(DelegationReportFormatter.Short(sibling.Id));
    }

    [Test]
    public async Task land_is_silent_when_the_sibling_was_landed_first()
    {
        using var repo = new ScratchGitRepo("antiphon-land-sib-first");
        using var remote = new TemporaryDirectory("antiphon-land-sib-fremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var card = await SeedCardAsync(db);
        var sibling = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await File.WriteAllTextAsync(Path.Combine(sibling.WorktreePath!, "plan.md"), "the plan\n");
        await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "add", "plan.md");
        await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "commit", "-m", "docs(plan): CARD-0215");

        await RequestHeadAsync(land, sibling);
        await land.RunAsync(sibling.Id, null, CancellationToken.None);
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == sibling.Id && e.Type == AgentTaskEventType.Landed)).ShouldBe(1);

        var build = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await File.WriteAllTextAsync(Path.Combine(build.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(build.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(build.WorktreePath!, "commit", "-m", "feature");

        await RequestHeadAsync(land, build);
        await land.RunAsync(build.Id, null, CancellationToken.None);

        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldNotContain("unlanded-sibling=");
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
    }

    [Test]
    public async Task a_held_land_leaves_started_at_and_attempt_untouched()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-held");
        using var remote = new TemporaryDirectory("antiphon-land-so-hremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        db.AgentTasks.Add(new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = Guid.NewGuid(),
            Title = "shared writer",
            Goal = "Writing in the main checkout.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            Status = AgentTaskStatus.Working,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await RequestHeadAsync(land, task);
        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Held);
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        stored.LandRequestedAt.ShouldNotBeNull();
        stored.LandStartedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(0);
        (await db.StageOutcomes.CountAsync(o => o.SubjectTaskId == task.Id)).ShouldBe(0);
    }

    /// <summary>
    /// V-9 / G-23, G-43: the land-side sibling comparison is patch-aware and pinned to the
    /// operation's verified SHA. An all-minus (rebased/cherry-picked) sibling produces no marker
    /// and no warning; a minus+plus sibling still names its exact short id and branch. The
    /// component row calls <c>CollectUnlandedSiblingsAsync</c> directly with a frozen verified
    /// SHA while the repository HEAD has moved on to contain the patch: the frozen SHA decides.
    /// One method per row (CARD-0567): two real lands plus the component row did not fit one
    /// 180 s budget on a loaded host, and a timeout hid which row was at fault.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C508_RebasedSiblingMarkerMatrix_AllMinusSiblingIsSilent()
    {
        // The build replays the sibling's patch, so nothing is left behind.
        using var repo = new ScratchGitRepo("c508-ls-allminus");
        using var remote = new TemporaryDirectory("c508-ls-allminus-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var card = await SeedCardAsync(db);
        var sibling = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await CommitInAsync(sibling.WorktreePath!, "plan.md", "the plan\n", "docs(plan): CARD-0215");
        var siblingTip = (await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();

        // The build task is cut independently from M, never from the sibling, and carries the
        // same patch by content. That is what makes git cherry report '-' and not '+'.
        var build = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        (await ScratchGitRepo.GitInAsync(build.WorktreePath!, "merge-base", "--is-ancestor", siblingTip, "HEAD"))
            .Ok.ShouldBeFalse("the build worktree must not be cut from the sibling");
        await CommitInAsync(build.WorktreePath!, "plan.md", "the plan\n", "docs(plan): CARD-0215 (replayed)");
        await CommitInAsync(build.WorktreePath!, "feature.md", "the work\n", "feature");

        await RequestHeadAsync(land, build);
        await land.RunAsync(build.Id, null, CancellationToken.None);

        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldNotContain("unlanded-sibling=");
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
        // The sibling branch itself is never touched by the land.
        (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", sibling.WorktreeBranch!)).StdOut.Trim()
            .ShouldBe(siblingTip);
        // The remote target really has the build content, not just a marker-free event.
        (await ScratchGitRepo.GitInAsync(remote.Path, "show", "master:feature.md")).StdOut
            .ShouldBe("the work\n");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C508_RebasedSiblingMarkerMatrix_MixedSiblingKeepsMarker()
    {
        // Minus + plus: one patch replayed, one still only on the sibling.
        using var repo = new ScratchGitRepo("c508-ls-mixed");
        using var remote = new TemporaryDirectory("c508-ls-mixed-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var card = await SeedCardAsync(db);
        var sibling = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await CommitInAsync(sibling.WorktreePath!, "plan.md", "the plan\n", "docs(plan): CARD-0215");
        await CommitInAsync(sibling.WorktreePath!, "extra.md", "only on the sibling\n", "docs(plan): extra");
        var siblingTip = (await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();

        var build = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await CommitInAsync(build.WorktreePath!, "plan.md", "the plan\n", "docs(plan): CARD-0215 (replayed)");
        await CommitInAsync(build.WorktreePath!, "feature.md", "the work\n", "feature");

        await RequestHeadAsync(land, build);
        await land.RunAsync(build.Id, null, CancellationToken.None);

        var landed = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Landed);
        landed.Detail.ShouldContain(
            $"unlanded-sibling={DelegationReportFormatter.Short(sibling.Id)}:{sibling.WorktreeBranch}");
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == build.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
        warning.Detail.ShouldContain(DelegationReportFormatter.Short(sibling.Id));
        (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", sibling.WorktreeBranch!)).StdOut.Trim()
            .ShouldBe(siblingTip);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C508_RebasedSiblingMarkerMatrix_PinnedVerifiedShaDecides()
    {
        // Component: the frozen verified SHA decides, not a moving HEAD.
        using var repo = new ScratchGitRepo("c508-ls-pinned");
        await repo.CommitFileAsync("README.md", "base\n");
        var pinnedM = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var card = await SeedCardAsync(db);
        var sibling = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);
        await CommitInAsync(sibling.WorktreePath!, "plan.md", "the plan\n", "docs(plan): CARD-0215");
        var siblingTip = (await ScratchGitRepo.GitInAsync(sibling.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();
        var build = await SeedSucceededWorktreeAsync(db, worktrees, repo, card.Id);

        // HEAD moves on to contain the sibling's patch; the pinned M still does not.
        await repo.GitAsync("cherry-pick", siblingTip);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldNotBe(pinnedM);

        var pinned = await CollectAsync(land, build, repo.Path, pinnedM);
        pinned.Marker.ShouldBe(
            $"unlanded-sibling={DelegationReportFormatter.Short(sibling.Id)}:{sibling.WorktreeBranch}");
        pinned.Warnings.Count.ShouldBe(1);
        pinned.Warnings[0].ShouldContain(sibling.WorktreeBranch!);

        // The same inputs read against the moving HEAD are silent — so the pinned row above is
        // the frozen SHA doing the work, not an unconditional warning.
        var moving = await CollectAsync(land, build, repo.Path, verifiedSha: null);
        moving.Marker.ShouldBeNull();
        moving.Warnings.ShouldBeEmpty();
    }

    private static async Task CommitInAsync(string worktree, string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(worktree, file), content);
        (await ScratchGitRepo.GitInAsync(worktree, "add", file)).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(worktree, "commit", "-m", message)).Ok.ShouldBeTrue();
    }

    /// <summary>
    /// Invokes the land-side collector with real git/DB inputs through its internal seam. This
    /// is a component row, not a land: it supplies the frozen verified SHA directly so the pinning
    /// can be separated from everything else the land does, and formats the marker with the same
    /// production helper the land uses, so a signature or format change fails at compile time
    /// rather than by reflection at run time (CARD-0567).
    /// </summary>
    private static async Task<(string? Marker, IReadOnlyList<string> Warnings)> CollectAsync(
        AgentTaskLandService land, AgentTask task, string rebasedHeadRepo, string? verifiedSha)
    {
        var (siblings, warnings) = await land.CollectUnlandedSiblingsAsync(
            task, rebasedHeadRepo, CancellationToken.None, verifiedSha);
        return (AgentTaskLandService.UnlandedMarker(siblings), warnings);
    }

    private static async Task<AgentTask> SeedSucceededWorktreeAsync(
        AppDbContext db, DelegationWorktreeService worktrees, ScratchGitRepo repo, Guid? cardId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "land stage-outcome task",
            Goal = "Land me.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            CardId = cardId,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };
        await worktrees.CreateForTaskAsync(task, CancellationToken.None);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

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
            new AgentTaskLandingProtocol(db, graph.Git, graph.Leases, graph.Manager, new LandingVerifier(), TimeProvider.System, graph.Journal,
                gitSettings: Options.Create(new GitSettings { WorktreeBasePath = repo.WorktreeRoot })),
            graph.Leases, graph.Git);
        return (land, worktrees);
    }

    private static async Task<LandRequestResult> RequestHeadAsync(AgentTaskLandService land, AgentTask task)
    {
        var sha = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        var branch = task.WorktreeBranch!.StartsWith("refs/", StringComparison.Ordinal)
            ? task.WorktreeBranch : "refs/heads/" + task.WorktreeBranch;
        var fetch = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "remote", "get-url", "origin")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "push", fetch, $"HEAD:{branch}")).Ok.ShouldBeTrue();
        return await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: sha), CancellationToken.None);
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"land-sib-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/land-sib.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"CARD-0215 land {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = "CARD-0215",
            Title = "CARD-0215 ancestry",
            Description = "Land sibling probe.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task SeedBuildableAsync(ScratchGitRepo repo)
    {
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "LandProbe.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "Marker.cs"),
            "namespace LandProbe; public static class Marker { public static int Value => 1; }\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "buildable");
    }

    private static async Task AssertPendingClearedAsync(AppDbContext db, Guid taskId, int attempt)
    {
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        stored.LandRequestedAt.ShouldBeNull();
        stored.LandVerifyFilter.ShouldBeNull();
        stored.LandStartedAt.ShouldBeNull();
        stored.LandAttempt.ShouldBe(attempt);
    }

    private static async Task<List<StageOutcome>> RowsAsync(AppDbContext db, Guid taskId) =>
        await db.StageOutcomes.AsNoTracking()
            .Where(o => o.SubjectTaskId == taskId)
            .OrderBy(o => o.RecordedAt).ThenBy(o => o.Stage)
            .ToListAsync();

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

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
}
