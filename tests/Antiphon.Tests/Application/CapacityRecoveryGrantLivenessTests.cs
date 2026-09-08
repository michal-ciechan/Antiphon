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
            wait.State.ShouldBe(CapacityRecoveryWaitState.Deferred);
            wait.AdmissionCount.ShouldBe(hasReceipt ? 1 : 0);
            wait.SelectedMessageId.ShouldBe(hasReceipt ? receipt : null);
        }
        (await service.RedeemAsync(older.Id, older.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Queue, CancellationToken.None)).Ok.ShouldBeFalse();
        (await service.RedeemAsync(younger.Id, younger.ActionKey, AgentKind.ClaudeCode,
            CapacityRedemptionPath.Dispatch, CancellationToken.None)).Ok.ShouldBeTrue();
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
