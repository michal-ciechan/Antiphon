namespace Antiphon.Agents.Pty;

/// <summary>A prompt could not be verifiably delivered into the agent's composer (see <see cref="VerifiedPromptSubmitter"/>).</summary>
public sealed class PromptDeliveryException(string message, bool composerMayHoldBody = false)
    : Exception(message)
{
    /// <summary>
    /// The composer was LOOKED AT after the failure and still shows the body — so a caller that
    /// retries by re-typing would splice a second copy onto the first (CARD-0108 S1).
    ///
    /// <para>False is the default and keeps every pre-existing caller's behaviour: for Claude and
    /// Grok this exception means <c>VerifiedPromptSubmitter</c> never saw composer evidence, which
    /// is itself proof the composer is NOT holding the body, and that is exactly what makes
    /// <c>AgentSessionService.SendBootPromptWithRetryAsync</c>'s re-type safe there. Codex's
    /// failure mode is the opposite one — a body that arrived fine and an Enter that folded — so
    /// its adapter sets this flag and the retry loop must skip the re-type when it is set.</para>
    /// </summary>
    public bool ComposerMayHoldBody { get; } = composerMayHoldBody;
}

/// <param name="EvidenceTimeout">How long typed text may take to show up on the rendered screen.</param>
/// <param name="PollInterval">Screen/output polling cadence for both verification phases.</param>
/// <param name="PostSubmitAdvanceTimeout">After the submitting Enter, the output mark must advance within this window.</param>
/// <param name="SubmitAttempts">Total Enter presses before giving up (a swallowed Enter is retried; a re-press on a submitted, empty composer is a no-op).</param>
public sealed record VerifiedSubmitOptions(
    TimeSpan EvidenceTimeout,
    TimeSpan PollInterval,
    TimeSpan PostSubmitAdvanceTimeout,
    int SubmitAttempts = 3);

/// <summary>
/// Verified prompt delivery into a Claude TUI — the boot-prompt analogue of the queue's
/// DeliverAsync (SessionMessageQueueService), sharing its exact contract:
///
/// 1. the body goes in PtyInputEncoding form (LF-normalized, bracket-wrapped when multi-line);
/// 2. the rendered screen must show <see cref="ComposerDeliveryEvidence"/> of the body BEFORE the
///    submitting Enter is sent — a wedged composer withholds the Enter so the prompt is never
///    lost into it;
/// 3. after Enter, the output mark must advance (a real submit redraws immediately); a swallowed
///    Enter — the 2026-08-08 live miss, where a relaunched agent's card prompt sat unsubmitted in
///    the composer for half an hour with nothing logged — is pressed again, and only after
///    <see cref="VerifiedSubmitOptions.SubmitAttempts"/> failures does the launch fail loudly.
///
/// Delegate-based so the runner-client and in-proc PTY terminals both fit, and tests can script
/// every phase without a terminal.
/// </summary>
public static class VerifiedPromptSubmitter
{
    // Same post-evidence pre-Enter pause semantics as SendLineAsync's production gate: the body and
    // CR must never coalesce into one chunk (the TUI folds a same-chunk CR into a literal newline
    // instead of submitting). This submitter retains its own evidence and retry logic.
    private static readonly TimeSpan PreSubmitPause = TimeSpan.FromMilliseconds(20);

    /// <param name="body">The prompt to type and submit (any line endings; encoded internally).</param>
    /// <param name="snapshotScreen">Returns the current rendered screen.</param>
    /// <param name="outputMark">Monotonic output progress marker (runner: output sequence; in-proc: buffer length).</param>
    /// <param name="write">Raw write into the terminal (no CR appended).</param>
    /// <param name="log">Receives non-fatal delivery events (an Enter retry); null to discard.</param>
    /// <exception cref="PromptDeliveryException">No composer evidence, or the submit never produced output.</exception>
    public static async Task SubmitAsync(
        string body,
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<CancellationToken, Task<long>> outputMark,
        Func<string, CancellationToken, Task> write,
        VerifiedSubmitOptions options,
        Action<string>? log,
        CancellationToken ct)
    {
        var normalized = PtyInputEncoding.NormalizeBody(body);
        if (normalized.Length == 0)
            return;

        var screenBefore = await snapshotScreen(ct);
        await write(PtyInputEncoding.WrapIfMultiline(normalized), ct);

        var evidenced = await PollAsync(
            async () => ComposerDeliveryEvidence.IsVisible(screenBefore, await snapshotScreen(ct), normalized),
            options.EvidenceTimeout, options.PollInterval, ct);
        if (!evidenced)
        {
            throw new PromptDeliveryException(
                $"Prompt ({normalized.Length} chars) produced no composer evidence within "
                + $"{options.EvidenceTimeout.TotalSeconds:F0}s — submit Enter withheld so the prompt "
                + "is not lost into a dead composer.");
        }

        var attempts = Math.Max(1, options.SubmitAttempts);
        for (var attempt = 1; ; attempt++)
        {
            var mark = await outputMark(ct);
            await Task.Delay(PreSubmitPause, ct);
            await write("\r", ct);

            var advanced = await PollAsync(
                async () => await outputMark(ct) > mark,
                options.PostSubmitAdvanceTimeout, options.PollInterval, ct);
            if (advanced)
                return;

            if (attempt >= attempts)
            {
                throw new PromptDeliveryException(
                    $"Submit Enter produced no output within {options.PostSubmitAdvanceTimeout.TotalSeconds:F0}s "
                    + $"across {attempts} attempt(s) — the composer holds the prompt but will not submit.");
            }

            log?.Invoke(
                $"Submit Enter produced no output within {options.PostSubmitAdvanceTimeout.TotalSeconds:F0}s "
                + $"(attempt {attempt}/{attempts}); pressing Enter again.");
        }
    }

    private static async Task<bool> PollAsync(
        Func<Task<bool>> condition, TimeSpan timeout, TimeSpan interval, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await condition())
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(interval, ct);
        }
    }
}
