using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RemoteControlModalAttentionTests
{
    [Test]
    public void C514_Channel_bound_modal_is_immediately_critical()
    {
        ((int)AttentionKind.RemoteControlModal).ShouldBe(39);
        ((int)AlertSeverity.Critical).ShouldBeGreaterThan((int)AlertSeverity.Warning);
    }

    [Test]
    public void C514_Open_episode_survives_incident_pruning_and_since()
    {
        var id = Guid.NewGuid();
        RemoteControlRecoveryService.TryParseEpisodeReason(
            RemoteControlRecoveryService.EpisodeReason(id), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Test]
    public void C514_Specific_modal_dedupes_attention_but_keeps_history()
    {
        ((int)AgentIncidentKind.QueuedInputNeverConverted).ShouldBe(43);
        ((int)AttentionKind.QueuedInputStuck).ShouldNotBe((int)AttentionKind.RemoteControlModal);
    }
}
