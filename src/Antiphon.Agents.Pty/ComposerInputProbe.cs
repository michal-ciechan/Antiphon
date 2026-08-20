namespace Antiphon.Agents.Pty;

/// <summary>How a <see cref="ComposerInputProbe"/> round trip ended.</summary>
public enum ComposerProbeOutcome
{
    /// <summary>The token rendered and was cleared again: the TUI is reading input.</summary>
    Responsive = 0,

    /// <summary>The token never rendered inside the budget — the TUI is painted but deaf.</summary>
    NeverAppeared = 1,

    /// <summary>
    /// The token rendered but the composer could not be emptied again. A composer we cannot clear
    /// must not have a boot prompt appended to it, so this is a failure, not a warning.
    /// </summary>
    NeverCleared = 2,
}

/// <param name="Timeout">
/// Total budget for the token to render. 90s by default: larger than the 48-200s dead zone's
/// measured lower end and far larger than the 15s evidence window that sat entirely inside it
/// (CARD-0103). This is a bound on a broken launch, not a cost a healthy one pays — the control
/// measurement puts a healthy round trip at 0.74s.
/// </param>
/// <param name="PollInterval">Screen polling cadence while waiting for the token.</param>
/// <param name="RetypeInterval">
/// How long a written token may go unseen before it is written again. Belt against a silent drop
/// that is NOT the measured buffering shape; the measured one needs no re-type, since ConPTY
/// retains buffered input and drains it on wake.
/// </param>
/// <param name="MaxWrites">Total token writes inside the budget, including the first.</param>
/// <param name="ClearTimeout">How long the composer has to lose the token after Ctrl+U.</param>
/// <param name="ClearAttempts">Total Ctrl+U presses inside <paramref name="ClearTimeout"/>.</param>
public sealed record ComposerProbeOptions(
    TimeSpan Timeout,
    TimeSpan PollInterval,
    TimeSpan RetypeInterval,
    int MaxWrites = 3,
    TimeSpan ClearTimeout = default,
    int ClearAttempts = 3)
{
    /// <summary>The production shape, in the units the settings carry.</summary>
    public static ComposerProbeOptions FromMilliseconds(
        int timeoutMs, int pollIntervalMs, int retypeIntervalMs, int clearTimeoutMs, int maxWrites = 3) =>
        new(TimeSpan.FromMilliseconds(timeoutMs),
            TimeSpan.FromMilliseconds(Math.Max(1, pollIntervalMs)),
            TimeSpan.FromMilliseconds(Math.Max(1, retypeIntervalMs)),
            Math.Max(1, maxWrites),
            TimeSpan.FromMilliseconds(Math.Max(1, clearTimeoutMs)));

    public TimeSpan EffectiveClearTimeout =>
        ClearTimeout > TimeSpan.Zero ? ClearTimeout : TimeSpan.FromSeconds(10);
}

/// <param name="Outcome">What happened.</param>
/// <param name="Token">The token written, so a log line and a screen capture can be tied together.</param>
/// <param name="Writes">How many times the token was written (1 on a healthy launch).</param>
/// <param name="Elapsed">Wall clock from the first write to the verdict.</param>
public readonly record struct ComposerProbeResult(
    ComposerProbeOutcome Outcome, string Token, int Writes, TimeSpan Elapsed)
{
    public bool Responsive => Outcome == ComposerProbeOutcome.Responsive;
}

/// <summary>
/// Proves the agent's TUI is <b>reading input</b>, not merely painted — the fourth rung of a ladder
/// this codebase has climbed one live miss at a time (CARD-0103).
///
/// <para>CARD-0052 proved the child produced visible output. CARD-0047 proved no trust modal is
/// standing. CARD-0048 proved the console host finished its DA1 handshake. <b>None of them prove
/// the TUI is draining stdin</b>, and on a loaded machine it is not: measured 2026-08-20, the same
/// 5 829-character body rendered in <b>48.8s</b> when sent ~2s after "ready" and <b>0.74s</b> when
/// sent 45s after it. An unresponsive TUI emits nothing, and "nothing" is exactly what a
/// quiet-period detector reads as ready. Only a round trip through the composer can tell the two
/// apart.</para>
///
/// <para>The round trip is: write a short harmless token, require it to RENDER, then clear it with
/// Ctrl+U (the kill-line keystroke <c>ClaudeHarness</c> already uses against real Claude) and
/// require it to be GONE. Ordering matters at the call site and is documented there: the probe runs
/// after the quiet wait, after the trust-dialog gate (typing into an unanswered modal was
/// CARD-0047's named hazard) and after the WebSocket floor (before it, writes are accepted and
/// silently DROPPED — the one window where a probe would time out falsely).</para>
///
/// <para>Delegate-based, like <see cref="VerifiedPromptSubmitter"/>: the runner client and the
/// in-process pty reach the same two primitives by different routes, and the answer must not fork
/// between them.</para>
/// </summary>
public static class ComposerInputProbe
{
    /// <summary>Ctrl+U — kill line. Empties the composer of a single typed line.</summary>
    public const string KillLine = "\x15";

    /// <summary>
    /// A token that cannot be mistaken for anything else the TUI acts on: letters and digits only,
    /// so it never begins with <c>/</c> (a slash command), <c>!</c> (bash) or <c>#</c> (memory), and
    /// session-derived so a screen collision is negligible and a log line is attributable.
    /// </summary>
    public static string TokenFor(Guid sessionId) => "zz" + sessionId.ToString("N")[..8];

    /// <param name="token">The probe token — see <see cref="TokenFor"/>.</param>
    /// <param name="snapshotScreen">Returns the current rendered screen.</param>
    /// <param name="write">Raw write into the terminal (no CR is ever appended — nothing is submitted).</param>
    /// <param name="log">Receives non-fatal events (a re-type, a lingering token); null to discard.</param>
    public static async Task<ComposerProbeResult> RunAsync(
        string token,
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        ComposerProbeOptions options,
        Action<string>? log,
        CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var maxWrites = Math.Max(1, options.MaxWrites);

        await write(token, ct);
        var writes = 1;
        var lastWriteAt = DateTime.UtcNow;

        var deadline = startedAt + options.Timeout;
        while (!ComposerDeliveryEvidence.FragmentIsVisible(await snapshotScreen(ct), token))
        {
            var now = DateTime.UtcNow;
            if (now >= deadline)
                return new ComposerProbeResult(
                    ComposerProbeOutcome.NeverAppeared, token, writes, now - startedAt);

            // The measured shape does NOT need this: ConPTY retains input written into a deaf TUI
            // and drains it in order on wake, so a doubled token is the normal price of the belt.
            // A doubled token still substring-matches, and the Ctrl+U below kills the whole line.
            if (writes < maxWrites && now - lastWriteAt >= options.RetypeInterval)
            {
                log?.Invoke(
                    $"Input probe token '{token}' has not rendered after "
                    + $"{(now - startedAt).TotalSeconds:F0}s; writing it again "
                    + $"(attempt {writes + 1}/{maxWrites}).");
                await write(token, ct);
                writes++;
                lastWriteAt = DateTime.UtcNow;
            }

            await Task.Delay(options.PollInterval, ct);
        }

        var cleared = await ClearAsync(token, snapshotScreen, write, options, log, ct);
        return new ComposerProbeResult(
            cleared ? ComposerProbeOutcome.Responsive : ComposerProbeOutcome.NeverCleared,
            token, writes, DateTime.UtcNow - startedAt);
    }

    private static async Task<bool> ClearAsync(
        string token,
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        ComposerProbeOptions options,
        Action<string>? log,
        CancellationToken ct)
    {
        var attempts = Math.Max(1, options.ClearAttempts);
        var clearTimeout = options.EffectiveClearTimeout;
        var deadline = DateTime.UtcNow + clearTimeout;
        var betweenPresses = clearTimeout / attempts;
        var presses = 0;
        var nextPressAt = DateTime.UtcNow;

        while (true)
        {
            var now = DateTime.UtcNow;
            if (presses < attempts && now >= nextPressAt)
            {
                if (presses > 0)
                    log?.Invoke($"Input probe token '{token}' is still on screen; pressing Ctrl+U again.");
                await write(KillLine, ct);
                presses++;
                nextPressAt = DateTime.UtcNow + betweenPresses;
            }

            await Task.Delay(options.PollInterval, ct);

            if (!ComposerDeliveryEvidence.FragmentIsVisible(await snapshotScreen(ct), token))
                return true;

            if (DateTime.UtcNow >= deadline && presses >= attempts)
                return false;
        }
    }
}
