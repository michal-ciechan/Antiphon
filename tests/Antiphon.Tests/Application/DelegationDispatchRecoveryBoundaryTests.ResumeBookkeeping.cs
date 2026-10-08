using System.Data.Common;
using Antiphon.Server.Application.Interfaces;
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
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair F10: the interrupted-launch backfill's ensure can type the brief before the
/// resume's own bookkeeping. A failure after that input must not kill or fail the recipient; a
/// failure before any input keeps today's launch-failure path.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    private const string RequeuedEventPrefix = "brief re-queued:";
    private const string ResumedEventPrefix = "launch resumed after a server restart";

    /// <summary>
    /// Review 818f247a's producer-to-recipient cut: a real queue inserts and types the whole brief,
    /// the recipient opens its turn, then saving "brief re-queued" fails once.
    /// </summary>
    [Test]
    public async Task C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient()
    {
        var outcome = await RunResumeBookkeepingAsync(ResumeCase.RequeuedEventSave);

        outcome.Faults.ShouldBe(1, "F10 the one-shot event-save fault was reached");
        outcome.Error.ShouldBeNull("F10 a post-input bookkeeping failure fails the resumed launch");
        outcome.Killed.ShouldBeFalse("F10 the Working recipient was killed");
        outcome.SessionStatus.ShouldBe(SessionStatus.Running, "F10");
        outcome.Working.ShouldBeTrue("F10 the recipient is in the brief's turn");
        outcome.TaskStatus.ShouldBe(AgentTaskStatus.Dispatched, "F10");
        outcome.TaskFailureReason.ShouldBeNull("F10");
        var brief = outcome.Rows.Where(m => m.ExecutionTaskId == outcome.TaskId).ToList()
            .ShouldHaveSingleItem("F10 one durable brief");
        outcome.Rows.Count.ShouldBe(1, "F10");
        brief.Status.ShouldBe(QueuedMessageStatus.Sent, "F10");
        brief.DeliveryAttempts.ShouldBe(1, "F10");
        brief.Body.Contains(DelegationReportFormatter.TaskMarker(outcome.TaskId), StringComparison.Ordinal)
            .ShouldBeTrue("F10");
        outcome.Prompts.ShouldHaveSingleItem("F10 the brief was typed exactly once").ShouldBe(brief.Body, "F10");
        PromptSubmissionMatch.IsCompleteIn(brief.Body, outcome.Prompts[0]).ShouldBeTrue("F10");
        outcome.Submitted.ShouldHaveSingleItem("F10").ShouldBe(brief.Body, "F10");
        outcome.PromptsAfterReflush.ShouldBe(1, "F10 a later flush does not type the brief again");
        outcome.RequeuedEvents.ShouldBe(0, "F10 the failed event was detached, not retried");
        outcome.ResumedEvents.ShouldBe(1, "F10 later bookkeeping still runs");
    }

    /// <summary>
    /// One flip per post-input step of <c>ResumeInterruptedLaunchAsync</c>, plus the healthy
    /// companion and two pre-input controls that keep today's kill-and-fail path.
    /// </summary>
    [Test]
    [Arguments("healthy")]
    [Arguments("requeued-event-save")]
    [Arguments("flush-after-ensure-input")]
    [Arguments("resumed-event-save")]
    [Arguments("flush-input-then-resumed-event-save")]
    [Arguments("pre-input-legacy-requeued-event-save")]
    [Arguments("pre-input-resumed-event-save")]
    public async Task C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(string label)
    {
        var flip = label switch
        {
            "healthy" => ResumeCase.Healthy,
            "requeued-event-save" => ResumeCase.RequeuedEventSave,
            "flush-after-ensure-input" => ResumeCase.FlushAfterEnsureInput,
            "resumed-event-save" => ResumeCase.ResumedEventSave,
            "flush-input-then-resumed-event-save" => ResumeCase.FlushInputThenResumedEventSave,
            "pre-input-legacy-requeued-event-save" => ResumeCase.PreInputLegacyRequeuedEventSave,
            "pre-input-resumed-event-save" => ResumeCase.PreInputResumedEventSave,
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
        };
        var outcome = await RunResumeBookkeepingAsync(flip);
        var why = "F10 " + label;

        outcome.Faults.ShouldBe(flip == ResumeCase.Healthy ? 0 : 1, why + ": injected fault reached");
        outcome.TaskStatus.ShouldBe(AgentTaskStatus.Dispatched, why);
        outcome.TaskFailureReason.ShouldBeNull(why);
        var brief = outcome.Rows.Where(m => m.ExecutionTaskId == outcome.TaskId).ToList()
            .ShouldHaveSingleItem(why + ": one durable brief");
        outcome.Rows.Count.ShouldBe(1, why);

        if (flip == ResumeCase.PreInputLegacyRequeuedEventSave)
        {
            // Today's path: nothing was typed, so the failed launch is killed and failed.
            outcome.Error.ShouldNotBeNull(why);
            outcome.Error.ShouldContain(ResumeBookkeepingFault.Message, Case.Sensitive, why);
            outcome.Killed.ShouldBeTrue(why);
            outcome.SessionStatus.ShouldBe(SessionStatus.Failed, why);
            outcome.Prompts.ShouldBeEmpty(why);
            outcome.Submitted.ShouldBeEmpty(why);
            brief.Status.ShouldBe(QueuedMessageStatus.Pending, why);
            brief.DeliveryAttempts.ShouldBe(0, why);
            return;
        }

        if (flip == ResumeCase.PreInputResumedEventSave)
        {
            // The brief was received before the restart and the turn ended; the resume types
            // nothing, so a failed event save keeps today's kill-and-fail path.
            outcome.Error.ShouldNotBeNull(why);
            outcome.Error.ShouldContain(ResumeBookkeepingFault.Message, Case.Sensitive, why);
            outcome.Killed.ShouldBeTrue(why);
            outcome.SessionStatus.ShouldBe(SessionStatus.Failed, why);
            outcome.Working.ShouldBeFalse(why);
            outcome.Submitted.ShouldBeEmpty(why);
            outcome.Prompts.ShouldHaveSingleItem(why).ShouldBe(brief.Body, why);
            return;
        }

        outcome.Error.ShouldBeNull(why + ": a post-input failure fails the resumed launch");
        outcome.Killed.ShouldBeFalse(why + ": the Working recipient was killed");
        outcome.SessionStatus.ShouldBe(SessionStatus.Running, why);
        outcome.Working.ShouldBeTrue(why + ": the recipient is in the brief's turn");
        brief.Status.ShouldBe(QueuedMessageStatus.Sent, why);
        brief.DeliveryAttempts.ShouldBe(1, why);
        outcome.Prompts.ShouldHaveSingleItem(why + ": typed exactly once").ShouldBe(brief.Body, why);
        PromptSubmissionMatch.IsCompleteIn(brief.Body, outcome.Prompts[0]).ShouldBeTrue(why);
        outcome.Submitted.ShouldHaveSingleItem(why).ShouldBe(brief.Body, why);
        outcome.PromptsAfterReflush.ShouldBe(1, why + ": a later flush does not type it again");
        var ensured = flip is not ResumeCase.FlushInputThenResumedEventSave;
        outcome.RequeuedEvents.ShouldBe(ensured && flip != ResumeCase.RequeuedEventSave ? 1 : 0, why);
        outcome.ResumedEvents.ShouldBe(flip is ResumeCase.ResumedEventSave
            or ResumeCase.FlushInputThenResumedEventSave ? 0 : 1, why);
    }

    private enum ResumeCase
    {
        Healthy,
        RequeuedEventSave,
        FlushAfterEnsureInput,
        ResumedEventSave,
        FlushInputThenResumedEventSave,
        PreInputLegacyRequeuedEventSave,
        PreInputResumedEventSave,
    }

    private sealed record ResumeOutcome(
        Guid TaskId,
        int Faults,
        string? Error,
        bool Killed,
        SessionStatus SessionStatus,
        bool Working,
        AgentTaskStatus TaskStatus,
        string? TaskFailureReason,
        List<SessionQueuedMessage> Rows,
        List<string> Prompts,
        List<string> Submitted,
        int PromptsAfterReflush,
        int RequeuedEvents,
        int ResumedEvents);

    private static async Task<ResumeOutcome> RunResumeBookkeepingAsync(ResumeCase flip)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        var fault = new ResumeBookkeepingFault(flip);
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(fault, fault.Commands),
            ConfigureServices = services =>
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter)),
        });
        var connection = schema.ConnectionString;
        var agentId = harness.AgentId;
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "resume-bookkeeping")).FullName;
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var priorBrief = $"{marker}\nResume this interrupted work and keep the accepted turn alive.";
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Starting, Cwd = directory, Cols = 120, Rows = 30,
                CreatedAt = now.AddMinutes(-2), StartedAt = now.AddMinutes(-2), LastSeenAt = now.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AgentStatus.Running)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Interrupted backfill bookkeeping cut",
                Goal = "Keep this accepted work alive after its complete brief arrives.",
                Role = AgentTaskRole.Plan, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = directory, AgentSessionId = sessionId,
                AgentId = agentId, Status = AgentTaskStatus.Dispatched, CreatedAt = now.AddMinutes(-2),
                // The legacy branch (no dispatch time) enqueues without delivering.
                DispatchedAt = flip == ResumeCase.PreInputLegacyRequeuedEventSave ? null : now.AddMinutes(-2),
            });
            if (flip == ResumeCase.FlushInputThenResumedEventSave)
            {
                // The dispatcher persisted the brief before the restart; only the flush types it.
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Body = priorBrief,
                    Status = QueuedMessageStatus.Pending, Sequence = 1, CreatedAt = now.AddMinutes(-2),
                    Origin = QueuedMessageOrigin.Delegation, ExecutionTaskId = taskId,
                });
            }

            await db.SaveChangesAsync();
        }

        if (flip == ResumeCase.PreInputResumedEventSave)
        {
            // Received and finished before the restart: the brief row is retained as Sent.
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Body = priorBrief,
                    Status = QueuedMessageStatus.Sent, Sequence = 1, CreatedAt = now.AddMinutes(-2),
                    SentAt = now.AddMinutes(-1), DeliveryAttempts = 1,
                    DeliveryVerdict = DeliveryVerdict.Delivered, DeliveryVerdictAt = now.AddMinutes(-1),
                    Origin = QueuedMessageOrigin.Delegation, ExecutionTaskId = taskId,
                });
                await db.SaveChangesAsync();
            }

            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt, priorBrief,
                timestamp: now.AddMinutes(-1), connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd,
                stopReason: "end_turn", connectionString: connection);
        }

        adapter.RegisterOnStart = harness.Runtime;
        adapter.OnSubmitted = async body =>
        {
            // The recipient accepts the whole body and opens its turn: no TurnEnd follows.
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt, body,
                timestamp: DateTime.UtcNow, connectionString: connection);
        };

        string? error = null;
        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                    .ResumeInterruptedLaunchAsync(sessionId, agentId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
        }

        fault.Disarm();
        bool working;
        SessionStatus status;
        AgentTask task;
        List<SessionQueuedMessage> rows;
        List<string> prompts;
        int requeuedEvents;
        int resumedEvents;
        await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            working = await SessionMessageQueueService.IsWorkingAsync(verify, sessionId, CancellationToken.None);
            status = (await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status;
            task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            rows = await verify.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId).ToListAsync();
            prompts = await UserPromptsAsync(connection, sessionId);
            requeuedEvents = await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                && e.Detail!.StartsWith(RequeuedEventPrefix));
            resumedEvents = await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                && e.Detail!.StartsWith(ResumedEventPrefix));
        }

        var submitted = adapter.SubmittedBodies.ToList();
        var killed = adapter.Killed;
        if (!killed && status == SessionStatus.Running)
            await harness.Queue.FlushSessionAsync(sessionId, CancellationToken.None);
        var reflushed = (await UserPromptsAsync(connection, sessionId)).Count;
        return new ResumeOutcome(taskId, fault.Calls, error, killed, status, working, task.Status,
            task.FailureReason, rows, prompts, submitted, reflushed, requeuedEvents, resumedEvents);
    }

    /// <summary>
    /// One-shot persistence faults. Event saves fail on their Warning detail. The flush fault
    /// arms once "brief re-queued" is saved and fails the flush's boot-time supervision read.
    /// </summary>
    private sealed class ResumeBookkeepingFault(ResumeCase flip) : SaveChangesInterceptor
    {
        public const string Message = "C1150 F10 injected resume bookkeeping failure";
        private int _calls;
        private int _disarmed;
        public int Calls => Volatile.Read(ref _calls);
        public FlushReadFault Commands { get; } = new();

        public void Disarm() => Interlocked.Exchange(ref _disarmed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var prefix = flip switch
            {
                ResumeCase.RequeuedEventSave or ResumeCase.PreInputLegacyRequeuedEventSave => RequeuedEventPrefix,
                ResumeCase.ResumedEventSave or ResumeCase.FlushInputThenResumedEventSave
                    or ResumeCase.PreInputResumedEventSave => ResumedEventPrefix,
                _ => null,
            };
            if (prefix is not null && Volatile.Read(ref _disarmed) == 0 && Adds(eventData, prefix)
                && Interlocked.CompareExchange(ref _calls, 1, 0) == 0)
                throw new DbUpdateException(Message);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (flip == ResumeCase.FlushAfterEnsureInput && !Commands.IsArmed
                && eventData.Context!.ChangeTracker.Entries<AgentTaskEvent>()
                    .Any(e => e.Entity.Detail?.StartsWith(RequeuedEventPrefix, StringComparison.Ordinal) == true))
                Commands.Arm(this);
            return ValueTask.FromResult(result);
        }

        internal bool TryCount() =>
            Volatile.Read(ref _disarmed) == 0 && Interlocked.CompareExchange(ref _calls, 1, 0) == 0;

        private static bool Adds(DbContextEventData eventData, string prefix) =>
            eventData.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.State == EntityState.Added
                && e.Entity.Type == AgentTaskEventType.Warning
                && e.Entity.Detail?.StartsWith(prefix, StringComparison.Ordinal) == true);
    }

    /// <summary>Fails the boot flush's pending-supervision read once armed.</summary>
    private sealed class FlushReadFault : DbCommandInterceptor
    {
        private ResumeBookkeepingFault? _owner;
        public bool IsArmed => Volatile.Read(ref _owner) is not null;

        public void Arm(ResumeBookkeepingFault owner) => Volatile.Write(ref _owner, owner);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var text = command.CommandText;
            if (Volatile.Read(ref _owner) is { } owner
                && text.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal)
                && text.Contains($"\"Origin\" = {(int)QueuedMessageOrigin.Supervision}", StringComparison.Ordinal)
                && owner.TryCount())
                throw new InvalidOperationException(ResumeBookkeepingFault.Message);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
