using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-4 / DL-1..DL-3, R-3/R-9/R-10/R-11. Producer-to-recipient delivery of the D-9 Completion
/// obligation (and the landing refusal and brief legs) through the real reply settlement, notification
/// scanner, reconcile service and session queue, into a controlled caller terminal. Receipt is only
/// the complete caller UserPrompt above the attempt floor; a busy caller receives zero writes before
/// its TurnEnd; recovery runs on recreated providers without editing task/notification/queue rows.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerificationRoundDeliveryTests
{
    private static readonly string[] FinalHeaderBits = ["verification=Final", "scope=Full", "final-review=none", "next=land"];

    [Test]
    public async Task C544_CompletionReceipt()
    {
        foreach (var busy in new[] { false, true })
        foreach (var spill in new[] { false, true })
        foreach (var distill in new[] { false, true })
        {
            var row = $"busy={busy} spill={spill} distilled={distill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill, distill);
            var (taskId, report) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row + ": obligation");
            var rows = await rig.RowsAsync(taskId);
            rows.Count.ShouldBe(1, row + ": one keyed row");
            rows[0].SourceLandNotificationId.ShouldBe(note.Id, row);
            rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": nothing typed at enqueue (WhenIdle)");
            if (distill)
            {
                rows[0].HoldUntil.ShouldNotBeNull(row + ": held for distillation");
                await rig.FlushAsync();
                rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": held row not typed");
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(row + ": one distiller admission");
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
                    .ShouldBeNull(row + ": distillation applied");
            }
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: distill ? "distilled" : "raw", expectSpill: spill);
        }
    }

    [Test]
    public async Task C544_CompletionRecovery()
    {
        var cuts = new[]
        {
            "obligation-insert", "settled-committed", "note-insert", "note-committed", "wakeup-dropped",
            "render-committed", "spill-written", "attempt-committed", "prompt-accepted",
        };
        foreach (var cut in cuts)
        foreach (var busy in new[] { false, true })
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"{cut} busy={busy} {(distilledSpill ? "distilled-spill" : "raw-inline")}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill, distill: distilledSpill);
            Guid taskId;
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else rig.Fault.Cut = cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted" ? null : cut;
            (taskId, _) = await rig.SettleReviewAsync();

            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row);
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row + ": no obligation without its settlement");
                (await rig.World.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, row + ": settlement rolled back");
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            }

            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row + ": committed obligation");
            if (distilledSpill && cut is not ("settled-committed" or "note-insert"))
            {
                // Distiller admission is part of the immediate path only; recovery never commissions another.
                if (rig.DistillQueue.TryDequeue(out var request))
                    (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
                        .ShouldBeNull(row + ": immediate distillation");
            }

            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                // The cut may surface to the flusher or be absorbed by a producer that logs and retries later;
                // either way only the armed cut may escape, and Throws proves it was reached.
                try
                {
                    if (busy) await rig.EndCallerTurnAsync();
                    else await rig.FlushAsync();
                    if (cut == "prompt-accepted") await rig.ScanAsync();
                }
                catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
            }

            // Unconfirmed until the complete prompt exists: never confirmed at enqueue.
            var promptsNow = await rig.CallerPromptsAsync();
            var current = (await rig.NotificationAsync(taskId))!;
            var delivery = TaskCompletionNotification.TryReadDelivery(current.CompletionDeliveryJson);
            var completeExists = delivery is not null && promptsNow.Any(p => PromptSubmissionMatch.IsCompleteIn(delivery.WireText, p.Text!));
            if (!completeExists)
            {
                current.State.ShouldNotBe(LandNotificationState.Confirmed, row + ": not confirmed without the complete prompt");
                current.ConfirmingPromptSequence.ShouldBeNull(row);
            }
            if (cut is "render-committed" or "spill-written")
                rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": nothing typed at the cut");

            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: null, expectSpill: distilledSpill ? null : false);
            (await rig.NotificationAsync(taskId))!.Id.ShouldBe(note.Id, row + ": same notification identity");
        }
    }

    /// <summary>
    /// DL-1: task/profile commit -> brief queue insert -> wakeup -> Sent -> confirmation, on the fresh
    /// spawn, warm-pool and pinned follow-up paths, inline and spilled, busy and eligible (12 rows).
    /// The delegate process is lost after the brief row commits (no live adapter, so nothing can be
    /// typed); recovery is a recreated provider, the real dispatcher tick and the queue's stranded
    /// sweep once the session is live again. Receipt is exactly one complete UserPrompt carrying the
    /// task marker and the profile block above the attempt floor.
    /// </summary>
    [Test]
    public async Task C544_BriefHandoffRecovery()
    {
        foreach (var path in new[] { "fresh", "warm", "follow-up" })
        foreach (var spilled in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
            var row = $"{path} {(spilled ? "spilled" : "inline")} busy={busy}";
            await using var world = await C544World.CreateAsync(delegation: d =>
            {
                d.PoolEnabled = path != "fresh"; // a disabled pool retires idle delegates before dispatch
                d.PoolReservedForCallerMinutes = 0;
                d.BriefInlineMaxBytes = spilled ? 900 : 43_200;
                d.ModernPtyBriefInlineMaxBytes = spilled ? 900 : 43_200;
            });
            var request = path == "fresh" ? world.FinalReview() : world.FinalReview() with { Workspace = WorkspaceMode.Shared };
            var created = await world.CreateTaskAsync(request);
            Guid? warmSession = path == "fresh" ? null : await SeedWarmAgentAsync(world, created.Id, pinned: path == "follow-up");

            await using (var scope = world.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

            var task = await world.TaskAsync(created.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, $"{row}: {task.FailureReason}");
            var session = task.AgentSessionId.ShouldNotBeNull(row);
            if (warmSession is Guid expected)
                session.ShouldBe(expected, $"{row}: reused the warm session (workspace={task.Workspace} dir={task.WorkingDirectory} "
                    + $"level={task.ModelLevel} kind={task.AgentKind} project={task.ProjectId} env={task.InheritedLaunchEnvJson} repo={world.RepositoryPath})");
            var marker = DelegationReportFormatter.TaskMarker(created.Id);
            async Task<List<SessionQueuedMessage>> BriefRowsAsync()
            {
                await using var db = world.CreateContext();
                return await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == session && m.ExecutionTaskId == created.Id && m.Body.Contains(marker))
                    .ToListAsync();
            }
            (await BriefRowsAsync()).Count.ShouldBe(1, row + ": the brief row committed with the task");
            (await PromptsAsync(world, session)).ShouldNotContain(p => p.Text!.Contains(marker), row + ": nothing typed before recovery");

            // Process lost: recreate every provider; the session's process comes back (reconciler resume).
            await world.RestartAsync();
            var adapter = new FakeAgentProtocolAdapter();
            await using var _ = adapter;
            var connection = world.Schema.ConnectionString;
            await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.UserPrompt, "prior turn", connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                connectionString: connection);
            if (busy)
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.AssistantText, "delegate is mid-turn", connectionString: connection);
            adapter.OnSubmitted = async submitted =>
            {
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow,
                    connectionString: connection);
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                    connectionString: connection);
            };
            world.Services.GetRequiredService<AgentSessionRuntime>().Register(session, adapter);
            // The substitute for the runner's launch/resume: the process is up. Only the session row moves;
            // task and queue rows are never edited.
            await using (var up = world.CreateContext())
                await up.AgentSessions.Where(s => s.Id == session && s.Status == SessionStatus.Starting)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Running));

            await using (var scope = world.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            var queue = world.Services.GetRequiredService<SessionMessageQueueService>();
            await queue.FlushStrandedQueuesAsync(CancellationToken.None);
            await queue.FlushIfIdleAsync(session, CancellationToken.None);
            if (busy)
            {
                adapter.SubmittedBodies.ShouldBeEmpty(row + ": a busy delegate gets zero writes before TurnEnd");
                await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                    connectionString: connection);
                await queue.OnTurnEndAsync(session, CancellationToken.None);
                // A warm unrelated reuse types the refocus /compact first; its own TurnEnd releases the brief.
                await queue.OnTurnEndAsync(session, CancellationToken.None);
            }

            var rows = await BriefRowsAsync();
            rows.Count.ShouldBe(1, row + ": recovery never re-enqueues a committed brief");
            var carrying = (await PromptsAsync(world, session)).Where(p => p.Text!.Contains(marker)).ToList();
            if (carrying.Count != 1)
            {
                await using var diag = world.CreateContext();
                var s = await diag.AgentSessions.AsNoTracking().SingleAsync(x => x.Id == session);
                carrying.Count.ShouldBe(1, $"{row}: exactly one UserPrompt carries the task marker; session={s.Status} "
                    + $"row={rows[0].Status}/{rows[0].DeliveryAttempts}/{rows[0].DeliveryVerdict}/{rows[0].HoldUntil} typed={adapter.SubmittedBodies.Count}");
            }
            carrying[0].Sequence.ShouldBeGreaterThan(rows[0].LastDeliveryBaselineSequence ?? 0, row + ": above the attempt floor");
            PromptSubmissionMatch.IsCompleteIn(rows[0].Body, carrying[0].Text!).ShouldBeTrue(row + ": complete brief prompt");
            var durable = carrying[0].Text!;
            if (spilled)
            {
                durable.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE", Case.Sensitive, row);
                var spill = Path.Combine(task.WorkingDirectory!, ".antiphon", $"task-{DelegationReportFormatter.Short(created.Id)}-brief.md");
                File.Exists(spill).ShouldBeTrue(row + ": spilled brief file");
                durable = await File.ReadAllTextAsync(spill);
            }
            else
            {
                durable.ShouldNotContain("YOUR BRIEF IS NOT IN THIS MESSAGE", Case.Sensitive, row);
            }
            durable.ShouldContain("--- verification profile ---", Case.Sensitive, row + ": profile block delivered");
            durable.ShouldContain("round: Final", Case.Sensitive, row);
        }
    }

    /// <summary>
    /// G-69 / PC-69 (restated after Review 5f4d2a5b). The dispatcher commits Dispatched and only then
    /// inserts the brief, swallowing an insert failure; nothing re-enqueues it. The real recovery is
    /// the delivery watchdog: the task fails as never-started ("no brief was queued") with a durable
    /// DeliveryFailure obligation committed with the Failed event and keyed into the caller's queue.
    /// A recreated process before the watchdog window adds no brief row.
    /// </summary>
    [Test]
    public async Task C544_BriefHandoffNeverStartedFailure()
    {
        var fault = new BriefInsertFault();
        await using var world = await C544World.CreateAsync(interceptor: fault);
        var created = await world.CreateTaskAsync(world.FinalReview());
        fault.TaskId = created.Id;

        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        fault.Throws.ShouldBe(1, "the brief insert failed once");
        var dispatched = await world.TaskAsync(created.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, "Dispatched committed before the brief insert: " + dispatched.FailureReason);
        dispatched.VerificationRound.ShouldBe(VerificationRound.Final);
        var session = dispatched.AgentSessionId.ShouldNotBeNull();
        async Task<int> BriefRowsAsync()
        {
            await using var db = world.CreateContext();
            return await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == created.Id);
        }
        (await BriefRowsAsync()).ShouldBe(0, "the swallowed insert left no brief row");

        // Process recreation inside the watchdog window: nothing re-enqueues the lost brief.
        await world.RestartAsync();
        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        (await BriefRowsAsync()).ShouldBe(0, "no re-enqueue path exists");
        (await world.TaskAsync(created.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "not judged inside the window");

        world.Clock.Advance(TimeSpan.FromMinutes(world.Delegation.DeliveryFailTimeoutMinutes + 1));
        await using (var scope = world.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailNeverStartedAsync(CancellationToken.None))
                .ShouldBeGreaterThanOrEqualTo(1);

        var failed = await world.TaskAsync(created.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull().ShouldContain("no brief was queued for this task after dispatch");
        (await BriefRowsAsync()).ShouldBe(0, "failing the task does not re-enqueue the brief either");
        await using var verify = world.CreateContext();
        var notification = (await verify.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == created.Id).ToListAsync()).ShouldHaveSingleItem();
        notification.Kind.ShouldBe(LandNotificationKind.DeliveryFailure);
        notification.ParentSessionId.ShouldBe(world.CallerSessionId);
        var failedEvent = await verify.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == notification.SourceEventId);
        failedEvent.AgentTaskId.ShouldBe(created.Id);
        failedEvent.Type.ShouldBe(AgentTaskEventType.Failed);
        var keyed = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == world.CallerSessionId && m.SourceLandNotificationId == notification.Id);
        keyed.ConversationKey.ShouldBe($"task:{failed.RootTaskId:N}");
        keyed.Body.ShouldBe(notification.Body);
        session.ShouldNotBe(world.CallerSessionId);
    }

    private sealed class BriefInsertFault : SaveChangesInterceptor
    {
        public Guid TaskId { get; set; }
        public int Throws { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Throws == 0 && TaskId != Guid.Empty && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                    .Any(e => e.State == EntityState.Added && e.Entity.ExecutionTaskId == TaskId))
            {
                Throws++;
                throw new IOException("c544 brief-insert cut");
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// DL-3 / PC-99: a Final/Full approval that loses its scope while the land is queued is refused by the
    /// recovered protocol; the terminal refusal and its keyed notification commit together, and the
    /// real notification service delivers it to a busy or eligible caller as one complete prompt.
    /// </summary>
    [Test]
    public async Task C544_LandRefusalReceipt()
    {
        foreach (var busy in new[] { false, true })
        {
            var row = $"busy={busy}";
            await using var rig = await RefusalRig.CreateAsync(busy);
            var note = await rig.RefuseAsync(row);
            await using var db = rig.CreateContext();
            var service = new AgentTaskLandNotificationService(db, rig.Caller.Queue, new CompletionNoteFlushQueue(), rig.Caller.Runtime,
                TimeProvider.System);
            await service.ReconcileAsync(note.Id, CancellationToken.None);
            await rig.DeliverAsync(service, note, row);
            await rig.AssertRefusalReceivedOnceAsync(note, row);
        }
    }

    /// <summary>
    /// DL-3 / PC-100: the process is lost before and after the refusal's queue insert (busy and eligible);
    /// a recreated notification service recovers the same notification ID and delivers exactly once.
    /// </summary>
    [Test]
    public async Task C544_LandRefusalRecovery()
    {
        foreach (var cut in new[] { "before-enqueue", "queue-inserted" })
        foreach (var busy in new[] { false, true })
        {
            var row = $"{cut} busy={busy}";
            await using var rig = await RefusalRig.CreateAsync(busy);
            var note = await rig.RefuseAsync(row);
            var boundary = new C544Boundary();
            boundary.Throw.Add(cut);
            // ReconcileAsync records the failure as retry-pending (backoff) or lets it escape; either way the cut is reached.
            await using (var crashed = rig.CreateContext())
            {
                try
                {
                    await new AgentTaskLandNotificationService(crashed, rig.Caller.Queue, new CompletionNoteFlushQueue(), rig.Caller.Runtime,
                        TimeProvider.System, boundary).ReconcileAsync(note.Id, CancellationToken.None);
                }
                catch (IOException e) when (e.Message.StartsWith("c544 boundary cut", StringComparison.Ordinal)) { }
            }
            boundary.Reached.ShouldContain(cut, row + ": cut reached");
            boundary.Throw.ShouldBeEmpty(row + ": the armed cut fired");
            await using (var observe = rig.CreateContext())
            {
                var rows = await observe.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.SourceLandNotificationId == note.Id);
                rows.ShouldBe(cut == "before-enqueue" ? 0 : 1, row + ": queue insert state at the cut");
                (await observe.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).State
                    .ShouldNotBe(LandNotificationState.Confirmed, row);
            }
            rig.Caller.Adapter.Inputs.ShouldBeEmpty(row + ": nothing typed before recovery");

            await using var db = rig.CreateContext();
            var restartClock = new C544Clock();
            restartClock.Advance(TimeSpan.FromMinutes(30)); // past any retry backoff the cut recorded
            var recovered = new AgentTaskLandNotificationService(db, rig.Caller.Queue, new CompletionNoteFlushQueue(), rig.Caller.Runtime,
                restartClock);
            await recovered.ReconcileAsync(note.Id, CancellationToken.None);
            await rig.DeliverAsync(recovered, note, row);
            await rig.AssertRefusalReceivedOnceAsync(note, row);
        }
    }

    private sealed class RefusalRig : IAsyncDisposable
    {
        public LandingProtocolHarness Land { get; private set; } = null!;
        public BridgeQueueHarness Caller { get; private set; } = null!;
        public bool Busy { get; private set; }
        public string Sha { get; private set; } = "";
        public Guid EvidenceId { get; private set; }

        public static async Task<RefusalRig> CreateAsync(bool busy)
        {
            var rig = new RefusalRig { Busy = busy, Land = new LandingProtocolHarness() };
            await rig.Land.InitializeAsync();
            rig.Caller = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = rig.Land.Schema.ConnectionString });
            rig.Land.Messages = rig.Caller.Queue;
            var connection = rig.Land.Schema.ConnectionString;
            await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.UserPrompt, "land the owner", connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: connection);
            if (busy)
                await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.AssistantText, "caller is mid-turn",
                    connectionString: connection);
            return rig;
        }

        public AppDbContext CreateContext() => Land.CreateContext();

        /// <summary>Latched owner, Final/Full approval admitted, scope invalidated, recovered land refuses.</summary>
        public async Task<AgentTaskLandNotification> RefuseAsync(string row)
        {
            Sha = await Land.AddSourceAsync();
            await using (var db = CreateContext())
            {
                await db.AgentTasks.Where(t => t.Id == Land.Git.TaskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RequiresFinalVerificationReview, true)
                    .SetProperty(t => t.VerificationProfileVersion, 1)
                    .SetProperty(t => t.VerificationRound, VerificationRound.Final)
                    .SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session)
                    .SetProperty(t => t.ParentSessionId, Caller.SessionId)
                    .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
                var evidence = new StageOutcome
                {
                    Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                    Source = StageOutcomeSource.Delegate, SubjectTaskId = Land.Git.TaskId, StageTaskId = Guid.NewGuid(),
                    ReviewedSourceSha = Sha, ReviewedSourceRef = Land.Git.SourceRef, ReviewedRepositoryPath = Land.Git.Repository,
                    VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final,
                    OrdinaryScopeCompleted = VerificationScope.Full, Detail = "c544 final full review", RecordedAt = DateTime.UtcNow,
                };
                db.StageOutcomes.Add(evidence);
                await db.SaveChangesAsync();
                EvidenceId = evidence.Id;
            }
            (await Land.RequestAsync(expectedSourceSha: Sha, reviewEvidenceId: EvidenceId)).Status.ShouldBe("queued", row);
            await using (var db = CreateContext())
                await db.StageOutcomes.Where(o => o.Id == EvidenceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.OrdinaryScopeCompleted, VerificationScope.Interim));
            await Land.RestartServicesAsync();
            await Land.RunAsync();

            Land.Git.Trace.ShouldNotContain(a => a[0] == "push", row + ": no publication");
            await using var verify = CreateContext();
            var refusal = await verify.AgentTaskEvents.AsNoTracking()
                .SingleAsync(e => e.AgentTaskId == Land.Git.TaskId && e.Type == AgentTaskEventType.LandRefused);
            refusal.Detail.ShouldContain(LandApproval.ScopeIneligibleCode, Case.Sensitive, row);
            var note = await verify.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.TaskId == Land.Git.TaskId && n.SourceEventId == refusal.Id);
            note.Kind.ShouldBe(LandNotificationKind.Outcome, row);
            note.ParentSessionId.ShouldBe(Caller.SessionId, row + ": snapshotted destination");
            note.State.ShouldBe(LandNotificationState.Queued, row + ": the outbox committed with the terminal refusal");
            (await verify.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.SourceLandNotificationId == note.Id))
                .ShouldBe(0, row + ": nothing enqueued yet");
            return note;
        }

        public async Task DeliverAsync(AgentTaskLandNotificationService service, AgentTaskLandNotification note, string row)
        {
            var typed = Caller.Adapter.SubmittedBodies.Count;
            await Caller.Queue.FlushIfIdleAsync(Caller.SessionId, CancellationToken.None);
            if (Busy)
            {
                Caller.Adapter.SubmittedBodies.Count.ShouldBe(typed, row + ": a busy caller gets zero writes before TurnEnd");
                await BridgeQueueHarness.InsertEntryAsync(Caller.SessionId, TranscriptKinds.TurnEnd,
                    stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: Land.Schema.ConnectionString);
                await Caller.Queue.OnTurnEndAsync(Caller.SessionId, CancellationToken.None);
            }
            await service.ReconcileAsync(note.Id, CancellationToken.None);
        }

        public async Task AssertRefusalReceivedOnceAsync(AgentTaskLandNotification original, string row)
        {
            await using var db = CreateContext();
            var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == original.Id);
            var queued = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceLandNotificationId == note.Id).ToListAsync();
            queued.Count.ShouldBe(1, row + ": exactly one keyed queue row");
            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == Caller.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                .ToListAsync();
            var carrying = prompts.Where(p => PromptSubmissionMatch.IsConfirmedBy(note.Body, p.Text)).ToList();
            carrying.Count.ShouldBe(1, row + ": exactly one caller UserPrompt carries the refusal");
            PromptSubmissionMatch.IsCompleteIn(note.Body, carrying[0].Text!).ShouldBeTrue(row + ": complete prompt");
            carrying[0].Sequence.ShouldBeGreaterThan(queued[0].LastDeliveryBaselineSequence ?? 0, row + ": above the attempt floor");
            note.State.ShouldBe(LandNotificationState.Confirmed, $"{row}: {note.LastErrorCode}");
            note.ConfirmingPromptSequence.ShouldBe(carrying[0].Sequence, row);
            note.Body.ShouldContain($"request={(await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == Land.Git.TaskId)).Id:N}",
                Case.Sensitive, row + ": names the saved request");
            note.Body.ShouldContain($"expected={Sha}", Case.Sensitive, row + ": names the approved SHA");
            note.Body.ShouldContain(LandApproval.ScopeIneligibleCode, Case.Sensitive, row + ": names the final-scope refusal");
            note.Body.ShouldContain("outcome=LandRefused", Case.Sensitive, row);
            (await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == Land.Git.TaskId).ToListAsync())
                .ShouldAllBe(o => !new AgentTaskLandingState().HasPublication(o), row + ": never published");
        }

        public async ValueTask DisposeAsync()
        {
            await Caller.DisposeAsync();
            await Land.DisposeAsync();
        }
    }

    private static async Task<List<TranscriptEntry>> PromptsAsync(C544World world, Guid session)
    {
        await using var db = world.CreateContext();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
            .OrderBy(t => t.Sequence).ToListAsync();
    }

    /// <summary>An idle pool delegate whose process is not live; pinned makes the task a same-root follow-up.</summary>
    private static async Task<Guid> SeedWarmAgentAsync(C544World world, Guid taskId, bool pinned)
    {
        await using var db = world.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        var now = DateTime.UtcNow;
        var session = C544World.Session(Guid.NewGuid(), "c544-warm", now.AddHours(-1));
        session.Cwd = world.RepositoryPath;
        db.AgentSessions.Add(session);
        var name = $"c544-warm-{Guid.NewGuid():N}"[..20];
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = name, Slug = name, WorkingDirectory = world.RepositoryPath,
            Details = "CARD-0544 warm delegate.", Status = AgentStatus.Idle, PoolIdleSince = now.AddMinutes(-1),
            Kind = task.AgentKind, ModelLevel = task.ModelLevel, IsPoolDelegate = true, PoolProjectId = task.ProjectId,
            PersistentSessionId = session.Id.ToString("D"), CreatedAt = now.AddHours(-2), UpdatedAt = now,
        };
        db.Agents.Add(agent);
        var priorId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = priorId, RootTaskId = pinned ? task.RootTaskId : priorId, Title = "prior work", Goal = "prior work",
            Role = AgentTaskRole.Review, ModelLevel = task.ModelLevel, Workspace = WorkspaceMode.Shared,
            WorkingDirectory = world.RepositoryPath, AgentId = agent.Id, AgentSessionId = session.Id,
            Status = AgentTaskStatus.Succeeded, CreatedAt = now.AddMinutes(-90), DispatchedAt = now.AddMinutes(-89),
            CompletedAt = now.AddMinutes(-61),
        });
        if (pinned)
        {
            task.AgentId = agent.Id;
            task.AgentName = name;
            task.ConcurrencyToken = Guid.NewGuid();
        }
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static readonly string[] InterimHeaderBits = ["verification=Interim", "scope=Interim", "final-review=pending", "next=review"];

    /// <summary>PC-105: recovery renders the settlement snapshot, not the current task/card state.</summary>
    [Test]
    public async Task C544_SnapshotRendersRecovery()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        var baseline = await rig.World.SettleReviewAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        rig.Fault.Cut = "settled-committed";
        var (taskId, report) = await rig.SettleReviewAsync(rig.World.InterimReview(baseline.Id), scope: "Interim", next: "land");
        rig.Fault.Throws.ShouldBe(1, "the immediate delivery path was lost");
        var original = (await rig.NotificationAsync(taskId)).ShouldNotBeNull();
        var snapshot = TaskCompletionNotification.TryReadSnapshot(original.CompletionSnapshotJson).ShouldNotBeNull();
        (await rig.RowsAsync(taskId)).ShouldBeEmpty("no queue row before recovery");

        // A foreign context rewrites everything recovery could be tempted to read instead.
        await using (var db = rig.World.CreateContext())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Result, "FOREIGN REWRITE of the result")
                .SetProperty(t => t.VerificationRound, VerificationRound.Final));
        await rig.World.SetCardPolicyAsync(CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly);

        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertReceivedOnceAsync(rig, taskId, "snapshot", InterimHeaderBits, expectKind: "raw", expectSpill: false);
        var delivered = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
        delivered.LogicalNote.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive);
        delivered.LogicalNote.ShouldContain("Reviewed the owner.", Case.Sensitive);
        delivered.LogicalNote.ShouldNotContain("FOREIGN REWRITE", Case.Sensitive);
        snapshot.RawSha256.ShouldBe(TaskCompletionNotification.Sha256(snapshot.RawResult));
    }

    /// <summary>PC-106: nothing is typed while the rendering is uncommitted; once committed the typed text is its wire text.</summary>
    [Test]
    public async Task C544_RenderingFrozenBeforeTyping()
    {
        foreach (var cut in new[] { "render-committed", "spill-written" })
        foreach (var spill in new[] { false, true })
        {
            var row = $"{cut} spill={spill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false, spill: spill);
            var (taskId, _) = await rig.SettleReviewAsync();
            rig.Fault.TaskId = taskId;
            rig.Fault.Cut = cut;
            try { await rig.FlushAsync(); }
            catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
            rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
            var atCut = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson);
            if (cut == "spill-written") atCut.ShouldBeNull(row + ": the rendering transaction rolled back");
            else atCut.ShouldNotBeNull(row + ": the rendering committed with the claim");
            rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": nothing typed before the committed rendering is typed");

            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.FlushAsync();
            await rig.ScanAsync();
            var final = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)
                .ShouldNotBeNull(row);
            if (atCut is not null) final.WireText.ShouldBe(atCut.WireText, row + ": replay types the frozen rendering");
            rig.Caller.SubmittedBodies.ShouldHaveSingleItem(row).ShouldBe(final.WireText, row + ": typed text is the committed wire text");
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: "raw", expectSpill: spill);
        }
    }

    /// <summary>PC-107: a distillation result that arrives after the attempt claim cannot replace the typed rendering.</summary>
    [Test]
    public async Task C544_LateReplacementRejected()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, distill: true);
        var (taskId, _) = await rig.SettleReviewAsync();
        var note = (await rig.NotificationAsync(taskId))!;
        var held = await rig.RowAsync(taskId);
        held.HoldUntil.ShouldNotBeNull();
        rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue();
        rig.World.Clock.Advance(held.HoldUntil!.Value - rig.World.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5));

        rig.Fault.TaskId = taskId;
        rig.Fault.Cut = "attempt-committed";
        try { await rig.FlushAsync(); }
        catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
        rig.Fault.Throws.ShouldBe(1, "attempt verdict cut reached");
        rig.Caller.SubmittedBodies.Count.ShouldBe(1, "the raw rendering was typed at the deadline");
        var before = await rig.RowAsync(taskId);
        var frozen = (await rig.NotificationAsync(taskId))!.CompletionDeliveryJson.ShouldNotBeNull();

        await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None);
        var after = await rig.RowAsync(taskId);
        after.Body.ShouldBe(before.Body, "late summary leaves the typed row body");
        (await rig.NotificationAsync(taskId))!.CompletionDeliveryJson.ShouldBe(frozen, "late summary leaves the frozen rendering");

        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertReceivedOnceAsync(rig, taskId, "late", FinalHeaderBits, expectKind: "raw", expectSpill: false);
        TaskCompletionNotification.TryReadDelivery(frozen)!.LogicalNote.ShouldNotContain(C544DeliveryRig.Summary, Case.Sensitive);
    }

    /// <summary>PC-108: raw, distilled, polled-shrunk and spilled renderings all keep the snapshot header.</summary>
    [Test]
    public async Task C544_RenderingKeepsHeader()
    {
        foreach (var kind in new[] { "raw", "distilled", "polled-shrunk", "spilled" })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false, spill: kind == "spilled", distill: kind == "distilled");
            var (taskId, _) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId))!;
            if (kind == "distilled")
            {
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(kind);
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
                    .ShouldBeNull(kind);
            }
            if (kind == "polled-shrunk")
            {
                await using var scope = rig.World.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .GetAsync(taskId, CancellationToken.None, pollingSessionId: rig.World.CallerSessionId);
            }
            await rig.DeliverAsync(kind);
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, kind, FinalHeaderBits,
                expectKind: kind is "distilled" ? "distilled" : null, expectSpill: kind == "spilled");
            var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
            var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!;
            if (kind == "polled-shrunk")
            {
                await using var db = rig.World.CreateContext();
                (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.NoteShrunk))
                    .ShouldBeTrue(kind + ": the poll shrink rendered this note");
                delivery.LogicalNote.ShouldNotBe(snapshot.RawBody, kind);
            }
        }
    }

    /// <summary>PC-109: only a Completion keyed row may take a distilled replacement; Outcome and DeliveryFailure stay immutable.</summary>
    [Test]
    public async Task C544_KindAwareDistillation()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: true, distill: true);
        var (taskId, _) = await rig.SettleReviewAsync();
        var completion = (await rig.NotificationAsync(taskId))!;
        rig.DistillQueue.TryDequeue(out var completionRequest).ShouldBeTrue();
        (await rig.Queue.TryApplyDistillationAsync(completionRequest, completion.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
            .ShouldBeNull("the Completion obligation may take the summary");
        (await rig.RowAsync(taskId)).Body.ShouldContain(C544DeliveryRig.Summary, Case.Sensitive);

        foreach (var kind in new[] { LandNotificationKind.Outcome, LandNotificationKind.DeliveryFailure })
        {
            AgentTaskLandNotification note;
            await using (var db = rig.World.CreateContext())
            {
                note = await AgentTaskLandReceiptTests.SeedAsync(db, rig.World.CallerSessionId);
                if (kind != LandNotificationKind.Outcome)
                {
                    await db.AgentTaskLandNotifications.Where(n => n.Id == note.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Kind, kind));
                    note.Kind = kind;
                }
                await new AgentTaskLandNotificationService(db, rig.Queue, new CompletionNoteFlushQueue(),
                    rig.World.Services.GetRequiredService<AgentSessionRuntime>(), rig.World.Clock).ReconcileAsync(note.Id, CancellationToken.None);
            }
            var keyed = await rig.RowAsync(note.TaskId);
            keyed.SourceLandNotificationId.ShouldBe(note.Id, kind.ToString());
            var now = rig.World.Clock.GetUtcNow();
            await rig.Queue.TryApplyDistillationAsync(new DistillRequest(note.TaskId, keyed.Id, now, now.AddMinutes(2), OutputDistillerMode.Apply),
                note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None);
            (await rig.RowAsync(note.TaskId)).Body.ShouldBe(note.Body, kind + ": immutable body");
            await using (var db = rig.World.CreateContext())
            {
                await new AgentTaskLandNotificationService(db, rig.Queue, new CompletionNoteFlushQueue(),
                    rig.World.Services.GetRequiredService<AgentSessionRuntime>(), rig.World.Clock).ReconcileAsync(note.Id, CancellationToken.None);
                var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
                saved.LastErrorCode.ShouldNotBe("queue_payload_changed_unconfirmed", kind.ToString());
                saved.CompletionDeliveryJson.ShouldBeNull(kind + ": no rendering record for a non-Completion kind");
            }
        }
    }

    /// <summary>PC-110: two sibling completions for one root batch into one caller prompt with shared membership.</summary>
    [Test]
    public async Task C544_SameRootBatchMembership()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: true);
        var root = await rig.RootOrchestratorAsync();
        var (first, _) = await rig.SettleReviewAsync(root: root);
        var (second, _) = await rig.SettleReviewAsync(root: root);
        var rows = new[] { await rig.RowAsync(first), await rig.RowAsync(second) };
        rows.ShouldAllBe(r => r.ConversationKey == TaskCompletionNotification.ConversationKey(root.Id));
        await rig.DeliverAsync("batch");
        await rig.ScanAsync();

        var a = (await rig.NotificationAsync(first))!;
        var b = (await rig.NotificationAsync(second))!;
        a.State.ShouldBe(LandNotificationState.Confirmed, a.LastErrorCode);
        b.State.ShouldBe(LandNotificationState.Confirmed, b.LastErrorCode);
        a.ConfirmingPromptSequence.ShouldBe(b.ConfirmingPromptSequence, "one prompt confirms both");
        var da = TaskCompletionNotification.TryReadDelivery(a.CompletionDeliveryJson)!;
        var db2 = TaskCompletionNotification.TryReadDelivery(b.CompletionDeliveryJson)!;
        da.WireSha256.ShouldBe(db2.WireSha256, "same wire text");
        da.MemberQueueIds.OrderBy(x => x).ShouldBe(rows.Select(r => r.Id).OrderBy(x => x), "membership names both rows");
        db2.MemberQueueIds.OrderBy(x => x).ShouldBe(rows.Select(r => r.Id).OrderBy(x => x));
        var prompts = (await rig.CallerPromptsAsync()).Where(p => PromptSubmissionMatch.IsConfirmedBy(da.WireText, p.Text)).ToList();
        prompts.ShouldHaveSingleItem("exactly one UserPrompt carries the batch");
        prompts[0].Text!.ShouldContain(da.LogicalNote.Split('\n')[0], Case.Sensitive);
        prompts[0].Text!.ShouldContain(db2.LogicalNote.Split('\n')[0], Case.Sensitive);
        rig.Caller.SubmittedBodies.ShouldHaveSingleItem();
    }

    /// <summary>PC-111: a pointer prompt confirms only while the referenced file still hashes to the recorded content.</summary>
    [Test]
    public async Task C544_PointerReceiptRequiresContent()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, spill: true);
        var (taskId, _) = await rig.SettleReviewAsync();
        await rig.DeliverAsync("pointer");
        var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
        var path = delivery.SpillPath.ShouldNotBeNull();
        var content = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, content + "\ntampered after typing\n");

        await rig.ScanAsync();
        var refused = (await rig.NotificationAsync(taskId))!;
        refused.State.ShouldNotBe(LandNotificationState.Confirmed);
        refused.ConfirmingPromptSequence.ShouldBeNull();
        refused.LastErrorCode.ShouldBe("completion_pointer_content_mismatch");

        await File.WriteAllTextAsync(path, content);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await AssertReceivedOnceAsync(rig, taskId, "restored", FinalHeaderBits, expectKind: "raw", expectSpill: true);
    }

    /// <summary>PC-112: a lost report file is regenerated from the snapshot raw result, not the current task.Result.</summary>
    [Test]
    public async Task C544_ReportRegeneratedFromSnapshot()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, spill: true, replyInlineMaxChars: 4_000, reportStore: true);
        rig.Fault.Cut = "settled-committed";
        var (taskId, _) = await rig.SettleReviewAsync(padding: 400);
        var snapshot = TaskCompletionNotification.TryReadSnapshot((await rig.NotificationAsync(taskId))!.CompletionSnapshotJson)!;
        var report = snapshot.ReportFilePath.ShouldNotBeNull("the settled report has a file");
        File.Delete(report);
        await using (var db = rig.World.CreateContext())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Result, "FOREIGN REWRITE"));

        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertReceivedOnceAsync(rig, taskId, "regenerated", FinalHeaderBits, expectKind: null, expectSpill: null);
        File.Exists(report).ShouldBeTrue("regenerated");
        (await AgentTaskLandNotificationService.FileHasSha256Async(report, snapshot.RawSha256, CancellationToken.None))
            .ShouldBeTrue("regenerated report hashes to the snapshot raw result");
        var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
        delivery.LogicalNote.ShouldNotContain("FOREIGN REWRITE", Case.Sensitive);
    }

    /// <summary>PC-113: a restart keeps the settlement's distillation deadline and commissions no new distiller call.</summary>
    [Test]
    public async Task C544_DistillationDeadlineSurvivesRestart()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, distill: true);
        var (taskId, _) = await rig.SettleReviewAsync();
        var hold = (await rig.RowAsync(taskId)).HoldUntil.ShouldNotBeNull();
        var snapshot = TaskCompletionNotification.TryReadSnapshot((await rig.NotificationAsync(taskId))!.CompletionSnapshotJson)!;
        // Postgres keeps microseconds; the snapshot JSON keeps ticks.
        (snapshot.DistillDeadlineAt!.Value - hold).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));

        await rig.RestartAsync();
        await rig.ScanAsync();
        rig.DistillQueue.TryDequeue(out _).ShouldBeFalse("no new distiller admission after restart");
        (await rig.RowAsync(taskId)).HoldUntil.ShouldBe(hold, "deadline unchanged by recovery");
        await rig.FlushAsync();
        rig.Caller.SubmittedBodies.ShouldBeEmpty("still held before the original deadline");

        rig.World.Clock.Advance(hold - rig.World.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5));
        await rig.FlushAsync();
        rig.Caller.SubmittedBodies.Count.ShouldBe(1, "raw delivered at the first flush after the original deadline");
        await rig.ScanAsync();
        rig.DistillQueue.TryDequeue(out _).ShouldBeFalse();
        await AssertReceivedOnceAsync(rig, taskId, "deadline", FinalHeaderBits, expectKind: "raw", expectSpill: false);
    }

    /// <summary>PC-114: an ID-only, header-only or truncated prompt never confirms; only the whole wire prompt does.</summary>
    [Test]
    public async Task C544_CompletionReceiptWholeWire()
    {
        foreach (var row in new[] { "header-only", "truncated", "id-only" })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
            var (taskId, _) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId))!;
            // The caller's terminal records a false prompt for the first attempt; nothing is inserted by hand.
            rig.SubmitTransform = wire => row switch
            {
                "id-only" => $"notification {note.Id:N} task {taskId:N}",
                "header-only" => wire.ReplaceLineEndings("\n").Split('\n')[0],
                _ => wire[..(wire.Length / 2)],
            };
            await rig.FlushAsync();
            rig.Caller.SubmittedBodies.Count.ShouldBeGreaterThanOrEqualTo(1, row + ": typed (the queue may retry an unconfirmed attempt)");
            var falseAttempts = rig.Caller.SubmittedBodies.Count;
            await rig.ScanAsync();
            var refused = (await rig.NotificationAsync(taskId))!;
            refused.State.ShouldNotBe(LandNotificationState.Confirmed, row);
            refused.ConfirmingPromptSequence.ShouldBeNull(row);
            (await rig.CallerPromptsAsync()).Count(p => p.Text != "dispatch the review").ShouldBe(falseAttempts, row + ": only false prompts exist");

            rig.SubmitTransform = null;
            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)
                .ShouldNotBeNull(row);
            if (row != "id-only")
            {
                // A recorded prefix is the queue's Truncated verdict: the row parks for a human and is not retyped.
                // Recovery must still refuse to confirm on the prefix evidence.
                var parked = (await rig.NotificationAsync(taskId))!;
                parked.State.ShouldNotBe(LandNotificationState.Confirmed, row + ": prefix evidence never confirms after recovery");
                parked.ConfirmingPromptSequence.ShouldBeNull(row);
                parked.LastErrorCode.ShouldBe("queue_truncated_unconfirmed", row);
                (await rig.CallerPromptsAsync()).ShouldNotContain(p => p.Text == delivery.WireText, row);
                continue;
            }
            var whole = (await rig.CallerPromptsAsync()).Where(p => p.Text == delivery.WireText).ToList()
                .ShouldHaveSingleItem(row + ": the whole wire prompt recorded once");
            var confirmed = (await rig.NotificationAsync(taskId))!;
            confirmed.State.ShouldBe(LandNotificationState.Confirmed, $"{row}: {confirmed.LastErrorCode}");
            confirmed.ConfirmingPromptSequence.ShouldBe(whole.Sequence, row + ": confirmed by the whole wire prompt only");
        }
    }

    /// <summary>PC-115: a sibling's root note and completion stamp are not a receipt for this obligation.</summary>
    [Test]
    public async Task C544_StampIsNotReceipt()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        var root = await rig.RootOrchestratorAsync();
        var (earlier, _) = await rig.SettleReviewAsync(root: root);
        await rig.DeliverAsync("earlier");
        await rig.ScanAsync();
        (await rig.NotificationAsync(earlier))!.State.ShouldBe(LandNotificationState.Confirmed);
        await using (var db = rig.World.CreateContext())
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == earlier)).CompletionNoteQueuedAt.ShouldNotBeNull("sibling stamp present");

        rig.Fault.Cut = "note-insert";
        var (later, _) = await rig.SettleReviewAsync(root: root);
        rig.Fault.Throws.ShouldBe(1, "this obligation's insert was lost");
        (await rig.RowsAsync(later)).ShouldBeEmpty();

        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertReceivedOnceAsync(rig, later, "later", FinalHeaderBits, expectKind: "raw", expectSpill: false);
        await AssertReceivedOnceAsync(rig, earlier, "earlier", FinalHeaderBits, expectKind: "raw", expectSpill: false);
    }

    /// <summary>PC-116: a profile-v1 settlement produces one logical note: one keyed row, one prompt.</summary>
    [Test]
    public async Task C544_SingleLogicalNote()
    {
        foreach (var busy in new[] { false, true })
        {
            var row = $"busy={busy}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy);
            var (taskId, _) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId))!;
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await using var db = rig.World.CreateContext();
            (await db.SessionQueuedMessages.CountAsync(m => m.ContentDigest == note.ContentDigest)).ShouldBe(1, row + ": one queue row for the digest");
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId)).ShouldBe(1, row);
            var header = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader;
            (await rig.CallerPromptsAsync()).Count(p => p.Text!.Contains(header, StringComparison.Ordinal)).ShouldBe(1, row + ": one prompt");
            rig.Caller.SubmittedBodies.Count.ShouldBe(1, row);
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: "raw", expectSpill: false);
        }
    }

    /// <summary>PC-117: an existing keyed row with a different destination or digest is never linked or confirmed.</summary>
    [Test]
    public async Task C544_CompletionLinkValidatesIdentity()
    {
        foreach (var wrong in new[] { "destination", "digest" })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
            rig.Fault.Cut = "settled-committed";
            var (taskId, _) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId))!;
            var destination = rig.World.CallerSessionId;
            if (wrong == "destination")
            {
                destination = Guid.NewGuid();
                await using var db = rig.World.CreateContext();
                db.AgentSessions.Add(C544World.Session(destination, "c544-foreign", DateTime.UtcNow));
                await db.SaveChangesAsync();
            }
            var digest = wrong == "digest" ? DelegationNoteDigest.Compute("a different payload") : note.ContentDigest;
            await rig.Queue.EnqueueAsync(destination, note.Body, MessageSendMode.WhenIdle, CancellationToken.None,
                QueuedMessageOrigin.Delegation, sourceTaskId: taskId, contentDigest: digest, deliverIfIdle: false,
                sourceLandNotificationId: note.Id);

            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            var saved = (await rig.NotificationAsync(taskId))!;
            saved.QueueMessageId.ShouldBeNull(wrong + ": never linked");
            saved.State.ShouldNotBe(LandNotificationState.Confirmed, wrong);
            saved.ConfirmingPromptSequence.ShouldBeNull(wrong);
            saved.LastErrorCode.ShouldBe("notification_reconcile_failed:ConflictException", wrong);
            rig.Caller.SubmittedBodies.ShouldBeEmpty(wrong);
            await using var verify = rig.World.CreateContext();
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(1, wrong + ": no replacement row");
        }
    }

    /// <summary>PC-120: a caller that stops after the prompt landed still yields a receipt from the existing transcript.</summary>
    [Test]
    public async Task C544_StoppedCallerReceipt()
    {
        foreach (var status in new[] { SessionStatus.Stopped, SessionStatus.Failed })
        {
            var row = status.ToString();
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
            var (taskId, _) = await rig.SettleReviewAsync();
            await rig.FlushAsync();
            rig.Caller.SubmittedBodies.Count.ShouldBe(1, row);
            rig.Fault.TaskId = taskId;
            rig.Fault.Cut = "prompt-accepted";
            try { await rig.ScanAsync(); }
            catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
            rig.Fault.Throws.ShouldBe(1, row + ": receipt save cut");
            (await rig.NotificationAsync(taskId))!.State.ShouldNotBe(LandNotificationState.Confirmed, row);

            await rig.SetCallerStatusAsync(status);
            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: "raw", expectSpill: false);
            rig.Caller.SubmittedBodies.Count.ShouldBe(1, row + ": nothing retyped");
        }
    }

    // ---- shared assertions ------------------------------------------------------------------------

    internal static async Task AssertReceivedOnceAsync(C544DeliveryRig rig, Guid taskId, string row, string[] headerBits,
        string? expectKind, bool? expectSpill)
    {
        var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
        var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row + ": snapshot");
        var delivery = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull(row + ": frozen rendering");
        var rows = await rig.RowsAsync(taskId);
        rows.Count.ShouldBe(1, row + ": exactly one queue row");
        var queued = rows[0];
        queued.SourceLandNotificationId.ShouldBe(note.Id, row);
        queued.AgentSessionId.ShouldBe(rig.World.CallerSessionId, row + ": snapshotted destination");
        queued.ConversationKey.ShouldBe($"task:{snapshot.RootTaskId:N}", row);
        queued.ContentDigest.ShouldBe(snapshot.NoteDigest, row);
        delivery.MemberQueueIds.ShouldContain(queued.Id, row);

        var prompts = await rig.CallerPromptsAsync();
        var carrying = prompts.Where(p => p.Text is not null && PromptSubmissionMatch.IsConfirmedBy(delivery.WireText, p.Text)).ToList();
        carrying.Count.ShouldBe(1, row + ": exactly one caller UserPrompt carries the wire text");
        PromptSubmissionMatch.IsCompleteIn(delivery.WireText, carrying[0].Text!).ShouldBeTrue(row + ": complete prompt");
        carrying[0].Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, row + ": above the attempt floor");
        TaskCompletionNotification.Sha256(delivery.WireText).ShouldBe(delivery.WireSha256, row);

        note.State.ShouldBe(LandNotificationState.Confirmed, $"{row}: {note.LastErrorCode}");
        note.ConfirmingPromptSequence.ShouldBe(carrying[0].Sequence, row);
        note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(snapshot.RawResult), row + ": raw digest identity");

        var logical = delivery.LogicalNote;
        foreach (var bit in headerBits)
        {
            snapshot.NoteHeader.ShouldContain(bit, Case.Sensitive, row + ": snapshot header " + bit);
            logical.ShouldContain(bit, Case.Sensitive, row + ": rendering keeps " + bit);
        }
        logical.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive, row + ": header first");
        if (expectKind is not null)
            delivery.RenderingKind.Split('+')[0].ShouldBe(expectKind, row + ": rendering kind");
        if (expectKind == "distilled")
            logical.ShouldContain(C544DeliveryRig.Summary, Case.Sensitive, row);
        if (expectSpill is { } spill)
            (delivery.SpillPath is not null).ShouldBe(spill, row + ": spill");
        if (delivery.SpillPath is { } path)
        {
            (await AgentTaskLandNotificationService.FileHasSha256Async(path, delivery.SpillSha256, CancellationToken.None))
                .ShouldBeTrue(row + ": pointer content hash");
            var content = await File.ReadAllTextAsync(path);
            foreach (var bit in headerBits)
                content.ShouldContain(bit, Case.Sensitive, row + ": spilled content keeps " + bit);
        }
    }
}
