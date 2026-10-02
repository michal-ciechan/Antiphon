using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>Conservative, explicit read-failure language tied to one delivered pointer.</summary>
internal static partial class TaskInputReadFailure
{
    private static readonly string[] ReadFailures =
        ["cannot read", "can't read", "cannot access", "can't access",
         "not mounted", "no such file", "does not exist", "is inaccessible"];

    public static bool Matches(string? text, string location, bool reply, bool soleInput)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !ReadFailures.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        var explicitPaths = PathPattern().Matches(text).Cast<Match>()
            .Select(m => Normalize(m.Value)).ToList();
        var target = Normalize(location);
        if (explicitPaths.Count > 0)
        {
            // The first path is the complained-about source; a later "copy to" path is a
            // proposed destination, not proof that the original input was readable.
            var source = explicitPaths[0];
            return target == source || target.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(target, StringComparison.OrdinalIgnoreCase);
        }

        return soleInput && text.Contains(reply ? "the reply file" : "the refinement file",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Replace('\\', '/')
        .Trim('\'', '"', '`', '.', ',', ';', ':', ')', '(', ']', '[')
        .ToLowerInvariant();

    public static string? LegacyLocation(string pointer)
    {
        var match = LegacyFilePattern().Match(pointer);
        return match.Success ? match.Value.Trim('\'', '"', '`', '.', ',', ';') : null;
    }

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/][^\s,;]+|\.antiphon[/\\]inbox[/\\][^\s,;]+|/api/agent-tasks/[^\s,;]+|/work/[^\s,;]+)")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/][^\s'""`]+\.md|\.antiphon[/\\]inbox[/\\][^\s'""`]+\.md)")]
    private static partial Regex LegacyFilePattern();
}
