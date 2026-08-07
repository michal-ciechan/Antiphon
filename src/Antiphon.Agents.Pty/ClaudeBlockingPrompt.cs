using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// What a modal dialog is asking for, when the Claude TUI is sitting on one.
/// </summary>
public enum ClaudeBlockingPromptKind
{
    None = 0,

    /// <summary>"Is this a project you created or one you trust?" — shown for an unknown directory.</summary>
    TrustFolder = 1,

    /// <summary>A tool-permission request ("Do you want to proceed?" / "Allow ... to run?").</summary>
    ToolPermission = 2,

    /// <summary>Any other numbered-choice modal waiting on a keypress.</summary>
    Choice = 3,
}

/// <summary>
/// A modal the TUI is blocked on, plus how to answer it.
/// </summary>
/// <param name="Kind">What is being asked.</param>
/// <param name="Title">The most descriptive line found on screen — for logs and incidents.</param>
/// <param name="AffirmativeKey">
/// The keystroke that accepts. Numbered menus want the DIGIT, not Enter: Enter accepts whatever is
/// currently highlighted, and after a stray arrow key that may not be option 1. Sending the digit is
/// unambiguous, and Claude's menus act on it immediately.
/// </param>
public sealed record ClaudeBlockingPrompt(
    ClaudeBlockingPromptKind Kind,
    string Title,
    string AffirmativeKey);

/// <summary>
/// Detects when the Claude TUI is BLOCKED on a modal question rather than working.
///
/// This matters well beyond tests. A session sitting on a dialog produces no transcript records at
/// all: it is not mid-turn and it is not idle, so anything deriving working/idle from the transcript
/// sees a session that never finishes, and queued deliveries strand behind it — the same class of
/// failure as the interrupt-marker and local-slash-command misses.
///
/// Detection reads the RENDERED SCREEN (<see cref="PtyAgentRunner.SnapshotScreen"/>), never the
/// accumulated output buffer. The buffer keeps a dialog's text forever once printed, so a
/// buffer-based check cannot tell "still waiting" from "already answered" — and a caller that
/// retries on that stale signal keeps firing keystrokes into a live session.
/// </summary>
public static partial class ClaudeBlockingPromptDetector
{
    /// <summary>
    /// Inspect a rendered screen. Returns null when the TUI is not blocked.
    /// Pure and allocation-light: safe to poll at 50ms.
    /// </summary>
    public static ClaudeBlockingPrompt? Detect(string screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
            return null;

        // Letters+digits only, lowercased: immune to box drawing, ANSI leftovers, wrapping and the
        // variable whitespace a TUI uses to centre things.
        var compact = Compact(screen);

        // A numbered menu awaiting a keypress is the shared shape of every blocking modal. Requiring
        // it keeps ordinary prose containing the word "trust" from reading as a dialog.
        var hasChoices = compact.Contains("1yes") || compact.Contains("2no")
            || compact.Contains("entertoconfirm") || compact.Contains("esctocancel");
        if (!hasChoices)
            return null;

        if (compact.Contains("doyoutrustthisfolder")
            || compact.Contains("isthisaprojectyoucreated")
            || compact.Contains("yesitrustthisfolder")
            || (compact.Contains("accessingworkspace") && compact.Contains("quicksafetycheck")))
        {
            return new ClaudeBlockingPrompt(
                ClaudeBlockingPromptKind.TrustFolder, FindTitle(screen, "trust"), "1");
        }

        if (compact.Contains("doyouwanttoproceed")
            || compact.Contains("doyouwanttoallow")
            || compact.Contains("wantstorun")
            || compact.Contains("requestspermission"))
        {
            return new ClaudeBlockingPrompt(
                ClaudeBlockingPromptKind.ToolPermission, FindTitle(screen, "proceed"), "1");
        }

        return new ClaudeBlockingPrompt(ClaudeBlockingPromptKind.Choice, FindTitle(screen, "?"), "1");
    }

    /// <summary>True when the screen shows a modal the TUI is waiting on.</summary>
    public static bool IsBlocked(string screen) => Detect(screen) is not null;

    /// <summary>
    /// Poll until a blocking prompt appears, or the timeout elapses. Default timeout is short on
    /// purpose: a modal renders in one frame, so if it is coming it is already there — waiting
    /// longer only delays the caller.
    /// </summary>
    public static async Task<ClaudeBlockingPrompt?> WaitForAsync(
        PtyAgentRunner runner, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ClaudeBlockingPrompt? found = null;
        await runner.WaitForScreenAsync(
            screen =>
            {
                found = Detect(screen);
                return found is not null;
            },
            timeout ?? TimeSpan.FromSeconds(3),
            ct);
        return found;
    }

    /// <summary>
    /// Answer a blocking prompt and confirm the screen actually cleared.
    ///
    /// Sends the affirmative DIGIT rather than Enter (see <see cref="ClaudeBlockingPrompt"/>), then
    /// verifies. Returns false if the modal is still up, so a caller can escalate instead of
    /// carrying on into a session that is silently swallowing input.
    /// </summary>
    public static async Task<bool> TryAnswerAsync(
        PtyAgentRunner runner,
        ClaudeBlockingPrompt prompt,
        TimeSpan? settleTimeout = null,
        CancellationToken ct = default)
    {
        await runner.WriteAsync(prompt.AffirmativeKey, ct);
        return await runner.WaitForScreenAsync(
            screen => !IsBlocked(screen), settleTimeout ?? TimeSpan.FromSeconds(10), ct);
    }

    private static string Compact(string screen)
    {
        Span<char> buffer = screen.Length <= 8192 ? stackalloc char[screen.Length] : new char[screen.Length];
        var length = 0;
        foreach (var c in screen)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..length]);
    }

    /// <summary>The most informative screen line for logs — the one carrying the question.</summary>
    private static string FindTitle(string screen, string hint)
    {
        var lines = screen.ReplaceLineEndings("\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim(' ', '│', '─', '╭', '╮', '╰', '╯', '❯');
            if (line.Length < 8)
                continue;
            if (line.Contains(hint, StringComparison.OrdinalIgnoreCase) || line.Contains('?'))
                return line.Trim();
        }
        return lines.FirstOrDefault(l => l.Trim().Length > 8)?.Trim() ?? "(blocked)";
    }
}
