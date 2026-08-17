using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Extracts model identifiers from <c>grok models</c> stdout. Real Grok 1.0.4 prints a prose
/// catalogue, not one identifier per line:
/// <code>
/// You are logged in with grok.com.
/// Default model: grok-4.6
/// Available models:
///   * grok-4.6 (default)
///   - grok-4.5
/// </code>
/// Wrappers and FakeGrok may also emit a bare identifier per line.
/// </summary>
public static partial class GrokModelListParser
{
    public static IReadOnlyList<string> Parse(string? standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
            return [];

        var identifiers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(standardOutput);
        while (reader.ReadLine() is { } line)
        {
            foreach (Match match in GrokModelIdRegex().Matches(line))
            {
                var identifier = match.Value;
                if (seen.Add(identifier))
                    identifiers.Add(identifier);
            }
        }

        return identifiers;
    }

    [GeneratedRegex(@"\bgrok-[A-Za-z0-9][A-Za-z0-9._-]*\b", RegexOptions.CultureInvariant)]
    private static partial Regex GrokModelIdRegex();
}
