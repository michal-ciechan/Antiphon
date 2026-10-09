using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    private static async Task<(int Total, string Roster)> MeasureAddedBudgetAsync(
        string connection, FullCommandCounter counter, string action)
    {
        return action switch
        {
            "initial-refused-dispatch" => await MeasureRefusedDispatchAsync(connection, counter),
            "runner-unknown-close-scan" => await MeasureUnknownCloseAsync(connection, counter),
            "repeated-blocked-tick" => await MeasureBlockedTickAsync(connection, counter),
            "running-recovery-existing-brief" => await MeasureRunningRecoveryAsync(connection, counter, existingBrief: true),
            "running-recovery-missing-brief" => await MeasureRunningRecoveryAsync(connection, counter, existingBrief: false),
            "repeated-discovery-after-delivery" => await MeasureRepeatedDiscoveryAsync(connection, counter),
            "running-recovery-readiness-hold" => await MeasureReadinessHoldAsync(connection, counter),
            "watchdog-first-recovery" => await MeasureWatchdogRecoveryAsync(connection, counter),
            "empty-healthy-scan" => await MeasureHealthyScansAsync(connection, counter),
            _ => throw new InvalidOperationException("Unknown budget action " + action),
        };
    }

    private static async Task MeasureDueAbsentHoldAsync(FullCommandCounter counter)
    {
        var without = await MeasureOneDueHoldAsync(counter, parent: false);
        var with = await MeasureOneDueHoldAsync(counter, parent: true);
        without.Total.ShouldBe(19, "no parent" + Environment.NewLine + without.Roster);
        with.Total.ShouldBe(21, "with parent" + Environment.NewLine + with.Roster);
    }

    private static async Task<(int Total, string Roster)> MeasureOneDueHoldAsync(FullCommandCounter counter, bool parent)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = parent, AgeMinutes = 1 });
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        await using var host = OpenSweep(
            schema.ConnectionString, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState(), counter);
        await host.SweepAsync();
        counter.Reset();
        host.Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
        await host.SweepAsync();
        var total = counter.Total;
        var roster = counter.Roster();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Blocked, parent ? "parent" : "no parent");
        task.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason);
        task.Attempt.ShouldBe(seeded.Attempt);
        stopper.Killed.ShouldBeEmpty();
        if (parent)
        {
            var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.ParentId);
            note.SourceTaskId.ShouldBe(seeded.TaskId);
            note.ExecutionTaskId.ShouldBeNull();
        }

        return (total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureRefusedDispatchAsync(
        string connection, FullCommandCounter counter)
    {
        var goal = "Keep this goal exact.\nLine two café \u2603.\n" + new string('x', 1200);
        var sink = new RefusingLaunchSink();
        var settings = new DelegationSettings { MaxConcurrentTasks = 16 };
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            AlwaysOn = false,
            Delegation = settings,
            ConfigureDbContext = options => options.AddInterceptors(counter),
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter));
                services.AddSingleton<IAgentTaskLaunchSink>(sink);
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "claude",
                        Definitions =
                        {
                            ["claude"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                            },
                        },
                    }));
            },
        });
        await using var _ = harness;
        settings.AllowedRoots = [harness.TempRoot];
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "worker")).FullName;
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            var boardId = await db.Agents.Where(a => a.Id == harness.AgentId).Select(a => a.BoardId).SingleAsync();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                BoardId = boardId,
                Name = "absent worker",
                Slug = "absent-" + agentId.ToString("N")[..12],
                Kind = AgentKind.ClaudeCode,
                WorkingDirectory = directory,
                AlwaysOn = false,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = parentId,
                DefinitionName = "parent",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Stopped,
                Cwd = directory,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                EndedAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Retain the refused launch",
                Goal = goal,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Role = AgentTaskRole.Custom,
                Workspace = WorkspaceMode.Shared,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = parentId,
                WorkingDirectory = directory,
                AgentId = agentId,
                Status = AgentTaskStatus.Queued,
                CreatedAt = now.AddMinutes(-2),
                ExecutionDeadlineAt = now.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        await using var scope = harness.Provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        counter.Reset();
        var tick = await dispatcher.TickAsync(CancellationToken.None);
        tick.SweepFailures.ShouldBe(0, counter.Roster());
        sink.Calls.ShouldBe(1, counter.Roster());
        return (counter.Total, counter.Roster());
    }

    private static async Task<(int Total, string Roster)> MeasureUnknownCloseAsync(
        string connection, FullCommandCounter counter)
    {
        var sessionId = Guid.NewGuid();
        var when = Pg(DateTime.UtcNow.AddMinutes(-5));
        var cwd = Path.Combine(Path.GetTempPath(), "antiphon-c1149-close", sessionId.ToString("N"));
        Directory.CreateDirectory(cwd);
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "unknown-close",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = when,
                StartedAt = when,
                LastSeenAt = when,
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
        (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Failed);
        (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).FailureReason
            .ShouldBe(SessionReconciliationService.RunnerUnknownSessionReason);
        roster.Contains("AgentTasks", StringComparison.Ordinal).ShouldBeFalse(roster);
        return (total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureBlockedTickAsync(
        string connection, FullCommandCounter counter)
    {
        var now = Pg(DateTime.UtcNow.AddMinutes(-1));
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), "antiphon-c1149-blocked", sessionId.ToString("N"));
        Directory.CreateDirectory(cwd);
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            var name = $"blk-{agentId:N}"[..16];
            seed.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = cwd,
                Details = "Already blocked.",
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
                DefinitionName = "blocked",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Failed,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                EndedAt = now,
                FailureReason = SessionReconciliationService.RunnerUnknownSessionReason,
            });
            seed.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Already held",
                Goal = "Stay blocked.",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Blocked,
                FailureReason = AgentTaskDispatcher.DispatchLaunchAbsentReason,
                Attempt = 1,
                ReplyTo = AgentTaskReplyTo.None,
                Ephemeral = false,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await seed.SaveChangesAsync();
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
                services.AddSingleton<ISessionRunnerClient>(new ListedInventoryRunner());
                services.AddSingleton<DeadSessionFirstSeenState>();
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        await using var _ = harness;
        await using var scope = harness.Provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        dispatcher.WorkspaceProbeOverride = new StubWorkspaceProgressProbe(
            new WorkspaceProgressArm(true, null, null, false));
        counter.Reset();
        var tick = await dispatcher.TickAsync(CancellationToken.None);
        tick.SweepFailures.ShouldBe(0, counter.Roster());
        var roster = counter.Roster();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason);
        task.Attempt.ShouldBe(1);
        return (counter.Total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureRunningRecoveryAsync(
        string connection, FullCommandCounter counter, bool existingBrief)
    {
        var seeded = await SeedCurrentAsync(connection, "Do the thing.");
        if (existingBrief)
        {
            await using var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            seed.SessionQueuedMessages.Add(HealthyRow(seeded, seeded.Goal));
            await seed.SaveChangesAsync();
        }

        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenDispatchAsync(connection, seeded, adapter, runner, counter);
        WireDispatchPrompt(adapter, world, seeded.SessionId);
        await RetireHarnessSessionAsync(world, seeded.SessionId);
        counter.Reset();
        await DiscoverCountedAsync(world, counter);
        var roster = counter.Roster();
        adapter.KillCount.ShouldBe(0, roster);
        runner.Kills.ShouldBe(0, roster);
        runner.Starts.ShouldBe(0, roster);
        return (counter.Total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureRepeatedDiscoveryAsync(
        string connection, FullCommandCounter counter)
    {
        var (_, _) = await MeasureRunningRecoveryAsync(connection, counter, existingBrief: false);
        var seeded = await FindOnlySessionAsync(connection);
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenDispatchAsync(connection, seeded, adapter, runner, counter);
        await RetireHarnessSessionAsync(world, seeded.SessionId);
        counter.Reset();
        await DiscoverCountedAsync(world, counter);
        var roster = counter.Roster();
        Writes(counter).ShouldBe(0, roster);
        adapter.Attached.ShouldBeFalse(roster);
        return (counter.Total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureReadinessHoldAsync(
        string connection, FullCommandCounter counter)
    {
        var seeded = await SeedCurrentAsync(connection, "Do the thing.");
        await AgeDispatchAsync(connection, seeded.TaskId, 11);
        var runner = new CountingRunner();
        runner.Sessions.Add(Listed(
            seeded.SessionId, "Running", seeded.StartedAt, pending: null, accepted: seeded.StartedAt.AddMinutes(-30)));
        var stopper = new RecordingSessionStopper();
        await using var host = OpenSweep(
            connection, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState(), counter);
        counter.Reset();
        (await host.NeverStartedAsync()).ShouldBe(0, counter.Roster());
        var roster = counter.Roster();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Blocked, roster);
        task.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchBriefRecoveryHeldReason, roster);
        (await verify.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1, roster);
        stopper.Killed.ShouldBeEmpty(roster);
        return (counter.Total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureWatchdogRecoveryAsync(
        string connection, FullCommandCounter counter)
    {
        var seeded = await SeedCurrentAsync(connection, "Do the thing.");
        await AgeDispatchAsync(connection, seeded.TaskId, 11);
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenDispatchAsync(connection, seeded, adapter, runner, counter);
        WireDispatchPrompt(adapter, world, seeded.SessionId);
        await RetireHarnessSessionAsync(world, seeded.SessionId);
        await using var scope = world.Harness.Provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        counter.Reset();
        (await dispatcher.FailNeverStartedAsync(CancellationToken.None)).ShouldBe(0, counter.Roster());
        var roster = counter.Roster();
        adapter.KillCount.ShouldBe(0, roster);
        runner.Kills.ShouldBe(0, roster);
        world.Stopper.Killed.ShouldBeEmpty(roster);
        return (counter.Total, roster);
    }

    private static async Task<(int Total, string Roster)> MeasureHealthyScansAsync(
        string connection, FullCommandCounter counter)
    {
        var seeded = await SeedCurrentAsync(connection, "Do the thing.");
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            seed.TranscriptEntries.Add(Prompt(seeded, "already delivered", 1, timestamp: seeded.DispatchedAt.AddMinutes(1)));
            await seed.SaveChangesAsync();
        }

        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection, counter));
        var service = SessionReconciliationServiceTests.BuildService(db, runner, new MockEventBus());
        counter.Reset();
        await service.ScanAsync(CancellationToken.None);
        var first = counter.Total;
        var firstRoster = counter.Roster();
        counter.Reset();
        await service.ScanAsync(CancellationToken.None);
        var second = counter.Total;
        var roster = counter.Roster();
        second.ShouldBe(first, "delta" + Environment.NewLine + firstRoster + Environment.NewLine + roster);
        (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status.ShouldBe(SessionStatus.Running);
        return (second, roster);
    }

    /// <summary>
    /// OpenDispatch creates its own Running session. Reconciliation would close that
    /// session as runner-unknown, and the close is not the measured recovery.
    /// </summary>
    private static async Task RetireHarnessSessionAsync(DispatchWorld world, Guid keep)
    {
        if (world.Harness.SessionId == keep)
            return;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Harness.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == world.Harness.SessionId);
        session.Status = SessionStatus.Stopped;
        session.EndedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task DiscoverCountedAsync(DispatchWorld world, FullCommandCounter counter)
    {
        var queue = world.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        await using var db = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(world.Harness.ConnectionString, counter));
        var service = SessionReconciliationServiceTests.BuildService(
            db,
            world.Harness.Provider.GetRequiredService<ISessionRunnerClient>(),
            world.Harness.EventBus,
            ownership: queue);
        await service.ScanAsync(CancellationToken.None);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static async Task<CurrentBrief> FindOnlySessionAsync(string connection)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.SingleAsync();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        return new CurrentBrief(
            task.Id, session.Id, task.AgentId!.Value, task.DispatchedAt!.Value, session.StartedAt,
            task.Goal, task.WorkingDirectory);
    }

    private static int Writes(FullCommandCounter counter) =>
        counter.Commands.Count(sql =>
        {
            var verb = sql.TrimStart();
            return verb.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                || verb.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || verb.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
        });
}
