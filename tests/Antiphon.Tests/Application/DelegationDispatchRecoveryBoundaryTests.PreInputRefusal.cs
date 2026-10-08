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
        int Inputs);

    private static async Task<PreInputOutcome> RunPreInputRefusalAsync(string label)
    {
        var ensure = label.StartsWith("ensure-", StringComparison.Ordinal);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = true,
            ThrowOnSend = label switch
            {
                "flush-courier-missing-spill" or "ensure-courier-missing-spill" =>
                    new RemoteSpillUndeliverableException(),
                "flush-spill-write-refusal" or "ensure-spill-write-refusal" => new RunnerSpillWriteException(),
                "may-have-typed-transport-failure" =>
                    new InvalidOperationException("C1150 F11 transport failed during the write"),
                _ => null,
            },
        };
        // The ensure path fails "brief re-queued"; the flush path fails "launch resumed".
        var fault = new ResumeBookkeepingFault(ensure ? ResumeCase.RequeuedEventSave : ResumeCase.ResumedEventSave);
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
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "pre-input-refusal")).FullName;
        var marker = DelegationReportFormatter.TaskMarker(taskId);
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
                Id = taskId, RootTaskId = taskId, Title = "Pre-input refusal keeps the failure path",
                Goal = "Keep the pre-S2 launch failure when nothing was typed.",
                Role = AgentTaskRole.Plan, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = directory, AgentSessionId = sessionId,
                AgentId = agentId, Status = AgentTaskStatus.Dispatched, CreatedAt = now.AddMinutes(-2),
                DispatchedAt = now.AddMinutes(-2),
            });
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
                    Status = QueuedMessageStatus.Pending, Sequence = 1, CreatedAt = now.AddMinutes(-2),
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
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var status = (await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status;
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var rows = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId).ToListAsync();
        var prompts = await UserPromptsAsync(connection, sessionId);
        return new PreInputOutcome(taskId, fault.Calls, error, adapter.Killed, status, task.Status,
            task.FailureReason, rows, prompts, adapter.SubmittedBodies.ToList(), adapter.Inputs.Count);
    }
}
