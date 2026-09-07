using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryPersistenceTests
{
    [Test]
    public async Task Card0412_V05_unique_wait_and_hold_link_survive_concurrent_ensure()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var holdId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = holdId,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "opus",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = now.AddHours(1),
                HitAt = now,
                Reason = "test",
                Revision = 1,
            });
            await db.SaveChangesAsync();
        }

        var registration = new CapacityWaitRegistration
        {
            ConsumerKey = $"session:{Guid.NewGuid():N}",
            ConsumerKind = CapacityWaitConsumerKind.LiveSession,
            ExecutionKind = AgentKind.ClaudeCode,
            RequestedKind = AgentKind.ClaudeCode,
            RequestedAlias = "opus",
            BlockedAt = now,
            HoldId = holdId,
            HoldRevision = 1,
        };

        var first = CreateService(schema).Service;
        var second = CreateService(schema).Service;
        await Task.WhenAll(
            first.EnsureWaitAsync(registration, CancellationToken.None),
            second.EnsureWaitAsync(registration, CancellationToken.None));

        await using var verify = CreateContext(schema);
        (await verify.CapacityRecoveryWaits.CountAsync(w => w.ConsumerKey == registration.ConsumerKey))
            .ShouldBe(1);
        (await verify.CapacityRecoveryWaitHolds.CountAsync(l => l.HoldId == holdId)).ShouldBe(1);
    }

    [Test]
    public async Task Card0412_V07_late_register_observes_already_cleared_hold()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var holdId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = holdId,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "opus",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = now.AddMinutes(-1),
                HitAt = now.AddHours(-1),
                ClearedAt = now,
                ClearCause = ModelAvailabilityClearCause.Expired,
                ReleasePendingAt = now,
                Reason = "expired",
                Revision = 1,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(schema).Service;
        var wait = await service.EnsureWaitAsync(new CapacityWaitRegistration
        {
            ConsumerKey = $"session:{Guid.NewGuid():N}",
            ConsumerKind = CapacityWaitConsumerKind.LiveSession,
            ExecutionKind = AgentKind.ClaudeCode,
            RequestedKind = AgentKind.ClaudeCode,
            HoldId = holdId,
            HoldRevision = 1,
            HoldAlreadyCleared = true,
            ClearCause = ModelAvailabilityClearCause.Expired,
            BlockedAt = now.AddHours(-1),
        }, CancellationToken.None);

        wait.State.ShouldBe(CapacityRecoveryWaitState.Ready);
        wait.ObservedClearCauses.ShouldContain("Expired");
    }

    [Test]
    public async Task Card0412_V11_fourth_admission_is_exhausted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time) = CreateService(schema, attempts: 3);
        var wait = await service.EnsureWaitAsync(new CapacityWaitRegistration
        {
            ConsumerKey = $"task:{Guid.NewGuid():N}",
            ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
            ExecutionKind = AgentKind.ClaudeCode,
            RequestedKind = AgentKind.ClaudeCode,
            HoldAlreadyCleared = true,
            BlockedAt = DateTime.UtcNow.AddMinutes(-5),
        }, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await service.GrantReadyAsync(CancellationToken.None);
            var grant = await ReloadGrant(schema, AgentKind.ClaudeCode);
            grant.ShouldNotBeNull();
            var result = await service.RedeemAsync(
                wait.Id, grant!.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Dispatch,
                CancellationToken.None);
            result.Ok.ShouldBeTrue();
            wait = await ReloadWait(schema, wait.Id);
            wait.AdmissionCount.ShouldBe(i + 1);
            time.Advance(TimeSpan.FromSeconds(2));
            if (i < 2)
                await service.PrepareNextAttemptAsync(wait.Id, CancellationToken.None);
        }

        await service.PrepareNextAttemptAsync(wait.Id, CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        wait = await ReloadWait(schema, wait.Id);
        wait.State.ShouldBe(CapacityRecoveryWaitState.Exhausted);
        wait.AdmissionCount.ShouldBe(3);
    }

    private static (CapacityRecoveryService Service, Microsoft.Extensions.Time.Testing.FakeTimeProvider Time) CreateService(
        IsolatedTestSchema schema, int attempts = 3)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = true,
                AdmissionIntervalSeconds = 1,
                JitterSeconds = 0,
                MaxEpisodeAttempts = attempts,
                ReconciliationBatchSize = 100,
            },
        }));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<CapacityRecoveryService>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<CapacityRecoveryService>(), time);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<CapacityRecoveryWait> ReloadWait(IsolatedTestSchema schema, Guid id)
    {
        await using var db = CreateContext(schema);
        return await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == id);
    }

    private static async Task<CapacityRecoveryProviderState?> ReloadGrant(IsolatedTestSchema schema, AgentKind kind)
    {
        await using var db = CreateContext(schema);
        return await db.CapacityRecoveryProviderStates.FirstOrDefaultAsync(s => s.Kind == kind);
    }
}
