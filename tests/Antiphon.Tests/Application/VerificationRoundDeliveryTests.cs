using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
