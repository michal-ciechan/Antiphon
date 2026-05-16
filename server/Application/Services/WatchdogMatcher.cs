using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

public sealed class WatchdogMatcher
{
    public WatchdogMatch? Match(string text, IReadOnlyList<WatchdogRuleSettings> rules)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var stripped = MentionScanner.StripAnsi(text);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name)
                || string.IsNullOrWhiteSpace(rule.Pattern)
                || rule.Response is null)
            {
                continue;
            }

            if (rule.IsRegex)
            {
                if (Regex.IsMatch(stripped, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return new WatchdogMatch(rule.Name, rule.Response);
            }
            else if (stripped.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new WatchdogMatch(rule.Name, rule.Response);
            }
        }

        return null;
    }
}

public sealed record WatchdogMatch(string RuleName, string Response);
