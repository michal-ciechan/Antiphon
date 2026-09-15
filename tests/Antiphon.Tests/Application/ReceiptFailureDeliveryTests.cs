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
    public async Task Receipt_failure_before_enqueue_reports_the_lost_brief_after_service_recreation(bool busyCaller)
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

        await using var recovered = CreateProvider(schema, null, clock);
        await using var caller = new FakeAgentProtocolAdapter();
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
            (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .FailNeverStartedAsync(CancellationToken.None)).ShouldBe(1);
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.Attempt.ShouldBe(3);
        failed.AgentSessionId.ShouldBe(sessionId);
        failed.ParentSessionId.ShouldBe(callerId);
        failed.FailureReason.ShouldNotBeNull().ShouldContain("never delivered");
        var note = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == task.Id);
        note.AgentSessionId.ShouldBe(callerId);
        note.ConversationKey.ShouldBe($"task:{task.Id:N}");
        if (busyCaller)
        {
            note.Status.ShouldBe(QueuedMessageStatus.Pending);
            caller.SubmittedBodies.ShouldBeEmpty();
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
            await recovered.GetRequiredService<SessionMessageQueueService>().OnTurnEndAsync(callerId, default);
        }
        var delivered = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == note.Id);
        delivered.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        delivered.SourceTaskId.ShouldBe(task.Id);
        delivered.AgentSessionId.ShouldBe(callerId);
        var submittedNote = caller.SubmittedBodies.ShouldHaveSingleItem();
        submittedNote.ShouldContain("never delivered");
        PromptSubmissionMatch.Normalize(submittedNote).ShouldBe(PromptSubmissionMatch.Normalize(note.Body));
        var receipt = (await verify.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == callerId
            && t.Kind == TranscriptKinds.UserPrompt).ToListAsync()).ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(note.Body, receipt.Text!).ShouldBeTrue();
        receipt.Sequence.ShouldBeGreaterThan(delivered.LastDeliveryBaselineSequence ?? 0);
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        (await recovered.GetRequiredService<CapacityRecoveryService>().CancelOrphanedWaitsAsync(default)).ShouldBe(1);
        var ended = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitId);
        ended.TaskId.ShouldBe(task.Id);
        ended.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
        ended.OutcomeReason.ShouldBe("task-terminal");
        ended.AdmissionCount.ShouldBe(1);
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
