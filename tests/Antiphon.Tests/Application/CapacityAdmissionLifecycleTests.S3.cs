using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CapacityAdmissionLifecycleTests
{
    [Test]
    [Arguments("launch")]
    [Arguments("admitted")]
    [Timeout(120_000)]
    public async Task C1593_S3_revalidation_grant_is_consumable_in_a_transaction(string mode, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, provider) = CapacityRecoveryTestSupport.CreateService(schema, intervalSeconds: 60, jitterSeconds: 0);
        await using var owned = provider;
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{sessionId:N}", CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true, sessionId: sessionId), ct);
        await CapacityRecoveryTestSupport.SeedHeldChannelDemandAsync(schema, sessionId, ct);
        (await service.GrantReadyAsync(ct)).ShouldBe(1);

        await using (var seed = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            var row = await seed.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id, ct);
            row.State = CapacityRecoveryWaitState.Admitted;
            row.NeedsRevalidationGrant = true;
            row.SelectedMessageId = messageId;
            row.AdmissionCount = 1;
            row.Outcome = nameof(CapacityRecoveryWaitState.Admitted);
            row.OutcomeReason = nameof(CapacityRedemptionPath.Queue);
            await seed.SaveChangesAsync(ct);
        }

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = mode == "admitted"
            ? await service.RedeemOnAsync(
                db, wait.Id, wait.ActionKey, wait.ExecutionKind, CapacityRedemptionPath.Start, ct,
                consume: true, receipt: CapacityConsumeReceipt.Admitted)
            : await service.RedeemOnAsync(
                db, wait.Id, wait.ActionKey, wait.ExecutionKind, CapacityRedemptionPath.Start, ct,
                consume: true, launchSessionId: sessionId);
        result.Reason.ShouldNotBe("not-action-pending");
        result.Ok.ShouldBeTrue(result.Reason);
        result.Revalidation.ShouldBeTrue();
        result.AdmissionCount.ShouldBe(1);

        var tracked = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id, ct);
        var grant = await db.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == wait.ExecutionKind, ct);
        if (mode == "admitted")
        {
            tracked.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            tracked.Outcome.ShouldBe(nameof(CapacityRecoveryWaitState.Admitted));
            tracked.OutcomeReason.ShouldBe(nameof(CapacityRedemptionPath.Start));
            tracked.NeedsRevalidationGrant.ShouldBeFalse();
            tracked.AdmissionCount.ShouldBe(1);
            tracked.SelectedMessageId.ShouldBe(messageId);
            tracked.LaunchSessionId.ShouldBeNull();
            tracked.LaunchReceipt.ShouldBeNull();
            tracked.DispatchAttemptId.ShouldBeNull();
            // RedeemAsync leaves the progress key the registration already stored.
            tracked.SessionId.ShouldBe(sessionId);
            grant.GrantedWaitId.ShouldBeNull();
            grant.GrantedActionKey.ShouldBeNull();
            grant.LastActionKey.ShouldBe(wait.ActionKey);
            grant.NextAdmissionAt.ShouldNotBeNull();
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            await using var after = CapacityRecoveryTestSupport.CreateContext(schema);
            var restored = await after.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
            restored.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
            restored.NeedsRevalidationGrant.ShouldBeTrue();
            restored.AdmissionCount.ShouldBe(1);
            restored.SelectedMessageId.ShouldBe(messageId);
            var stillGranted = await after.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == wait.ExecutionKind, ct);
            stillGranted.GrantedWaitId.ShouldBe(wait.Id);
            stillGranted.GrantedActionKey.ShouldBe(wait.ActionKey);
            return;
        }

        tracked.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        tracked.AdmissionCount.ShouldBe(1);
        tracked.LaunchSessionId.ShouldBe(sessionId);
        tracked.SessionId.ShouldBe(sessionId);
        tracked.DispatchAttemptId.ShouldNotBeNull();
        grant.GrantedWaitId.ShouldBeNull();
        await tx.CommitAsync(ct);
    }
}
