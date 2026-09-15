using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.TestHelpers;

internal static class CapacityRecoveryTestSupport
{
    public static (CapacityRecoveryService Service, FakeTimeProvider Time, ServiceProvider Provider) CreateService(
        IsolatedTestSchema schema,
        int attempts = 3,
        int intervalSeconds = 60,
        int jitterSeconds = 0,
        int batch = 100,
        bool enabled = true,
        IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // PostgreSQL timestamps preserve microseconds; start on that precision so exact
        // persisted-clock assertions do not compare an unrepresentable 100 ns remainder.
        var now = DateTimeOffset.UtcNow;
        var time = new FakeTimeProvider(new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = enabled,
                AdmissionIntervalSeconds = intervalSeconds,
                JitterSeconds = jitterSeconds,
                MaxEpisodeAttempts = attempts,
                ReconciliationBatchSize = batch,
            },
        }));
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(schema.ConnectionString);
            if (interceptor is not null)
                o.AddInterceptors(interceptor);
        });
        services.AddSingleton<CapacityRecoveryService>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<CapacityRecoveryService>(), time, provider);
    }

    public static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    /// <summary>
    /// Grant-expiry tests model a live owner that is not redeeming, rather than an orphan.
    /// A dispatched task also stays outside compatibility's legacy queued-task registration.
    /// </summary>
    public static async Task<CapacityRecoveryWait> EnsureWaitWithLiveOwnerAsync(
        CapacityRecoveryService service, IsolatedTestSchema schema, CapacityWaitRegistration registration,
        CancellationToken ct)
    {
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        if (registration.ConsumerKind == CapacityWaitConsumerKind.LiveSession)
        {
            var id = registration.SessionId ?? Guid.Parse(registration.ConsumerKey["session:".Length..]);
            if (!await db.AgentSessions.AnyAsync(s => s.Id == id, ct))
                db.AgentSessions.Add(new AgentSession
                {
                    Id = id, DefinitionName = "fake", AgentKind = registration.ExecutionKind,
                    Status = SessionStatus.Running, CreatedAt = now, StartedAt = now,
                });
        }
        else if (registration.ConsumerKind is CapacityWaitConsumerKind.QueuedTask or CapacityWaitConsumerKind.RoutingBlockedTask)
        {
            var id = registration.TaskId ?? Guid.Parse(registration.ConsumerKey["task:".Length..]);
            if (!await db.AgentTasks.AnyAsync(t => t.Id == id, ct))
                db.AgentTasks.Add(new AgentTask
                {
                    Id = id, RootTaskId = id, Title = "live capacity consumer", Goal = "test",
                    AgentKind = registration.ExecutionKind, Status = AgentTaskStatus.Dispatched,
                    CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
                });
        }
        await db.SaveChangesAsync(ct);
        return await service.EnsureWaitAsync(registration, ct);
    }

    public static ModelAvailabilityHold Hold(
        Guid id,
        string alias = "opus",
        DateTime? until = null,
        AgentKind kind = AgentKind.ClaudeCode,
        bool cleared = false,
        ModelAvailabilityClearCause? cause = null)
    {
        var now = DateTime.UtcNow;
        return new ModelAvailabilityHold
        {
            Id = id,
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.AutoDetected,
            DisabledUntil = until ?? now.AddHours(1),
            HitAt = now,
            Reason = "test",
            Revision = 1,
            ClearedAt = cleared ? now : null,
            ClearCause = cause,
            ReleasePendingAt = cleared ? now : null,
        };
    }

    public static CapacityWaitRegistration Registration(
        string consumerKey,
        CapacityWaitConsumerKind consumerKind,
        AgentKind executionKind = AgentKind.ClaudeCode,
        Guid? holdId = null,
        bool holdAlreadyCleared = false,
        DateTime? blockedAt = null,
        Guid? taskId = null,
        Guid? sessionId = null,
        Guid? agentId = null,
        string? alias = "opus") =>
        new()
        {
            ConsumerKey = consumerKey,
            ConsumerKind = consumerKind,
            ExecutionKind = executionKind,
            RequestedKind = executionKind,
            RequestedAlias = alias,
            HoldId = holdId,
            HoldRevision = 1,
            HoldAlreadyCleared = holdAlreadyCleared,
            ClearCause = holdAlreadyCleared ? ModelAvailabilityClearCause.Expired : null,
            BlockedAt = blockedAt ?? DateTime.UtcNow.AddMinutes(-5),
            TaskId = taskId,
            SessionId = sessionId,
            AgentId = agentId,
        };
}
