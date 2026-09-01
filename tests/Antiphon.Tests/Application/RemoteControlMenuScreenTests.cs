using Antiphon.Server.Application.Services;
using Antiphon.Tests.Agents;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0292 S2: the matcher is conservative on purpose — both literals, or it is not this menu.
/// A miss degrades to today's behaviour; a false positive costs one Esc (a measured no-op on an
/// idle empty composer).
/// </summary>
[Category("Unit")]
public class RemoteControlMenuScreenTests
{
    private const string IncidentScreen =
        """
        > /remote-control

        > /rename Antiphon
          |_ Session renamed to: Antiphon

        ------------------------------------------------------------------ Antiphon -

          Remote Control

          This session is available in the Claude mobile app and at
          https://claude.ai/code/session_011D79CHh3qcgGNB3mXGgdPz.

            Disconnect this session
            Show QR code  Scan with your phone to open this session
          > Continue

          Enter to select . Esc to continue
        """;

    [Test]
    public void Incident_screen_is_the_management_menu() =>
        RemoteControlMenuScreen.IsPresent(IncidentScreen).ShouldBeTrue();

    [Test]
    public void Fake_adapter_menu_fixture_matches() =>
        RemoteControlMenuScreen.IsPresent(FakeAgentProtocolAdapter.RemoteControlMenuScreenText)
            .ShouldBeTrue();

    [Test]
    public void Trust_dialog_is_not_the_menu() =>
        RemoteControlMenuScreen.IsPresent(
                "Is this a project you created or one you trust?\n1. Yes, I trust this folder")
            .ShouldBeFalse();

    [Test]
    public void Plain_composer_is_not_the_menu() =>
        RemoteControlMenuScreen.IsPresent("> ").ShouldBeFalse();

    [Test]
    public void Armed_marker_scrollback_is_not_the_menu() =>
        RemoteControlMenuScreen.IsPresent("remote-control is active\n> ")
            .ShouldBeFalse();

    [Test]
    public void Heading_alone_is_too_generic() =>
        RemoteControlMenuScreen.IsPresent("Remote Control\nthis session is available")
            .ShouldBeFalse();

    [Test]
    public void Disconnect_without_the_footer_is_not_enough() =>
        RemoteControlMenuScreen.IsPresent("Disconnect this session\nShow QR code")
            .ShouldBeFalse();

    [Test]
    public void Footer_without_disconnect_is_not_enough() =>
        RemoteControlMenuScreen.IsPresent("Enter to select . Esc to continue")
            .ShouldBeFalse();

    [Test]
    public void Null_or_empty_is_absent()
    {
        RemoteControlMenuScreen.IsPresent(null).ShouldBeFalse();
        RemoteControlMenuScreen.IsPresent("").ShouldBeFalse();
    }
}
