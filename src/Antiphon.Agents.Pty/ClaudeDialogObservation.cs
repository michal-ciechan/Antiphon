namespace Antiphon.Agents.Pty;

public enum ClaudeDialogState { Unknown, Clear, Blocked }

/// <summary>
/// Read-only screen evidence. It may name the slash token Enter would select, and it never
/// carries a key to send.
/// </summary>
public sealed record ClaudeDialogObservation(
    ClaudeDialogState State,
    ClaudeBlockingPromptKind Kind = ClaudeBlockingPromptKind.None,
    bool? CanCancel = null,
    string? EnterCommand = null)
{
    /// <summary>
    /// Enter submits /remote-control only when this screen is Clear and that is the command
    /// Enter would run. On a slash palette, Enter selects the highlighted token.
    /// </summary>
    public bool AllowsRemoteControlSubmit =>
        State == ClaudeDialogState.Clear
        && string.Equals(EnterCommand, "/remote-control", StringComparison.Ordinal);

    public static ClaudeDialogObservation Observe(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen) || screen.Length > 65_536)
            return new(ClaudeDialogState.Unknown);
        var rows = screen.ReplaceLineEndings("\n").Split('\n');
        if (rows.Length > 512) return new(ClaudeDialogState.Unknown);

        // A readable bottom composer excludes a dialog beneath it. A slash palette is the
        // exception: Enter selects the highlighted command, and any other dialog on the screen
        // stays blocking. Text in an ordinary draft is still not an active choice menu.
        if (ClaudeScreen.TryReadComposer(screen, out var content))
        {
            var enter = content;
            if (ClaudeScreen.TryReadHighlightedSlashCommand(screen, out var highlighted))
            {
                enter = highlighted;
                if (!string.Equals(highlighted, content, StringComparison.Ordinal))
                    return new(ClaudeDialogState.Unknown, EnterCommand: enter);
                if (ClaudeScreen.SlashPaletteCarriesAnotherDialog(screen))
                {
                    var dialog = ClaudeBlockingPromptDetector.Detect(screen);
                    return new(dialog is null ? ClaudeDialogState.Unknown : ClaudeDialogState.Blocked,
                        dialog?.Kind ?? ClaudeBlockingPromptKind.None,
                        dialog is null ? null : EscCancels(rows),
                        enter);
                }
            }
            return new(ClaudeDialogState.Clear, EnterCommand: enter);
        }
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

    private static bool? EscCancels(string[] rows)
    {
        var last = rows.LastOrDefault(r => !string.IsNullOrWhiteSpace(r))?.Trim() ?? "";
        var footer = string.Concat(last.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return footer.Contains("esctocancel", StringComparison.Ordinal) ? true : null;
    }
}
