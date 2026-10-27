namespace Antiphon.Agents.Pty;

/// <summary>
/// Live composer facts for an adopted session. Known-history admission still uses
/// <see cref="ClaudeDialogObservation"/>, which classifies a working spinner as Clear.
/// </summary>
public static class AdoptedComposerEvidence
{
    public static bool IsWorking(string? screen) =>
        !string.IsNullOrEmpty(screen) && ClaudeScreen.IsWorking(screen);

    public static bool IsEmptyIdleComposer(string? screen)
    {
        if (string.IsNullOrEmpty(screen) || ClaudeScreen.IsWorking(screen)) return false;
        return ClaudeScreen.TryReadComposer(screen, out var content)
            && ClaudeScreen.ComposerContentIsEmpty(content);
    }
}
