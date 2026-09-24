using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    private readonly bool _fenced;

    private RunnerSettlementWorld(SyncWorld git, bool fenced)
    {
        Git = git;
        _fenced = fenced;
    }

    /// <param name="fenced">Give settlement sync the real workspace reservation journal (the
    /// production fence), so a competing retirement claim can be raced against it.</param>
    public static async Task<RunnerSettlementWorld> CreateAsync(
        AgentTaskRole role = AgentTaskRole.Code, bool pushBranch = true, bool fenced = false)
    {
        var world = new RunnerSettlementWorld(await SyncWorld.CreateAsync(pushBranch), fenced);
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
            services.AddScoped(sp => Git.Service(reservations: sp.GetRequiredService<IWorkspaceReservationJournal>()));
        }
        else
            services.AddScoped(_ => Git.Service());
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
