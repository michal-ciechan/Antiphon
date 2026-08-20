using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Messaging.Slack;

/// <summary>
/// Renders agent-style Markdown to Slack <c>mrkdwn</c> — the sibling of
/// <c>TelegramMarkdownRenderer</c>, which renders the same input to Telegram HTML.
///
/// Slack's flavour is close to Markdown but NOT Markdown: bold is <c>*one star*</c>, italic is
/// <c>_underscore_</c>, strike is <c>~one tilde~</c>, and links are <c>&lt;url|text&gt;</c>. Only
/// <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> need escaping. There are no headings, no tables and no
/// underline, so those degrade to the nearest thing Slack does render.
///
/// **No plain-text fallback arm exists here, and none is needed.** Telegram REJECTS a message whose
/// entities don't parse ("can't parse entities"), which is why its adapter resends the original text
/// unformatted. Slack's <c>chat.postMessage</c> has no such failure: unbalanced or nonsense mrkdwn is
/// rendered literally, never rejected. The text-related errors Slack does return — <c>msg_too_long</c>,
/// <c>no_text</c> — are not fixable by resending the same body plain. (The one parse-style error,
/// <c>invalid_blocks</c>, can only come from caller-supplied <c>RawOverrides.blocks</c>, which this
/// adapter passes through verbatim and does not try to repair.)
///
/// Like its Telegram sibling this is deliberately line-based and regex-driven, not a CommonMark
/// parser: predictable output for the Markdown agents actually emit, and anything it can't improve
/// is left as readable escaped text — never dropped.
/// </summary>
public static partial class SlackMrkdwnRenderer
{
    public static string ToMrkdwn(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var output = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Fenced code block -> Slack's triple-backtick block. Slack has no language hint, so the
            // fence's language is dropped (keeping it would render as the block's first content line).
            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
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
                    body.Length--;

                output.Append(Fence).Append('\n').Append(body).Append('\n').Append(Fence).Append('\n');
                if (!closed)
                    break;   // unterminated fence swallowed the rest — nothing left to render
                continue;
            }

            // Table block: consecutive |-rows kept aligned in a code block (Slack has no tables).
            if (IsTableRow(line) && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var rows = new List<string>();
                for (; i < lines.Length && IsTableRow(lines[i]); i++)
                    if (!IsTableSeparator(lines[i]))
                        rows.Add(Escape(lines[i]));
                i--;
                output.Append(Fence).Append('\n').Append(string.Join('\n', rows)).Append('\n').Append(Fence).Append('\n');
                continue;
            }

            output.Append(RenderLine(line)).Append('\n');
        }

        if (output.Length > 0)
            output.Length--;   // drop the final newline added by the loop
        return output.ToString();
    }

    private const string Fence = "```";

    private static string RenderLine(string line)
    {
        // Blockquote: Slack renders '>' natively, so only the inline content is converted.
        if (QuoteRegex().IsMatch(line))
            return "> " + RenderInline(QuoteRegex().Replace(line, "", 1));

        // Heading -> bold (Slack has no heading entity).
        var heading = HeadingRegex().Match(line);
        if (heading.Success)
            return $"*{RenderInline(heading.Groups["text"].Value)}*";

        if (HrRegex().IsMatch(line))
            return "———";

        var bullet = BulletRegex().Match(line);
        if (bullet.Success)
            return $"{bullet.Groups["indent"].Value}• {RenderInline(bullet.Groups["text"].Value)}";

        var ordered = OrderedRegex().Match(line);
        if (ordered.Success)
            return $"{ordered.Groups["indent"].Value}{ordered.Groups["num"].Value}. {RenderInline(ordered.Groups["text"].Value)}";

        return RenderInline(line);
    }

    /// <summary>
    /// Inline formatting. Code spans are carved out first (their content gets no further
    /// processing), everything else is escaped and then converted span-regex by span-regex.
    /// </summary>
    private static string RenderInline(string text)
    {
        var result = new StringBuilder();
        var parts = CodeSpanRegex().Split(text);
        // Split with a capturing group alternates literal / code-content parts.
        for (var p = 0; p < parts.Length; p++)
        {
            if (p % 2 == 1)
                result.Append('`').Append(Escape(parts[p])).Append('`');
            else
                result.Append(RenderSpans(Escape(parts[p])));
        }
        return result.ToString();
    }

    private static string RenderSpans(string escaped)
    {
        // Links first: <url|text> reintroduces RAW angle brackets, so it must run after Escape and
        // its output must never be escaped again.
        var s = LinkRegex().Replace(escaped, m => $"<{m.Groups[2].Value}|{m.Groups[1].Value}>");

        // ITALIC BEFORE BOLD, which is the inverse of the Telegram renderer's order and is forced by
        // mrkdwn's own syntax: Telegram's bold output is <b>…</b> and carries no '*', but Slack's is
        // '*b*' — run the italic pass afterwards and it would eat every bold span it just produced.
        // The reverse cannot happen: '**b**' never matches the italic regex (its second '*' is
        // rejected by the lookarounds), so bold survives an italic pass unharmed.
        s = ItalicStarRegex().Replace(s, "_$1_");
        s = ItalicUnderscoreRegex().Replace(s, "_$1_");

        // '**b**' and '__b__' are both CommonMark STRONG; Slack has no underline entity, so unlike
        // the Telegram renderer (which maps '__' to <u>) both land on Slack's single-star bold.
        s = BoldRegex().Replace(s, "*$1*");
        s = BoldUnderscoreRegex().Replace(s, "*$1*");

        s = StrikeRegex().Replace(s, "~$1~");
        return s;
    }

    /// <summary>The only three characters Slack requires escaping in mrkdwn text.</summary>
    public static string Escape(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

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

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex CodeSpanRegex();

    [GeneratedRegex(@"\[([^\]\n]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*(?!\s)(.+?)(?<!\s)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"__(?!\s)(.+?)(?<!\s)__")]
    private static partial Regex BoldUnderscoreRegex();

    // Word-boundary guards so arithmetic (2*3*4) and snake_case identifiers survive untouched.
    [GeneratedRegex(@"(?<![\w*])\*(?![\s*])([^*\n]+?)(?<![\s*])\*(?![\w*])")]
    private static partial Regex ItalicStarRegex();

    [GeneratedRegex(@"(?<![\w_])_(?![\s_])([^_\n]+?)(?<![\s_])_(?![\w_])")]
    private static partial Regex ItalicUnderscoreRegex();

    [GeneratedRegex(@"~~(?!\s)(.+?)(?<!\s)~~")]
    private static partial Regex StrikeRegex();
}
