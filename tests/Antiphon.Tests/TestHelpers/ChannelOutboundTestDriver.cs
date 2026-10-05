using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Explicit capture, preparation and publication boundaries for S11 reply fixtures.</summary>
internal static class ChannelOutboundTestDriver
{
    public static async Task AssertCapturedAsync(ServiceProvider provider, Guid sessionId,
        string sendKind, int memberCount)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = (await db.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => d.SourceSessionId == sessionId && d.State == ChannelOutboundDeliveryState.Captured)
            .ToListAsync()).ShouldHaveSingleItem();
        delivery.SendKind.ShouldBe(sendKind);
        delivery.CaptureJson.ShouldNotBeNullOrWhiteSpace();
        delivery.PublishedAt.ShouldBeNull();
        delivery.PublicationAttempts.ShouldBe(0);
        delivery.InputPath.ShouldBeEmpty();
        var members = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.ChannelOutboundDeliveryId == delivery.Id).ToListAsync();
        members.Count.ShouldBe(memberCount);
        members.ShouldAllBe(m => m.ChannelReplySettledAt == null);
    }

    public static async Task DrainAsync(ServiceProvider provider, Guid sessionId)
    {
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var captured = await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => d.SourceSessionId == sessionId && d.State == ChannelOutboundDeliveryState.Captured)
                .ToListAsync();
            foreach (var delivery in captured)
            {
                delivery.CaptureJson.ShouldNotBeNullOrWhiteSpace();
                delivery.PublishedAt.ShouldBeNull();
                delivery.PublicationAttempts.ShouldBe(0);
                var members = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.ChannelOutboundDeliveryId == delivery.Id).ToListAsync();
                members.ShouldAllBe(m => m.ChannelReplySettledAt == null);
            }
        }

        // A passthrough capture takes one materialization step and one publication step.
        // Each uses a fresh scope, as the hosted loop does. No retries or wall-clock sleeps.
        for (var step = 0; step < 2; step++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ChannelOutboundDeliveryPump>()
                .TickAsync(CancellationToken.None);
        }

        await using var checkScope = provider.CreateAsyncScope();
        var check = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var published = await check.ChannelOutboundDeliveries.AsNoTracking()
            .Where(d => d.SourceSessionId == sessionId && d.State == ChannelOutboundDeliveryState.Published)
            .ToListAsync();
        foreach (var delivery in published)
        {
            delivery.PublishedAt.ShouldNotBeNull();
            delivery.MetadataAppliedAt.ShouldNotBeNull();
            var members = await check.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.ChannelOutboundDeliveryId == delivery.Id).ToListAsync();
            members.ShouldAllBe(m => m.ChannelReplySettledAt != null);
        }
    }

    public static async Task AssertExpiredCaptureLossAsync(BridgeQueueHarness harness,
        Guid sourceId, long promptSequence, string response)
    {
        await using var scope = harness.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == sourceId);
        source.ChannelReplySettledAt.ShouldBeNull();
        var captured = await db.ChannelOutboundDeliveries.AsNoTracking()
            .SingleAsync(d => d.Id == source.ChannelOutboundDeliveryId);
        captured.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        captured.PromptSequence.ShouldBe(promptSequence);
        ChannelReplyPreparation.Deserialize(captured.CaptureJson!).Body.OriginalResponse.ShouldBe(response);
        captured.PreparationDeadlineAt.ShouldNotBeNull();
        captured.PreparationDeadlineAt.Value.ShouldBeLessThan(harness.Now);
        harness.Messaging.SentReplies.ShouldBeEmpty();

        await harness.DrainOutboundAsync();
        var failed = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == captured.Id);
        failed.State.ShouldBe(ChannelOutboundDeliveryState.Failed);
        failed.PublishedAt.ShouldBeNull();
        failed.PublicationAttempts.ShouldBe(0);
        failed.FailureReportedEpisode.ShouldBe(failed.FailureEpisode);
        failed.FailureReason.ShouldContain("original obligation deadline was exhausted");
        (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == sourceId))
            .ChannelReplySettledAt.ShouldNotBeNull();
        var incident = await db.AgentIncidents.SingleAsync(i => i.AgentId == harness.AgentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost);
        incident.FailureReason.ShouldBe("Failed");
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain(captured.Id.ToString());
        incident.Message.ShouldContain(sourceId.ToString());
        (await db.Alerts.CountAsync(a => a.AgentId == harness.AgentId)).ShouldBe(1);
        harness.Messaging.SentReplies.ShouldBeEmpty();
    }

    public static async Task AcknowledgeUncertainAsync(BridgeQueueHarness harness, Guid sourceId)
    {
        await using var scope = harness.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == sourceId);
        source.ChannelReplySettledAt.ShouldBeNull();
        source.ChannelOutboundDeliveryId.ShouldNotBeNull();
        var delivery = await db.ChannelOutboundDeliveries.AsNoTracking()
            .SingleAsync(d => d.Id == source.ChannelOutboundDeliveryId);
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        delivery.PublishedAt.ShouldBeNull();
        delivery.PublicationAttempts.ShouldBe(1);
        await scope.ServiceProvider.GetRequiredService<ChannelOutboundService>()
            .RetryUncertainAsync(delivery.Id, acknowledgePossibleDuplicate: true, CancellationToken.None);
    }
}
