using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0047 slice 1 — declaring an expected duration and arming the first check-in.
///
/// The load-bearing property of the whole card is stated here rather than implied: the declared
/// duration is a HINT, NEVER A DEADLINE. It schedules when someone looks; it must never fail,
/// escalate, cancel or reprioritise anything, which is what
/// <see cref="running_far_past_the_expected_duration_changes_nothing"/> holds down.
///
/// The dispatch tests drive <c>TickAsync</c>, which runs the global sweeps, so this class takes
/// <c>[NotInParallel]</c> with NO group key (CLAUDE.md's shared-Postgres rule) and every assertion
/// is scoped to a row this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskCheckScheduleTests
{
    // ---- declaring it ----------------------------------------------------------------------

    [Test]
    public async Task an_explicit_expected_duration_is_stored_on_the_task()
    {
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        var service = CreateService(db);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "rebuild the client bundle", ExpectedMinutes: 45),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .ExpectedDurationMinutes.ShouldBe(45);
    }

    [Test]
    public async Task an_undeclared_duration_takes_the_configured_default()
    {
        // Always a number on the row, so nothing downstream has to reason about "unset".
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        var service = CreateService(db);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "run the suite"), ManualCaller(workspace.Path), CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .ExpectedDurationMinutes.ShouldBe(
                new DelegationSettings().DefaultExpectedMinutes,
                "the shipped default is 10 minutes");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-5)]
    [Arguments(1441)]
    public async Task an_out_of_range_expected_duration_is_rejected(int minutes)
    {
        // 0 is rejected rather than reinterpreted as "never check": opting a single task out of
        // checking is not offered until someone needs it, and a silent reinterpretation would make
        // "expectedMinutes: 0" mean two different things depending on who read the code.
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "do the thing", ExpectedMinutes: minutes),
            ManualCaller(workspace.Path),
            CancellationToken.None));

        ex.Errors.ShouldContainKey(nameof(CreateAgentTaskRequest.ExpectedMinutes));
        ex.Errors[nameof(CreateAgentTaskRequest.ExpectedMinutes)]
            .ShouldContain(e => e.Contains("1 and 1440"), "the message has to say what the range IS");
    }

    // ---- arming the first check ------------------------------------------------------------

    [Test]
    public async Task dispatch_arms_the_first_check_at_dispatch_plus_the_expected_duration()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var (_, sessionId) = await SeedWarmAgentAsync(workspace.Path);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, expectedMinutes: 25, parentSessionId: Guid.NewGuid());

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.AgentSessionId.ShouldBe(sessionId);
        dispatched.DispatchedAt.ShouldNotBeNull();
        dispatched.NextCheckAt.ShouldNotBeNull();
        dispatched.NextCheckAt!.Value.ShouldBe(
            dispatched.DispatchedAt!.Value.AddMinutes(25), TimeSpan.FromSeconds(1));
        dispatched.CheckCount.ShouldBe(0);
    }

    [Test]
    public async Task a_task_with_nowhere_to_report_is_never_armed()
    {
        // A check with no one to deliver to is dead weight: it would gather facts, render a digest
        // and throw both away. NextCheckAt stays null so the sweep never sees the task at all.
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        await SeedWarmAgentAsync(workspace.Path);
        var task = await SeedQueuedTaskAsync(workspace.Path, expectedMinutes: 25, parentSessionId: null);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, "it still runs — it is just never checked");
        dispatched.NextCheckAt.ShouldBeNull();
    }

    [Test]
    public async Task the_feature_switch_disarms_the_schedule_entirely()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(settings => settings.CheckEnabled = false);
        await SeedWarmAgentAsync(workspace.Path);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, expectedMinutes: 25, parentSessionId: Guid.NewGuid());

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).NextCheckAt.ShouldBeNull();
    }

    // ---- a hint, never a deadline ----------------------------------------------------------

    /// <summary>
    /// THE guarantee of the card (§1.2). A task declared at 5 minutes and still running six hours
    /// later is not late — it has merely passed the point where someone wanted a look. Every clock
    /// that CAN end a task is driven here against a fake clock advanced far past the declaration,
    /// and none of them may notice it: the status, the failure reason and the event timeline all
    /// have to come out untouched.
    /// </summary>
    [Test]
    public async Task running_far_past_the_expected_duration_changes_nothing()
    {
        using var workspace = new TempWorkspace();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var dispatcher = CreateDispatcher(timeProvider: clock);

        var task = await SeedRunningTaskAsync(
            workspace.Path,
            expectedMinutes: 5,
            dispatchedAt: clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
            // Code has no EscalateTo in the role policy, so a stall cannot bump it — this test is
            // about the expected duration alone, not about the (independent, untouched) stall clock.
            role: AgentTaskRole.Code);
        // Proof of delivery: the delivery watchdog only fires on a session with ZERO transcript
        // entries, and a task that never started is a different failure from one running long.
        await SeedTranscriptActivityAsync(task.AgentSessionId!.Value, clock.GetUtcNow().UtcDateTime);

        // 72x its declared duration.
        clock.Advance(TimeSpan.FromMinutes(360));

        await dispatcher.AutoEscalateStalledAsync(CancellationToken.None);
        await dispatcher.FailNeverStartedAsync(CancellationToken.None);
        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        await dispatcher.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var after = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "an expected duration is a hint — nothing may fail, escalate or cancel a task off it");
        after.FailureReason.ShouldBeNull();
        after.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "and nothing may re-tier it either");
        after.EscalatedFrom.ShouldBeNull();
        after.CompletedAt.ShouldBeNull();

        var events = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id).ToListAsync();
        events.ShouldNotContain(
            e => e.Type == AgentTaskEventType.Failed
                || e.Type == AgentTaskEventType.Escalated
                || e.Type == AgentTaskEventType.Canceled
                || e.Type == AgentTaskEventType.Blocked,
            "no timeline entry may blame the task for running long");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static AgentTaskService CreateService(AppDbContext db) =>
        new(db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    private static AgentTaskDispatcher CreateDispatcher(
        Action<DelegationSettings>? configure = null, TimeProvider? timeProvider = null)
    {
        var settings = new DelegationSettings
        {
            PoolReservedForCallerMinutes = 0,
            // The fixture database is shared across suites; leftover Dispatched/Working rows must
            // never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
        };
        configure?.Invoke(settings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(settings));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-check-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    /// <summary>A warm pool delegate so dispatch reuses a live session instead of spawning a process.</summary>
    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(NewSession(sessionId, directory, now));
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"chk-{agentId:N}"[..12],
            Slug = $"chk-{agentId:N}"[..12],
            WorkingDirectory = directory,
            Details = "Warm pool delegate for the check-schedule tests.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-10),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory, int expectedMinutes, Guid? parentSessionId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            Title = "check-schedule test",
            Goal = "do the next piece of work",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            ExpectedDurationMinutes = expectedMinutes,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedRunningTaskAsync(
        string directory, int expectedMinutes, DateTime dispatchedAt, AgentTaskRole role)
    {
        var sessionId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentSessions.Add(NewSession(sessionId, directory, dispatchedAt));
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = Guid.NewGuid(),
            Title = "a task that is simply taking a long time",
            Goal = "do the slow thing",
            Role = role,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            ReplyTo = AgentTaskReplyTo.Session,
            ExpectedDurationMinutes = expectedMinutes,
            CreatedAt = dispatchedAt,
            DispatchedAt = dispatchedAt,
            NextCheckAt = dispatchedAt.AddMinutes(expectedMinutes),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedTranscriptActivityAsync(Guid sessionId, DateTime at)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.UserPrompt,
            Uuid = $"check-{Guid.NewGuid():N}",
            Role = "user",
            Text = "[delegated task] do the slow thing",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static AgentSession NewSession(Guid id, string directory, DateTime at) => new()
    {
        Id = id,
        DefinitionName = "fake",
        AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Running,
        Cwd = directory,
        Cols = 120,
        Rows = 30,
        CreatedAt = at,
        StartedAt = at,
        LastSeenAt = at,
    };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the workspace resolver verifies existence.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-check-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a stray file lock must not fail the test */ }
        }
    }
}
