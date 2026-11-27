using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Review.Workflows;

internal sealed partial class ReviewSummaryTemplate
{
    public const int MaxTemplateBytes = 8 * 1024;

    private readonly string _text;
    private readonly IReadOnlyList<Match> _slots;

    private ReviewSummaryTemplate(string text, IReadOnlyList<Match> slots)
    {
        _text = text;
        _slots = slots;
    }

    public static bool TryParse(object? value, ReviewPublishConfiguration? configuration, out ReviewSummaryTemplate? template)
    {
        template = null;
        if (value is not string text || Encoding.UTF8.GetByteCount(text) > MaxTemplateBytes)
            return false;

        var slots = Slot().Matches(text).ToList();
        var position = 0;
        var coverage = 0;
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
                coverage++;
        }

        if (coverage != 1 || HasDelimiters(text.AsSpan(position)))
            return false;

        template = new(text, slots);
        return true;
    }

    public bool TryRender(Func<string, string> resolve, int maxBytes, out string body)
    {
        var result = new StringBuilder();
        var position = 0;
        var bytes = 0;
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
            result.Append(literal).Append(value);
            position = slot.Index + slot.Length;
        }

        var tail = _text.AsSpan(position);
        if (bytes + Encoding.UTF8.GetByteCount(tail) > maxBytes)
        {
            body = "";
            return false;
        }
        body = result.Append(tail).ToString();
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

    [GeneratedRegex(@"\{\{(?<name>[A-Za-z.]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Slot();
}
