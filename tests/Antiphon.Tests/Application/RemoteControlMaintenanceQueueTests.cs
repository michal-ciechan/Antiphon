using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RemoteControlMaintenanceQueueTests
{
    [Test]
    public void C514_Producer_identity_survives_reload()
    {
        RemoteControlRecoveryService.IsExactRemoteControlToken("/remote-control").ShouldBeTrue();
        RemoteControlRecoveryService.IsExactRemoteControlToken(" /remote-control extra").ShouldBeTrue();
        RemoteControlRecoveryService.IsExactRemoteControlToken("/remote-control-other").ShouldBeFalse();
        RemoteControlRecoveryService.IsExactRemoteControlToken("please remote-control").ShouldBeFalse();
        QueuedMessageOrigin.System.ShouldNotBe(QueuedMessageOrigin.Ui);
        QueuedMessageOrigin.Supervision.ShouldNotBe(QueuedMessageOrigin.Ui);
    }

    [Test]
    public void C514_Unknown_never_authorizes_automatic_arm()
    {
        RemoteControlBridgeClassifier.Classify(null, pid: 1, probeFailed: true)
            .ShouldBe(RemoteControlBridgeState.Unknown);
        RemoteControlBridgeClassifier.Classify(new(false, 0, false), pid: 1, probeFailed: false)
            .ShouldBe(RemoteControlBridgeState.Unknown);
        RemoteControlBridgeClassifier.Classify(new(false, 0, true), pid: null, probeFailed: false)
            .ShouldBe(RemoteControlBridgeState.Unknown);
    }

    [Test]
    public void C514_Current_menu_independently_suppresses_arm()
    {
        RemoteControlMenuScreen.IsPresent(FakeMenu).ShouldBeTrue();
        RemoteControlBridgeClassifier.Classify(new(false, 0, true), 7, false)
            .ShouldBe(RemoteControlBridgeState.Unarmed);
    }

    [Test]
    public void C514_Boot_and_health_keep_their_explicit_origins()
    {
        ((int)QueuedMessageOrigin.System).ShouldBe(2);
        ((int)QueuedMessageOrigin.Supervision).ShouldBe(5);
    }

    private const string FakeMenu =
        """
          Remote Control
            Disconnect this session
            Show QR code
          > Continue
          Enter to select . Esc to continue
        """;
}
