using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>Exact, non-general exceptions to automated dialog refusal.</summary>
public static class ClaudeDialogActions
{
    public static bool IsUnsafeDeleteApproval(string screen)
    {
        if (screen.Length > 65_536) return false;
        var lines = screen.Replace("\r", "").Split('\n').Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length is < 7 or > 512
            || !Regex.IsMatch(lines[^1], @"^\s*Esc to cancel\s*·\s*Tab to amend\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^2], @"^\s*2\. No\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^3], @"^\s*❯\s*1\. Yes\s*$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(lines[^4], @"^\s*Do you want to proceed\?\s*$", RegexOptions.CultureInvariant)) return false;
        var question = lines.Length - 4;
        var bash = Array.FindLastIndex(lines, question - 1, question, l => l.Trim() == "Bash command");
        if (bash < 0 || question - bash > 12) return false;
        var warning = string.Join(" ", lines[(bash + 1)..question].Select(l => l.Trim().TrimStart('│').Trim()));
        return warning.Contains("Dangerous rm operation on possibly-empty variable path:", StringComparison.Ordinal)
            && warning.Contains("rm -rf", StringComparison.Ordinal);
    }

    public static bool AllowsTrustKey(string screen, string key)
    {
        if (ClaudeDialogObservation.Observe(screen).State != ClaudeDialogState.Blocked
            || ClaudeBlockingPromptDetector.Detect(screen) is not { Kind: ClaudeBlockingPromptKind.TrustFolder } prompt)
            return false;
        return prompt.Layout switch
        {
            ClaudeTrustDialogLayout.NumberedMenu => key == ClaudeTrustDialogKeys.LegacyDigit,
            ClaudeTrustDialogLayout.HighlightedList => key == "\r"
                ? ClaudeBlockingPromptDetector.ReadHighlight(screen) == ClaudeTrustDialogHighlight.Yes
                : ClaudeBlockingPromptDetector.ReadHighlight(screen) == ClaudeTrustDialogHighlight.No
                    && ClaudeTrustDialogKeys.HighlightNextCandidates.Contains(key),
            _ => false
        };
    }

    public static bool AllowsEffortKey(string screen, string key, ClaudeEffortOption target)
    {
        if (ClaudeDialogObservation.Observe(screen).State != ClaudeDialogState.Blocked
            || ClaudeEffortPrompt.Parse(screen) is not { } menu || menu.Highlight == ClaudeEffortOption.Unknown)
            return false;
        return key == "\r" ? menu.Highlight == target
            : menu.Highlight != target && ClaudeTrustDialogKeys.HighlightNextCandidates.Contains(key);
    }
}
