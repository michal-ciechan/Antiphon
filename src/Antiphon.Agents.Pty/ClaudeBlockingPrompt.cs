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
/// What happened when a launch tried to get past whatever the TUI was sitting on.
/// </summary>
public enum ClaudeStartupBlockOutcome
{
    /// <summary>Nothing was blocking — the usual case.</summary>
    None = 0,

    /// <summary>A trust dialog was up, was answered, and the screen verifiably cleared.</summary>
    TrustCleared = 1,

    /// <summary>A trust dialog was up, was answered, and is STILL up. The session is unusable.</summary>
    TrustNotCleared = 2,

    /// <summary>
    /// Blocked on a modal we deliberately do not auto-answer (a tool-permission request, or any
    /// other numbered choice). Reported, never keyed into.
    /// </summary>
    NotAnswerable = 3,
}

/// <summary>The outcome plus the modal it was about, for logging and incidents.</summary>
public readonly record struct ClaudeStartupBlockResolution(
    ClaudeStartupBlockOutcome Outcome,
    ClaudeBlockingPrompt? Prompt)
{
    /// <summary>True when a keystroke was actually sent — i.e. the launch changed the TUI's state.</summary>
    public bool Answered => Outcome is ClaudeStartupBlockOutcome.TrustCleared
        or ClaudeStartupBlockOutcome.TrustNotCleared;
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
    public static Task<bool> TryAnswerAsync(
        PtyAgentRunner runner,
        ClaudeBlockingPrompt prompt,
        TimeSpan? settleTimeout = null,
        CancellationToken ct = default)
        => TryAnswerAsync(
            _ => Task.FromResult(runner.SnapshotScreen()),
            (input, token) => runner.WriteAsync(input, token),
            prompt,
            settleTimeout,
            ct);

    /// <inheritdoc cref="TryAnswerAsync(PtyAgentRunner, ClaudeBlockingPrompt, TimeSpan?, CancellationToken)"/>
    /// <remarks>
    /// Delegate form so the SERVER adapters can use it. The in-process pty and the session-runner
    /// client reach the same two primitives by different routes (a local <see cref="PtyAgentRunner"/>
    /// vs an HTTP snapshot/send-input pair), and the answering logic must not fork between them —
    /// only one of those two paths is what production actually runs.
    /// </remarks>
    public static async Task<bool> TryAnswerAsync(
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        ClaudeBlockingPrompt prompt,
        TimeSpan? settleTimeout = null,
        CancellationToken ct = default)
    {
        await write(prompt.AffirmativeKey, ct);
        return await PollScreenAsync(
            snapshotScreen, screen => !IsBlocked(screen), settleTimeout ?? TimeSpan.FromSeconds(10), ct);
    }

    /// <summary>
    /// The launch-time gate: get past a trust dialog, and ONLY a trust dialog.
    ///
    /// <para>A brand-new working directory makes Claude open on "Is this a project you created or one
    /// you trust?" and wait. Nothing downstream can see that: the TUI is perfectly quiet, so the
    /// quiet-period ready detector calls it READY, the composer never receives anything, delivery
    /// verification correctly reports no composer evidence, and the always-on kill restarts the
    /// session — into the same directory, onto the same dialog. That loop cost CARD-0047's standing
    /// check interpreter every session it ever had (2026-08-16).</para>
    ///
    /// <para><b>Only the trust prompt is answered.</b> Choosing the working directory IS the trust
    /// decision, and the operator already made it when they configured the agent; there is no other
    /// way for a headless session to get past it. A tool-permission modal is the opposite — keying
    /// "1" into one would grant a tool call nobody authorised — so those are reported and left
    /// standing, and the caller decides. For the same reason the generic
    /// <see cref="ClaudeBlockingPromptKind.Choice"/> arm is never answered here: it matches on the
    /// shape of a numbered menu, which is too weak a signal to type into a live session on.</para>
    /// </summary>
    public static async Task<ClaudeStartupBlockResolution> ClearStartupTrustPromptAsync(
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        TimeSpan? settleTimeout = null,
        CancellationToken ct = default)
    {
        // One snapshot, no waiting: a modal renders in one frame and the caller has already waited
        // for the TUI to go quiet, so if a dialog is coming it is on screen now. Polling here would
        // add its timeout to every healthy launch.
        var prompt = Detect(await snapshotScreen(ct));
        if (prompt is null)
            return new ClaudeStartupBlockResolution(ClaudeStartupBlockOutcome.None, null);

        if (prompt.Kind != ClaudeBlockingPromptKind.TrustFolder)
            return new ClaudeStartupBlockResolution(ClaudeStartupBlockOutcome.NotAnswerable, prompt);

        var cleared = await TryAnswerAsync(snapshotScreen, write, prompt, settleTimeout, ct);
        return new ClaudeStartupBlockResolution(
            cleared ? ClaudeStartupBlockOutcome.TrustCleared : ClaudeStartupBlockOutcome.TrustNotCleared,
            prompt);
    }

    private static async Task<bool> PollScreenAsync(
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(await snapshotScreen(ct)))
                return true;
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
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
