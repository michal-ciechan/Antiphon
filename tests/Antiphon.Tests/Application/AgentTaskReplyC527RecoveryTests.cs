using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments("check-ignore")]
    [Arguments("diff-tree")]
    [Arguments("history")]
    [Arguments("upstream")]
    public async Task C527_unavailable_child_audit_is_reported_to_parent(string inspection)
    {
        var seeded = await SeedC527Async(t => { t.Role = AgentTaskRole.Commit; t.CommitOnSettle = CommitOnSettlePolicy.Never; });
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.CommitFileAsync("a.md", "a");
        await using (var db = CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.CommitBaselineSha = baseline;
            task.CommitUpstreamBaselineJson = JsonSerializer.Serialize(new GitWorkspaceService.UpstreamSnapshot(true, null, null));
            await db.SaveChangesAsync();
        }
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => (inspection == "history" ? args[0] == "log" && args.Contains("-z")
            : inspection == "upstream" ? args[0] == "symbolic-ref" : args[0] == inspection)
            ? (-1, "", "inspection timeout") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Audit report.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, "Audit report.");
        receipt.Prompt.Text.ShouldContain("audit unavailable");
        if (inspection != "upstream") receipt.Prompt.Text.ShouldContain("inspection timeout");
    }

    [Test]
    [Arguments("settlement-before-save", false, false)]
    [Arguments("settlement-before-save", false, true)]
    [Arguments("settlement-before-save", true, false)]
    [Arguments("settlement-before-save", true, true)]
    [Arguments("settlement-saved", false, false)]
    [Arguments("settlement-saved", false, true)]
    [Arguments("settlement-saved", true, false)]
    [Arguments("settlement-saved", true, true)]
    [Arguments("before-enqueue", false, false)]
    [Arguments("before-enqueue", false, true)]
    [Arguments("before-enqueue", true, false)]
    [Arguments("before-enqueue", true, true)]
    [Arguments("queue-inserted", false, false)]
    [Arguments("queue-inserted", false, true)]
    [Arguments("queue-inserted", true, false)]
    [Arguments("queue-inserted", true, true)]
    [Arguments("receipt-before-save", false, false)]
    [Arguments("receipt-before-save", false, true)]
    [Arguments("receipt-before-save", true, false)]
    [Arguments("receipt-before-save", true, true)]
    public async Task C527_completion_outbox_recovers_complete_receipt(string failure, bool busy, bool childOutcome)
    {
        var seeded = await SeedC527Async(t =>
        {
            if (childOutcome)
            {
                t.Role = AgentTaskRole.Commit;
                t.CommitOnSettle = CommitOnSettlePolicy.Never;
            }
        });
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        if (childOutcome)
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.secret"), "audit fixture");
            await repo.GitAsync("add", "-f", "a.secret");
            await repo.GitAsync("commit", "-m", "ungated audit fixture");
            await using var seed = CreateContext();
            var task = await seed.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.CommitBaselineSha = baseline;
            task.CommitUpstreamBaselineJson = JsonSerializer.Serialize(new GitWorkspaceService.UpstreamSnapshot(true, null, null));
            await seed.SaveChangesAsync();
        }
        else
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");

        if (busy) await SeedEntryAsync(seeded.Parent, TranscriptKinds.AssistantText, "Still working.", DateTime.UtcNow);
        var boundary = new C527FailureBoundary(failure);
        var factory = C527Factory(repo.WorktreeRoot, boundary: boundary);
        var terminal = AttachTerminal(factory, seeded.Parent);
        // The parent has unattributable work, so its immutable outcome must name the actual spawned child.
        var report = childOutcome ? "Child audit completed." : "Work completed.";
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        if (failure == "settlement-before-save")
        {
            await using var unsaved = CreateContext();
            (await unsaved.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == seeded.Task.Id)).ShouldBeFalse();
            (await unsaved.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
            factory = C527Factory(repo.WorktreeRoot, boundary: boundary);
            terminal = AttachTerminal(factory, seeded.Parent);
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        }

        await using var verify = CreateContext();
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == seeded.Task.Id)).ShouldBe(1);
        var note = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == seeded.Task.Id);
        note.Kind.ShouldBe(LandNotificationKind.TaskCompletion);
        note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(report));
        note.ConfirmedAt.ShouldBeNull();
        var frozenBody = note.Body;
        if (childOutcome) frozenBody.ShouldContain("REVERT commit");
        else
        {
            var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
            frozenBody.ShouldContain("commit task " + DelegationReportFormatter.Short(child.Id));
            child.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
        }

        // Recreate the reader with a clock beyond its persisted retry time, as the hosted scan does.
        await C527ReconcileAsync(factory, note.Id);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        if (busy)
        {
            terminal.SubmittedBodies.ShouldBeEmpty();
            await SeedEntryAsync(seeded.Parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
            await Queue(factory).OnTurnEndAsync(seeded.Parent, CancellationToken.None);
        }
        if (failure == "receipt-before-save")
        {
            await C527ReconcileAsync(factory, note.Id, boundary);
            (await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
        }
        await C527ReconcileAsync(factory, note.Id);
        await C527ReconcileAsync(factory, note.Id);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        boundary.Hits.ShouldBe(1);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Note.SourceLandNotificationId.ShouldBe(note.Id);
        receipt.Prompt.Text.ShouldBe(frozenBody);
        var confirmed = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        confirmed.ConfirmedAt.ShouldNotBeNull();
        confirmed.ConfirmingPromptSequence.ShouldBe(receipt.Prompt.Sequence);
        (await verify.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(1);
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    private static async Task C527ReconcileAsync(TestScopeFactory factory, Guid id, LandDeliveryBoundary? boundary = null)
    {
        await using var db = CreateContext();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10));
        var service = new AgentTaskLandNotificationService(db, Queue(factory), new CompletionNoteFlushQueue(),
            factory.ServiceProvider.GetRequiredService<AgentSessionRuntime>(), clock, boundary);
        await service.ReconcileAsync(id, CancellationToken.None);
    }

    private sealed class C527FailureBoundary(string target) : LandDeliveryBoundary
    {
        public int Hits { get; private set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == target && Hits == 0)
            {
                Hits++;
                throw new InvalidOperationException("C527 injected interruption at " + boundary);
            }
            return Task.CompletedTask;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C527_partial_or_incidental_task_commit_does_not_hide_dirty_work(bool explicitCommit)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var factory = C527Factory(repo.WorktreeRoot);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "first.md"), "first");
        if (explicitCommit)
        {
            var result = await factory.ServiceProvider.GetRequiredService<GatedCommitService>().CommitAsync(
                repo.Path, ["first.md"], "Partial work",
                [("antiphon", "true"), ("antiphon-task", seeded.Task.Id.ToString("D")), ("antiphon-commit", "gated")],
                CancellationToken.None);
            result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        }
        else
        {
            await repo.GitAsync("add", "first.md");
            await repo.GitAsync("commit", "-m", "Incidental reference to " + seeded.Task.Id);
        }
        var previous = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "remaining.md"), "remaining");
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `remaining.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldNotBe(previous);
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("remaining.md");
        (await repo.GitReadAsync("status", "--porcelain")).ShouldBeEmpty();
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain("antiphon-settlement:");
    }

    [Test]
    public async Task C527_recovered_settlement_reports_new_dirty_work_without_committing_it()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        var failed = C527Factory(repo.WorktreeRoot, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `a.md`.");
        await CreateService(failed).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var committed = await repo.GitReadAsync("rev-parse", "HEAD");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "another writer's edit");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "other.md"), "other writer");
        var recovery = C527Factory(repo.WorktreeRoot);
        AttachTerminal(recovery, seeded.Parent);
        await CreateService(recovery).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(recovery).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, "Wrote `a.md`.");
        receipt.Prompt.Text.ShouldContain("2 dirty path(s) remain after recovered settlement");
        receipt.Prompt.Text.ShouldContain("other.md");
        receipt.Prompt.Text.ShouldContain("a.md");
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(committed);
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "a.md"))).ShouldBe("another writer's edit");
    }

    [Test]
    [Arguments("ahead")]
    [Arguments("behind")]
    [Arguments("moved-to-old-head")]
    [Arguments("removed")]
    [Arguments("added")]
    [Arguments("changed-ref")]
    public async Task C527_spawned_child_audit_compares_independent_upstream_baselines(string variant)
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        if (variant != "added") await repo.AddBareOriginAsync();
        var old = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.CommitFileAsync("ahead.md", "ahead");
        var advanced = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        if (variant == "behind")
        {
            await repo.GitAsync("update-ref", "refs/remotes/origin/master", advanced);
            await repo.GitAsync("reset", "--hard", old);
        }
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Work completed.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var db = CreateContext();
        var child = await db.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        var snapshot = JsonSerializer.Deserialize<GitWorkspaceService.UpstreamSnapshot>(child.CommitUpstreamBaselineJson!)!;
        snapshot.Succeeded.ShouldBeTrue();
        if (variant is "ahead" or "behind") snapshot.Sha.ShouldNotBe(child.CommitBaselineSha);
        if (variant == "moved-to-old-head") await repo.GitAsync("update-ref", "refs/remotes/origin/master", advanced);
        if (variant == "removed") await repo.GitAsync("branch", "--unset-upstream");
        if (variant == "added") await repo.AddBareOriginAsync();
        if (variant == "changed-ref")
        {
            await repo.GitAsync("update-ref", "refs/remotes/origin/other", old);
            await repo.GitAsync("branch", "--set-upstream-to=origin/other");
        }
        var childSession = await SeedSessionAsync(repo.Path);
        child.AgentSessionId = childSession;
        child.Status = AgentTaskStatus.Dispatched;
        child.DispatchedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await SeedTurnAsync(childSession, DelegationReportFormatter.TaskMarker(child.Id), "No commit needed.");
        await CreateService(factory).OnTurnEndAsync(childSession, CancellationToken.None);
        var warnings = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == child.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("upstream")).Select(e => e.Detail).ToListAsync();
        if (variant is "ahead" or "behind") warnings.ShouldBeEmpty();
        else
        {
            warnings.ShouldHaveSingleItem();
            warnings[0].ShouldContain(variant == "moved-to-old-head" ? "upstream moved" : "upstream configuration changed");
            warnings[0].ShouldContain("child action is unproven");
            warnings[0].ShouldNotContain("pushed");
        }
    }
}
