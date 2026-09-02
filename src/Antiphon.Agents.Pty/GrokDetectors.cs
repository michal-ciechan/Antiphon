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

/// <summary>
/// Grok Build 1.0.13's OAuth device-approval / welcome sign-in screens. An unauthenticated
/// <c>GROK_HOME</c> parks here after quiet-period ready detection, so the brief is typed into
/// a screen whose only bound key is <c>ctrl+q</c> (CARD-0324). Detected from the rendered
/// screen only — this detector never answers, so a raw-buffer arm can only add false positives.
/// Evaluated only inside <c>WaitForReadyAsync</c> (launch preamble).
/// </summary>
public static class GrokSignInPromptDetector
{
    public static bool IsVisibleOnScreen(string? renderedScreen)
    {
        if (string.IsNullOrEmpty(renderedScreen))
            return false;

        var compact = Regex.Replace(renderedScreen, @"\s+", "", RegexOptions.CultureInvariant)
            .ToLowerInvariant();
        return compact.Contains("approveinyourbrowsertofinishsigningin")
            || compact.Contains("waitingforapproval")
            || compact.Contains("makesureyourbrowsershowsthiscode")
            || compact.Contains("pasteyourtokenhere")
            || compact.Contains("openthisurlinyourbrowsertoapprove")
            || compact.Contains("couldnotopenabrowser")
            || compact.Contains("signintogrok")
            || (compact.Contains("loginwith") && compact.Contains("ctrl+q"));
    }

    /// <summary>
    /// The durable launch-block reason: names the store and the <c>grok login</c> remedy.
    /// </summary>
    public static string BlockReason(string grokHome) =>
        "ProviderSignInRequired: Grok opened on its sign-in screen (\"Approve in your browser to finish "
        + "signing in\" / \"Paste your token here\") — the credential store "
        + Path.Combine(grokHome, "auth.json")
        + " has no usable session. Nothing was typed into it. Run `grok login` (or "
        + "`grok login --device-auth` on a headless host) as the Windows user that runs the "
        + "session-runner, then re-dispatch. Every Grok pool launch on this machine will fail "
        + "the same way until then.";
}
