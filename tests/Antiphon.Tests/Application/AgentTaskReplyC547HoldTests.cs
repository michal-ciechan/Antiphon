using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0547 S3 end to end: a real gated commit whose settlement save failed, the overdue watchdog
// from OverdueSweepHarness over the same database, and the parent's terminal on the reply side.
// The class is global NotInParallel + ProcessSpawnLimit, which a fleet-global sweep needs.
public partial class AgentTaskReplyIntegrationTests
{
    private const string C547Report = "Wrote `a.md`.";

    private static async Task<(ScratchGitRepo Repo, Server.Domain.Entities.AgentTask Task, Guid SessionId, Guid Parent,
        RecordingGitWorkspaceService Spy, string Committed)> SettleInterruptedPastTheCeilingAsync()
    {
        // DispatchedAt is part of the settlement digest, so it is back-dated before settling.
        var seeded = await SeedC527Async(t => t.DispatchedAt = DateTime.UtcNow.AddMinutes(-150_000), "c547");
        var repo = seeded.Repo;
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), C547Report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await AssertC527RecoveryPendingAsync(seeded.Task.Id);
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        return (repo, seeded.Task, seeded.SessionId, seeded.Parent, spy, committed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it(bool busy)
    {
        var (repo, task, sessionId, parent, spy, committed) = await SettleInterruptedPastTheCeilingAsync();
        using var _ = repo;
        Guid startedId;
        string digest;
        await using (var db = CreateContext())
        {
            var started = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted);
            (startedId, digest) = (started.Id, started.Detail);
            await db.AgentTaskEvents.Where(e => e.Id == startedId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.At, DateTime.UtcNow.AddMinutes(-800)));
        }
        if (busy) await SeedEntryAsync(parent, TranscriptKinds.AssistantText, "still working", DateTime.UtcNow);

        // Git is live: the CARD-0085 bind-refusal gate could find the gated commit by its
        // "task <short>:" subject, so this proves the watchdog skips that recovery for a task
        // holding an obligation and reaches the abandonment instead.
        var sweep = OverdueSweepHarness.Create(gitSpy: spy);
        await using (sweep.Provider)
            await sweep.Dispatcher.FailOverdueTasksAsync(CancellationToken.None);

        Server.Domain.Entities.AgentTask stored;
        await using (var db = CreateContext())
        {
            stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            stored.Status.ShouldBe(AgentTaskStatus.Failed);
            stored.FailureReason.ShouldNotBeNull();
            stored.FailureReason.ShouldContain(startedId.ToString("D"));
            stored.FailureReason.ShouldContain(digest);
            var abandoned = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryAbandoned);
            abandoned.Detail.ShouldStartWith($"{startedId:D} ");
            abandoned.Detail.ShouldContain("overdue-task deadline");
        }
        sweep.Stopper.Killed.ShouldBeEmpty();

        var reply = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(reply, parent);
        await Queue(reply).FlushSessionAsync(parent, CancellationToken.None);
        if (busy)
        {
            terminal.SubmittedBodies.ShouldBeEmpty();
            await SeedEntryAsync(parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
            await Queue(reply).OnTurnEndAsync(parent, CancellationToken.None);
        }
        var receipt = await AssertParentReceivedNoteAsync(parent, task, stored.FailureReason!);
        receipt.Prompt.Text.ShouldContain($"git=commit-recovery-abandoned:{startedId.ToString("N")[..8]}");
        receipt.Prompt.Text.ShouldContain(digest);
        receipt.Prompt.Text.ShouldContain("git log --all --reflog");
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");

        await CreateService(reply).OnTurnEndAsync(sessionId, CancellationToken.None);
        await Queue(reply).FlushSessionAsync(parent, CancellationToken.None);
        terminal.SubmittedBodies.Count.ShouldBe(1);
        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
    }

    [Test]
    public async Task C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it()
    {
        var (repo, task, sessionId, parent, spy, committed) = await SettleInterruptedPastTheCeilingAsync();
        using var _ = repo;
        Server.Domain.Entities.AgentTask before;
        DateTime startedAt;
        await using (var db = CreateContext())
        {
            before = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            startedAt = (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.CommitRecoveryStarted)).At;
        }

        var sweep = OverdueSweepHarness.Create(gitSpy: spy);
        await using (sweep.Provider)
            await sweep.Dispatcher.FailOverdueTasksAsync(CancellationToken.None);

        await using (var db = CreateContext())
        {
            var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            held.Status.ShouldBe(AgentTaskStatus.Dispatched);
            held.AgentSessionId.ShouldBe(before.AgentSessionId);
            held.DispatchedAt.ShouldBe(before.DispatchedAt);
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id && (e.Type == AgentTaskEventType.Failed
                || e.Type == AgentTaskEventType.CommitRecoveryNotNeeded
                || e.Type == AgentTaskEventType.CommitRecoveryAbandoned))).ShouldBeFalse();
            (await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == parent
                && m.Origin == QueuedMessageOrigin.Delegation)).ShouldBeFalse();
        }
        sweep.Stopper.Killed.ShouldBeEmpty();

        var rehand = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(rehand, parent);
        await CreateService(rehand).OnTurnEndAsync(sessionId, CancellationToken.None);
        await Queue(rehand).FlushSessionAsync(parent, CancellationToken.None);

        await AssertC527RecoveredReceiptAsync(task, parent, C547Report, committed, 1);
        await using var verify = CreateContext();
        (await CommitRecoveryObligations.LoadUnresolvedAsync(verify, task.Id, CancellationToken.None)).ShouldBeEmpty();
        (await verify.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Committed))
            .At.ShouldBeGreaterThan(startedAt);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
    }
}
