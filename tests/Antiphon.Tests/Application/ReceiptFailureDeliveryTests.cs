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
