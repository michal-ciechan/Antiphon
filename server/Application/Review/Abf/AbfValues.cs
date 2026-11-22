using System.Globalization;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Review.Abf;

/// <summary>A <c>lines</c> value: <c>N</c> or <c>N-M</c>.</summary>
public readonly record struct AbfLineRange(int Start, int End);

/// <summary>Value conversions (spec 7.0). Each returns the typed value and whether it was repaired.</summary>
internal static class AbfValues
{
    private static readonly Regex NumberPattern = new("^(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex LinesPattern = new("^(?<start>[0-9]+)(?:\\s*-\\s*(?<end>[0-9]+))?$", RegexOptions.CultureInvariant);
    private static readonly Regex HexPattern = new("^[0-9a-fA-F]+$", RegexOptions.CultureInvariant);

    public enum Outcome
    {
        Ok,
        Repaired,
        BadValue,
        BadEnum,
        ShortSha,
    }

    public static Outcome Convert(AbfValueType type, IReadOnlyList<string>? members, string raw, out object? value)
    {
        value = null;
        var text = raw.Trim();
        switch (type)
        {
            case AbfValueType.Text:
            case AbfValueType.Path:
                value = raw;
                return Outcome.Ok;
            case AbfValueType.Id:
                if (text.Length == 0 || text.Any(char.IsWhiteSpace))
                    return Outcome.BadValue;
                value = text;
                return Outcome.Ok;
            case AbfValueType.Enum:
                var member = members?.FirstOrDefault(m => m.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (member is null)
                    return Outcome.BadEnum;
                value = member;
                return Outcome.Ok;
            case AbfValueType.Bool:
                return ConvertBool(text, out value);
            case AbfValueType.Prob:
                var outcome = TryProbability(text, out var probability);
                value = outcome is Outcome.Ok or Outcome.Repaired ? probability : null;
                return outcome;
            case AbfValueType.Int:
                if (text.Length == 0 || !text.All(char.IsAsciiDigit) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    return Outcome.BadValue;
                value = number;
                return Outcome.Ok;
            case AbfValueType.List:
                value = SplitList(raw);
                return Outcome.Ok;
            case AbfValueType.Sha:
                if (!HexPattern.IsMatch(text))
                    return Outcome.BadValue;
                value = text.ToLowerInvariant();
                if (text.Length is 40 or 64)
                    return Outcome.Ok;
                return text.Length is >= 7 and <= 39 ? Outcome.ShortSha : Outcome.BadValue;
            case AbfValueType.Lines:
                var match = LinesPattern.Match(text);
                if (!match.Success || !int.TryParse(match.Groups["start"].Value, out var start))
                    return Outcome.BadValue;
                var end = start;
                if (match.Groups["end"].Success && !int.TryParse(match.Groups["end"].Value, out end))
                    return Outcome.BadValue;
                if (end < start)
                    return Outcome.BadValue;
                value = new AbfLineRange(start, end);
                return Outcome.Ok;
            case AbfValueType.TypeRef:
                if (!AbfTypeRef.TryParse(text, out var typeRef))
                    return Outcome.BadValue;
                value = typeRef;
                return Outcome.Ok;
            default:
                value = raw;
                return Outcome.Ok;
        }
    }

    /// <summary><c>0.85</c>, <c>.85</c>; <c>85%</c> is repaired. Outside [0, 1] is a bad value.</summary>
    public static Outcome TryProbability(string text, out double value)
    {
        value = 0;
        var repaired = false;
        var number = text.Trim();
        if (number.EndsWith('%'))
        {
            number = number[..^1].TrimEnd();
            repaired = true;
        }

        if (!NumberPattern.IsMatch(number) || !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return Outcome.BadValue;
        if (repaired)
            parsed /= 100;
        if (parsed is < 0 or > 1 || double.IsNaN(parsed))
            return Outcome.BadValue;
        value = parsed;
        return repaired ? Outcome.Repaired : Outcome.Ok;
    }

    private static Outcome ConvertBool(string text, out object? value)
    {
        value = null;
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return Outcome.Ok;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return Outcome.Ok;
        }

        if (text.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return Outcome.Repaired;
        }

        if (text.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return Outcome.Repaired;
        }

        return Outcome.BadValue;
    }

    public static List<string> SplitList(string raw) =>
        raw.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
}
