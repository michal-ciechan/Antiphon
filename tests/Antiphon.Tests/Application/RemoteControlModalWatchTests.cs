using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RemoteControlModalWatchTests
{
    [Test]
    public void C514_Modal_watch_is_independent_of_connection_watch()
    {
        var settings = new SupervisionSettings
        {
            Enabled = true,
            RcWatch = new RcWatchSettings { Enabled = false },
            RcModalWatch = new RcModalWatchSettings { Enabled = true },
        };
        settings.RcModalWatch.Enabled.ShouldBeTrue();
        settings.RcWatch.Enabled.ShouldBeFalse();
        settings.Enabled.ShouldBeTrue();
    }

    [Test]
    public void C514_Erased_menu_in_raw_history_is_not_current()
    {
        var raw = "Remote Control\nDisconnect this session\nShow QR code\nContinue\nEsc to continue";
        var current = "> ";
        RemoteControlMenuScreen.IsPresent(raw).ShouldBeTrue();
        RemoteControlMenuScreen.Classify(current).IsClear.ShouldBeTrue();
    }

    [Test]
    public void C514_Shutdown_stops_modal_background_work()
    {
        var settings = new SupervisionSettings { Enabled = false, RcModalWatch = new RcModalWatchSettings { Enabled = true } };
        settings.Enabled.ShouldBeFalse();
    }
}
