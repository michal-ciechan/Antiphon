using Antiphon.Messaging.Telegram;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Pins the Markdown -> Telegram HTML mapping documented in docs/telegram.md. Every entity kind
/// Telegram's HTML parse mode supports is covered, plus the escaping and don't-mangle guarantees.
/// </summary>
public sealed class TelegramMarkdownRendererTests
{
    [Test]
    [Arguments("**bold**", "<b>bold</b>")]
    [Arguments("*italic*", "<i>italic</i>")]
    [Arguments("_italic_", "<i>italic</i>")]
    [Arguments("__underline__", "<u>underline</u>")]
    [Arguments("~~gone~~", "<s>gone</s>")]
    [Arguments("||surprise||", "<tg-spoiler>surprise</tg-spoiler>")]
    [Arguments("`var x = 1;`", "<code>var x = 1;</code>")]
    [Arguments("[docs](https://example.com/a?b=1)", "<a href=\"https://example.com/a?b=1\">docs</a>")]
    public async Task Inline_spans_map_to_telegram_entities(string markdown, string expected)
    {
        TelegramMarkdownRenderer.ToHtml(markdown).ShouldBe(expected);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Nested_and_mixed_spans_compose()
    {
        TelegramMarkdownRenderer.ToHtml("**bold with *italic* inside**")
            .ShouldBe("<b>bold with <i>italic</i> inside</b>");
        TelegramMarkdownRenderer.ToHtml("a **b** and `c()` and [d](https://d.example)")
            .ShouldBe("a <b>b</b> and <code>c()</code> and <a href=\"https://d.example\">d</a>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Html_specials_are_escaped_everywhere()
    {
        TelegramMarkdownRenderer.ToHtml("if a < b && b > c write **<html>**")
            .ShouldBe("if a &lt; b &amp;&amp; b &gt; c write <b>&lt;html&gt;</b>");
        TelegramMarkdownRenderer.ToHtml("`<script>alert(1)</script>`")
            .ShouldBe("<code>&lt;script&gt;alert(1)&lt;/script&gt;</code>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Code_span_content_is_never_formatted()
    {
        TelegramMarkdownRenderer.ToHtml("run `git commit -m \"**wip**\"` now")
            .ShouldBe("run <code>git commit -m &quot;**wip**&quot;</code> now");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Fenced_code_block_with_language_becomes_pre_code()
    {
        var md = "```python\nprint(\"hi\")\nx = 1 < 2\n```";
        TelegramMarkdownRenderer.ToHtml(md)
            .ShouldBe("<pre><code class=\"language-python\">print(&quot;hi&quot;)\nx = 1 &lt; 2</code></pre>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Fenced_code_block_without_language_becomes_pre()
    {
        TelegramMarkdownRenderer.ToHtml("```\nplain block\n```").ShouldBe("<pre>plain block</pre>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Headings_become_bold_lines()
    {
        TelegramMarkdownRenderer.ToHtml("# Title\n## Sub *heading*")
            .ShouldBe("<b>Title</b>\n<b>Sub <i>heading</i></b>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Lists_render_as_bullets_and_keep_numbers()
    {
        TelegramMarkdownRenderer.ToHtml("- first\n- **second**\n  - nested\n1. one\n2) two")
            .ShouldBe("• first\n• <b>second</b>\n  • nested\n1. one\n2. two");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Blockquote_run_becomes_one_blockquote()
    {
        TelegramMarkdownRenderer.ToHtml("> quoted **line**\n> second line\nafter")
            .ShouldBe("<blockquote>quoted <b>line</b>\nsecond line</blockquote>\nafter");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Table_rows_are_preserved_in_monospace()
    {
        var md = "| a | b |\n|---|---|\n| 1 | 2 |";
        TelegramMarkdownRenderer.ToHtml(md).ShouldBe("<pre>| a | b |\n| 1 | 2 |</pre>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Horizontal_rule_becomes_a_divider()
    {
        TelegramMarkdownRenderer.ToHtml("above\n---\nbelow").ShouldBe("above\n———\nbelow");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Prose_that_is_not_markdown_is_left_readable()
    {
        // Arithmetic, snake_case, and lone markers must not be eaten.
        TelegramMarkdownRenderer.ToHtml("2*3*4 = 24 and my_var_name stays, 5 * 3 too")
            .ShouldBe("2*3*4 = 24 and my_var_name stays, 5 * 3 too");
        TelegramMarkdownRenderer.ToHtml("a single * star and _ underscore")
            .ShouldBe("a single * star and _ underscore");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Unterminated_fence_still_renders_the_content()
    {
        TelegramMarkdownRenderer.ToHtml("```\nno closing fence")
            .ShouldBe("<pre>no closing fence</pre>");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Real_agent_reply_shape_renders_cleanly()
    {
        var md = "**2. iPhone Shortcuts automation (most reliable phone-side fix)**\n"
            + "- Open the **Shortcuts** app → **Automation** tab\n"
            + "- Choose **Bluetooth** → pick your **car**";
        TelegramMarkdownRenderer.ToHtml(md).ShouldBe(
            "<b>2. iPhone Shortcuts automation (most reliable phone-side fix)</b>\n"
            + "• Open the <b>Shortcuts</b> app → <b>Automation</b> tab\n"
            + "• Choose <b>Bluetooth</b> → pick your <b>car</b>");
        await Task.CompletedTask;
    }
}
