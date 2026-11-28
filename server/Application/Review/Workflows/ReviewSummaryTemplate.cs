using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Antiphon.Server.Application.Review.Workflows;

internal sealed partial class ReviewSummaryTemplate
{
    public const int MaxTemplateBytes = 8 * 1024;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UsePreciseSourceLocation()
        .Build();

    private readonly string _text;
    private readonly IReadOnlyList<Match> _slots;
    private readonly Match _coverage;

    private ReviewSummaryTemplate(string text, IReadOnlyList<Match> slots, Match coverage)
    {
        _text = text;
        _slots = slots;
        _coverage = coverage;
    }

    public static bool TryParse(object? value, ReviewPublishConfiguration? configuration, out ReviewSummaryTemplate? template)
    {
        template = null;
        if (value is not string raw)
            return false;
        var text = raw.ReplaceLineEndings("\n");
        if (Encoding.UTF8.GetByteCount(text) > MaxTemplateBytes)
            return false;

        var slots = Slot().Matches(text).ToList();
        var position = 0;
        Match? coverage = null;
        foreach (var slot in slots)
        {
            var name = slot.Groups["name"].Value;
            if (HasDelimiters(text.AsSpan(position, slot.Index - position)))
                return false;
            position = slot.Index + slot.Length;

            if (name is "run.title" or "run.subject" or "round" or "iteration")
                continue;
            if (!IsSection(name, configuration) || !StandsAlone(text, slot))
                return false;
            if (name is "defaultBody" or "findings")
            {
                if (coverage is not null)
                    return false;
                coverage = slot;
            }
        }

        if (coverage is null || HasDelimiters(text.AsSpan(position)) || !InVisibleParagraph(text, coverage.Index, coverage.Value))
            return false;

        template = new(text, slots, coverage);
        return true;
    }

    public bool TryRender(Func<string, string> resolve, int maxBytes, out string body)
    {
        var result = new StringBuilder();
        var position = 0;
        var bytes = 0;
        var coverageStart = 0;
        var coverageLength = 0;
        foreach (var slot in _slots)
        {
            var literal = _text.AsSpan(position, slot.Index - position);
            var value = resolve(slot.Groups["name"].Value);
            bytes += Encoding.UTF8.GetByteCount(literal) + Encoding.UTF8.GetByteCount(value);
            if (bytes > maxBytes)
            {
                body = "";
                return false;
            }
            result.Append(literal);
            if (slot == _coverage)
            {
                coverageStart = result.Length;
                coverageLength = value.Length;
            }
            result.Append(value);
            position = slot.Index + slot.Length;
        }

        var tail = _text.AsSpan(position);
        if (bytes + Encoding.UTF8.GetByteCount(tail) > maxBytes)
        {
            body = "";
            return false;
        }
        body = result.Append(tail).ToString();
        var prefix = body.AsSpan(0, coverageStart);
        if (prefix.Contains('<'))
        {
            // Substitutions can complete authored markup, even when values are escaped.
            var probe = string.Concat(prefix, _coverage.Value, body.AsSpan(coverageStart + coverageLength));
            if (!InVisibleParagraph(probe, coverageStart, _coverage.Value))
            {
                body = "";
                return false;
            }
        }
        return true;
    }

    private static bool HasDelimiters(ReadOnlySpan<char> segment) =>
        segment.Contains("{{", StringComparison.Ordinal) || segment.Contains("}}", StringComparison.Ordinal);

    private static bool IsSection(string name, ReviewPublishConfiguration? configuration) => name switch
    {
        "defaultBody" or "findings" or "verification" or "belowBar" or "blastRadius" or "categories" => true,
        "categorisation" => configuration?.Categorise is not null,
        "selectedChecks" => configuration?.Select is not null,
        _ => false,
    };

    private static bool StandsAlone(string text, Match slot)
    {
        var first = text.LastIndexOf('\n', slot.Index) + 1;
        var last = text.IndexOf('\n', slot.Index + slot.Length);
        return text.AsSpan(first, (last < 0 ? text.Length : last) - first).Trim().SequenceEqual(slot.Value);
    }

    private static bool InVisibleParagraph(string text, int slotStart, string slot)
    {
        var document = Markdown.Parse(text, Pipeline);
        var topLevelText = document.OfType<ParagraphBlock>().Any(paragraph =>
            paragraph.Inline is { } inlines && inlines.OfType<LiteralInline>().Any(inline =>
                inline.Span.Start <= slotStart && inline.Span.End >= slotStart + slot.Length - 1));
        return topLevelText && (!text.AsSpan(0, slotStart).Contains('<') || OutsideHtmlContainers(document, slot));
    }

    private static bool OutsideHtmlContainers(MarkdownDocument document, string slot)
    {
        // Raw opening and closing tags can render around a sibling Markdown paragraph.
        // Check the emitted tags so escapes, code and image alt text cannot supply closures.
        var html = Markdown.ToHtml(document, Pipeline);
        var slotStart = html.IndexOf(slot, StringComparison.Ordinal);
        if (slotStart < 0 || html.LastIndexOf(slot, StringComparison.Ordinal) != slotStart)
            return false;

        var openTags = new Stack<string>();
        var position = 0;
        while ((position = html.IndexOf('<', position, slotStart - position)) >= 0)
        {
            var token = HtmlToken().Match(html, position);
            if (!token.Success || token.Index + token.Length > slotStart)
                return false;
            position += token.Length;

            var tag = token.Groups["tag"].Value.ToLowerInvariant();
            if (token.Groups["close"].Success)
            {
                if (!openTags.TryPop(out var opening) || opening != tag)
                    return false;
            }
            else if (tag is "a" or "b" or "blockquote" or "code" or "del" or "details" or "div" or "em"
                or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "i" or "ins" or "kbd" or "li"
                or "mark" or "ol" or "p" or "pre" or "s" or "span" or "strong" or "sub" or "summary" or "sup"
                or "table" or "tbody" or "td" or "th" or "thead" or "tr" or "ul")
                openTags.Push(tag);
            else if (tag is not ("" or "br" or "hr" or "img" or "wbr"))
                return false;
        }

        // Require only the paragraph Markdig generated, regardless of authored attributes
        // or which elements the host sanitizer keeps. Unknown or malformed tags fail closed.
        return openTags.Count == 1 && openTags.Peek() == "p";
    }

    [GeneratedRegex(
        """
        \G(?:
            <!-- (?! > | -> ) (?:(?! -- )[\s\S])* -->
          | < (?<close>/) (?<tag>[A-Za-z][A-Za-z0-9]*) [\t\n\f\r\x20]* >
          | < (?<tag>[A-Za-z][A-Za-z0-9]*)
              (?> [\t\n\f\r\x20]+ [A-Za-z_:][A-Za-z0-9_.:-]*
                  (?: [\t\n\f\r\x20]* = [\t\n\f\r\x20]*
                      (?:"[^"]*" | '[^']*' | [^\t\n\f\r\x20"'=<>`]+)
                  )?
              )*
              [\t\n\f\r\x20]* /? >
        )
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlToken();

    [GeneratedRegex(@"\{\{(?<name>[A-Za-z.]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Slot();
}
