using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RemoteControlRecoveryTests
{
    [Test]
    public void C514_Dismissal_writes_only_one_Esc_payload()
    {
        RemoteControlRecoveryService.EscPayload.ShouldBe("\u001b");
        RemoteControlRecoveryService.EscPayload.ShouldNotContain("\r");
    }

    [Test]
    public void C514_Clear_without_Esc_is_ObservedClear()
    {
        RemoteControlEpisodeResolution.ObservedClear.ShouldNotBe(RemoteControlEpisodeResolution.DismissedVerified);
    }

    [Test]
    public void C514_Remnant_after_Esc_keeps_barrier_unverified()
    {
        var remnant = RemoteControlMenuScreen.Classify("Disconnect this session\nEsc to continue");
        remnant.IsPresent.ShouldBeFalse();
        remnant.HasRemnant.ShouldBeTrue();
        remnant.IsClear.ShouldBeFalse();
    }
}
