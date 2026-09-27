using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

public sealed class WatchdogMatcher
{
    public const string UnsafeRmRefusalRule = "unsafe-rm-refusal";

    /// <summary>
    /// This is deliberately a rendered Claude modal recognizer, not a search of terminal history.
    /// The footer and ordered choice rows must still be the bottom visible prompt.
    /// </summary>
    public bool IsActiveUnsafeRmApproval(string renderedScreen)
    {
        if (string.IsNullOrWhiteSpace(renderedScreen))
            return false;

        var lines = MentionScanner.StripAnsi(renderedScreen).Replace("\r", string.Empty)
            .Split('\n').Select(line => line.TrimEnd()).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length < 7
            || !Regex.IsMatch(lines[^1], @"^\s*Esc to cancel\s*·\s*Tab to amend\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^2], @"^\s*2\. No\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^3], @"^\s*❯\s*1\. Yes\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^4], @"^\s*Do you want to proceed\?\s*$", RegexOptions.CultureInvariant))
            return false;

        var question = lines.Length - 4;
        var bash = Array.FindLastIndex(lines, question - 1, question, line => line.Trim() == "Bash command");
        if (bash < 0 || question - bash > 12)
            return false;

        var warning = string.Join(" ", lines[(bash + 1)..question].Select(line => line.Trim().TrimStart('│').Trim()));
        return warning.Contains("Dangerous rm operation on possibly-empty variable path:", StringComparison.Ordinal)
            && warning.Contains("rm -rf", StringComparison.Ordinal);
    }

    public bool IsUnsafeRmApprovalCandidate(string renderedScreen)
    {
        if (string.IsNullOrWhiteSpace(renderedScreen))
            return false;
        var text = MentionScanner.StripAnsi(renderedScreen);
        return text.Contains("Dangerous rm operation on possibly-empty variable path:", StringComparison.Ordinal)
            || (text.Contains("1. Yes", StringComparison.Ordinal)
                && text.Contains("2. No", StringComparison.Ordinal));
    }

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
