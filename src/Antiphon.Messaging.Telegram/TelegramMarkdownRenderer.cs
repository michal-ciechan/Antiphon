using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Messaging.Telegram;

/// <summary>
/// Renders agent-style Markdown to Telegram Bot API HTML (<c>parse_mode=HTML</c>).
///
/// HTML is deliberately chosen over MarkdownV2: MarkdownV2 requires escaping 18 punctuation
/// characters in ALL prose (a single unescaped '.' or '-' rejects the whole message), while HTML
/// only needs <c>&amp;</c>/<c>&lt;</c>/<c>&gt;</c> escaping and is what Telegram itself recommends
/// for programmatic senders. The full mapping, supported syntax, and fallback behaviour are
/// documented in <c>docs/telegram.md</c>.
///
/// The renderer is intentionally line-based and regex-driven (not a full CommonMark parser):
/// predictable output for the Markdown agents actually emit, and any input it can't improve is
/// left as readable escaped text — never dropped. If Telegram still rejects the rendered HTML,
/// <see cref="TelegramChannelAdapter"/> falls back to sending the original text plain.
/// </summary>
public static partial class TelegramMarkdownRenderer
{
    public static string ToHtml(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var output = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Fenced code block: ```lang ... ``` -> <pre><code class="language-lang">, or bare <pre>.
            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
                var lang = fence.Groups["lang"].Value.Trim();
                var body = new StringBuilder();
                var closed = false;
                for (i++; i < lines.Length; i++)
                {
                    if (FenceRegex().IsMatch(lines[i]))
                    {
                        closed = true;
                        break;
                    }
                    body.Append(Escape(lines[i])).Append('\n');
                }
                if (body.Length > 0)
                    body.Length--; // drop the trailing newline inside the block

                output.Append(lang.Length > 0
                    ? $"<pre><code class=\"language-{Escape(lang)}\">{body}</code></pre>"
                    : $"<pre>{body}</pre>");
                output.Append('\n');
                if (!closed)
                    break; // unterminated fence swallowed the rest — nothing left to render
                continue;
            }

            // Table block: consecutive |-rows kept aligned in monospace (Telegram has no tables).
            if (IsTableRow(line) && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var rows = new List<string>();
                for (; i < lines.Length && IsTableRow(lines[i]); i++)
                    if (!IsTableSeparator(lines[i]))
                        rows.Add(Escape(lines[i]));
                i--;
                output.Append("<pre>").Append(string.Join('\n', rows)).Append("</pre>\n");
                continue;
            }

            // Blockquote run: consecutive '>' lines -> one <blockquote>.
            if (QuoteRegex().IsMatch(line))
            {
                var inner = new List<string>();
                for (; i < lines.Length && QuoteRegex().IsMatch(lines[i]); i++)
                    inner.Add(RenderInline(QuoteRegex().Replace(lines[i], "", 1)));
                i--;
                output.Append("<blockquote>").Append(string.Join('\n', inner)).Append("</blockquote>\n");
                continue;
            }

            output.Append(RenderLine(line)).Append('\n');
        }

        if (output.Length > 0)
            output.Length--; // drop the final newline added by the loop
        return output.ToString();
    }

    private static string RenderLine(string line)
    {
        // Heading -> bold (Telegram has no heading entity).
        var heading = HeadingRegex().Match(line);
        if (heading.Success)
            return $"<b>{RenderInline(heading.Groups["text"].Value)}</b>";

        // Horizontal rule -> a visual divider.
        if (HrRegex().IsMatch(line))
            return "———";

        // Bullet list item -> '•' (indent preserved for nesting).
        var bullet = BulletRegex().Match(line);
        if (bullet.Success)
            return $"{bullet.Groups["indent"].Value}• {RenderInline(bullet.Groups["text"].Value)}";

        // Ordered list item: keep the number, render the text.
        var ordered = OrderedRegex().Match(line);
        if (ordered.Success)
            return $"{ordered.Groups["indent"].Value}{ordered.Groups["num"].Value}. {RenderInline(ordered.Groups["text"].Value)}";

        return RenderInline(line);
    }

    /// <summary>
    /// Inline formatting. Code spans are carved out first (their content gets no further
    /// processing), everything else is HTML-escaped and then converted span-regex by span-regex.
    /// </summary>
    private static string RenderInline(string text)
    {
        var result = new StringBuilder();
        var parts = CodeSpanRegex().Split(text);
        // Split with a capturing group alternates literal / code-content parts.
        for (var p = 0; p < parts.Length; p++)
        {
            if (p % 2 == 1)
                result.Append("<code>").Append(Escape(parts[p])).Append("</code>");
            else
                result.Append(RenderSpans(Escape(parts[p])));
        }
        return result.ToString();
    }

    private static string RenderSpans(string escaped)
    {
        var s = LinkRegex().Replace(escaped, "<a href=\"$2\">$1</a>");
        s = BoldRegex().Replace(s, "<b>$1</b>");
        s = UnderlineRegex().Replace(s, "<u>$1</u>");
        s = ItalicStarRegex().Replace(s, "<i>$1</i>");
        s = ItalicUnderscoreRegex().Replace(s, "<i>$1</i>");
        s = StrikeRegex().Replace(s, "<s>$1</s>");
        s = SpoilerRegex().Replace(s, "<tg-spoiler>$1</tg-spoiler>");
        return s;
    }

    // '&' first, then the angle brackets; '"' too so URLs are safe inside href="...".
    private static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static bool IsTableRow(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith('|') && t.Length > 1;
    }

    private static bool IsTableSeparator(string line) => TableSeparatorRegex().IsMatch(line);

    [GeneratedRegex(@"^\s*```\s*(?<lang>[\w+#.-]*)\s*$")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^\s*\|[\s|:-]+\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"^\s{0,3}>\s?")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(?<text>.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s{0,3}([-*_])\s*(\1\s*){2,}$")]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+(?<text>.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(?<indent>\s*)(?<num>\d{1,4})[.)]\s+(?<text>.*)$")]
    private static partial Regex OrderedRegex();

    // Single-backtick spans on one line; the capture makes Regex.Split alternate text/code parts.
    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex CodeSpanRegex();

    [GeneratedRegex(@"\[([^\]\n]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*(?!\s)(.+?)(?<!\s)\*\*")]
    private static partial Regex BoldRegex();

    // '__' maps to UNDERLINE (Telegram's own markdown convention), documented in docs/telegram.md.
    [GeneratedRegex(@"__(?!\s)(.+?)(?<!\s)__")]
    private static partial Regex UnderlineRegex();

    // Word-boundary guards so arithmetic (2*3*4) and snake_case identifiers survive untouched.
    [GeneratedRegex(@"(?<![\w*])\*(?![\s*])([^*\n]+?)(?<![\s*])\*(?![\w*])")]
    private static partial Regex ItalicStarRegex();

    [GeneratedRegex(@"(?<![\w_])_(?![\s_])([^_\n]+?)(?<![\s_])_(?![\w_])")]
    private static partial Regex ItalicUnderscoreRegex();

    [GeneratedRegex(@"~~(?!\s)(.+?)(?<!\s)~~")]
    private static partial Regex StrikeRegex();

    // '||spoiler||' — Telegram's spoiler syntax, rendered as <tg-spoiler>.
    [GeneratedRegex(@"\|\|(?!\s)(.+?)(?<!\s)\|\|")]
    private static partial Regex SpoilerRegex();
}
