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

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryTaskTests
{
    [Test]
    public async Task Card0412_V19_retained_wait_excluded_from_global_active_count()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"claude-wait-{i}",
                Goal = "wait",
                Status = AgentTaskStatus.Working,
                AgentKind = AgentKind.ClaudeCode,
                CapacityWaitRetained = true,
                CapacityWaitId = Guid.NewGuid(),
                CreatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        var grokId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = grokId,
            RootTaskId = grokId,
            Title = "grok-ready",
            Goal = "go",
            Status = AgentTaskStatus.Queued,
            AgentKind = AgentKind.Grok,
            CreatedAt = now.AddMinutes(-1),
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var active = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        active.ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V19_retained_return_beats_older_queued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        Guid? firstCounted = null;
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            firstCounted ??= id;
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"counted-{i}",
                Goal = "busy",
                Status = AgentTaskStatus.Working,
                AgentKind = AgentKind.ClaudeCode,
                CreatedAt = now.AddMinutes(-10),
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        var retainedId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = retainedId,
            RootTaskId = retainedId,
            Title = "retained-ready",
            Goal = "return",
            Status = AgentTaskStatus.Working,
            AgentKind = AgentKind.ClaudeCode,
            CapacityWaitRetained = true,
            CapacityWaitId = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-1),
            ConcurrencyToken = Guid.NewGuid(),
        });

        var queuedId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = queuedId,
            RootTaskId = queuedId,
            Title = "older-queued",
            Goal = "queue",
            Status = AgentTaskStatus.Queued,
            AgentKind = AgentKind.ClaudeCode,
            CreatedAt = now.AddMinutes(-20),
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var counted = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        counted.ShouldBe(6);

        var first = await db.AgentTasks.SingleAsync(t => t.Id == firstCounted);
        first.Status = AgentTaskStatus.Succeeded;
        await db.SaveChangesAsync();

        var remainingCounted = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        remainingCounted.ShouldBe(5);

        var retained = await db.AgentTasks.SingleAsync(t => t.Id == retainedId);
        retained.CapacityWaitRetained.ShouldBeTrue();
        var queued = await db.AgentTasks.SingleAsync(t => t.Id == queuedId);
        queued.CreatedAt.ShouldBeLessThan(retained.CreatedAt);
    }

    [Test]
    public async Task Card0412_V19_historical_capacity_wait_id_still_counts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"admitted-{i}",
                Goal = "busy",
                Status = AgentTaskStatus.Working,
                AgentKind = AgentKind.ClaudeCode,
                CapacityWaitId = Guid.NewGuid(),
                CapacityWaitRetained = false,
                CreatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        await db.SaveChangesAsync();
        var counted = await db.AgentTasks
            .Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained);
        counted.ShouldBe(6);
    }

    [Test]
    public async Task Card0412_V20_missing_wait_is_registered_on_hold_skip()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        var dispatcher = CreateDispatcher(schema.ConnectionString);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace.Path, agentId, AgentModelLevel.High, "opus wait");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = holdId,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "opus",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = DateTime.UtcNow.AddHours(1),
                HitAt = DateTime.UtcNow,
                Reason = "held",
                Revision = 1,
            });
            await db.SaveChangesAsync();
        }

        await dispatcher.TickAsync(CancellationToken.None);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        (await verify.CapacityRecoveryWaits.CountAsync(w => w.TaskId == task.Id)).ShouldBe(1);
    }

    private static AgentTaskDispatcher CreateDispatcher(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = true,
                AdmissionIntervalSeconds = 1,
                JitterSeconds = 0,
                MaxEpisodeAttempts = 3,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 6,
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
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c412-task-wt"),
        });
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c412-task").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
