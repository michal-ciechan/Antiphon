using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Separating the TUI's animating chrome from its content — the thing that makes "has the screen
/// settled?" answerable at all. Fixtures are real screen shapes captured from the live CLI while
/// debugging the delegation E2E.
/// </summary>
[Category("Unit")]
public class ClaudeScreenTests
{
    [Test]
    public void two_frames_that_differ_only_by_the_spinner_are_the_same_content()
    {
        // The headline case. The spinner glyph AND the verb both change every frame — the verb is
        // chosen at random ("Inferring", "Scurrying", "Cultivating"), so matching words is hopeless
        // and the shape has to carry it. Without this, no two snapshots are ever equal and a
        // settle-detector can never fire.
        const string frameOne = """
            ● Write(step1.txt)
              ⎿  Wrote 1 line to step1.txt
            ✻ Cultivating… (4s · ↓ 126 tokens)
            """;
        const string frameTwo = """
            ● Write(step1.txt)
              ⎿  Wrote 1 line to step1.txt
            ✽ Scurrying… (9s · ↓ 512 tokens)
            """;

        ClaudeScreen.IsSettled(frameOne, frameTwo).ShouldBeTrue();
    }

    [Test]
    public void a_frame_with_new_content_is_not_settled()
    {
        // The other half: stripping must not be so aggressive that real output vanishes, or the
        // detector reports "finished" while the model is still writing.
        const string before = """
            ● Write(step1.txt)
            ✻ Cultivating… (4s · ↓ 126 tokens)
            """;
        const string after = """
            ● Write(step1.txt)
            ● Created step1.txt with the single line ANTIPHON-STEP-ONE-OK.
            ✽ Scurrying… (9s · ↓ 512 tokens)
            """;

        ClaudeScreen.IsSettled(before, after).ShouldBeFalse();
    }

    [Test]
    public void an_elapsed_counter_beside_real_content_is_dropped_but_the_content_survives()
    {
        var stable = ClaudeScreen.Stable("● Listing 1 directory… (21s · ↓ 619 tokens)");

        stable.ShouldContain("Listing 1 directory");
        stable.ShouldNotContain("21s");
        stable.ShouldNotContain("619");
    }

    [Test]
    public void hint_bars_are_chrome_and_are_dropped()
    {
        var stable = ClaudeScreen.Stable("""
            ● Done, no issues.
              ⏵⏵ bypass permissions on (shift+tab to cycle)                    ◉ xhigh · /effort
            """);

        stable.ShouldContain("Done, no issues.");
        stable.ShouldNotContain("bypass permissions");
    }

    [Test]
    public void box_drawing_is_dropped()
    {
        ClaudeScreen.Stable("────────────────────────────────").Trim().ShouldBeEmpty();
    }

    [Test]
    public void working_is_the_interrupt_hint_only()
    {
        // An elapsed counter also appears on COMPLETED tool annotations that never leave the
        // screen, so keying on it makes every turn look like it runs forever — the bug that made
        // the first three E2E attempts time out.
        ClaudeScreen.IsWorking("✻ Cultivating… (4s) · esc to interrupt").ShouldBeTrue();
        ClaudeScreen.IsWorking("● Listing 1 directory… (21s · ↓ 619 tokens)").ShouldBeFalse();
        ClaudeScreen.IsWorking("● Done, no issues.").ShouldBeFalse();
    }

    [Test]
    public void the_live_composer_is_recognised()
    {
        ClaudeScreen.ComposerIsLive("❯ Try \"create a util logging.py that...\"\n  ? for shortcuts")
            .ShouldBeTrue();
        ClaudeScreen.ComposerIsLive("✻ Cultivating…").ShouldBeFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public void empty_input_is_handled(string screen)
    {
        ClaudeScreen.Stable(screen).Trim().ShouldBeEmpty();
        ClaudeScreen.IsWorking(screen).ShouldBeFalse();
    }
}
