using System.Data.Common;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1149 S1. V-1 is a real refused claim. V-2 through V-5 seed the same shape and drive only
/// the dead-session sweep, with no reply service and no bind-refusal recovery.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public async Task C1149_Absent_launch_is_blocked_with_original_input()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var goal = "Keep this goal exact.\nLine two café \u2603.\n" + new string('x', 1200);
        var sink = new RefusingLaunchSink();
        var settings = new DelegationSettings { MaxConcurrentTasks = 16 };
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            AlwaysOn = false,
            Delegation = settings,
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
        settings.AllowedRoots = [harness.TempRoot];
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "worker")).FullName;
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
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

        await using (var scope = harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        Guid sessionId;
        Guid briefId;
        byte[] bodyBytes;
        byte[]? spillBytes;
        long sequence;
        int attempt;
        DateTime? dispatchedAt;
        byte[] fileBytes;
        string spillPath;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            sink.Calls.ShouldBe(1, "V-1");
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-1");
            task.ParentSessionId.ShouldBe(parentId, "V-1");
            task.AgentSessionId.ShouldNotBeNull("V-1");
            sessionId = task.AgentSessionId.Value;
            attempt = task.Attempt;
            dispatchedAt = task.DispatchedAt;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Starting, "V-1");
            var brief = (await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId).ToListAsync())
                .ShouldHaveSingleItem("V-1");
            brief.Status.ShouldBe(QueuedMessageStatus.Pending, "V-1");
            briefId = brief.Id;
            sequence = brief.Sequence;
            bodyBytes = Encoding.UTF8.GetBytes(brief.Body);
            spillBytes = brief.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(brief.RemoteSpillBody);
            spillPath = Directory.GetFiles(directory, "task-*-brief.md", SearchOption.AllDirectories)
                .ShouldHaveSingleItem("V-1");
            fileBytes = await File.ReadAllBytesAsync(spillPath);

            var clock = new FakeTimeProvider(
                new DateTimeOffset(DateTime.SpecifyKind(session.StartedAt, DateTimeKind.Utc)).AddSeconds(91));
            var recon = SessionReconciliationServiceTests.BuildService(
                db,
                new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [] },
                new MockEventBus(),
                time: clock);
            await recon.ScanAsync(CancellationToken.None);
            var closed = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            closed.Status.ShouldBe(SessionStatus.Failed, "V-1");
            closed.FailureReason.ShouldBe(SessionReconciliationService.RunnerUnknownSessionReason, "V-1");
        }

        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var seen = new DeadSessionFirstSeenState();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, sweepClock, seen))
        {
            await host.SweepAsync();
            sweepClock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
            await host.SweepAsync();
            await host.SweepAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var held = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "V-1");
        held.CompletedAt.ShouldBeNull("V-1");
        held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "V-1");
        held.FailureReason!.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeTrue("V-1");
        held.Attempt.ShouldBe(attempt, "V-1");
        held.AgentSessionId.ShouldBe(sessionId, "V-1");
        held.AgentId.ShouldBe(agentId, "V-1");
        held.DispatchedAt.ShouldBe(dispatchedAt, "V-1");
        held.Goal.ShouldBe(goal, "V-1");
        var blocked = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked).ToListAsync();
        blocked.Count.ShouldBe(1, "V-1");
        blocked[0].Detail.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "V-1");
        var briefAfter = (await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId).ToListAsync())
            .ShouldHaveSingleItem("V-1");
        briefAfter.Id.ShouldBe(briefId, "V-1");
        briefAfter.Status.ShouldBe(QueuedMessageStatus.Pending, "V-1");
        briefAfter.Sequence.ShouldBe(sequence, "V-1");
        Encoding.UTF8.GetBytes(briefAfter.Body).ShouldBe(bodyBytes, "V-1");
        (briefAfter.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(briefAfter.RemoteSpillBody))
            .ShouldBe(spillBytes, "V-1");
        (await File.ReadAllBytesAsync(spillPath)).ShouldBe(fileBytes, "V-1");
        var note = (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == parentId).ToListAsync())
            .ShouldHaveSingleItem("V-1");
        note.ExecutionTaskId.ShouldBeNull("V-1");
        note.SourceTaskId.ShouldBe(taskId, "V-1");
        sink.Calls.ShouldBe(1, "V-1");
        Quiet(runner, stopper, "V-1");
    }

    [Test]
    public async Task C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true });
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var seen = new DeadSessionFirstSeenState();
        var fault = new BlockedSaveFault();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen, fault))
        {
            await host.SweepAsync();
            clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
            fault.Armed = true;
            await host.SweepAsync();
        }

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-2");
            task.FailureReason.ShouldBeNull("V-2");
            task.Attempt.ShouldBe(seeded.Attempt, "V-2");
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0, "V-2");
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seeded.ParentId))
                .ShouldBe(0, "V-2");
        }

        fault.Armed.ShouldBeFalse("V-2");
        await using (var again = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen))
        {
            await again.SweepAsync();
            await again.SweepAsync();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var held = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "V-2");
        held.CompletedAt.ShouldBeNull("V-2");
        held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "V-2");
        held.Attempt.ShouldBe(seeded.Attempt, "V-2");
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(1, "V-2");
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seeded.ParentId)).ShouldBe(1, "V-2");
        var brief = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
        brief.Status.ShouldBe(QueuedMessageStatus.Pending, "V-2");
        Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, "V-2");
        Encoding.UTF8.GetBytes(brief.RemoteSpillBody!).ShouldBe(seeded.Spill, "V-2");
        Quiet(runner, stopper, "V-2");
    }

    [Test]
    [Arguments("listed-running")]
    [Arguments("listed-starting")]
    [Arguments("listed-exited")]
    [Arguments("wrong-generation")]
    [Arguments("unavailable-inventory")]
    public async Task C1149_Listed_or_unknown_runner_is_never_absence(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var remote = shape == "unavailable-inventory";
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Parent = false,
            RunnerId = remote ? "remote-owner" : null,
        });
        var runner = new CountingRunner();
        if (!remote)
        {
            var accepted = shape == "wrong-generation" ? seeded.StartedAt.AddHours(1) : seeded.StartedAt;
            var status = shape switch
            {
                "listed-running" => "Running",
                "listed-starting" => "Starting",
                _ => "Exited",
            };
            runner.Sessions.Add(Listed(seeded.SessionId, status, seeded.StartedAt, shape == "listed-starting" ? "waiting" : null, accepted));
        }

        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(
            schema.ConnectionString, runner, stopper, new FakeTimeProvider(DateTimeOffset.UtcNow),
            new DeadSessionFirstSeenState(), unavailable: remote))
            await host.DueAsync();

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-3");
        task.CompletedAt.ShouldBeNull("V-3");
        task.FailureReason.ShouldBeNull("V-3");
        task.Attempt.ShouldBe(seeded.Attempt, "V-3");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0, "V-3");
        var brief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
        Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, "V-3");
        Quiet(runner, stopper, "V-3");
    }

    [Test]
    [Arguments("working-task")]
    [Arguments("working-transcript")]
    [Arguments("attempt-rebind")]
    public async Task C1149_Changed_or_working_attempt_is_untouched(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Parent = false,
            Status = shape == "working-task" ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched,
            Prompt = shape == "working-transcript",
        });
        var bump = new AttemptBump { Connection = schema.ConnectionString, TaskId = seeded.TaskId };
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = OpenSweep(
            schema.ConnectionString, runner, stopper, clock, new DeadSessionFirstSeenState(), bump);
        await host.SweepAsync();
        clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
        if (shape == "attempt-rebind")
            bump.Armed = true;
        await host.SweepAsync();

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(shape == "working-task" ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched, "V-4");
        task.CompletedAt.ShouldBeNull("V-4");
        task.FailureReason.ShouldBeNull("V-4");
        task.Attempt.ShouldBe(shape == "attempt-rebind" ? seeded.Attempt + 1 : seeded.Attempt, "V-4");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId)).ShouldBe(0, "V-4");
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == seeded.SessionId))
            .ShouldBe(shape == "working-transcript" ? 1 : 0, "V-4");
        Quiet(runner, stopper, "V-4");
    }

    [Test]
    [Arguments("other-reason")]
    [Arguments("delivery-evidence")]
    [Arguments("attempted-turn")]
    public async Task C1149_Different_reason_or_attempted_brief_still_uses_failure_policy(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var reason = shape == "other-reason"
            ? "the pty-host exited (code 1)"
            : SessionReconciliationService.RunnerUnknownSessionReason;
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Parent = false,
            SessionReason = reason,
            DeliveryAttempts = shape == "delivery-evidence" ? 1 : 0,
            Prompt = shape == "attempted-turn",
            TurnEnd = shape == "attempted-turn",
        });
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(
            schema.ConnectionString, runner, stopper, new FakeTimeProvider(DateTimeOffset.UtcNow),
            new DeadSessionFirstSeenState()))
            await host.DueAsync();

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed, "V-5");
        task.CompletedAt.ShouldNotBeNull("V-5");
        task.FailureReason.ShouldNotBeNull("V-5");
        task.FailureReason!.Contains(reason, StringComparison.Ordinal).ShouldBeTrue("V-5");
        task.FailureReason.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse("V-5");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0, "V-5");
        Quiet(runner, stopper, "V-5");
    }

    private static void Quiet(CountingRunner runner, RecordingSessionStopper stopper, string label)
    {
        runner.Starts.ShouldBe(0, label);
        runner.Kills.ShouldBe(0, label);
        runner.Releases.ShouldBe(0, label);
        runner.Inputs.ShouldBe(0, label);
        stopper.Killed.ShouldBeEmpty(label);
    }

    private static async Task<SeededAbsent> SeedAsync(string connection, AbsentShape shape)
    {
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var parentId = shape.Parent ? Guid.NewGuid() : (Guid?)null;
        var briefId = Guid.NewGuid();
        var dispatched = Pg(DateTime.UtcNow.AddMinutes(-1));
        var cwd = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "antiphon-c1149-s1", taskId.ToString("N"))).FullName;
        var name = $"c1149-{agentId:N}"[..16];
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        if (parentId is Guid parent)
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = parent,
                DefinitionName = "parent",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Stopped,
                Cwd = cwd,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = dispatched,
                EndedAt = dispatched,
            });
        }

        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "absent-launch",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Failed,
            Cwd = cwd,
            RunnerId = shape.RunnerId,
            RunnerStoreId = shape.RunnerId is null ? null : Guid.NewGuid(),
            RunnerCwd = shape.RunnerId is null ? null : cwd,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
            EndedAt = dispatched,
            FailureReason = shape.SessionReason,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "CARD-1149 absent-launch hold.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = false,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = dispatched,
            UpdatedAt = dispatched,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Absent launch " + shape.Status,
            Goal = shape.Goal,
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = shape.Status,
            Attempt = 1,
            ReplyTo = shape.Parent ? AgentTaskReplyTo.Session : AgentTaskReplyTo.None,
            ParentSessionId = parentId,
            Ephemeral = false,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
            AgentId = agentId,
            AgentSessionId = sessionId,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = briefId,
            AgentSessionId = sessionId,
            Body = shape.Body,
            RemoteSpillBody = shape.Spill,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            Origin = QueuedMessageOrigin.Delegation,
            ExecutionTaskId = taskId,
            DeliveryAttempts = shape.DeliveryAttempts,
            CreatedAt = dispatched,
        });
        if (shape.Prompt)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Text = "real work",
                Timestamp = dispatched,
            });
        }

        if (shape.TurnEnd)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 2,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "end_turn",
                Timestamp = dispatched.AddSeconds(1),
            });
        }

        await db.SaveChangesAsync();
        return new SeededAbsent(
            taskId, sessionId, parentId, briefId,
            Encoding.UTF8.GetBytes(shape.Body),
            shape.Spill is null ? null : Encoding.UTF8.GetBytes(shape.Spill),
            dispatched, dispatched, 1, shape.Goal);
    }

    private static SweepHost OpenSweep(
        string connection,
        CountingRunner runner,
        RecordingSessionStopper stopper,
        FakeTimeProvider clock,
        DeadSessionFirstSeenState firstSeen,
        IInterceptor? interceptor = null,
        bool unavailable = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connection);
            if (interceptor is not null)
                o.AddInterceptors(interceptor);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { DeadSessionFailGraceMinutes = 3 }));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c1149-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton(firstSeen);
        services.AddSingleton(new BootWedgeRelaunchState());
        if (unavailable)
            services.AddSingleton<ISessionRunnerDirectory>(new UnavailableDirectory());
        services.AddScoped<AgentTaskDispatcher>();
        return new SweepHost(services.BuildServiceProvider(), runner, stopper, clock);
    }

    private static SessionRunnerSessionDto Listed(
        Guid sessionId, string status, DateTime startedAt, string? pending, DateTime? accepted) =>
        new(sessionId, Pid: 7, StartedAt: startedAt, Status: status, ExitCode: null,
            ExitReason: AgentExitReason.Unknown, LastSequence: 0, Pending: pending, AcceptedStartedAt: accepted);

    private sealed class SweepHost(
        ServiceProvider provider, CountingRunner runner, RecordingSessionStopper stopper, FakeTimeProvider clock) : IAsyncDisposable
    {
        public CountingRunner Runner { get; } = runner;
        public RecordingSessionStopper Stopper { get; } = stopper;
        public FakeTimeProvider Clock { get; } = clock;

        public async Task SweepAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .FailDeadSessionTasksAsync(CancellationToken.None);
        }

        public async Task DueAsync()
        {
            await SweepAsync();
            Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
            await SweepAsync();
        }

        public async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }

    private sealed class AbsentShape
    {
        public AgentTaskStatus Status { get; init; } = AgentTaskStatus.Dispatched;
        public string? SessionReason { get; init; } = SessionReconciliationService.RunnerUnknownSessionReason;
        public string? RunnerId { get; init; }
        public int DeliveryAttempts { get; init; }
        public bool Prompt { get; init; }
        public bool TurnEnd { get; init; }
        public bool Parent { get; init; }
        public string Goal { get; init; } = "Keep this goal exact.\nLine two café \u2603.";
        public string Body { get; init; } = "brief café \u2603\n";
        public string? Spill { get; init; } = "spill café \u2603\n";
    }

    private sealed record SeededAbsent(
        Guid TaskId, Guid SessionId, Guid? ParentId, Guid BriefId,
        byte[] Body, byte[]? Spill, DateTime DispatchedAt, DateTime StartedAt, int Attempt, string Goal);

    private sealed class CountingRunner : ISessionRunnerClient
    {
        public List<SessionRunnerSessionDto> Sessions { get; } = [];
        public int Lists { get; private set; }
        public int Starts { get; private set; }
        public int Kills { get; private set; }
        public int Releases { get; private set; }
        public int Inputs { get; private set; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            Lists++;
            return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(Sessions.ToList());
        }

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            var match = Sessions.FirstOrDefault(s => s.SessionId == sessionId)
                ?? throw new KeyNotFoundException($"session {sessionId} is not listed");
            return Task.FromResult(match);
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Starts++;
            return Task.FromResult(Listed(sessionId, "Running", DateTime.UtcNow, null, null));
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs++;
            return Task.CompletedTask;
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Kills++;
            return Task.FromResult(Listed(sessionId, "Exited", DateTime.UtcNow, null, null));
        }

        public Task<SessionRunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct)
        {
            Releases++;
            return Task.FromResult(Listed(sessionId, "Exited", DateTime.UtcNow, null, null));
        }

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class UnavailableDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => throw new NotSupportedException();
        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException();
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("owning runner inventory unavailable"));
        public IReadOnlyList<string> KnownRunnerIds => [];
        public Guid? GetLiveStoreId(string? runnerId) => null;
    }

    private sealed class BlockedSaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTask>().Any(e =>
                    e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Blocked))
            {
                Armed = false;
                throw new IOException("injected absent-launch save fault");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class AttemptBump : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public string Connection { get; init; } = "";
        public Guid TaskId { get; init; }
        private int _fired;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && _fired == 0
                && command.CommandText.Contains("SessionQueuedMessages", StringComparison.Ordinal))
            {
                _fired++;
                var id = TaskId;
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(Connection));
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "AgentTasks" SET "Attempt" = "Attempt" + 1 WHERE "Id" = {id}""", cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class LaunchAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RefusingLaunchSink : IAgentTaskLaunchSink
    {
        public int Calls { get; private set; }

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            Calls++;
            throw new IOException("Injected launch enqueue refusal after the committed claim");
        }
    }
}
