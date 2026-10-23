namespace Antiphon.Agents.Pty;

public enum ClaudeDialogState { Unknown, Clear, Blocked }

/// <summary>Read-only screen evidence; never carries an affirmative key or command text.</summary>
public sealed record ClaudeDialogObservation(
    ClaudeDialogState State,
    ClaudeBlockingPromptKind Kind = ClaudeBlockingPromptKind.None,
    bool? CanCancel = null)
{
    public static ClaudeDialogObservation Observe(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen) || screen.Length > 65_536)
            return new(ClaudeDialogState.Unknown);
        var rows = screen.ReplaceLineEndings("\n").Split('\n');
        if (rows.Length > 512) return new(ClaudeDialogState.Unknown);

        // A readable bottom composer excludes a dialog beneath it. Text in the
        // conversation or in the draft is not an active choice menu.
        if (ClaudeScreen.TryReadComposer(screen, out _)) return new(ClaudeDialogState.Clear);
        var last = rows.LastOrDefault(r => !string.IsNullOrWhiteSpace(r))?.Trim() ?? "";
        if (last.Length > 0 && "✻✽✶✳✢·*".Contains(last[0])
            && ClaudeScreen.IsWorking(last))
            return new(ClaudeDialogState.Clear);

        var prompt = ClaudeBlockingPromptDetector.Detect(screen);
        if (prompt is null) return new(ClaudeDialogState.Unknown);
        // With no live bottom composer, recognized choices with an active footer
        // or a final numbered answer are blocking. A partial redraw stays Unknown.
        var footer = string.Concat(last.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var bottomChoice = last.Trim('│', ' ', '❯', '>').StartsWith("2.", StringComparison.Ordinal)
            || last.All(c => char.IsWhiteSpace(c) || "╰╯─".Contains(c))
            || prompt.Kind == ClaudeBlockingPromptKind.EffortChoice
                && last.Trim('│', ' ', '❯', '>').StartsWith("Switch ", StringComparison.OrdinalIgnoreCase);
        if (!bottomChoice && !footer.Contains("enterto") && !footer.Contains("escto")
            && !footer.Contains("spacetoselect")) return new(ClaudeDialogState.Unknown);
        return new(ClaudeDialogState.Blocked, prompt.Kind,
            footer.Contains("esctocancel") ? true : null);
    }
}
