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

    // One Redpanda broker shared by the C519 classes (SharedRedpandaBroker.C519: isolation, the reuse probe,
    // replacement, fail-fast, bounds and the CARD-1934 timed start); each test's own deadline starts once it has the
    // broker, and each publication cut asks for it again (the reuse probe runs before every cut).
    private static SharedRedpandaBroker SharedBroker => SharedRedpandaBroker.C519;

    // World teardowns run in the background (UnifiedOutboundTransport.DisposeAsync); none outlives its test.
    [After(Test)]
    public Task DrainWorldDisposalsAsync() => UnifiedOutboundTransport.DrainDisposalsAsync();

    // The probes' compiled model (ProbeGeneratedModel), generated or reused before any test's own deadline starts.
    [Before(Class)]
    public static Task ProbeModelAsync() => ProbeGeneratedModel.ResultAsync();

    // Disposed once both C519 classes have finished (else after the assembly), never by one class alone.
    [After(Class)]
    public static Task SharedBrokerClassFinishedAsync() => SharedRedpandaBroker.C519.ClassFinishedAsync(nameof(ChannelOutboundUnifiedCrashTests));

    private static async Task PreparationAsync(string kind, string cut)
    {
        var broker = await SharedBroker.GetAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        // Probe children: the death's, then the main kind's first recovery (FirstRecoveryAsync).
        await using var w = await UnifiedOutboundTransport.CreateAsync(broker, warmProbes: kind == "main" ? 2 : 1);
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
        // The first recovery after the death (a fresh child for the main kind); the repeat restart runs in process.
        await w.FirstRecoveryAsync(kind);
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

    // Run d88de419 g29: a cut probe stalled with no marker and no exit, and the parent's reach
    // poll expired before the child's own deadline, so the red carried no child evidence.
    [Test]
    public async Task C519_Stalled_cut_probe_fails_with_its_own_evidence()
    {
        var broker = await SharedBroker.GetAsync();
        await using var w = await UnifiedOutboundTransport.CreateAsync(broker);
        await w.SeedCrashSourceAsync("trailing");
        await using var child = await w.StartProbeAsync("capture-committed", stallPoint: "capture-committed");
        var failure = await Should.ThrowAsync<InvalidOperationException>(() => child.ReachAsync("capture-committed"));
        failure.Message.ShouldContain("probe stall capture-committed");
        failure.Message.ShouldContain("Canceled");
        Console.WriteLine(failure.Message);
    }

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Attempt_death_stays_uncertain(string kind) =>
        PublicationAsync(kind, ["publishing-committed", "before-producer-call", "producer-entered"], coldFirstCut: true);

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Accepted_death_stays_uncertain(string kind) =>
        PublicationAsync(kind, ["producer-accepted", "outcome-refused"]);

    [Test, Arguments("main"), Arguments("trailing"), Arguments("machine")]
    public Task C519_Published_death_never_replays(string kind) =>
        PublicationAsync(kind, ["published-committed"]);

    // coldFirstCut: every kind's first recoveries at cuts[0] are fresh children (UnifiedOutboundTransport.FirstRecoveryAsync).
    private static async Task PublicationAsync(string kind, string[] cuts, bool coldFirstCut = false)
    {
        foreach (var cut in cuts)
        {
            var broker = await SharedBroker.GetAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            // Probe children: the death's, then the first restart's and the retry republish's where they are fresh
            // children (FirstRecoveryAsync; a published cut has no retry).
            var fresh = kind == "main" || coldFirstCut && cut == cuts[0];
            await using var w = await UnifiedOutboundTransport.CreateAsync(broker,
                warmProbes: 1 + (fresh ? 1 : 0) + (fresh && cut != "published-committed" ? 1 : 0));
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
                // The first restart after the death is a first recovery; the second runs in process.
                await (restart > 0 ? w.RecoverAsync() : w.FirstRecoveryAsync(kind, coldFirstCut && cut == cuts[0]));
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
                // The republish after the operator's retry is a first recovery too.
                await w.FirstRecoveryAsync(kind, coldFirstCut && cut == cuts[0]);
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
