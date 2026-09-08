using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryAcceptanceTests
{
    [Test]
    public async Task Card0412_V09_oldest_blocked_gets_the_only_grant()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-10)), CancellationToken.None);
        var younger = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-1)), CancellationToken.None);

        var granted = await service.GrantReadyAsync(CancellationToken.None);
        granted.ShouldBe(1);
        var grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        grant.GrantedWaitId.ShouldBe(older.Id);
        grant.GrantedActionKey.ShouldBe(older.ActionKey);

        var youngerRedeem = await service.RedeemAsync(
            younger.Id, younger.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None);
        youngerRedeem.Ok.ShouldBeFalse();

        var first = await service.RedeemAsync(
            older.Id, older.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Dispatch,
            CancellationToken.None);
        first.Ok.ShouldBeTrue();
        var t0 = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(59.999));
        await service.GrantReadyAsync(CancellationToken.None);
        var mid = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        mid.GrantedWaitId.ShouldBeNull();
        time.SetUtcNow(t0.AddSeconds(60));
        await service.GrantReadyAsync(CancellationToken.None);
        var next = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        next.GrantedWaitId.ShouldBe(younger.Id);
        (await service.RedeemAsync(
            younger.Id, next.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None)).Ok.ShouldBeTrue();
        var t1 = time.GetUtcNow();
        (t1 - t0).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task Card0412_V09_faster_younger_path_cannot_steal_older_grant()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true,
            blockedAt: DateTime.UtcNow.AddMinutes(-30)), CancellationToken.None);
        var younger = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.PendingQueue,
            holdAlreadyCleared: true,
            blockedAt: DateTime.UtcNow.AddMinutes(-1)), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            var stolen = await service.RedeemAsync(
                younger.Id, younger.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
                CancellationToken.None);
            stolen.Ok.ShouldBeFalse();
        }

        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var grant = await verify.Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        grant.GrantedWaitId.ShouldBe(older.Id);
        (await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == younger.Id)).AdmissionCount.ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V09_grok_advances_independently()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            executionKind: AgentKind.ClaudeCode,
            holdAlreadyCleared: true), CancellationToken.None);
        var grok = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            executionKind: AgentKind.Grok,
            alias: "grok-4.6",
            holdAlreadyCleared: true), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        var grokGrant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.Grok);
        grokGrant.GrantedWaitId.ShouldBe(grok.Id);
        (await service.RedeemAsync(
            grok.Id, grok.ActionKey, AgentKind.Grok, CapacityRedemptionPath.Dispatch,
            CancellationToken.None)).Ok.ShouldBeTrue();
    }

    [Test]
    public async Task Card0412_V09_redeem_refuses_before_next_admission_clock()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var state = await db.Set<CapacityRecoveryProviderState>()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            state.GrantedWaitId.ShouldBe(wait.Id);
            state.NextAdmissionAt = time.GetUtcNow().UtcDateTime.AddSeconds(60);
            await db.SaveChangesAsync();
        }

        var premature = await service.RedeemAsync(
            wait.Id, wait.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Dispatch,
            CancellationToken.None);
        premature.Ok.ShouldBeFalse();
        premature.Deferred.ShouldBeTrue();
        premature.Reason.ShouldBe("clock-not-due");
        (await CapacityRecoveryTestSupport.CreateContext(schema).CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id))
            .AdmissionCount.ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V09_outstanding_ready_grant_survives_elapsed_interval()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-30)), CancellationToken.None);
        var younger = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-1)), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(5));
        await service.GrantReadyAsync(CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var grant = await verify.Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        grant.GrantedWaitId.ShouldBe(older.Id);
        (await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == younger.Id)).AdmissionCount.ShouldBe(0);
        (await service.RedeemAsync(
            younger.Id, younger.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None)).Ok.ShouldBeFalse();
    }

    [Test]
    public async Task Card0412_V10_wave_invalidates_unredeemed_grant()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        var wallAt = time.GetUtcNow().UtcDateTime;
        await service.BumpWaveAsync(AgentKind.ClaudeCode, wallAt, CancellationToken.None);
        var grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        grant.GrantedWaitId.ShouldBeNull();
        grant.WaveRevision.ShouldBeGreaterThan(0);
        grant.NextAdmissionAt.ShouldNotBeNull();
        var redeemed = await service.RedeemAsync(
            wait.Id, wait.ActionKey, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None);
        redeemed.Ok.ShouldBeFalse();
    }

    [Test]
    public async Task Card0412_V10_wall_observation_floor_blocks_until_interval()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-10)), CancellationToken.None);
        var younger = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true,
            blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-1)), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        var wallAt = time.GetUtcNow().UtcDateTime;
        await service.BumpWaveAsync(AgentKind.ClaudeCode, wallAt, CancellationToken.None);
        var afterWave = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        afterWave.GrantedWaitId.ShouldBeNull();
        afterWave.NextAdmissionAt.ShouldNotBeNull();
        (afterWave.NextAdmissionAt!.Value - wallAt).TotalSeconds.ShouldBeGreaterThan(59);
        await service.GrantReadyAsync(CancellationToken.None);
        var tooSoon = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        tooSoon.GrantedWaitId.ShouldBeNull();
        time.SetUtcNow(new DateTimeOffset(wallAt.AddSeconds(60), TimeSpan.Zero));
        await service.GrantReadyAsync(CancellationToken.None);
        var due = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        due.GrantedWaitId.ShouldBe(younger.Id);
        (await CapacityRecoveryTestSupport.CreateContext(schema).CapacityRecoveryWaits.SingleAsync(w => w.Id == older.Id))
            .State.ShouldBe(CapacityRecoveryWaitState.Reheld);
    }

    [Test]
    public async Task Card0412_V10_revalidation_does_not_increment()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 1, jitterSeconds: 0);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"agent:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.StandingStart,
            holdAlreadyCleared: true), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        var grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        (await service.RedeemAsync(
            wait.Id, grant.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Start,
            CancellationToken.None)).Ok.ShouldBeTrue();
        await service.RequestRevalidationGrantAsync(wait.Id, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(2));
        await service.GrantReadyAsync(CancellationToken.None);
        grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        var again = await service.RedeemAsync(
            wait.Id, grant.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Start,
            CancellationToken.None);
        again.Ok.ShouldBeTrue();
        again.Revalidation.ShouldBeTrue();
        (await CapacityRecoveryTestSupport.CreateContext(schema).CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id))
            .AdmissionCount.ShouldBe(1);
    }

    [Test]
    public async Task Card0412_V25_expired_clear_then_grant_and_confirm()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var holdId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
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

        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 1, jitterSeconds: 0);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdId: holdId,
            holdAlreadyCleared: true), CancellationToken.None);
        wait.ObservedClearCauses.ShouldContain("Expired");
        await service.GrantReadyAsync(CancellationToken.None);
        var grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        (await service.RedeemAsync(
            wait.Id, grant.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None)).Ok.ShouldBeTrue();
        await service.ConfirmPromptAsync(wait.Id, 42, CancellationToken.None);
        await service.MarkProgressedAsync(wait.Id, CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var done = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
        done.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
        done.ConfirmedPromptSequence.ShouldBe(42);
        (await verify.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.CapacityRecovery))
            .ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Card0412_V25_operator_cleared_same_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 1, jitterSeconds: 0);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true), CancellationToken.None);
        wait.ObservedClearCauses.ShouldContain("Expired");
        await service.GrantReadyAsync(CancellationToken.None);
        var grant = await CapacityRecoveryTestSupport.CreateContext(schema)
            .Set<CapacityRecoveryProviderState>()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        (await service.RedeemAsync(
            wait.Id, grant.GrantedActionKey!, AgentKind.ClaudeCode, CapacityRedemptionPath.Queue,
            CancellationToken.None)).Ok.ShouldBeTrue();
        await service.ConfirmPromptAsync(wait.Id, 7, CancellationToken.None);
        (await CapacityRecoveryTestSupport.CreateContext(schema).CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id))
            .ConfirmedPromptSequence.ShouldBe(7);
    }

    [Test]
    public async Task Card0412_V26_disabled_feature_refuses_admission()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema, enabled: false);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true), CancellationToken.None);
        (await service.GrantReadyAsync(CancellationToken.None)).ShouldBe(0);
        wait.State.ShouldBe(CapacityRecoveryWaitState.Ready);
    }
}
