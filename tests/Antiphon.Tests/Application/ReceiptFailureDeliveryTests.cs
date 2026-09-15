using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
using static Antiphon.Tests.TestHelpers.WallRerouteFixture;

namespace Antiphon.Tests.Application;

// Real dispatcher -> real durable queue -> runtime adapter -> complete UserPrompt.
// No process is spawned: the fake TUI emits only the bytes submitted to its composer.
[Category("Integration")]
public class ReceiptFailureDeliveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Receipt_failure_preserves_complete_brief_acceptance(bool busy) =>
        await VerifyDeliveryAsync(busy, "none");

    [Test]
    [Arguments("queue-committed")]
    [Arguments("attempt-committed")]
    [Arguments("prompt-accepted")]
    public async Task Receipt_failure_recovers_the_same_queue_row_after_service_recreation(string cut) =>
        await VerifyDeliveryAsync(false, cut);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Receipt_failure_before_enqueue_reports_the_lost_brief_after_service_recreation(bool busyCaller) =>
        await VerifyCallerFailureAsync(busyCaller, "none");

    [Test]
    [Arguments(false, "obligation-insert")]
    [Arguments(true, "obligation-insert")]
    [Arguments(false, "failed-committed")]
    [Arguments(true, "failed-committed")]
    [Arguments(false, "note-insert")]
    [Arguments(true, "note-insert")]
    [Arguments(false, "note-committed")]
    [Arguments(true, "note-committed")]
    [Arguments(false, "attempt-committed")]
    [Arguments(true, "attempt-committed")]
    [Arguments(false, "prompt-accepted")]
    [Arguments(true, "prompt-accepted")]
    public async Task Caller_failure_obligation_survives_persistence_cuts(bool busyCaller, string cut) =>
        await VerifyCallerFailureAsync(busyCaller, cut);

    [Test]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    public async Task Delivered_caller_failure_is_confirmed_after_caller_stops_and_services_restart(SessionStatus terminalCaller) =>
        await VerifyCallerFailureAsync(false, "none", terminalCaller);

    private static async Task VerifyCallerFailureAsync(bool busyCaller, string cut, SessionStatus? terminalCaller = null)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var (_, callerId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.High, "lost receipt-failure brief");
        await using (var db = CreateContext(schema))
        {
            (await db.Agents.SingleAsync(a => a.Id == agentId)).ModelLevel = AgentModelLevel.High;
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Attempt = 3;
            stored.ParentSessionId = callerId;
            stored.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        var clock = new OffsetClock();
        var fault = new DeliveryFault(task.Id, "before-queue-commit");
        Guid waitId;
        await using (var producer = CreateProvider(schema, fault, clock))
        {
            var capacity = producer.GetRequiredService<CapacityRecoveryService>();
            var wait = await capacity.EnsureWaitAsync(new CapacityWaitRegistration
            {
                ConsumerKey = $"task:{task.Id:N}", ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
                ExecutionKind = AgentKind.ClaudeCode, RequestedKind = AgentKind.ClaudeCode,
                RequestedAlias = "opus", TaskId = task.Id, HoldAlreadyCleared = true,
                BlockedAt = clock.GetUtcNow().UtcDateTime,
            }, CancellationToken.None);
            waitId = wait.Id;
            (await capacity.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
            using var scope = producer.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
            result.Dispatched.ShouldBe(1);
            result.Failures.ShouldBe(0);
            fault.QueueThrows.ShouldBe(1);
            fault.ReceiptThrows.ShouldBe(1);
        }
        await using var verify = CreateContext(schema);
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == task.Id)).ShouldBe(0);
        (await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitId))
            .State.ShouldBe(CapacityRecoveryWaitState.Admitted);
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);

        var callerFault = new CallerFailureFault(task.Id, cut);
        var recovered = CreateProvider(schema, callerFault, clock);
        await using var caller = new FakeAgentProtocolAdapter();
        try
        {
            caller.OnSubmitted = async submitted =>
            {
                await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.UserPrompt, submitted,
                    timestamp: clock.GetUtcNow().UtcDateTime, connectionString: schema.ConnectionString);
                await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                    stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
            };
            recovered.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
            if (busyCaller)
                await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.AssistantText,
                    "caller is busy", connectionString: schema.ConnectionString);
            clock.Advance(TimeSpan.FromMinutes(11));
            using (var scope = recovered.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                if (cut is "obligation-insert" or "failed-committed")
                    await Should.ThrowAsync<IOException>(() => dispatcher.FailNeverStartedAsync(default));
                else
                    (await dispatcher.FailNeverStartedAsync(default)).ShouldBe(1);
            }
            if (cut == "obligation-insert")
            {
                // The task, event and obligation are a single write. No Failed row may escape alone.
                callerFault.AtomicPairObserved.ShouldBeTrue();
                (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
                    .Status.ShouldBe(AgentTaskStatus.Dispatched);
                (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
                    && e.Type == AgentTaskEventType.Failed)).ShouldBe(0);
                (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == task.Id)).ShouldBe(0);
                await recovered.DisposeAsync();
                recovered = CreateProvider(schema, callerFault, clock);
                recovered.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
                using var retry = recovered.CreateScope();
                (await retry.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .FailNeverStartedAsync(default)).ShouldBe(1);
            }
            var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.Attempt.ShouldBe(3);
            failed.AgentSessionId.ShouldBe(sessionId);
            failed.ParentSessionId.ShouldBe(callerId);
            failed.FailureReason.ShouldNotBeNull().ShouldContain("never delivered");
            var obligation = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == task.Id);
            obligation.Kind.ShouldBe(LandNotificationKind.DeliveryFailure);
            obligation.ParentSessionId.ShouldBe(callerId);
            obligation.RequestId.ShouldBeNull();
            obligation.LandingOperationId.ShouldBeNull();
            obligation.Body.ShouldContain("never delivered");
            obligation.SourceEventId.ShouldBe((await verify.AgentTaskEvents.AsNoTracking()
                .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).Id);
            obligation.ConfirmedAt.ShouldBeNull();
            var beforeRestart = await verify.SessionQueuedMessages.AsNoTracking()
                .SingleOrDefaultAsync(m => m.SourceTaskId == task.Id);
            if (cut is "failed-committed" or "note-insert")
            {
                beforeRestart.ShouldBeNull();
                caller.SubmittedBodies.ShouldBeEmpty();
            }
            if (busyCaller)
            {
                caller.SubmittedBodies.ShouldBeEmpty();
                beforeRestart?.Status.ShouldBe(QueuedMessageStatus.Pending);
            }
            if (cut != "none")
            {
                // For the busy variants, reach the delivery cut only when the caller ends its turn.
                if (busyCaller && cut is "attempt-committed" or "prompt-accepted")
                {
                    await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                        stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
                    await Should.ThrowAsync<IOException>(() => recovered.GetRequiredService<SessionMessageQueueService>()
                        .OnTurnEndAsync(callerId, default));
                }
                callerFault.Throws.ShouldBe(1);
                var cutRow = await verify.SessionQueuedMessages.AsNoTracking()
                    .SingleOrDefaultAsync(m => m.SourceTaskId == task.Id);
                if (cut is "attempt-committed" or "prompt-accepted")
                {
                    cutRow.ShouldNotBeNull().Status.ShouldBe(QueuedMessageStatus.Sent);
                    cutRow.DeliveryVerdict.ShouldBeNull();
                    caller.SubmittedBodies.Count.ShouldBe(cut == "prompt-accepted" ? 1 : 0);
                }
                await recovered.DisposeAsync();
                recovered = CreateProvider(schema, null, clock);
                recovered.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
                // Delivery recovery is mandatory even when optional check reminders are disabled.
                recovered.GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>()
                    .Value.CheckEnabled = false;
                clock.Advance(TimeSpan.FromSeconds(40));
                using (var scope = recovered.CreateScope())
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                    (await dispatcher.FailNeverStartedAsync(default)).ShouldBe(0);
                    (await dispatcher.RemindUnacknowledgedFailuresAsync(default)).ShouldBe(1);
                }
                var awaiting = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == obligation.Id);
                awaiting.Body.ShouldBe(obligation.Body);
                awaiting.SourceEventId.ShouldBe(obligation.SourceEventId);
                awaiting.State.ShouldBe(cut == "prompt-accepted" || (cut == "obligation-insert" && !busyCaller)
                    ? LandNotificationState.Confirmed : LandNotificationState.AwaitingReceipt);
                if (cutRow is not null)
                    awaiting.QueueMessageId.ShouldBe(cutRow.Id);
            }
            var note = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == task.Id);
            note.SourceLandNotificationId.ShouldBe(obligation.Id);
            note.Body.ShouldBe(obligation.Body);
            note.AgentSessionId.ShouldBe(callerId);
            note.ConversationKey.ShouldBe($"task:{task.Id:N}");
            if (busyCaller && cut is not ("attempt-committed" or "prompt-accepted"))
            {
                note.Status.ShouldBe(QueuedMessageStatus.Pending);
                caller.SubmittedBodies.ShouldBeEmpty();
                await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                    stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
                await recovered.GetRequiredService<SessionMessageQueueService>().OnTurnEndAsync(callerId, default);
            }
            await recovered.GetRequiredService<SessionMessageQueueService>().FlushStrandedQueuesAsync(default);
            var delivered = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == note.Id);
            delivered.DeliveryVerdict.ShouldBe(cut == "prompt-accepted"
                ? DeliveryVerdict.LateConfirmed : DeliveryVerdict.Delivered);
            delivered.SourceTaskId.ShouldBe(task.Id);
            delivered.AgentSessionId.ShouldBe(callerId);
            var submittedNote = caller.SubmittedBodies.ShouldHaveSingleItem();
            submittedNote.ShouldContain("never delivered");
            PromptSubmissionMatch.Normalize(submittedNote).ShouldBe(PromptSubmissionMatch.Normalize(note.Body));
            var receipt = (await verify.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == callerId
                && t.Kind == TranscriptKinds.UserPrompt).ToListAsync()).ShouldHaveSingleItem();
            PromptSubmissionMatch.IsCompleteIn(note.Body, receipt.Text!).ShouldBeTrue();
            receipt.Sequence.ShouldBeGreaterThan(delivered.LastDeliveryBaselineSequence ?? 0);
            if (terminalCaller is { } terminalStatus)
            {
                // Immediate watchdog delivery commits the row and complete prompt, but not the outbox link.
                obligation.QueueMessageId.ShouldBeNull();
                delivered.Status.ShouldBe(QueuedMessageStatus.Sent);
                delivered.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                await verify.AgentSessions.Where(s => s.Id == callerId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, terminalStatus));
                await recovered.DisposeAsync();
                recovered = CreateProvider(schema, null, clock);
                recovered.GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>()
                    .Value.CheckEnabled = false;
            }
            using (var scope = recovered.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .RemindUnacknowledgedFailuresAsync(default);
            var confirmed = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == obligation.Id);
            confirmed.State.ShouldBe(LandNotificationState.Confirmed);
            confirmed.QueueMessageId.ShouldBe(note.Id);
            confirmed.ConfirmingPromptSequence.ShouldBe(receipt.Sequence);
            confirmed.ConfirmedAt.ShouldNotBeNull();
            if (terminalCaller is { } expectedStatus)
            {
                confirmed.LastErrorCode.ShouldBeNull();
                confirmed.EnqueueAttempts.ShouldBe(0, "recovery links the existing row without a new enqueue");
                (await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == callerId)).Status.ShouldBe(expectedStatus);
                var recoveredRow = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == note.Id);
                recoveredRow.Sequence.ShouldBe(delivered.Sequence);
                recoveredRow.DeliveryAttempts.ShouldBe(delivered.DeliveryAttempts);
                recoveredRow.LastDeliveryBaselineSequence.ShouldBe(delivered.LastDeliveryBaselineSequence);
                recoveredRow.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            }
            var stamped = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            stamped.CompletionNoteQueuedAt.ShouldNotBeNull();
            stamped.CompletionNoteDigest.ShouldBe(obligation.ContentDigest);
            (await AgentTaskCheckService.HasCompletionNoteAsync(verify, callerId, task.RootTaskId, default))
                .ShouldBeTrue();
            // A further recreation must neither produce another queue row nor type again.
            await recovered.DisposeAsync();
            recovered = CreateProvider(schema, null, clock);
            recovered.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
            using (var scope = recovered.CreateScope())
                (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .RemindUnacknowledgedFailuresAsync(default)).ShouldBe(0);
            await recovered.GetRequiredService<SessionMessageQueueService>().FlushStrandedQueuesAsync(default);
            caller.SubmittedBodies.ShouldHaveSingleItem();
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
            var stillFailed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            stillFailed.Status.ShouldBe(AgentTaskStatus.Failed);
            stillFailed.Attempt.ShouldBe(3);
            stillFailed.AgentSessionId.ShouldBe(sessionId);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
            (await recovered.GetRequiredService<CapacityRecoveryService>().CancelOrphanedWaitsAsync(default)).ShouldBe(1);
            var ended = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitId);
            ended.TaskId.ShouldBe(task.Id);
            ended.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
            ended.OutcomeReason.ShouldBe("task-terminal");
            ended.AdmissionCount.ShouldBe(1);
        }
        finally
        {
            await recovered.DisposeAsync();
        }
    }

    private sealed class CallerFailureFault(Guid taskId, string cut) : SaveChangesInterceptor
    {
        public int Throws;
        public bool AtomicPairObserved;
        private bool _afterSave;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Throws != 0) return ValueTask.FromResult(result);
            var entries = data.Context!.ChangeTracker;
            var obligation = entries.Entries<AgentTaskLandNotification>().Any(e => e.State == EntityState.Added
                && e.Entity.TaskId == taskId && e.Entity.Kind == LandNotificationKind.DeliveryFailure);
            if (obligation)
            {
                AtomicPairObserved = entries.Entries<AgentTask>().Any(e => e.Entity.Id == taskId
                    && e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Failed)
                    && entries.Entries<AgentTaskEvent>().Any(e => e.Entity.AgentTaskId == taskId
                        && e.State == EntityState.Added && e.Entity.Type == AgentTaskEventType.Failed);
                if (cut == "obligation-insert") Throw();
                if (cut == "failed-committed") _afterSave = true;
            }
            var queue = entries.Entries<SessionQueuedMessage>().SingleOrDefault(e => e.Entity.SourceTaskId == taskId);
            if (queue is not null)
            {
                if (cut == "note-insert" && queue.State == EntityState.Added) Throw();
                if (cut == "prompt-accepted" && queue.Entity.DeliveryVerdict == DeliveryVerdict.Delivered) Throw();
                _afterSave |= (cut == "note-committed" && queue.State == EntityState.Added)
                    || (cut == "attempt-committed" && queue.State == EntityState.Modified
                        && queue.Entity.Status == QueuedMessageStatus.Sent && queue.Entity.DeliveryVerdict is null);
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_afterSave) { _afterSave = false; Throw(); }
            return ValueTask.FromResult(result);
        }

        private void Throw()
        {
            Throws++;
            throw new IOException($"caller failure persistence cut: {cut}");
        }
    }

    private static async Task VerifyDeliveryAsync(bool busy, string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.High, "receipt failure delivery");
        await using (var db = CreateContext(schema))
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.ModelLevel = AgentModelLevel.High;
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Attempt = 3;
            stored.Goal = "Keep this entire brief.\nFirst line belongs to this attempt.\nFinal line must also arrive.";
            await db.SaveChangesAsync();
        }

        var clock = new OffsetClock();
        var fault = new DeliveryFault(task.Id, cut);
        var provider = CreateProvider(schema, fault, clock);
        var adapter = new FakeAgentProtocolAdapter();
        adapter.OnSubmitted = async submitted =>
        {
            // Read back the actual adapter submission; never synthesize acceptance from
            // a queue row or invoke CapacityRecoveryService.ObserveTranscriptAsync.
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt,
                submitted, timestamp: clock.GetUtcNow().UtcDateTime, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
        };
        try
        {
            provider.GetRequiredService<AgentSessionRuntime>().Register(sessionId, adapter);
            if (busy)
                await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.AssistantText,
                    "recipient is busy", connectionString: schema.ConnectionString);
            var capacity = provider.GetRequiredService<CapacityRecoveryService>();
            var wait = await capacity.EnsureWaitAsync(new CapacityWaitRegistration
            {
                ConsumerKey = $"task:{task.Id:N}", ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
                ExecutionKind = AgentKind.ClaudeCode, RequestedKind = AgentKind.ClaudeCode,
                RequestedAlias = "opus", TaskId = task.Id, HoldAlreadyCleared = true,
                BlockedAt = clock.GetUtcNow().UtcDateTime,
            }, CancellationToken.None);
            (await capacity.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
            using (var scope = provider.CreateScope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .TickAsync(CancellationToken.None);
                result.Dispatched.ShouldBe(1);
                result.Failures.ShouldBe(0);
            }
            fault.ReceiptThrows.ShouldBe(1);
            fault.QueueThrows.ShouldBe(cut == "none" ? 0 : 1);

            await using var verify = CreateContext(schema);
            var queued = await verify.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.ExecutionTaskId == task.Id);
            var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id);
            admitted.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            admitted.DispatchAttemptId.ShouldBeNull();
            admitted.SessionId.ShouldBeNull();
            admitted.LaunchSessionId.ShouldBeNull();
            admitted.AdmissionCount.ShouldBe(1);
            admitted.TaskId.ShouldBe(task.Id);
            queued.AgentSessionId.ShouldBe(sessionId);
            queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
            queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
            // FitBriefForTyping may use its durable spill file. The complete pointer is
            // the typed brief; verify the full intended goal survives there too.
            var briefPath = Path.Combine(workspace.Path, ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            var fullBrief = File.Exists(briefPath) ? await File.ReadAllTextAsync(briefPath) : queued.Body;
            fullBrief.ShouldContain("First line belongs to this attempt.");
            fullBrief.ShouldContain("Final line must also arrive.");

            if (busy || cut is "queue-committed" or "attempt-committed")
            {
                adapter.SubmittedBodies.ShouldBeEmpty();
                (await verify.TranscriptEntries.CountAsync(e => e.AgentSessionId == sessionId
                    && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
                queued.Status.ShouldBe(cut == "attempt-committed"
                    ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending);
                queued.DeliveryVerdict.ShouldBeNull();
            }
            else if (cut == "prompt-accepted")
            {
                adapter.SubmittedBodies.ShouldHaveSingleItem();
                queued.Status.ShouldBe(QueuedMessageStatus.Sent);
                queued.DeliveryVerdict.ShouldBeNull();
            }
            else
            {
                // Already eligible: producer's enqueue must deliver without another tick.
                queued.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                adapter.SubmittedBodies.ShouldHaveSingleItem();
            }

            if (busy)
            {
                await provider.GetRequiredService<SessionMessageQueueService>()
                    .FlushIfIdleAsync(sessionId, CancellationToken.None);
                adapter.SubmittedBodies.ShouldBeEmpty();
                await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd,
                    stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
                await provider.GetRequiredService<SessionMessageQueueService>()
                    .OnTurnEndAsync(sessionId, CancellationToken.None);
            }
            if (cut != "none")
            {
                // Destroy all producer/queue/capacity service instances and their contexts.
                // Keep only the recipient composer, as a server restart keeps its runner.
                await provider.DisposeAsync();
                provider = CreateProvider(schema, null, clock);
                provider.GetRequiredService<AgentSessionRuntime>().Register(sessionId, adapter);
                clock.Advance(TimeSpan.FromSeconds(40));
                await provider.GetRequiredService<SessionMessageQueueService>()
                    .FlushStrandedQueuesAsync(CancellationToken.None);
            }

            var accepted = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
            accepted.DeliveryVerdict.ShouldBe(cut == "prompt-accepted"
                ? DeliveryVerdict.LateConfirmed : DeliveryVerdict.Delivered);
            accepted.Status.ShouldBe(QueuedMessageStatus.Sent);
            accepted.ExecutionTaskId.ShouldBe(task.Id);
            accepted.AgentSessionId.ShouldBe(sessionId);
            accepted.Sequence.ShouldBe(queued.Sequence);
            accepted.Body.ShouldBe(queued.Body);
            accepted.DeliveryAttempts.ShouldBe(cut == "attempt-committed" ? 2 : 1);
            var submitted = adapter.SubmittedBodies.ShouldHaveSingleItem();
            var prompt = (await verify.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == sessionId
                    && e.Kind == TranscriptKinds.UserPrompt).ToListAsync()).ShouldHaveSingleItem();
            PromptSubmissionMatch.Normalize(submitted).ShouldBe(PromptSubmissionMatch.Normalize(queued.Body));
            PromptSubmissionMatch.Normalize(prompt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(submitted));
            PromptSubmissionMatch.IsCompleteIn(queued.Body, prompt.Text!).ShouldBeTrue();
            prompt.Sequence.ShouldBeGreaterThan(accepted.LastDeliveryBaselineSequence ?? 0);
            prompt.Timestamp.ShouldNotBeNull();
            var unchanged = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            unchanged.Status.ShouldBe(AgentTaskStatus.Dispatched);
            unchanged.AgentSessionId.ShouldBe(sessionId);
            unchanged.Attempt.ShouldBe(3);
            unchanged.FailureReason.ShouldBeNull();
            var finalWait = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id);
            finalWait.TaskId.ShouldBe(task.Id);
            finalWait.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            finalWait.ActionKey.ShouldBe(admitted.ActionKey);
            finalWait.ActionOrdinal.ShouldBe(admitted.ActionOrdinal);
            finalWait.AdmissionCount.ShouldBe(1);
            (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == task.Id)).ShouldBe(1);
            (await verify.CapacityRecoveryWaits.CountAsync(w => w.TaskId == task.Id)).ShouldBe(1);
        }
        finally
        {
            await provider.DisposeAsync();
            await adapter.DisposeAsync();
        }
    }

    private static ServiceProvider CreateProvider(IsolatedTestSchema schema, IInterceptor? fault, TimeProvider clock) =>
        CapacityRecoveryTaskTests.CreateDispatcherProvider(schema.ConnectionString, fault, clock,
            new DeliveryVerificationSettings
            {
                EvidenceTimeoutSeconds = 1, PollIntervalMs = 50, PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0, TranscriptConfirmTimeoutSeconds = 3, ReEnterIntervalSeconds = 1,
                PostFailureConfirmGraceSeconds = 3, UnobservableBaselineConfirmClockToleranceSeconds = 30,
                BootPromptRetryDelaySeconds = 0,
            });

    private sealed class OffsetClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan amount) => _offset += amount;
    }

    private sealed class DeliveryFault(Guid taskId, string cut) : SaveChangesInterceptor
    {
        public int ReceiptThrows;
        public int QueueThrows;
        private bool _cutAfterSave;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<CapacityRecoveryWait>().Any(e => e.Entity.TaskId == taskId
                    && e.Entity.State == CapacityRecoveryWaitState.StartAccepted)
                && Interlocked.CompareExchange(ref ReceiptThrows, 1, 0) == 0)
                throw new InvalidOperationException("single-use capacity receipt save failure");

            var queue = db.ChangeTracker.Entries<SessionQueuedMessage>()
                .SingleOrDefault(e => e.Entity.ExecutionTaskId == taskId);
            if (queue is not null && QueueThrows == 0)
            {
                if (cut == "before-queue-commit" && queue.State == EntityState.Added)
                {
                    QueueThrows++;
                    throw new IOException("cut before brief insert commit");
                }
                if (cut == "prompt-accepted" && queue.Entity.DeliveryVerdict == DeliveryVerdict.Delivered)
                {
                    QueueThrows++;
                    throw new IOException("cut after prompt acceptance, before verdict commit");
                }
                _cutAfterSave = (cut == "queue-committed" && queue.State == EntityState.Added)
                    || (cut == "attempt-committed" && queue.State == EntityState.Modified
                        && queue.Entity.Status == QueuedMessageStatus.Sent && queue.Entity.DeliveryVerdict is null);
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_cutAfterSave)
            {
                _cutAfterSave = false;
                QueueThrows++;
                throw new IOException($"cut after durable {cut}");
            }
            return ValueTask.FromResult(result);
        }
    }
}
