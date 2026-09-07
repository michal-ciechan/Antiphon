using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
        bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
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
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<CapacityRecoveryService>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<CapacityRecoveryService>(), time, provider);
    }

    public static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

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
