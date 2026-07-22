namespace Antiphon.Agents.Pty;

/// <summary>
/// Waits until the Claude TUI is ready to accept and process input.
///
/// The TUI renders its prompt (❯) quickly (~1 s), but the backend WebSocket
/// connection can take 3–7 s to establish on a cold start.  Input sent before
/// the connection is ready is accepted by the TUI but silently dropped.
///
/// Strategy: wait for a 5 s quiet window (no PTY output), then also enforce
/// <see cref="MinTotalWait"/> from when <see cref="PtyAgentRunner.StartAsync"/>
/// was called.  The minimum covers cases where startup noise clears quickly but
/// the backend is still connecting.
/// </summary>
public sealed class ClaudeReadyDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Minimum time from process start before we consider the TUI ready.
    /// Prevents sending input before the backend WebSocket finishes connecting,
    /// even when TUI startup noise clears faster than the backend.
    /// </summary>
    public TimeSpan MinTotalWait { get; init; } = TimeSpan.FromSeconds(9);

    public async Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
    {
        var quiet = await runner.WaitForQuietAsync(QuietPeriod, MaxWait, ct);
        if (!quiet) return false;

        var elapsed = DateTime.UtcNow - runner.StartedAt;
        var remaining = MinTotalWait - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return true;
    }
}

/// <summary>
/// Quiet-period heuristic: waits until the PTY has been silent for 3 s.
/// Works because Claude's spinner fires every ~65 ms while processing, so
/// a 3 s gap reliably means Claude has finished its turn.
/// Prefer <see cref="ClaudeCrunchedDetector"/> when possible — it fires
/// immediately on the definitive done signal rather than waiting 3 extra seconds.
/// </summary>
public sealed class ClaudeDoneDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(2);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForQuietAsync(QuietPeriod, MaxWait, ct);
}

/// <summary>
/// Positive done signal: detects when Claude finishes a response turn.
///
/// Two complementary signals are checked:
/// 1. OSC title reverts to ✳ (idle): the TUI always emits <c>ESC]0;✳…</c>
///    when transitioning from processing back to idle — even when the
///    "[CookingVerb] for Ns" summary text uses cursor-forward optimisation
///    that breaks a literal string search in the raw PTY buffer.
/// 2. "[CookingVerb] for Ns" pattern (e.g. "Crunched for 3s", "Sautéed for
///    17s") as a fallback for when the text IS written literally.
///
/// Important: call <see cref="PtyAgentRunner.ClearLiveBuffer"/> before each
/// <c>SendLineAsync</c> so the detector does not match the previous turn's
/// startup <c>✳</c> title or completion signal.
/// </summary>
public sealed class ClaudeCrunchedDetector
{
    private static readonly System.Text.RegularExpressions.Regex DonePattern =
        new(@" for \d+s", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The TUI sets the terminal title to "✳ <context>" when a turn completes.
    // ESC ] 0 ; ✳  — reliable even when the "for Ns" text is cursor-optimised.
    private const string IdleTitleSignal = "\x1b]0;✳";

    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(5);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForOutputAsync(
            text => text.Contains(IdleTitleSignal, StringComparison.Ordinal)
                    || DonePattern.IsMatch(text),
            MaxWait, ct);
}

/// <summary>
/// Detects a completed context compaction from the rendered/raw screen. The primary compaction
/// signal is the transcript JSONL boundary record (<c>TranscriptNormalizer</c> →
/// <c>TranscriptKinds.CompactBoundary</c>); this detector is the FALLBACK/canary surface —
/// deliberately not wired into product code, available to session health if the transcript path
/// ever goes quiet.
/// </summary>
public sealed class ClaudeCompactedDetector
{
    // PINNED-BY: ClaudeCompactionCanaryTests — real Claude renders
    // "⎿  Compacted (ctrl+o to see full summary)" after a compaction; the stable core is matched
    // so prefix chrome (⎿, indentation) and suffix changes don't break detection.
    private static readonly System.Text.RegularExpressions.Regex CompactedPattern =
        new(@"Compacted \(ctrl\+o", System.Text.RegularExpressions.RegexOptions.Compiled);

    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(4);

    public static bool Matches(string? text) =>
        text is not null && CompactedPattern.IsMatch(text);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForOutputAsync(Matches, MaxWait, ct);
}

/// <summary>
/// Analyses a raw PTY snapshot to characterise what Claude just said.
/// All methods accept the raw (un-stripped) snapshot from
/// <see cref="PtyAgentRunner.SnapshotText"/> and strip ANSI internally.
/// </summary>
public static class ClaudeResponseAnalyzer
{
    /// <summary>
    /// Returns true when the response portion of the snapshot (i.e. before the
    /// "Crunched for" summary) contains at least one question mark — a reliable
    /// signal that Claude is waiting for the user to answer before proceeding.
    /// </summary>
    public static bool IsAskingQuestion(string? rawSnapshot)
    {
        var region = ResponseRegion(rawSnapshot);
        return region.Contains('?');
    }

    /// <summary>
    /// Returns the cleaned text of the current turn's response, trimmed of
    /// spinner noise and TUI chrome.  The region ends just before the
    /// "Crunched for" summary line.
    /// </summary>
    public static string ExtractResponse(string? rawSnapshot)
        => ResponseRegion(rawSnapshot).Trim();

    private static readonly System.Text.RegularExpressions.Regex CompletionPattern =
        new(@" for \d+s", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ResponseRegion(string? rawSnapshot)
    {
        var clean = AnsiStripper.Clean(rawSnapshot) ?? "";
        var m = CompletionPattern.Match(clean);
        // Trim from just before " for Ns" so the cooking verb stays outside.
        var idx = m.Success ? m.Index : -1;
        return idx > 0 ? clean[..idx] : clean;
    }
}
