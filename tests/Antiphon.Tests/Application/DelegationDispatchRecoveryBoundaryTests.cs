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
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1149/1150 boundary. TestDesign pins the three inherited statement rows.
/// Code adds the other eleven <c>C1149_C1150_Statement_budgets</c> arguments before CP-19.
/// </summary>
[Category("Integration")]
[ParallelLimiter<LoadTimeoutParallelLimit>]
// Above the 30s wall cap and the 48.71s Linux max row. ceil(95/4)*90s = 2160s,
// under the 2400s lone-class ceiling for a planner chunk of about 95 rows.
[Timeout(90_000)]
public partial class DelegationDispatchRecoveryBoundaryTests
{
    // 30s keeps the existing idle bound. 2x the 9.2s cost-file pace (3505s/381) is 18.4s.
    // The 20s HTTP bound rises to this cap. The pace row is not tightened.
    private static readonly TimeSpan LoadWallCap = TimeSpan.FromSeconds(30);

    private static Task WaitForLaunchIdleAsync(AgentSessionLaunchQueue queue)
    {
        // The queue timeout follows this clock. Leaving it unadvanced keeps a launch
        // that is still running off the inner budget. LoadWallCap fails one that never idles.
        var logical = new FakeTimeProvider();
        return LoadTimeoutWait.UntilSignaledAsync(
            ct => queue.WaitForIdleAsync(TimeSpan.FromDays(1), ct, logical),
            clock: null, budget: null, wallCap: LoadWallCap, label: "launch idle");
    }

    [Test]
    // CARD-0097 enabled the shared harness's routing-blocked recovery sweep:
    // one empty-result SELECT raises each historical 18-command tick to exactly 19.
    [Arguments("held-dispatched-tick", 19)]
    [Arguments("working-live-tick", 19)]
    // BuildService, one Starting session inside the 90s grace, empty runner,
    // launch ownership absent and census alerts off. Measured roster is the live
    // AgentSessions select, the FOR UPDATE, the reload, and the Agents select.
    // The investigation's 8 is not this graph; the plan's exact 8 has to be
    // revised before Code treats it as acceptance for this fixture.
    [Arguments("inside-grace-absent-scan", 4)]
    // CARD-0097 adds the same sweep SELECT (18 + 1); first-observation grace adds no recovery SQL.
    [Arguments("first-dead-session-observation", 19)]
    // Historical 42 + CARD-0097's routing sweep SELECT + CARD-1285's required
    // TokenHash UPDATE before the refusing launch sink = exactly 44.
    [Arguments("initial-refused-dispatch", 44)]
    // Measured 5. The plan's 6 counted a commit or alert this interceptor does not see.
    // Live select, FOR UPDATE, reload, the close UPDATE, then the Agents select. No AgentTasks query.
    [Arguments("runner-unknown-close-scan", 5)]
    // No-parent 19 and with-parent 21 are asserted in the measure. Ceilings were 40 and 48.
    [Arguments("due-absent-hold", 19)]
    // Historical 16 + CARD-0097's routing sweep SELECT = exactly 17.
    // The blocked task is not a delivery suspect; there is no repeated recovery transition.
    [Arguments("repeated-blocked-tick", 17)]
    // CARD-1543 removed four kind-only probes (71 - 4). CARD-1574 adds three fresh
    // owner/generation reads: idle-flush receipt fence, delivery receipt fence, transcript baseline.
    [Arguments("running-recovery-existing-brief", 70)]
    // CARD-1543 removed four probes (67 - 4). CARD-1574 adds two owner/generation reads:
    // direct ensure's delivery receipt fence and transcript baseline; no idle flush is needed.
    [Arguments("running-recovery-missing-brief", 65)]
    // Measured 6. The delivered session drops out of pass 1c: selects only, no write.
    [Arguments("repeated-discovery-after-delivery", 6)]
    // Measured 12. One Blocked event; under the design ceiling of 65.
    [Arguments("running-recovery-readiness-hold", 12)]
    // CARD-1543 removed four probes (67 - 4). CARD-1574 adds the same two direct-ensure
    // receipt/baseline reads; the independent watchdog and fresh body/Enter guards remain.
    [Arguments("watchdog-first-recovery", 65)]
    // Two scans are equal. Measured 5 on the second scan. Delta 0.
    [Arguments("empty-healthy-scan", 5)]
    public async Task C1149_C1150_Statement_budgets(string action, int expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var counter = new FullCommandCounter();
        if (action == "due-absent-hold")
        {
            expected.ShouldBe(19);
            await MeasureDueAbsentHoldAsync(counter);
            return;
        }

        int total;
        string roster;
        if (action == "inside-grace-absent-scan")
            (total, roster) = await MeasureScanAsync(schema.ConnectionString, counter);
        else if (action is "held-dispatched-tick" or "working-live-tick" or "first-dead-session-observation")
            (total, roster) = await MeasureTickAsync(schema.ConnectionString, counter, action);
        else
            (total, roster) = await MeasureAddedBudgetAsync(schema.ConnectionString, counter, action);

        if (action is "running-recovery-existing-brief" or "running-recovery-missing-brief" or "watchdog-first-recovery")
            counter.AppendFullCommands(FullCommandCounter.CreateEvidenceFile("c1529-" + action), action);
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
        var observeDead = action == "first-dead-session-observation";
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
                Status = observeDead
                    ? SessionStatus.Failed
                    : working ? SessionStatus.Running : SessionStatus.Starting,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                EndedAt = observeDead ? now : null,
                FailureReason = observeDead ? SessionReconciliationService.RunnerUnknownSessionReason : null,
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

        var runner = new ListedInventoryRunner();
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
            .ShouldBe(observeDead
                ? SessionStatus.Failed
                : working ? SessionStatus.Running : SessionStatus.Starting);
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
