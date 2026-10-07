using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1149/1150 boundary. TestDesign pins the three inherited statement rows.
/// Code adds the other eleven <c>C1149_C1150_Statement_budgets</c> arguments before CP-19.
/// </summary>
[Category("Integration")]
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("held-dispatched-tick", 18)]
    [Arguments("working-live-tick", 18)]
    [Arguments("inside-grace-absent-scan", 8)]
    public async Task C1149_C1150_Statement_budgets(string action, int expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var counter = new FullCommandCounter();
        int total;
        string roster;
        if (action == "inside-grace-absent-scan")
            (total, roster) = await MeasureScanAsync(schema.ConnectionString, counter);
        else
            (total, roster) = await MeasureTickAsync(schema.ConnectionString, counter, action);

        Console.WriteLine($"C1149-BUDGET {action} total={total}");
        total.ShouldBe(expected, $"action {action}{Environment.NewLine}{roster}");
    }

    private static async Task<(int Total, string Roster)> MeasureTickAsync(
        string connection, FullCommandCounter counter, string action)
    {
        var now = Pg(DateTime.UtcNow.AddMinutes(-1));
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var working = action == "working-live-tick";
        var cwd = Path.Combine(Path.GetTempPath(), "antiphon-c1149-budget", sessionId.ToString("N"));
        Directory.CreateDirectory(cwd);
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            var name = $"bud-{agentId:N}"[..16];
            seed.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = cwd,
                Details = "CARD-1149/1150 statement budget.",
                Status = AgentStatus.Running,
                Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                IsPoolDelegate = false,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "budget",
                AgentKind = AgentKind.ClaudeCode,
                Status = working ? SessionStatus.Running : SessionStatus.Starting,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            seed.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Statement budget",
                Goal = "Count one young tick.",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = working ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched,
                Attempt = 1,
                ReplyTo = AgentTaskReplyTo.None,
                Ephemeral = false,
                CreatedAt = now,
                DispatchedAt = now,
            });
            if (working)
            {
                seed.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"budget-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = "the brief",
                    Timestamp = now,
                    CreatedAt = now,
                });
            }
            else
            {
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Body = "pending brief",
                    Status = QueuedMessageStatus.Pending,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.Delegation,
                    CreatedAt = now,
                });
            }

            await seed.SaveChangesAsync();
        }

        var runner = new RecordingRunnerClient();
        if (working)
        {
            runner.Sessions.Add(new SessionRunnerSessionDto(
                sessionId, Pid: 7, StartedAt: now, Status: "Running", ExitCode: null,
                ExitReason: AgentExitReason.Unknown, LastSequence: 1, AcceptedStartedAt: now));
        }

        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = sessionId,
            AttachAgentId = agentId,
            Delegation = new DelegationSettings { MaxConcurrentTasks = 2 },
            ConfigureDbContext = options => options.AddInterceptors(counter),
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<DeadSessionFirstSeenState>();
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        await using var _ = harness;

        counter.Reset();
        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            dispatcher.WorkspaceProbeOverride = new StubWorkspaceProgressProbe(
                new WorkspaceProgressArm(true, null, null, false));
            var tick = await dispatcher.TickAsync(CancellationToken.None);
            tick.SweepFailures.ShouldBe(0, counter.Roster());
            tick.Dispatched.ShouldBe(0);
        }

        var total = counter.Total;
        var roster = counter.Roster();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(working ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched);
        task.Attempt.ShouldBe(1);
        task.FailureCode.ShouldBeNull();
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
            .ShouldBe(working ? SessionStatus.Running : SessionStatus.Starting);
        return (total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureScanAsync(
        string connection, FullCommandCounter counter)
    {
        var now = Pg(DateTime.UtcNow.AddSeconds(-30));
        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), "antiphon-c1149-scan", sessionId.ToString("N"));
        Directory.CreateDirectory(cwd);
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "budget-scan",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Starting,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            var taskId = Guid.NewGuid();
            seed.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Inside grace",
                Goal = "Stay Starting.",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Dispatched,
                Attempt = 1,
                ReplyTo = AgentTaskReplyTo.None,
                Ephemeral = false,
                CreatedAt = now,
                DispatchedAt = now,
            });
            seed.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = "pending brief",
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = now,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection, counter));
        var service = SessionReconciliationServiceTests.BuildService(
            db, new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [] }, new MockEventBus());
        counter.Reset();
        await service.ScanAsync(CancellationToken.None);
        var total = counter.Total;
        var roster = counter.Roster();
        (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Starting);
        return (total, roster);
    }

    private static DateTime Pg(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }
}
