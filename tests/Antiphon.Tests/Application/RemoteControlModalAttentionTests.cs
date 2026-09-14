using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("RemoteControlRecovery")]
public class RemoteControlModalAttentionTests
{
    [Test]
    public async Task C514_Channel_bound_modal_is_immediately_critical()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.BindChannelAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldNotBeNull();
        episode!.ChannelBound.ShouldBeTrue();

        var items = (await h.AttentionAsync()).Items
            .Where(i => i.SessionId == h.SessionId && i.Kind == AttentionKind.RemoteControlModal)
            .ToList();
        items.ShouldHaveSingleItem();
        items[0].Severity.ShouldBe(AlertSeverity.Critical);
        items[0].Headline.ShouldBe("Remote Control menu blocks input");
    }

    [Test]
    public async Task C514_Open_episode_survives_incident_pruning_and_since()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldNotBeNull();

        await using (var db = h.CreateDb())
        {
            await db.AgentIncidents
                .Where(i => i.SessionId == h.SessionId)
                .ExecuteDeleteAsync();
        }

        var items = (await h.AttentionAsync()).Items
            .Where(i => i.SessionId == h.SessionId && i.Kind == AttentionKind.RemoteControlModal)
            .ToList();
        items.ShouldHaveSingleItem();
        items[0].ConditionKey.ShouldBe(RemoteControlRecoveryService.EpisodeReason(episode!.Id));
        items[0].Evidence.ShouldContain("Input conversion remains unproven");
    }

    [Test]
    public async Task C514_Specific_modal_dedupes_attention_but_keeps_history()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        await h.Recovery.DetectAsync(h.SessionId, h.Generation, observation, null, CancellationToken.None);

        await using (var db = h.CreateDb())
        {
            db.AgentIncidents.Add(new Antiphon.Server.Domain.Entities.AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = h.AgentId,
                SessionId = h.SessionId,
                Kind = AgentIncidentKind.QueuedInputNeverConverted,
                Severity = AlertSeverity.Warning,
                Message = "Queued input never converted",
                FailureReason = "enqueueSeq=1",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var items = (await h.AttentionAsync()).Items.Where(i => i.SessionId == h.SessionId).ToList();
        items.ShouldContain(i => i.Kind == AttentionKind.RemoteControlModal);
        items.ShouldNotContain(i => i.Kind == AttentionKind.QueuedInputStuck);

        await using var verify = h.CreateDb();
        (await verify.AgentIncidents.AnyAsync(i =>
            i.SessionId == h.SessionId && i.Kind == AgentIncidentKind.QueuedInputNeverConverted))
            .ShouldBeTrue();
    }

    [Test]
    public async Task C514_Receipt_projection_survives_publication_failure()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldNotBeNull();
        var items = (await h.AttentionAsync()).Items
            .Where(i => i.Kind == AttentionKind.RemoteControlModal && i.SessionId == h.SessionId)
            .ToList();
        items.ShouldHaveSingleItem();
        items[0].Evidence.ShouldContain(RemoteControlRecoveryService.EpisodeReason(episode!.Id));
        items[0].Evidence.ShouldNotContain("https://");
        items[0].Headline.ShouldNotContain("Delivered");
    }
}
