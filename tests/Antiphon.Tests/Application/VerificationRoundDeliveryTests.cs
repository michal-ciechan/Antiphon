using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
