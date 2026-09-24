using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using LeaseBusySignal = Antiphon.Tests.Application.RunnerSettlementSyncTests.LeaseBusySignal;
using SyncWorld = Antiphon.Tests.Application.RunnerSettlementSyncTests.SyncWorld;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0657 S2. A runner-bound Worktree task settled through the REAL reply service. The Git
/// topology is <see cref="SyncWorld"/>'s (bare origin, desktop worktree at B, a separate runner
/// clone that alone commits and pushes); the task row, the delegate session and the caller live
/// in an isolated schema, and the turn is the owning prompt, the assistant report and a TurnEnd.
/// </summary>
internal sealed class RunnerSettlementWorld : IAsyncDisposable
{
    public SyncWorld Git { get; }
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid CallerSessionId { get; } = Guid.NewGuid();
    public AgentTask Task { get; private set; } = null!;
    public Guid TaskId => Git.TaskId;

    /// <summary>The settlement sync's budget clock when created with a controlled clock; else null.</summary>
    public FakeTimeProvider? SyncClock { get; }

    /// <summary>Every time the settlement sync finds the repository lease busy and waits for it.</summary>
    public LeaseBusySignal LeaseBusy { get; } = new();

    private readonly bool _fenced;

    private RunnerSettlementWorld(SyncWorld git, bool fenced, bool controlledSyncClock)
    {
        Git = git;
        _fenced = fenced;
        SyncClock = controlledSyncClock ? new FakeTimeProvider() : null;
    }

    /// <param name="fenced">Give settlement sync the real workspace reservation journal (the
    /// production fence), so a competing retirement claim can be raced against it.</param>
    /// <param name="profiled">Commission the task under verification profile v1 (Final), so its
    /// settlement owes the caller the CARD-0544 D-9 durable completion obligation.</param>
    /// <param name="controlledSyncClock">Measure the settlement sync's budget on <see cref="SyncClock"/>,
    /// so a lease-busy wait ends only when the test advances it.</param>
    public static async Task<RunnerSettlementWorld> CreateAsync(
        AgentTaskRole role = AgentTaskRole.Code, bool pushBranch = true, bool fenced = false, bool profiled = false,
        bool controlledSyncClock = false)
    {
        var world = new RunnerSettlementWorld(await SyncWorld.CreateAsync(pushBranch), fenced, controlledSyncClock);
        world.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        world.BuildServices();

        await using var db = world.CreateContext();
        var now = DateTime.UtcNow;
        foreach (var (id, name) in new[] { (world.CallerSessionId, "caller"), (world.SessionId, "delegate") })
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = id,
                DefinitionName = name,
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = world.Git.Desktop,
                Cols = 120, Rows = 30,
                CreatedAt = now.AddHours(-1),
                StartedAt = now.AddHours(-1),
                LastSeenAt = now,
            });
        }

        var shape = world.Git.Task;
        db.AgentTasks.Add(new AgentTask
        {
            Id = shape.Id,
            RootTaskId = shape.Id,
            Title = "runner " + role,
            Goal = "runner work",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = shape.WorkingDirectory,
            RepoPath = shape.RepoPath,
            WorktreePath = shape.WorktreePath,
            WorktreeBranch = shape.WorktreeBranch,
            WorktreeBaseSha = shape.WorktreeBaseSha,
            RemoteWorktreePath = shape.RemoteWorktreePath,
            RunnerId = shape.RunnerId,
            ProgressBaselineJson = shape.ProgressBaselineJson,
            VerificationProfileVersion = profiled ? 1 : null,
            VerificationRound = profiled ? VerificationRound.Final : null,
            ParentSessionId = world.CallerSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Working,
            AgentSessionId = world.SessionId,
            CreatedAt = now.AddMinutes(-30),
            DispatchedAt = now.AddMinutes(-20),
        });
        await db.SaveChangesAsync();
        await world.ReloadAsync();
        return world;
    }

    private void BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            AllowedRoots = [Git.Desktop],
            OutputDistillerEnabled = false,
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<ApiErrorRecoveryService>();
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<RecordingSessionStopper>();
        services.AddSingleton<IDelegateSessionStopper>(sp => sp.GetRequiredService<RecordingSessionStopper>());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<IRepositoryMutationLease>(Git.Leases);
        services.AddSingleton<ILandingGit>(Git.Git);
        services.AddSingleton<ITaskProgressGit>(Git.Git);
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Git.Root,
            WorktreeAddTimeoutSeconds = 180,
        });
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(Schema.ConnectionString));
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<AgentFilesService>();
        services.AddScoped<IWorkspaceProgressProbe>(sp => sp.GetRequiredService<AgentFilesService>());
        if (_fenced)
        {
            services.AddScoped<IWorkspaceReservationJournal>(sp => new WorkspaceReservationJournal(
                sp.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System));
            services.AddScoped(sp => Git.Service(
                SyncClock, sp.GetRequiredService<IWorkspaceReservationJournal>(), LeaseBusy.Observe));
        }
        else
            services.AddScoped(_ => Git.Service(SyncClock, leaseBusy: LeaseBusy.Observe));
        services.AddScoped<IRemoteSettlementSync>(sp => sp.GetRequiredService<RemoteWorkspaceService>());
        services.AddScoped(sp => new TaskCompletionProgressService(
            sp.GetRequiredService<ITaskProgressGit>(),
            sp.GetRequiredService<IWorkspaceProgressProbe>(),
            TimeProvider.System,
            sp.GetRequiredService<IRemoteSettlementSync>()));
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<ModelAvailability>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddSingleton<AgentTaskLandQueue>();
        services.AddScoped<AgentTaskLandService>();
        // CARD-0085 bind-refusal recovery as the dispatcher's watchdog runs it. The desktop's Claude
        // projects root is an empty directory: a runner session's transcript is never on this box.
        services.AddSingleton<GitWorkspaceService>();
        services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
        {
            ClaudeProjectsRoot = Path.Combine(Git.Root, "desktop-claude-projects"),
        }));
        services.AddSingleton<DelegateBindRefusalRecovery>();
        Services = services.BuildServiceProvider();
    }

    public AppDbContext CreateContext() =>
        new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public string Claim(string sha) => $"[antiphon-progress:{TaskId:D} commit={sha}]";

    public static string Report(string body, string next = "review", string? artifact = null) =>
        body + "\n--- next stage ---\nnext: " + next + "\nhandoff: continue\n"
        + (artifact is null ? "" : "artifact: " + artifact + "\n");

    /// <summary>
    /// The owning prompt, the report and a TurnEnd, then the real reply service. With
    /// <paramref name="closingVerdict"/> false the report carries no verdict token (unmarked).
    /// </summary>
    public async Task SettleAsync(string report, string? prompt = null, bool closingVerdict = true)
    {
        await TurnSeeding.SeedTurnAsync(
            CreateContext, SessionId, prompt ?? DelegationReportFormatter.TaskMarker(TaskId), report, closingVerdict);
        await Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(SessionId, CancellationToken.None);
        await ReloadAsync();
    }

    /// <summary>The owning prompt, the marked report and a TurnEnd, left for a sweep to hand off.</summary>
    public Task SeedReportAsync(string report) =>
        TurnSeeding.SeedTurnAsync(CreateContext, SessionId, DelegationReportFormatter.TaskMarker(TaskId), report);

    /// <summary>
    /// A second, local ReadOnly task on its own delegate session with its marked report already in
    /// the transcript: ordinary work the same deferred-report sweep settles. Returns its id.
    /// </summary>
    public async Task<Guid> AddLocalReportedTaskAsync(string report)
    {
        var id = Guid.NewGuid();
        var session = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = CreateContext())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = session,
                DefinitionName = "local",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Git.Desktop,
                Cols = 120, Rows = 30,
                CreatedAt = now.AddHours(-1),
                StartedAt = now.AddHours(-1),
                LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "local investigate",
                Goal = "local work",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Investigate,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.ReadOnly,
                WorkingDirectory = Git.Desktop,
                ParentSessionId = CallerSessionId,
                ReplyTo = AgentTaskReplyTo.Session,
                Status = AgentTaskStatus.Working,
                AgentSessionId = session,
                CreatedAt = now.AddMinutes(-30),
                DispatchedAt = now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }
        await TurnSeeding.SeedTurnAsync(CreateContext, session, DelegationReportFormatter.TaskMarker(id), report);
        return id;
    }

    /// <summary>One run of the dispatcher's real deferred-report sweep on its own scope.</summary>
    public async Task<int> SweepDeferredReportsAsync()
    {
        int swept;
        await using (var scope = Services.CreateAsyncScope())
        {
            swept = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .SettleDeferredReportsAsync(CancellationToken.None);
        }
        await ReloadAsync();
        return swept;
    }

    public async Task<AgentTaskStatus> StatusOfAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => t.Status).SingleAsync();
    }

    /// <summary>The delivery watchdog's bind-refusal recovery, through the real reply service.</summary>
    public async Task RecoverAsync(DelegateBindRefusalEvidence evidence)
    {
        await Services.GetRequiredService<AgentTaskReplyService>()
            .RecoverFromBindRefusalAsync(TaskId, evidence, CancellationToken.None);
        await ReloadAsync();
    }

    /// <summary>
    /// The delivery watchdog's real bind-refusal path, from the dispatcher down. The task is still
    /// Dispatched past the delivery timeout and its session ingested no prompt. The watchdog's pull of
    /// the runner's own transcript view lands the delegate's closing <c>done</c> report and a TurnEnd,
    /// with no prompt row, so the ordinary completion path cannot correlate it. With
    /// <paramref name="reportBody"/> null the pull lands nothing. The real
    /// <see cref="AgentTaskDispatcher.FailNeverStartedAsync"/> then asks
    /// <see cref="DelegateBindRefusalRecovery"/> for evidence before it would fail the task.
    /// </summary>
    public async Task RecoverThroughWatchdogAsync(string? reportBody)
    {
        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
            row.Status = AgentTaskStatus.Dispatched;
            await db.SaveChangesAsync();
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            dispatcher.CatchUpOverride = async (session, _) =>
            {
                if (session == SessionId && reportBody is not null)
                    await SeedRunnerReportAsync(reportBody);
            };
            await dispatcher.FailNeverStartedAsync(CancellationToken.None);
        }
        await ReloadAsync();
    }

    /// <summary>The runner's transcript rows for a done report whose prompt row never arrived.</summary>
    private async Task SeedRunnerReportAsync(string body)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        var now = DateTime.UtcNow;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = ++seq,
            Kind = TranscriptKinds.AssistantText, Role = "assistant",
            Text = Report(body) + DelegationReportFormatter.ReportToken(TaskId, "done"),
            Timestamp = now, CreatedAt = now,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = ++seq,
            Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
            Timestamp = now, CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The CARD-0544 D-9 completion obligation(s) committed for this task.</summary>
    public async Task<List<AgentTaskLandNotification>> ObligationsAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == TaskId && n.Kind == LandNotificationKind.TaskCompletion)
            .ToListAsync();
    }

    public async Task<SessionQueuedMessage?> QueuedForAsync(Guid landNotificationId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.SourceLandNotificationId == landNotificationId);
    }

    /// <summary>The delegate session has ended, so an unmarked turn settles without a nudge.</summary>
    public async Task EndDelegateSessionAsync()
    {
        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        session.Status = SessionStatus.Stopped;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ReloadAsync()
    {
        await using var db = CreateContext();
        Task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
    }

    public CompletionProgressEvidence? Evidence() => TaskProgressJson.TryReadEvidence(Task.CompletionProgressEvidenceJson);

    public async Task<List<AgentTaskEvent>> EventsAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == TaskId).ToListAsync();
    }

    public async Task<int> NoProgressIncidentsAsync()
    {
        await using var db = CreateContext();
        return await db.AgentIncidents.AsNoTracking()
            .CountAsync(i => i.Kind == AgentIncidentKind.DelegateCompletedWithoutProgress);
    }

    public async Task<SessionQueuedMessage?> NoteAsync()
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == CallerSessionId && m.SourceTaskId == TaskId)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        await Git.DisposeAsync();
    }
}
