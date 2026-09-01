using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0022 S3: queued work whose model is held stays Queued; other models on the same tick
/// still dispatch. Shared-Postgres: assertions are scoped to rows this class created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ModelAvailabilityDispatcherTests
{
    [Test]
    public async Task A_fable_hold_skips_fable_and_dispatches_sonnet_on_the_same_tick()
    {
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();
        var (fableAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var (sonnetAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var fable = await SeedQueuedTaskAsync(
            workspace.Path, pinnedAgentId: fableAgent, level: AgentModelLevel.Frontier, title: "fable plan");
        var sonnet = await SeedQueuedTaskAsync(
            workspace.Path, pinnedAgentId: sonnetAgent, level: AgentModelLevel.Medium, title: "sonnet docs");
        await SeedHoldAsync(holdId, "fable", until: DateTime.UtcNow.AddHours(1), manual: false);

        try
        {
            var result = await dispatcher.TickAsync(CancellationToken.None);

            result.SkippedModelAvailability.ShouldBeGreaterThanOrEqualTo(1);
            await using var verify = CreateContext();
            (await verify.AgentTasks.SingleAsync(t => t.Id == fable.Id)).Status
                .ShouldBe(AgentTaskStatus.Queued);
            (await verify.AgentTasks.SingleAsync(t => t.Id == sonnet.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched);
            var held = await verify.AgentTaskEvents
                .Where(e => e.AgentTaskId == fable.Id && e.Type == AgentTaskEventType.Held)
                .ToListAsync();
            held.ShouldContain(e => e.Detail.Contains("fable"));
        }
        finally
        {
            await CleanupAsync(holdId, fable.Id, sonnet.Id, fableAgent, sonnetAgent);
        }
    }

    [Test]
    public async Task A_manual_fable_hold_skips_queued_fable_and_dispatches_sonnet()
    {
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();
        var (fableAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var (sonnetAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var fable = await SeedQueuedTaskAsync(
            workspace.Path, pinnedAgentId: fableAgent, level: AgentModelLevel.Frontier, title: "manual fable");
        var sonnet = await SeedQueuedTaskAsync(
            workspace.Path, pinnedAgentId: sonnetAgent, level: AgentModelLevel.Medium, title: "manual sonnet");
        await SeedHoldAsync(holdId, "fable", until: DateTime.UtcNow.AddHours(1), manual: true);

        try
        {
            var result = await dispatcher.TickAsync(CancellationToken.None);

            result.SkippedModelAvailability.ShouldBeGreaterThanOrEqualTo(1);
            await using var verify = CreateContext();
            (await verify.AgentTasks.SingleAsync(t => t.Id == fable.Id)).Status
                .ShouldBe(AgentTaskStatus.Queued);
            (await verify.AgentTasks.SingleAsync(t => t.Id == sonnet.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched);
        }
        finally
        {
            await CleanupAsync(holdId, fable.Id, sonnet.Id, fableAgent, sonnetAgent);
        }
    }

    [Test]
    public async Task An_expired_hold_clears_and_then_dispatches()
    {
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var task = await SeedQueuedTaskAsync(
            workspace.Path, pinnedAgentId: agentId, level: AgentModelLevel.Frontier, title: "expired hold");
        await SeedHoldAsync(holdId, "fable", until: DateTime.UtcNow.AddSeconds(-2));

        try
        {
            await dispatcher.TickAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched);
            (await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == holdId)).ClearedAt
                .ShouldNotBeNull();
        }
        finally
        {
            await CleanupAsync(holdId, task.Id, agentId);
        }
    }

    private static AgentTaskDispatcher CreateDispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-model-hold-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    private static async Task SeedHoldAsync(Guid id, string alias, DateTime? until, bool manual = false)
    {
        await using var db = CreateContext();
        await db.ModelAvailabilityHolds
            .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == alias && h.ClearedAt == null)
            .ExecuteDeleteAsync();
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = id,
            Kind = AgentKind.ClaudeCode,
            ModelAlias = alias,
            Source = manual ? ModelAvailabilitySource.Manual : ModelAvailabilitySource.AutoDetected,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = manual
                ? "manual hold"
                : until is null ? "Fable 5 per-model cap (no reset stated)" : "session-limit resets 18:10 Europe/London",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory, Guid pinnedAgentId, AgentModelLevel level, string title)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            AgentId = pinnedAgentId,
            Ephemeral = false,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task CleanupAsync(Guid holdId, params Guid[] ids)
    {
        await using var db = CreateContext();
        await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        await db.AgentTaskEvents.Where(e => ids.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
        var sessionIds = await db.Agents
            .Where(a => ids.Contains(a.Id) && a.PersistentSessionId != null)
            .Select(a => a.PersistentSessionId!)
            .ToListAsync();
        foreach (var text in sessionIds)
        {
            if (Guid.TryParse(text, out var sid))
                await db.AgentSessions.Where(s => s.Id == sid).ExecuteDeleteAsync();
        }
        await db.Agents.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-model-hold").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
