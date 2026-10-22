using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.AgentTaskReplyIntegrationTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1474. The one closing-line nudge is a request for a missing report. When a valid report
/// settles the task first, the still-queued, never-typed ask and the parent's matching waiting
/// note are obsolete and must not reach either session. Observed: task ca843435 settled at
/// 07:19:33.048Z and the stale nudge was typed 87 ms later, prompting a second full report.
/// Only never-typed rows bound to this task's recorded identity may be retired.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public class ClosingReportNudgeLifecycleTests
{
    private const string NudgeText = "Your turn ended without the closing report line.";

    [Test]
    public async Task Valid_report_cancels_unsent_child_and_parent_wait_notes()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        await SeedParentHistoryAsync(parent);
        var (task, session) = await SeedDispatchedTaskAsync(workspace.Path, parent);
        using var factory = NewDeliveryFactory();
        var child = AttachTerminal(factory, session);
        var parentTerminal = AttachTerminal(factory, parent);

        var phantom = await SeedPhantomBoundaryAsync(session, task);
        var nudgeId = await EnqueueRecordedNudgeAsync(factory, task, session, phantom);
        var waitId = await EnqueueParentWaitNoteAsync(factory, task, parent);
        const string report = "Findings: the stale ask outlived its report.";
        await SeedMarkedReportAsync(session, task, report);

        await CreateService(factory).OnTurnEndAsync(session, CancellationToken.None);

        await using (var verify = CreateContext())
        {
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
            stored.ReportNudgeMessageId.ShouldBe(nudgeId, "the nudge identity stays on the record");
            var nudge = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == nudgeId);
            nudge.Status.ShouldBe(QueuedMessageStatus.Canceled,
                "a valid report makes the never-typed closing-line ask obsolete");
            nudge.DeliveryAttempts.ShouldBe(0);
            var wait = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == waitId);
            wait.Status.ShouldBe(QueuedMessageStatus.Canceled,
                "the parent must not hear an obsolete idle wait for a settled task");
        }

        await Queue(factory).FlushSessionAsync(session, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(parent, CancellationToken.None);
        child.SubmittedBodies.ShouldNotContain(b => b.Contains(NudgeText, StringComparison.Ordinal));
        parentTerminal.SubmittedBodies.ShouldNotContain(b => b.Contains(WaitHeader(task), StringComparison.Ordinal));
        parentTerminal.SubmittedBodies.ShouldContain(b => b.Contains(report, StringComparison.Ordinal),
            "the completion note itself still reaches the parent");
    }

    [Test]
    public async Task Sent_or_attempted_nudge_is_not_retyped_or_deleted()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        var (task, session) = await SeedDispatchedTaskAsync(workspace.Path, parent);
        using var factory = new TestScopeFactory();

        var phantom = await SeedPhantomBoundaryAsync(session, task);
        var nudgeId = await EnqueueRecordedNudgeAsync(factory, task, session, phantom);
        var waitId = await EnqueueParentWaitNoteAsync(factory, task, parent);
        var attemptedAt = DateTime.UtcNow.AddSeconds(-5);
        await using (var db = CreateContext())
        {
            // Typed once and reverted: whether it reached the composer is ambiguous.
            var nudge = await db.SessionQueuedMessages.SingleAsync(m => m.Id == nudgeId);
            nudge.DeliveryAttempts = 1;
            nudge.LastDeliveryStartedAt = attemptedAt;
            var wait = await db.SessionQueuedMessages.SingleAsync(m => m.Id == waitId);
            wait.Status = QueuedMessageStatus.Sent;
            wait.SentAt = attemptedAt;
            wait.DeliveryAttempts = 1;
            await db.SaveChangesAsync();
        }
        await SeedMarkedReportAsync(session, task, "Report after an attempted nudge.");

        await CreateService(factory).OnTurnEndAsync(session, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Succeeded);
        var storedNudge = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == nudgeId);
        storedNudge.Status.ShouldBe(QueuedMessageStatus.Pending, "an attempted row is not this path's to cancel");
        storedNudge.DeliveryAttempts.ShouldBe(1);
        storedNudge.CanceledAt.ShouldBeNull();
        var storedWait = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == waitId);
        storedWait.Status.ShouldBe(QueuedMessageStatus.Sent);
        storedWait.CanceledAt.ShouldBeNull();
    }

    [Test]
    public async Task Unrelated_pending_input_survives()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        var (task, session) = await SeedDispatchedTaskAsync(workspace.Path, parent);
        using var factory = new TestScopeFactory();
        var queue = Queue(factory);

        var phantom = await SeedPhantomBoundaryAsync(session, task);
        await EnqueueRecordedNudgeAsync(factory, task, session, phantom);
        await EnqueueParentWaitNoteAsync(factory, task, parent);
        var human = await EnqueueAsync(queue, session, "human: also check the logs", QueuedMessageOrigin.Ui);
        var sibling = Guid.NewGuid();
        var siblingInput = await EnqueueAsync(queue, session, "a sibling's delegation input",
            QueuedMessageOrigin.Delegation, $"task:{sibling:N}", sibling);
        var siblingWait = await EnqueueAsync(queue, parent, $"[task {DelegationReportFormatter.Short(sibling)} waiting] other",
            QueuedMessageOrigin.Delegation, $"task-wait:{sibling:N}", sibling);
        var parentHuman = await EnqueueAsync(queue, parent, "human: status?", QueuedMessageOrigin.Ui);
        await SeedMarkedReportAsync(session, task, "Report with neighbours queued.");

        await CreateService(factory).OnTurnEndAsync(session, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Succeeded);
        foreach (var id in new[] { human, siblingInput, siblingWait, parentHuman })
        {
            var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, $"unrelated row '{row.Body}' must survive");
            row.CanceledAt.ShouldBeNull();
        }
    }

    [Test]
    public async Task Delivery_claim_racing_report_keeps_single_input_ownership()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        await SeedParentHistoryAsync(parent);
        var (task, session) = await SeedDispatchedTaskAsync(workspace.Path, parent);
        var saved = new SettlementSavedSignal();
        using var factory = NewDeliveryFactory(boundary: saved);
        var child = AttachTerminal(factory, session);
        AttachTerminal(factory, parent);
        var typing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.BeforeInput = async (input, ct) =>
        {
            if (!input.Contains(NudgeText, StringComparison.Ordinal))
                return;
            typing.TrySetResult();
            await release.Task.WaitAsync(ct);
        };

        var phantom = await SeedPhantomBoundaryAsync(session, task);
        var nudgeId = await EnqueueRecordedNudgeAsync(factory, task, session, phantom);
        await SeedMarkedReportAsync(session, task, "Report racing the delivery claim.");

        // The queue claims the nudge (Sent, attempt charged) and is held mid-write.
        var flush = Task.Run(() => Queue(factory).FlushSessionAsync(session, CancellationToken.None));
        await typing.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var settle = Task.Run(() => CreateService(factory).OnTurnEndAsync(session, CancellationToken.None));
        // Settlement either persists (its retirement runs next) or declines while the write is
        // in flight; either way the claimed write is then let go.
        await Task.WhenAny(saved.Saved.Task, settle).WaitAsync(TimeSpan.FromSeconds(60));
        release.TrySetResult();
        await Task.WhenAll(flush, settle).WaitAsync(TimeSpan.FromSeconds(60));

        child.SubmittedBodies.Count(b => b.Contains(NudgeText, StringComparison.Ordinal))
            .ShouldBe(1, "the claimed nudge is typed exactly once");
        await using var verify = CreateContext();
        var nudge = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == nudgeId);
        nudge.Status.ShouldNotBe(QueuedMessageStatus.Canceled, "a claimed write is never canceled under it");
        nudge.CanceledAt.ShouldBeNull();
        nudge.DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task Old_attempt_nudge_cannot_cancel_current_attempt_input()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedSessionAsync(workspace.Path);
        var oldSession = await SeedSessionAsync(workspace.Path);
        var (task, session) = await SeedDispatchedTaskAsync(workspace.Path, parent, t => t.Attempt = 2);
        using var factory = new TestScopeFactory();
        var queue = Queue(factory);

        // Attempt 1 ran on another session; its nudge identity was left on the row.
        var oldNudge = await EnqueueAsync(queue, oldSession,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} {NudgeText}", QueuedMessageOrigin.Delegation);
        var current = await EnqueueAsync(queue, session,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} Reply: also cover the parser.", QueuedMessageOrigin.Delegation);
        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.ReportNudgeMessageId = oldNudge;
            row.ReportNudgedAt = DateTime.UtcNow.AddMinutes(-20);
            await db.SaveChangesAsync();
        }
        await SeedEntryAsync(session, TranscriptKinds.UserPrompt,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} attempt 2 brief", DateTime.UtcNow);
        await SeedMarkedReportAsync(session, task, "Attempt 2 report.");

        await CreateService(factory).OnTurnEndAsync(session, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Succeeded);
        var currentRow = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == current);
        currentRow.Status.ShouldBe(QueuedMessageStatus.Pending, "current-attempt input is not the recorded nudge");
        currentRow.CanceledAt.ShouldBeNull();
        var oldRow = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == oldNudge);
        oldRow.Status.ShouldBe(QueuedMessageStatus.Pending, "another session's row is outside this task's binding");
        oldRow.CanceledAt.ShouldBeNull();
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string WaitHeader(AgentTask task) => $"[task {DelegationReportFormatter.Short(task.Id)} waiting]";

    /// <summary>The brief and a premature boundary: the shape that queued ca843435's nudge.</summary>
    private static async Task<long> SeedPhantomBoundaryAsync(Guid session, AgentTask task)
    {
        await SeedEntryAsync(session, TranscriptKinds.UserPrompt,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} Investigate the thing.", DateTime.UtcNow);
        await SeedEntryAsync(session, TranscriptKinds.AssistantText, "Brief read. Gathering evidence.", DateTime.UtcNow);
        await SeedEntryAsync(session, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
        await using var db = CreateContext();
        return await db.TranscriptEntries.Where(e => e.AgentSessionId == session).MaxAsync(e => e.Sequence);
    }

    private static async Task SeedMarkedReportAsync(Guid session, AgentTask task, string report)
    {
        await SeedEntryAsync(session, TranscriptKinds.AssistantText,
            report + "\n" + DelegationReportFormatter.ReportToken(task.Id, "done"), DateTime.UtcNow);
        await SeedEntryAsync(session, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
    }

    /// <summary>Queues the ask through the real queue and records it the way the nudge path does.</summary>
    private static async Task<Guid> EnqueueRecordedNudgeAsync(
        TestScopeFactory factory, AgentTask task, Guid session, long boundarySequence)
    {
        var id = await EnqueueAsync(Queue(factory), session,
            $"{DelegationReportFormatter.TaskMarker(task.Id)} {NudgeText} If the work is finished, send the report now.",
            QueuedMessageOrigin.Delegation);
        await using var db = CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        row.ReportNudgedAt = DateTime.UtcNow;
        row.ReportNudgedSequence = boundarySequence;
        row.ReportNudgeMessageId = id;
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<Guid> EnqueueParentWaitNoteAsync(TestScopeFactory factory, AgentTask task, Guid parent) =>
        EnqueueAsync(Queue(factory), parent,
            $"{WaitHeader(task)} Child ended a turn without the closing report line. Session is idle.",
            QueuedMessageOrigin.Delegation, $"task-wait:{task.Id:N}", task.Id);

    private static async Task<Guid> EnqueueAsync(
        SessionMessageQueueService queue, Guid session, string body, QueuedMessageOrigin origin,
        string? conversationKey = null, Guid? sourceTaskId = null)
    {
        Guid? created = null;
        await queue.EnqueueAsync(session, body, MessageSendMode.WhenIdle, CancellationToken.None, origin,
            conversationKey, sourceTaskId, onCreated: id => created = id, deliverIfIdle: false);
        return created.ShouldNotBeNull();
    }

    private sealed class SettlementSavedSignal : LandDeliveryBoundary
    {
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "settlement-saved")
                Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
