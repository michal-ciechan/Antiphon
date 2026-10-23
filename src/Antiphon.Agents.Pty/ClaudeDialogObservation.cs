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
        var prompt = ClaudeBlockingPromptDetector.Detect(screen ?? "");
        return prompt is null ? new(ClaudeDialogState.Clear) : new(ClaudeDialogState.Blocked, prompt.Kind);
    }
}
