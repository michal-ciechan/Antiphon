using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryGrantLivenessTests
{
    [Test]
    [Arguments(false, 100)]
    [Arguments(true, 100)]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    public async Task Card0412_D7_two_dead_older_consumers_do_not_starve_a_healthy_newer_wait(bool hasReceipt, int batch)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema, batch: batch);
        await using var services = provider;
        var now = time.GetUtcNow().UtcDateTime;
        var deadWaits = new List<CapacityRecoveryWait>();
        for (var index = 0; index < 2; index++)
        {
            var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{Guid.NewGuid():N}", CapacityWaitConsumerKind.LiveSession,
                holdAlreadyCleared: true, blockedAt: now.AddMinutes(-30 + index)), CancellationToken.None);
            deadWaits.Add(wait);
            if (hasReceipt)
            {
                await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
                var row = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
                row.State = CapacityRecoveryWaitState.Admitted;
                row.AdmissionCount = 1;
                row.SelectedMessageId = Guid.NewGuid();
                row.UpdatedAt = now.AddMinutes(-10);
                await db.SaveChangesAsync();
            }
        }
        var healthy = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}", CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true, blockedAt: now.AddMinutes(-1)), CancellationToken.None);

        int? admittedTick = null;
        for (var tick = 0; tick <= 6; tick++)
        {
            await service.ReconcileAsync(CancellationToken.None);
            await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
            var grant = await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            if (grant.GrantedWaitId == healthy.Id)
            {
                var accepted = await service.RedeemAsync(healthy.Id, grant.GrantedActionKey!,
                    AgentKind.ClaudeCode, CapacityRedemptionPath.Dispatch, CancellationToken.None);
                accepted.Ok.ShouldBeTrue(accepted.Reason);
                accepted.AdmissionCount.ShouldBe(1);
                await service.MarkProgressedAsync(healthy.Id, CancellationToken.None);
                admittedTick = tick;
                break;
            }
            // Only the healthy consumer redeems; the two orphan consumers never do.
            time.Advance(TimeSpan.FromSeconds(60));
        }

        admittedTick.ShouldNotBeNull("two expired older grants must not ping-pong forever");
        admittedTick.Value.ShouldBeLessThanOrEqualTo(6);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var deadIds = deadWaits.Select(w => w.Id).ToArray();
        var deadRows = await verify.CapacityRecoveryWaits.Where(w => deadIds.Contains(w.Id)).ToListAsync();
        deadRows.ShouldAllBe(w => w.AdmissionCount == (hasReceipt ? 1 : 0));
        deadRows.ShouldAllBe(w => w.State != CapacityRecoveryWaitState.Exhausted);
        if (hasReceipt)
            deadRows.ShouldAllBe(w => w.ActionOrdinal == 0 && w.SelectedMessageId != null);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Card0412_D4_abandoned_grant_expires_and_younger_task_advances(bool hasReceipt)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        time.SetUtcNow(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var now = time.GetUtcNow().UtcDateTime;
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}", CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true, blockedAt: now.AddMinutes(-30)), CancellationToken.None);
        var younger = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}", CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true, blockedAt: now.AddMinutes(-1)), CancellationToken.None);
        var receipt = Guid.NewGuid();
        if (hasReceipt)
        {
            // A's consumer has disappeared after executing: its durable receipt remains,
            // but nobody will redeem another grant. B still has an active dispatch consumer.
            await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
            var wait = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == older.Id);
            wait.State = CapacityRecoveryWaitState.Admitted;
            wait.AdmissionCount = 1;
            wait.SelectedMessageId = receipt;
            wait.UpdatedAt = now.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        await service.ReconcileAsync(CancellationToken.None);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var grant = await db.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            grant.GrantedWaitId.ShouldBe(older.Id);
            grant.GrantedAt.ShouldBe(now);
            var wait = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == older.Id);
            wait.NeedsRevalidationGrant.ShouldBe(hasReceipt);
            wait.State.ShouldBe(hasReceipt ? CapacityRecoveryWaitState.Admitted : CapacityRecoveryWaitState.ActionPending);
        }

        // The grant survives an admission interval, but expires at two intervals.
        for (var tick = 1; tick <= 2; tick++)
        {
            time.Advance(TimeSpan.FromSeconds(60));
            await service.ReconcileAsync(CancellationToken.None);
            await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
            var grant = await db.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            grant.GrantedWaitId.ShouldBe(tick == 1 ? older.Id : younger.Id);
            if (tick == 1)
                grant.GrantedAt.ShouldBe(now);
        }

        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var wait = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == older.Id);
            wait.State.ShouldBe(hasReceipt ? CapacityRecoveryWaitState.Admitted : CapacityRecoveryWaitState.Ready);
            wait.NeedsRevalidationGrant.ShouldBe(hasReceipt);
            wait.DueAt.ShouldBe(now.AddSeconds(240));
            wait.OutcomeReason.ShouldBe("grant-expired");
            wait.AdmissionCount.ShouldBe(hasReceipt ? 1 : 0);
            wait.SelectedMessageId.ShouldBe(hasReceipt ? receipt : null);
        }
        (await service.RedeemAsync(older.Id, older.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Queue, CancellationToken.None)).Ok.ShouldBeFalse();
        (await service.RedeemAsync(younger.Id, younger.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Dispatch, CancellationToken.None)).Ok.ShouldBeTrue();
        await service.MarkProgressedAsync(younger.Id, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(119));
        await service.ReconcileAsync(CancellationToken.None);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            (await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
                .GrantedWaitId.ShouldBeNull();
        time.Advance(TimeSpan.FromSeconds(1));
        await service.ReconcileAsync(CancellationToken.None);
        var rearmed = (await service.FindUnfinishedAsync(older.ConsumerKey, CancellationToken.None))!;
        rearmed.ActionOrdinal.ShouldBe(hasReceipt ? 0 : 1);
        var accepted = await service.RedeemAsync(rearmed.Id, rearmed.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Queue, CancellationToken.None);
        accepted.Ok.ShouldBeTrue();
        accepted.Revalidation.ShouldBe(hasReceipt);
        accepted.AdmissionCount.ShouldBe(1);
    }

    [Test]
    public async Task Card0412_D6_three_queued_waits_survive_twelve_unredeemed_ticks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var waits = new List<CapacityRecoveryWait>();
        for (var index = 0; index < 3; index++)
            waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{Guid.NewGuid():N}", CapacityWaitConsumerKind.QueuedTask,
                holdAlreadyCleared: true, blockedAt: time.GetUtcNow().UtcDateTime.AddMinutes(-30 + index)),
                CancellationToken.None));

        // Exact review probe: reconcile, advance 60s, twelve times, with no redemption.
        for (var tick = 0; tick < 12; tick++)
        {
            await service.ReconcileAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(60));
        }

        await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
        var grant = await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
        var accepted = await service.RedeemAsync(grant.GrantedWaitId ?? waits[0].Id, grant.GrantedActionKey ?? waits[0].ActionKey,
            AgentKind.ClaudeCode, CapacityRedemptionPath.Dispatch, CancellationToken.None);
        accepted.Ok.ShouldBeTrue(accepted.Reason);
        grant.GrantedWaitId.ShouldNotBeNull();
        accepted.AdmissionCount.ShouldBe(1);
        var rows = await db.CapacityRecoveryWaits.AsNoTracking()
            .Where(w => waits.Select(x => x.Id).Contains(w.Id)).ToListAsync();
        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(w => w.State == CapacityRecoveryWaitState.Ready
            || w.State == CapacityRecoveryWaitState.Admitted || w.State == CapacityRecoveryWaitState.ActionPending);
        rows.Sum(w => w.AdmissionCount).ShouldBe(1);
        rows.Sum(w => w.ActionOrdinal).ShouldBeGreaterThan(0);

        // Once capacity is available, every consumer can redeem its current action.
        await service.MarkProgressedAsync(grant.GrantedWaitId.Value, CancellationToken.None);
        for (var remaining = 0; remaining < 2; remaining++)
        {
            time.Advance(TimeSpan.FromSeconds(120));
            await service.ReconcileAsync(CancellationToken.None);
            var next = await db.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode);
            next.GrantedWaitId.ShouldNotBeNull();
            (await service.RedeemAsync(next.GrantedWaitId!.Value, next.GrantedActionKey!, AgentKind.ClaudeCode,
                CapacityRedemptionPath.Dispatch, CancellationToken.None)).Ok.ShouldBeTrue();
            await service.MarkProgressedAsync(next.GrantedWaitId.Value, CancellationToken.None);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Card0412_D6_revocation_rearms_and_updates_cached_consumer_keys(bool waveBump)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        time.SetUtcNow(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var sessionId = Guid.NewGuid();
        var registration = CapacityRecoveryTestSupport.Registration(
            $"session:{sessionId:N}", CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true, sessionId: sessionId);
        var wait = await service.EnsureWaitAsync(registration, CancellationToken.None);
        var messageId = Guid.NewGuid();
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                CapacityWaitId = wait.Id, CapacityRecoveryActionKey = wait.ActionKey,
                CreatedAt = time.GetUtcNow().UtcDateTime, StartedAt = time.GetUtcNow().UtcDateTime,
            });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = messageId, AgentSessionId = sessionId, Body = "held body", Sequence = 1,
                CapacityWaitId = wait.Id, CapacityRecoveryActionKey = wait.ActionKey,
                CapacityWaitVersion = wait.Version, CreatedAt = time.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
        }
        await service.StampQueueActionAsync(messageId, wait.Id, wait.ActionKey, CancellationToken.None);
        await service.ReconcileAsync(CancellationToken.None);
        if (waveBump)
            await service.BumpWaveAsync(AgentKind.ClaudeCode, time.GetUtcNow().UtcDateTime, CancellationToken.None);
        else
        {
            time.Advance(TimeSpan.FromSeconds(120));
            await service.ReconcileAsync(CancellationToken.None);
        }

        var rearmed = await service.EnsureWaitAsync(registration, CancellationToken.None);
        rearmed.Id.ShouldBe(wait.Id);
        rearmed.State.ShouldBe(CapacityRecoveryWaitState.Ready);
        rearmed.ActionOrdinal.ShouldBe(1);
        rearmed.ActionKey.ShouldNotBe(wait.ActionKey);
        rearmed.DueAt.ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(120));
        rearmed.OutcomeReason.ShouldBe(waveBump ? "new-wall-wave" : "grant-expired");
        rearmed.AdmissionCount.ShouldBe(0);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            (await db.AgentSessions.SingleAsync(s => s.Id == sessionId))
                .CapacityRecoveryActionKey.ShouldBe(wait.ActionKey);
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
            message.CapacityRecoveryActionKey.ShouldBe(rearmed.ActionKey);
            message.CapacityWaitVersion.ShouldBe(rearmed.Version);
            message.Body.ShouldBe("held body");
            message.DeliveryAttempts.ShouldBe(0);
        }
        time.Advance(TimeSpan.FromSeconds(120));
        await service.ReconcileAsync(CancellationToken.None);
        (await service.RedeemAsync(wait.Id, wait.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Queue, CancellationToken.None)).Reason.ShouldBe("grant-mismatch");
        (await service.RedeemAsync(rearmed.Id, rearmed.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Queue, CancellationToken.None)).Ok.ShouldBeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Card0412_D6_revoked_revalidation_at_attempt_cap_preserves_receipt(bool waveBump)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        time.SetUtcNow(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"agent:{Guid.NewGuid():N}", CapacityWaitConsumerKind.StandingStart,
            holdAlreadyCleared: true), CancellationToken.None);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var row = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            row.AdmissionCount = service.MaxAttempts;
            row.LaunchReceipt = "accepted-launch";
            await db.SaveChangesAsync();
        }
        await service.RequestRevalidationGrantAsync(wait.Id, CancellationToken.None);
        await service.ReconcileAsync(CancellationToken.None);
        if (waveBump)
            await service.BumpWaveAsync(AgentKind.ClaudeCode, time.GetUtcNow().UtcDateTime, CancellationToken.None);
        else
        {
            time.Advance(TimeSpan.FromSeconds(120));
            await service.ReconcileAsync(CancellationToken.None);
        }
        var rearmed = (await service.FindUnfinishedAsync(wait.ConsumerKey, CancellationToken.None))!;
        rearmed.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
        rearmed.NeedsRevalidationGrant.ShouldBeTrue();
        rearmed.ActionKey.ShouldBe(wait.ActionKey);
        rearmed.LaunchReceipt.ShouldBe("accepted-launch");
        rearmed.DueAt.ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(120));
        time.Advance(TimeSpan.FromSeconds(120));
        await service.ReconcileAsync(CancellationToken.None);
        var accepted = await service.RedeemAsync(wait.Id, wait.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Start, CancellationToken.None);
        accepted.Ok.ShouldBeTrue();
        accepted.Revalidation.ShouldBeTrue();
        accepted.AdmissionCount.ShouldBe(service.MaxAttempts);
    }

    [Test]
    public async Task Card0412_D4_rearm_holds_provider_lock_until_saved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var pause = new PauseRearmSaveInterceptor();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema, interceptor: pause);
        await using var services = provider;
        var now = time.GetUtcNow().UtcDateTime;
        var older = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}", CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true, blockedAt: now.AddMinutes(-30)), CancellationToken.None);
        await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}", CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true, blockedAt: now.AddMinutes(-1)), CancellationToken.None);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var wait = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == older.Id);
            wait.State = CapacityRecoveryWaitState.Admitted;
            wait.SelectedMessageId = Guid.NewGuid();
            wait.UpdatedAt = now.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var rearm = service.ReconcileAsync(timeout.Token);
        Task<int>? concurrentGrant = null;
        var completedWhilePaused = false;
        try
        {
            await pause.Arrived.Task.WaitAsync(timeout.Token);
            concurrentGrant = service.GrantReadyAsync(timeout.Token);
            completedWhilePaused = await Task.WhenAny(concurrentGrant, Task.Delay(400, timeout.Token)) == concurrentGrant;
        }
        finally
        {
            pause.Release.TrySetResult();
            await rearm;
            if (concurrentGrant is not null)
                await concurrentGrant;
        }

        completedWhilePaused.ShouldBeFalse("grant selection must wait for the re-arm transaction");
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        (await verify.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
            .GrantedWaitId.ShouldBe(older.Id);
    }

    private sealed class PauseRearmSaveInterceptor : SaveChangesInterceptor
    {
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CapacityRecoveryWait>().Any(e =>
                    e.Entity.NeedsRevalidationGrant && e.Property(w => w.NeedsRevalidationGrant).IsModified) == true)
            {
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
