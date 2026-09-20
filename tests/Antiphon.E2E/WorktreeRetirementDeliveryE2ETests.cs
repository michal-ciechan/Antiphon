using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel("C467LandDelivery")]
[Category("OptIn")]
public sealed class WorktreeRetirementDeliveryE2ETests
{
    [Test]
    [Arguments("idle")]
    [Arguments("busy-then-released")]
    [Arguments("claim-first")]
    public async Task C459_AdmittedWorkspaceInputReachesRecipient(string producer)
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(busy: producer == "busy-then-released");
        if (producer == "claim-first")
        {
            using var scope = CreateScope(f);
            var reservations = scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>();
            var claimed = await reservations.TryClaimRetirementAsync(new WorkspaceReservationCommand(
                new WorkspaceReservationKey(f.Source, "refs/heads/c467-source", f.Repository),
                WorkspaceReservationKind.Retirement, f.TaskId, RetirementId: Guid.NewGuid()), CancellationToken.None);
            claimed.Accepted.ShouldBeTrue();
            var failed = false;
            try
            {
                await f.RequestAsync();
            }
            catch (Exception)
            {
                failed = true;
            }

            failed.ShouldBeTrue("claim-first Land must not admit a retired workspace");
            return;
        }

        await f.RequestAsync();
        await f.ReleaseExecutionAsync();
        if (producer == "busy-then-released")
        {
            await LandDeliveryFixture.UntilAsync(async () =>
            {
                await using var db = f.CreateContext();
                return await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome && n.QueueMessageId != null);
            }, "busy caller has queued outcome");
            await f.ReleaseBusyAsync();
        }

        var note = await f.ReceiptAsync();
        await f.AssertRemoteAsync();
        await f.AssertOnePromptAsync(note);
    }

    [Test]
    [Arguments("lost-enqueue")]
    [Arguments("unknown-start")]
    public async Task C459_LaunchReservationRecoveryRetainsOwnership(string cut)
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync();
        using var scope = CreateScope(f);
        var reservations = scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>();
        var key = new WorkspaceReservationKey(f.Source, "refs/heads/c467-source", f.Repository);
        var launch = await reservations.TryAdmitConsumerAsync(new WorkspaceReservationCommand(
            key, WorkspaceReservationKind.Launch, f.TaskId), CancellationToken.None);
        launch.Accepted.ShouldBeTrue();
        var retirement = await reservations.TryClaimRetirementAsync(new WorkspaceReservationCommand(
            key, WorkspaceReservationKind.Retirement, f.TaskId, RetirementId: Guid.NewGuid()), CancellationToken.None);
        retirement.Accepted.ShouldBeFalse();
        await using var db = f.CreateContext();
        if (cut == "unknown-start")
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "grok", AgentKind = AgentKind.Grok,
                Cwd = f.Source, Status = SessionStatus.Starting, Cols = 80, Rows = 24,
                CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            (await reservations.TryClaimRetirementAsync(new WorkspaceReservationCommand(
                key, WorkspaceReservationKind.Retirement, f.TaskId, RetirementId: Guid.NewGuid()), CancellationToken.None))
                .Accepted.ShouldBeFalse();
        }

        (await db.WorkspaceUseReservations.CountAsync(r => r.TaskId == f.TaskId && r.Active && r.Kind == WorkspaceReservationKind.Launch))
            .ShouldBe(1);
    }

    private static IServiceScope CreateScope(LandDeliveryFixture f) => f.Services.CreateScope();
}
