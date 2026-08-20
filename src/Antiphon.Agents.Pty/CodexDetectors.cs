using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Waits until the Codex TUI has settled enough to accept input.
/// Codex does not currently expose a stable ready token through the PTY, so
/// readiness is visible child output followed by a quiet terminal window
/// (CARD-0052: empty or title-only snapshots are not ready).
/// </summary>
public sealed class CodexReadyDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);

    public Task<bool> WaitAsync(
        PtyAgentRunner runner,
        Func<CancellationToken, Task>? observeStartupAsync = null,
        CancellationToken ct = default)
        => runner.WaitForQuietAfterVisibleAsync(QuietPeriod, MaxWait, ct, observeStartupAsync);
}

/// <summary>
/// The Codex TUI's turn-in-progress indicator — the only positive screen signal a live turn
/// produces, and the gate the screen fallback now hangs on (CARD-0108 S2).
///
/// <para><b>MEASURED 2026-08-20, codex-cli 0.147.0, modern ConPTY:</b> while a turn runs the TUI
/// repaints a line of the form <c>• Working (12s • esc to interrupt)</c> at roughly 1 Hz (the
/// bullet alternates •/◦ and the elapsed count climbs), and the line LEAVES the screen the moment
/// the turn completes. Codex renders no "Worked for Ns" done-line the way Grok does — a completed
/// turn's screen carries the answer and a fresh composer, nothing else — so the indicator's
/// disappearance is the whole screen signal. The OSC-0 title also carries a braille spinner while
/// busy, but it spins during MCP startup with no turn running: that is "busy", not "turn running",
/// and it is deliberately not used here.</para>
///
/// <para>Both halves of the line are matched, on one row, because either alone is text a model
/// could plausibly emit into its own answer.</para>
/// </summary>
public static class CodexWorkingIndicator
{
    public const string Prefix = "Working (";
    public const string Suffix = "esc to interrupt)";

    public static bool IsVisible(string? renderedScreen)
    {
        if (string.IsNullOrEmpty(renderedScreen))
            return false;

        foreach (var line in renderedScreen.Split('\n'))
        {
            var open = line.IndexOf(Prefix, StringComparison.Ordinal);
            if (open >= 0 && line.IndexOf(Suffix, open + Prefix.Length, StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }
}

/// <summary>
/// The screen half of Codex turn-completion, as a per-poll state machine so the two adapters share
/// one rule: the in-process <see cref="CodexDoneDetector"/> drives it in a loop of its own, and
/// <c>RunnerCodexAdapter</c> drives it inside the loop that also polls the transcript.
///
/// <para><b>CARD-0108: quiet alone is not done, and for Codex it is actively dangerous.</b> The
/// old rule was bare <c>WaitForQuietAfterVisible(3s)</c>. Measured on 2026-08-20, the production
/// submit path stranded the prompt in a silent composer 6 times out of 6 — no turn ran, the TUI
/// emitted nothing for at least 100 s, and this detector certified that non-turn as complete at
/// ~3.2 s, whereupon <see cref="CodexResponseAnalyzer.ExtractResponse"/> scraped the STATUS BAR as
/// the response (the status bar's model and cwd, verbatim), the shape CARD-0108 records. Quiet
/// now counts only AFTER the measured lifecycle: the <see cref="CodexWorkingIndicator"/> line
/// appeared and then went. A session where it never appears never completes here — it reaches max
/// wait and reports <c>false</c>, which for the stranded shape is the truth.</para>
///
/// <para>Widening the quiet period cannot substitute for this and must not be tried: the stranded
/// shape is silent forever, so no margin reaches it.</para>
/// </summary>
public sealed class CodexTurnScreenTracker(TimeSpan quietPeriod)
{
    private bool _primed;
    private bool _indicatorSeen;
    private bool _indicatorGone;
    private long _lastMark;
    private DateTime _lastChangeUtc;

    /// <summary>True once a live turn has been positively observed on screen.</summary>
    public bool IndicatorSeen => _indicatorSeen;

    /// <summary>True once an observed indicator has left the screen again.</summary>
    public bool IndicatorGone => _indicatorGone;

    /// <param name="renderedScreen">Rendered screen (the indicator lives here, not in raw output).</param>
    /// <param name="rawOutput">Raw pty output, for the CARD-0052 empty-snapshot guard.</param>
    /// <param name="outputMark">Monotonic output progress marker (runner: output sequence; in-proc: buffer length).</param>
    /// <returns>True when this poll completes the turn on screen evidence alone.</returns>
    public bool Observe(string? renderedScreen, string? rawOutput, long outputMark, DateTime nowUtc)
    {
        if (!_primed)
        {
            _primed = true;
            _lastMark = outputMark;
            _lastChangeUtc = nowUtc;
        }

        if (CodexWorkingIndicator.IsVisible(renderedScreen))
        {
            _indicatorSeen = true;
            _indicatorGone = false;
        }
        else if (_indicatorSeen)
        {
            _indicatorGone = true;
        }

        if (outputMark != _lastMark)
        {
            _lastMark = outputMark;
            _lastChangeUtc = nowUtc;
            return false;
        }

        return _indicatorSeen
            && _indicatorGone
            && nowUtc - _lastChangeUtc >= quietPeriod
            && VisiblePtyOutput.HasVisibleOutput(rawOutput);
    }
}

/// <summary>
/// Codex turn completion from the screen alone — the FALLBACK for a session with no transcript.
/// The primary signal is the tailed <c>TurnEnd</c> row Codex writes as <c>event_msg/task_complete</c>
/// (<c>RunnerCodexAdapter.WaitForTurnCompleteAsync</c>, CARD-0108 S2); this class exists for the
/// sessions that have no such rows, and its contract is now the indicator lifecycle described on
/// <see cref="CodexTurnScreenTracker"/> rather than bare quiet.
/// </summary>
public sealed class CodexDoneDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => WaitAsync(
            _ => Task.FromResult(runner.SnapshotScreen()),
            _ => Task.FromResult(runner.SnapshotText()),
            ct);

    /// <summary>Delegate-based so a runner-mediated terminal fits the same rule without a PTY.</summary>
    public async Task<bool> WaitAsync(
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<CancellationToken, Task<string>> snapshotRaw,
        CancellationToken ct = default)
    {
        var tracker = new CodexTurnScreenTracker(QuietPeriod);
        var deadline = DateTime.UtcNow + MaxWait;

        while (DateTime.UtcNow < deadline)
        {
            var raw = await snapshotRaw(ct);
            if (tracker.Observe(await snapshotScreen(ct), raw, raw.Length, DateTime.UtcNow))
                return true;

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }
}

public static class CodexResponseAnalyzer
{
    private static readonly Regex BlankLineRun =
        new(@"\n{3,}", RegexOptions.Compiled);

    public static bool IsAskingQuestion(string? rawSnapshot, string? prompt = null) =>
        ExtractResponse(rawSnapshot, prompt).Contains('?');

    public static string ExtractResponse(string? rawSnapshot, string? prompt = null)
    {
        var clean = AnsiStripper.Clean(rawSnapshot) ?? "";
        clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            var echoEnd = FindPromptEchoEnd(clean, prompt);
            if (echoEnd >= 0)
                clean = clean[echoEnd..];
        }

        clean = BlankLineRun.Replace(clean, "\n\n");
        return clean.Trim();
    }

    /// <summary>
    /// Offset just past the echoed prompt, or -1 when no echo is present.
    /// <para>
    /// The terminal hard-wraps the echo at the window width, so a prompt whose echo crosses
    /// the right margin is NOT literally present in the snapshot — a newline sits inside it.
    /// Matching literally only, the echo survived into the response, which both leaked the
    /// user's own prompt into <c>ResponseText</c> and reported any '?' the prompt contained as
    /// the agent asking a question (which parks a channel-bound session waiting on a human).
    /// </para>
    /// <para>
    /// Wrapping is a function of the echo's column offset, so ordinary long prompts trip this
    /// in production. <c>CodexAdapterLocalShellTests</c> only saw it when cmd's prompt prefix —
    /// the cwd — pushed the echo past the margin, so it presented as depending on where the
    /// repo was checked out: green from C:\src\Antiphon, red from a deep worktree path.
    /// </para>
    /// </summary>
    private static int FindPromptEchoEnd(string clean, string prompt)
    {
        var normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');

        var literal = clean.IndexOf(normalized, StringComparison.Ordinal);
        if (literal >= 0)
            return literal + normalized.Length;

        // Project the snapshot onto its newline-free form, remembering where each kept
        // character came from, so a match can be mapped back to an offset in the original.
        // Only newlines are dropped: ConPTY wraps by inserting a break at the margin and
        // does not pad or re-flow, so every other character of the echo survives verbatim.
        var flat = new StringBuilder(clean.Length);
        var origin = new int[clean.Length];
        for (var i = 0; i < clean.Length; i++)
        {
            if (clean[i] == '\n') continue;
            origin[flat.Length] = i;
            flat.Append(clean[i]);
        }

        var flatPrompt = normalized.Replace("\n", "");
        if (flatPrompt.Length == 0)
            return -1;

        var match = flat.ToString().IndexOf(flatPrompt, StringComparison.Ordinal);
        if (match < 0)
            return -1;

        return origin[match + flatPrompt.Length - 1] + 1;
    }
}

public static class CodexTrustPromptDetector
{
    public static bool IsVisible(string? rawSnapshot, string? renderedScreen = null)
    {
        var text = $"{AnsiStripper.Clean(rawSnapshot) ?? ""}\n{renderedScreen ?? ""}";
        var compact = Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant)
            .ToLowerInvariant();

        return compact.Contains("doyoutrustthecontentsofthisdirectory")
            && compact.Contains("yes,continue");
    }
}
