using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class VisiblePtyOutputTests
{
    private const string Esc = "\x1b";
    private const string Bel = "\x07";

    [Test]
    public void Empty_snapshot_is_not_visible()
    {
        VisiblePtyOutput.HasVisibleOutput("").ShouldBeFalse();
        VisiblePtyOutput.HasVisibleOutput(null).ShouldBeFalse();
        VisiblePtyOutput.HasVisibleOutput("   \r\n\t").ShouldBeFalse();
    }

    [Test]
    public void Host_init_CSI_is_not_visible()
    {
        // CARD-0048 init burst: window ops, DA1, mouse tracking, win32-input.
        var snapshot = $"{Esc}[1t{Esc}[c{Esc}[?1004h{Esc}[?9001h";
        VisiblePtyOutput.HasVisibleOutput(snapshot).ShouldBeFalse();
    }

    [Test]
    public void Lone_OSC_title_is_not_visible()
    {
        // CARD-0050 title-only: cmd writes ESC]0;cmd.exe - … before the batch body.
        var snapshot = $"{Esc}]0;cmd.exe - C:\\src\\Antiphon\\slow.bat{Bel}";
        VisiblePtyOutput.HasVisibleOutput(snapshot).ShouldBeFalse();
    }

    [Test]
    public void Prompt_and_body_are_visible()
    {
        VisiblePtyOutput.HasVisibleOutput("> ").ShouldBeTrue();
        VisiblePtyOutput.HasVisibleOutput("HELLO").ShouldBeTrue();
    }

    [Test]
    public void Mixed_title_and_body_is_visible()
    {
        var snapshot = $"{Esc}]0;cmd.exe - slow.bat{Bel}SLOW_START_BODY";
        VisiblePtyOutput.HasVisibleOutput(snapshot).ShouldBeTrue();
    }
}
