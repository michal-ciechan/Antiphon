using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Application;

public sealed partial class TaskLaunchTokenBoundaryTests
{
    private static readonly DateTimeOffset FixtureTime = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static AppDbContext Open(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static ServiceProvider CreateServices(
        IsolatedTestSchema schema, string directory, RecordingLaunchSink sink,
        TokenHashSaveFault? fault = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(schema.ConnectionString);
            if (fault is not null) options.AddInterceptors(fault);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        // Keep the launch generation exactly representable by PostgreSQL's microsecond
        // timestamp storage, without weakening the equality asserted at enqueue.
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixtureTime));
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 8,
            AllowedRoots = [directory],
            RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(settings =>
        {
            settings.DefaultDefinition = "codex";
            settings.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<ISessionRunnerClient, BridgeQueueHarness.EmptyRunnerClient>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = directory });
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed record Seeded(Guid TaskId, Guid AgentId, Guid PreviousSessionId);

    private static async Task<Seeded> SeedAsync(IsolatedTestSchema schema, string directory, AgentTaskRole role)
    {
        var seeded = new Seeded(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = FixtureTime.UtcDateTime;
        using var db = Open(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = seeded.PreviousSessionId, DefinitionName = "codex", AgentKind = AgentKind.Codex,
            Status = SessionStatus.Stopped, Cwd = directory, Cols = 120, Rows = 30,
            CreatedAt = now.AddDays(-1), StartedAt = now.AddDays(-1),
            LastSeenAt = now.AddDays(-1), EndedAt = now.AddDays(-1),
        });
        db.Agents.Add(new Agent
        {
            Id = seeded.AgentId, Name = "specialist", Slug = $"c1301-{seeded.AgentId:N}",
            WorkingDirectory = directory, Status = AgentStatus.Running, Kind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Low, AlwaysOn = false, IsPoolDelegate = false,
            RemoteControlEnabled = false, PersistentSessionId = seeded.PreviousSessionId.ToString("D"),
            CreatedAt = now, UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = seeded.TaskId, RootTaskId = seeded.TaskId,
            Title = "specialist launch token", Goal = "inspect this task",
            Kind = AgentTaskKind.Worker, Role = role, ReplyTo = AgentTaskReplyTo.None,
            AgentKind = AgentKind.Codex, ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = directory,
            AgentId = seeded.AgentId, Ephemeral = false, Status = AgentTaskStatus.Queued,
            TokenHash = null, CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return seeded;
    }

    private sealed record BoundaryObservation(bool TokenCommitted, bool GenerationCommitted);
    private sealed record Launch(Guid SessionId, Guid AgentId, AgentLaunchSpec Spec, BoundaryObservation? Boundary);

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<Launch> Launches { get; } = [];
        public Func<Guid, DateTime, AgentLaunchSpec, BoundaryObservation>? Observe { get; init; }
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Launches.Add(new Launch(sessionId, agentId, spec, Observe?.Invoke(sessionId, acceptedGeneration, spec)));
    }

    private sealed class TokenHashSaveFault(IsolatedTestSchema schema, Guid taskId) : SaveChangesInterceptor
    {
        private string? _priorHash;
        private bool _armed;
        public int Fired { get; private set; }
        public bool PriorHashStillDurable { get; private set; }
        public bool StartingGenerationCommitted { get; private set; }
        public Guid? SessionId { get; private set; }
        public DateTime Generation { get; private set; }

        public void Arm(string priorHash)
        {
            _priorHash = priorHash;
            _armed = true;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_armed || Fired != 0) return result;
            var context = eventData.Context!;
            context.ChangeTracker.DetectChanges();
            var entry = context.ChangeTracker.Entries<AgentTask>()
                .SingleOrDefault(e => e.Entity.Id == taskId);
            if (entry is null || !entry.Property(t => t.TokenHash).IsModified
                || entry.Entity.TokenHash == _priorHash) return result;

            Fired++;
            using var observe = Open(schema);
            var stored = await observe.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, cancellationToken);
            PriorHashStillDurable = stored.TokenHash == _priorHash;
            SessionId = stored.AgentSessionId;
            var session = await observe.AgentSessions.AsNoTracking()
                .SingleAsync(s => s.Id == stored.AgentSessionId, cancellationToken);
            var agent = await observe.Agents.AsNoTracking()
                .SingleAsync(a => a.Id == stored.AgentId, cancellationToken);
            Generation = session.StartedAt;
            StartingGenerationCommitted = stored.AgentSessionId == entry.Entity.AgentSessionId
                && session.Status == SessionStatus.Starting
                && agent.PersistentSessionId == session.Id.ToString("D");
            throw new IOException("C1301 injected replacement-token hash save failure");
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c1301-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
