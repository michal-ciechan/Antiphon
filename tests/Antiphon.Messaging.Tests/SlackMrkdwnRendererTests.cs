using Antiphon.Messaging.Slack;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Unit tests for <see cref="SlackMrkdwnRenderer"/> — the mirror of
/// <see cref="TelegramMarkdownRendererTests"/> over the same agent-style Markdown, checked against
/// Slack's mrkdwn instead of Telegram HTML.
/// </summary>
public sealed class SlackMrkdwnRendererTests
{
    [Test]
    public void Bold_becomes_one_star()
        => SlackMrkdwnRenderer.ToMrkdwn("**important**").ShouldBe("*important*");

    [Test]
    public void Double_underscore_is_bold_too_because_slack_has_no_underline()
        => SlackMrkdwnRenderer.ToMrkdwn("__important__").ShouldBe("*important*");

    [Test]
    public void Italic_normalizes_to_underscores()
    {
        SlackMrkdwnRenderer.ToMrkdwn("*soft*").ShouldBe("_soft_");
        SlackMrkdwnRenderer.ToMrkdwn("_soft_").ShouldBe("_soft_");
    }

    // Bold renders to '*b*', which the italic rule would otherwise swallow — the ordering bug that
    // does not exist in the Telegram renderer, where bold becomes <b>…</b>.
    [Test]
    public void Bold_survives_the_italic_pass()
        => SlackMrkdwnRenderer.ToMrkdwn("**bold** and *soft* together").ShouldBe("*bold* and _soft_ together");

    [Test]
    public void Strikethrough_becomes_one_tilde()
        => SlackMrkdwnRenderer.ToMrkdwn("~~gone~~").ShouldBe("~gone~");

    [Test]
    public void Links_become_slacks_angle_form()
        => SlackMrkdwnRenderer.ToMrkdwn("see [the docs](https://example.com/x)")
            .ShouldBe("see <https://example.com/x|the docs>");

    [Test]
    public void Headings_become_bold_lines()
        => SlackMrkdwnRenderer.ToMrkdwn("## Findings").ShouldBe("*Findings*");

    [Test]
    public void Bullets_become_dots_and_keep_their_indent()
        => SlackMrkdwnRenderer.ToMrkdwn("- one\n  - nested").ShouldBe("• one\n  • nested");

    [Test]
    public void Ordered_lists_keep_their_numbers()
        => SlackMrkdwnRenderer.ToMrkdwn("1. first\n2. second").ShouldBe("1. first\n2. second");

    [Test]
    public void Blockquotes_pass_through_slacks_native_marker()
        => SlackMrkdwnRenderer.ToMrkdwn("> quoted **thing**").ShouldBe("> quoted *thing*");

    [Test]
    public void Horizontal_rules_become_a_divider()
        => SlackMrkdwnRenderer.ToMrkdwn("---").ShouldBe("———");

    [Test]
    public void Angle_brackets_and_ampersands_are_escaped()
        => SlackMrkdwnRenderer.ToMrkdwn("a < b && c > d").ShouldBe("a &lt; b &amp;&amp; c &gt; d");

    [Test]
    public void Code_spans_keep_their_content_verbatim_but_escaped()
        => SlackMrkdwnRenderer.ToMrkdwn("run `a < b && **c**`").ShouldBe("run `a &lt; b &amp;&amp; **c**`");

    [Test]
    public void Fenced_code_blocks_lose_the_language_hint_slack_cannot_use()
        => SlackMrkdwnRenderer.ToMrkdwn("```csharp\nvar x = a < b;\n```")
            .ShouldBe("```\nvar x = a &lt; b;\n```");

    [Test]
    public void Tables_become_a_code_block_because_slack_has_none()
        => SlackMrkdwnRenderer.ToMrkdwn("| a | b |\n|---|---|\n| 1 | 2 |")
            .ShouldBe("```\n| a | b |\n| 1 | 2 |\n```");

    [Test]
    public void Arithmetic_and_snake_case_survive_untouched()
    {
        SlackMrkdwnRenderer.ToMrkdwn("2 * 3 * 4 = 24").ShouldBe("2 * 3 * 4 = 24");
        SlackMrkdwnRenderer.ToMrkdwn("call some_snake_case_name(x)").ShouldBe("call some_snake_case_name(x)");
    }

    [Test]
    public void A_url_inside_a_rendered_link_is_not_double_escaped()
        => SlackMrkdwnRenderer.ToMrkdwn("[q](https://x.dev/a?b=1&c=2)")
            .ShouldBe("<https://x.dev/a?b=1&amp;c=2|q>", "Slack wants &amp; inside link targets");

    [Test]
    public void An_unterminated_fence_still_produces_readable_output()
        => SlackMrkdwnRenderer.ToMrkdwn("```\nno closing fence").ShouldBe("```\nno closing fence\n```");

    [Test]
    public void Plain_prose_is_left_alone()
        => SlackMrkdwnRenderer.ToMrkdwn("Nothing to do here.").ShouldBe("Nothing to do here.");
}
