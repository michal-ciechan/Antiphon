using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundUnifiedCrashTests
{
    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Before_capture_death_is_discovered(string kind) =>
        PreparationAsync(kind, "capture-before-commit");

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Captured_death_needs_no_wake_signal(string kind) =>
        PreparationAsync(kind, "capture-committed");

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Partial_stage_death_retries_preparation(string kind) =>
        PreparationAsync(kind, "input-temporary-partial");

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Complete_stage_death_preserves_snapshot(string kind) =>
        PreparationAsync(kind, "input-stage-complete");

    private static async Task PreparationAsync(string kind, string cut)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await broker.StartAsync(deadline.Token);
        await using var w = await UnifiedOutboundTransport.CreateAsync(broker);
        await w.SeedCrashSourceAsync(kind);
        await using (var child = await w.StartProbeAsync(cut))
        {
            await child.ReachAsync(cut);
            var delivery = await w.DeliveryAsync(kind);
            if (cut == "capture-before-commit") delivery.ShouldBeNull();
            else
            {
                delivery.ShouldNotBeNull();
                delivery.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
                delivery.InputPath.ShouldBeEmpty(); delivery.PublishedAt.ShouldBeNull();
                delivery.PublicationAttempts.ShouldBe(0);
                delivery.RootDeliveryId.ShouldBe(w.RootId);
            }
            w.Slack.SentMessages.Count.ShouldBe(w.BaselineReceipts);
            await child.KillAsync();
        }
        if (cut == "input-stage-complete")
            await File.WriteAllBytesAsync(w.SourcePath, "replacement must never escape"u8.ToArray());
        await w.RecoverAsync();
        await w.AssertReceiptAsync();
        var published = (await w.DeliveryAsync(kind))!;
        published.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        published.PublicationAttempts.ShouldBe(1);
        published.MetadataAppliedAt.ShouldNotBeNull();
        await AssertSourceAsync(w, kind, true);
        await w.RecoverAsync();
        await w.AssertReceiptAsync();
        deadline.Token.ThrowIfCancellationRequested();
    }

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Attempt_death_stays_uncertain(string kind) =>
        PublicationAsync(kind, ["publishing-committed", "before-producer-call", "producer-entered"]);

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Accepted_death_stays_uncertain(string kind) =>
        PublicationAsync(kind, ["producer-accepted", "outcome-refused"]);

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Published_death_never_replays(string kind) =>
        PublicationAsync(kind, ["published-committed"]);

    private static async Task PublicationAsync(string kind, string[] cuts)
    {
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await broker.StartAsync(startup.Token);
        foreach (var cut in cuts)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using var w = await UnifiedOutboundTransport.CreateAsync(broker);
            await w.SeedCrashSourceAsync(kind);
            var accepted = cut is "producer-accepted" or "outcome-refused" or "published-committed";
            var published = cut == "published-committed";
            Guid deliveryId;
            await using (var child = await w.StartProbeAsync(cut, refuseOutcome: cut == "outcome-refused"))
            {
                await child.ReachAsync(cut);
                var row = (await w.DeliveryAsync(kind))!;
                deliveryId = row.Id;
                row.State.ShouldBe(published ? ChannelOutboundDeliveryState.Published : ChannelOutboundDeliveryState.Publishing);
                row.PublicationAttempts.ShouldBe(1);
                if (accepted) await w.AssertReceiptAsync();
                else w.Slack.SentMessages.Count.ShouldBe(w.BaselineReceipts);
                await child.KillAsync();
            }
            for (var restart = 0; restart < 2; restart++)
            {
                await w.RecoverAsync();
                var row = (await w.DeliveryAsync(kind))!;
                row.Id.ShouldBe(deliveryId);
                row.State.ShouldBe(published ? ChannelOutboundDeliveryState.Published : ChannelOutboundDeliveryState.PublishUncertain);
                row.PublicationAttempts.ShouldBe(1);
                w.Slack.SentMessages.Count.ShouldBe(w.BaselineReceipts + (accepted ? 1 : 0));
                await AssertSourceAsync(w, kind, published);
            }
            if (!published)
            {
                await using (var db = w.Db())
                {
                    var row = (await w.DeliveryAsync(kind))!;
                    row.PublishedAt.ShouldBeNull();
                    row.FailureEpisode.ShouldBeGreaterThan(0);
                    row.FailureReportedEpisode.ShouldBe(row.FailureEpisode);
                    (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.ChannelReplyLost)).ShouldBe(1);
                    (await db.Alerts.CountAsync()).ShouldBe(1);
                }
                await using var scope = w.H.Provider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
                await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
                    service.RetryUncertainAsync(deliveryId, false, default));
                await service.RetryUncertainAsync(deliveryId, true, default);
                await w.RecoverAsync();
                await w.AssertReceiptAsync(accepted ? 2 : 1);
                (await w.DeliveryAsync(kind))!.PublicationAttempts.ShouldBe(2);
                await AssertSourceAsync(w, kind, true);
            }
            deadline.Token.ThrowIfCancellationRequested();
        }
    }

    private static async Task AssertSourceAsync(UnifiedOutboundTransport w, string kind, bool published)
    {
        await using var db = w.Db();
        var member = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == w.MemberId);
        if (kind == "trailing")
        {
            member.ChannelOutboundDeliveryId.ShouldBe(w.RootId);
            var root = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == w.RootId);
            root.State.ShouldBe(ChannelOutboundDeliveryState.Published);
            root.PublicationAttempts.ShouldBe(1);
            member.ChannelReplySettledAt.ShouldBe(root.PublishedAt);
        }
        else
        {
            member.ChannelOutboundDeliveryId.ShouldBe((await w.DeliveryAsync(kind))!.Id);
            if (published) member.ChannelReplySettledAt.ShouldNotBeNull();
            else member.ChannelReplySettledAt.ShouldBeNull();
        }
    }
}
