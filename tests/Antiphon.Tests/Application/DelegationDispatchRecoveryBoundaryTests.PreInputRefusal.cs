using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-1150 S2 repair F11 (Review 0336a6b0): a delivery that the queue refuses before any byte
/// is typed is not input. A later resume bookkeeping failure must still kill and fail the launch,
/// exactly as before S2. A write that began and then failed stays possible input.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// One pre-input refusal class per case, through the boot flush (existing brief row) or the
    /// backfill ensure (no row), followed by a one-shot event-save fault. The last case is the
    /// may-have-typed companion: the write began, so the recipient is kept.
    /// </summary>
    [Test]
    [Arguments("flush-missing-spill")]
    [Arguments("flush-courier-missing-spill")]
    [Arguments("flush-spill-write-refusal")]
    [Arguments("ensure-courier-missing-spill")]
    [Arguments("ensure-spill-write-refusal")]
    [Arguments("may-have-typed-transport-failure")]
    public async Task C1150_Pre_input_refusal_keeps_the_resume_failure_path(string label)
    {
        var outcome = await RunPreInputRefusalAsync(label);
        var why = "F11 " + label;

        outcome.Faults.ShouldBe(1, why + ": injected event-save fault reached");
        outcome.Inputs.ShouldBe(0, why + ": the fake terminal received no bytes");
        outcome.Submitted.ShouldBeEmpty(why);
        outcome.Prompts.ShouldBeEmpty(why);
        outcome.TaskStatus.ShouldBe(AgentTaskStatus.Dispatched, why);
        outcome.TaskFailureReason.ShouldBeNull(why);
        var brief = outcome.Rows.ShouldHaveSingleItem(why + ": one brief row");
        brief.ExecutionTaskId.ShouldBe(outcome.TaskId, why);

        if (label == "may-have-typed-transport-failure")
        {
            // The write call began: the terminal may hold the body, so bookkeeping is best-effort.
            outcome.Error.ShouldBeNull(why + ": a possibly typed delivery fails the resumed launch");
            outcome.Killed.ShouldBeFalse(why);
            outcome.SessionStatus.ShouldBe(SessionStatus.Running, why);
            brief.DeliveryAttempts.ShouldBe(1, why);
            return;
        }

        // Pre-S2 behaviour: nothing was typed, so the failed bookkeeping kills and fails the launch.
        outcome.Error.ShouldNotBeNull(why + ": a pre-input refusal suppressed the launch failure");
        outcome.Error.ShouldContain(ResumeBookkeepingFault.Message, Case.Sensitive, why);
        outcome.Killed.ShouldBeTrue(why);
        outcome.SessionStatus.ShouldBe(SessionStatus.Failed, why);
        if (label == "flush-missing-spill")
        {
            // The queue cancels the untypable pointer before charging an attempt.
            brief.Status.ShouldBe(QueuedMessageStatus.Canceled, why);
            brief.DeliveryAttempts.ShouldBe(0, why);
        }
    }

    /// <summary>
    /// CARD-1150 S2 repair 6 (Review 6bab136d): a UI local command queued while the session is
    /// Starting is flushed ahead of the brief. Its helper refuses a modal or a missing snapshot,
    /// or its modal read throws, before the first byte; that is not input, so the resume
    /// bookkeeping fault still kills and fails the launch as before S2. A local-command write
    /// that began and then failed keeps the recipient. Each class also runs without the fault.
    /// </summary>
    [Test]
    [Arguments("local-no-snapshot", false)]
    [Arguments("local-no-snapshot", true)]
    [Arguments("local-modal", false)]
    [Arguments("local-modal", true)]
    [Arguments("local-pre-write-exception", false)]
    [Arguments("local-pre-write-exception", true)]
    [Arguments("local-write-failure", false)]
    [Arguments("local-write-failure", true)]
    public async Task C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command(
        string label, bool bookkeepingFault)
    {
        var outcome = await RunPreInputRefusalAsync(label, bookkeepingFault);
        var why = $"F11 {label} bookkeepingFault={bookkeepingFault}";

        outcome.Faults.ShouldBe(bookkeepingFault ? 1 : 0, why + ": injected event-save fault reached");
        outcome.HelperFaults.ShouldBe(label is "local-modal" or "local-pre-write-exception" ? 1 : 0,
            why + ": the local-command helper's modal read was reached once");
        outcome.Inputs.ShouldBe(0, why + ": the fake terminal received no bytes");
        outcome.Submitted.ShouldBeEmpty(why);
        outcome.Prompts.ShouldBeEmpty(why);
        outcome.TaskStatus.ShouldBe(AgentTaskStatus.Dispatched, why);
        outcome.TaskFailureReason.ShouldBeNull(why);
        var local = outcome.Rows.Where(m => m.Origin == QueuedMessageOrigin.Ui).ShouldHaveSingleItem(why);
        local.Body.ShouldBe("/status", why);
        local.DeliveryAttempts.ShouldBe(1, why + ": the local command was the delivery attempted");
        var brief = outcome.Rows.Where(m => m.Origin == QueuedMessageOrigin.Delegation).ShouldHaveSingleItem(why);
        brief.ExecutionTaskId.ShouldBe(outcome.TaskId, why);
        brief.Status.ShouldBe(QueuedMessageStatus.Pending, why + ": the brief waits behind the local command");
        brief.DeliveryAttempts.ShouldBe(0, why);

        if (!bookkeepingFault || label == "local-write-failure")
        {
            // No bookkeeping failure, or the write began and the recipient may be Working.
            outcome.Error.ShouldBeNull(why);
            outcome.Killed.ShouldBeFalse(why);
            outcome.SessionStatus.ShouldBe(SessionStatus.Running, why);
            return;
        }

        // Pre-S2 behaviour: nothing was typed, so the failed bookkeeping kills and fails the launch.
        outcome.Error.ShouldNotBeNull(why + ": a local-command refusal before its write suppressed the launch failure");
        outcome.Error.ShouldContain(ResumeBookkeepingFault.Message, Case.Sensitive, why);
        outcome.Killed.ShouldBeTrue(why);
        outcome.SessionStatus.ShouldBe(SessionStatus.Failed, why);
    }

    private sealed record PreInputOutcome(
        Guid TaskId,
        int Faults,
        string? Error,
        bool Killed,
        SessionStatus SessionStatus,
        AgentTaskStatus TaskStatus,
        string? TaskFailureReason,
        List<SessionQueuedMessage> Rows,
        List<string> Prompts,
        List<string> Submitted,
        int Inputs,
        int HelperFaults);

    private static async Task<PreInputOutcome> RunPreInputRefusalAsync(string label, bool bookkeepingFault = true)
    {
        var ensure = label.StartsWith("ensure-", StringComparison.Ordinal);
        var localCommand = label.StartsWith("local-", StringComparison.Ordinal);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = true,
            ThrowOnRenderedSnapshot = label == "local-no-snapshot",
            ThrowOnSend = label switch
            {
                "flush-courier-missing-spill" or "ensure-courier-missing-spill" =>
                    new RemoteSpillUndeliverableException(),
                "flush-spill-write-refusal" or "ensure-spill-write-refusal" => new RunnerSpillWriteException(),
                "may-have-typed-transport-failure" or "local-write-failure" =>
                    new InvalidOperationException("C1150 F11 transport failed during the write"),
                _ => null,
            },
        };
        // The ensure path fails "brief re-queued"; the flush path fails "launch resumed".
        var fault = new ResumeBookkeepingFault(!bookkeepingFault ? ResumeCase.Healthy
            : ensure ? ResumeCase.RequeuedEventSave : ResumeCase.ResumedEventSave);
        var helper = new LocalCommandHelperFault(label, schema.ConnectionString);
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(fault, fault.Commands, helper, helper.Commands),
            ConfigureServices = services =>
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter)),
        });
        var connection = schema.ConnectionString;
        var agentId = harness.AgentId;
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "pre-input-refusal")).FullName;
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "fake", AgentKind = localCommand ? AgentKind.Codex : AgentKind.ClaudeCode,
                Status = SessionStatus.Starting, Cwd = directory, Cols = 120, Rows = 30,
                CreatedAt = now.AddMinutes(-2), StartedAt = now.AddMinutes(-2), LastSeenAt = now.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AgentStatus.Running)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Pre-input refusal keeps the failure path",
                Goal = "Keep the pre-S2 launch failure when nothing was typed.",
                Role = AgentTaskRole.Plan, AgentKind = localCommand ? AgentKind.Codex : AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = directory, AgentSessionId = sessionId,
                AgentId = agentId, Status = AgentTaskStatus.Dispatched, CreatedAt = now.AddMinutes(-2),
                DispatchedAt = now.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
            if (localCommand)
            {
                // A UI local command (no UserPrompt row) queued while Starting, ahead of the brief.
                await harness.Queue.EnqueueAsync(sessionId, "/status", MessageSendMode.WhenIdle,
                    CancellationToken.None, QueuedMessageOrigin.Ui, deliverIfIdle: false);
            }

            if (!ensure)
            {
                // The dispatcher persisted the brief before the restart; only the boot flush can type it.
                var messageId = Guid.NewGuid();
                var relative = TypedBodySpill.InboxRelativePath(messageId.ToString("D"));
                var missingSpill = label == "flush-missing-spill";
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = messageId, AgentSessionId = sessionId,
                    // A spill pointer whose bytes are stored nowhere cannot be typed.
                    Body = missingSpill
                        ? $"{marker} YOUR BRIEF IS NOT IN THIS MESSAGE.\n\n    {relative}"
                        : $"{marker}\nDeliver this interrupted brief.",
                    RemoteSpillRelativePath = missingSpill ? relative : null,
                    Status = QueuedMessageStatus.Pending, Sequence = localCommand ? 2 : 1, CreatedAt = now.AddMinutes(-2),
                    Origin = QueuedMessageOrigin.Delegation, ExecutionTaskId = taskId,
                });
            }

            await db.SaveChangesAsync();
        }

        adapter.RegisterOnStart = harness.Runtime;
        adapter.OnSubmitted = async body =>
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt, body,
                timestamp: DateTime.UtcNow, connectionString: connection);

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
        helper.Disarm();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var status = (await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status;
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var rows = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId).ToListAsync();
        var prompts = await UserPromptsAsync(connection, sessionId);
        return new PreInputOutcome(taskId, fault.Calls, error, adapter.Killed, status, task.Status,
            task.FailureReason, rows, prompts, adapter.SubmittedBodies.ToList(), adapter.Inputs.Count,
            helper.Reached);
    }
    /// <summary>
    /// Once the local command's row is stamped Sent, the helper's own modal read (the second after
    /// the stamp; DeliverAsync's opening check is the first) sees a newly opened modal
    /// ("local-modal") or throws ("local-pre-write-exception"). Both precede the helper's write.
    /// </summary>
    private sealed class LocalCommandHelperFault(string label, string connection) : SaveChangesInterceptor
    {
        public const string Message = "C1150 F11 injected local-command modal read failure";
        private int _armed;
        private int _queries;
        private int _reached;
        private Guid _sessionId;
        public int Reached => Volatile.Read(ref _reached);
        public ModalReadFault Commands { get; } = new();

        public void Disarm() => Interlocked.Exchange(ref _armed, 2);

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (label is "local-modal" or "local-pre-write-exception"
                && eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e =>
                    e.Entity.Origin == QueuedMessageOrigin.Ui && e.Entity.Status == QueuedMessageStatus.Sent
                    && e.Entity.DeliveryAttempts > 0))
            {
                _sessionId = eventData.Context.ChangeTracker.Entries<SessionQueuedMessage>()
                    .First(e => e.Entity.Origin == QueuedMessageOrigin.Ui).Entity.AgentSessionId;
                Interlocked.CompareExchange(ref _armed, 1, 0);
                Commands.Owner = this;
            }
            return ValueTask.FromResult(result);
        }

        internal async Task OnModalReadAsync()
        {
            if (Volatile.Read(ref _armed) != 1 || Interlocked.Increment(ref _queries) != 2)
                return;
            Interlocked.Increment(ref _reached);
            if (label == "local-pre-write-exception")
                throw new InvalidOperationException(Message);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == _sessionId);
            db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
            {
                Id = Guid.NewGuid(), SessionId = session.Id,
                AcceptedStartedAt = SessionGeneration.Normalize(session.StartedAt),
                FirstObservedAt = DateTime.UtcNow, LastObservedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    private sealed class ModalReadFault : DbCommandInterceptor
    {
        public LocalCommandHelperFault? Owner { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Owner is { } owner
                && command.CommandText.Contains("\"RemoteControlModalEpisodes\"", StringComparison.Ordinal))
                await owner.OnModalReadAsync();
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
