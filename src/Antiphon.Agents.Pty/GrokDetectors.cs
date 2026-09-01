using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Grok Build 1.0.13's first-use directory-trust dialog. A brand-new cwd — including every
/// Antiphon <c>-Worktree</c> path under <c>C:\Antiphon\worktrees\card-task-*</c> — parks the TUI
/// on this modal. It is quiet, so <c>WaitForQuietAfterVisibleAsync</c> calls the session ready
/// and the brief is then typed into the dialog (CARD-0315).
/// </summary>
/// <remarks>
/// The question text is the same as Codex's, but the choices are not: Grok wants the letter
/// <c>y</c>, and both "Yes, proceed" and "No, quit" render bold, so Enter is not safe.
/// Codex's detector requires <c>yes,continue</c> and will not match this screen.
/// </remarks>
public static class GrokTrustPromptDetector
{
    public const string AffirmativeKey = "y";

    public static bool IsVisible(string? rawSnapshot, string? renderedScreen = null) =>
        CompactMatch(renderedScreen) || CompactMatch(AnsiStripper.Clean(rawSnapshot));

    /// <summary>
    /// Rendered-screen only, for "did the dialog actually leave?" after answering. Raw output
    /// keeps the question forever once printed, so it cannot tell still-waiting from already
    /// answered (the same stale-buffer trap as <see cref="ClaudeBlockingPromptDetector"/>).
    /// </summary>
    public static bool IsVisibleOnScreen(string? renderedScreen) => CompactMatch(renderedScreen);

    private static bool CompactMatch(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var compact = Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant)
            .ToLowerInvariant();
        return compact.Contains("doyoutrustthecontentsofthisdirectory")
            && compact.Contains("yes,proceed");
    }
}
