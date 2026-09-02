using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents;

/// <param name="ReEnterInterval">How long a typed body has to reach the rollout before Enter is pressed again.</param>
/// <param name="ExtraEnterAttempts">Enter presses AFTER the initial body+CR (the body is never re-typed).</param>
/// <param name="ConfirmTimeout">Total budget for the whole confirm loop.</param>
/// <param name="PollInterval">Transcript polling cadence.</param>
public sealed record CodexSubmitOptions(
    TimeSpan ReEnterInterval,
    int ExtraEnterAttempts,
    TimeSpan ConfirmTimeout,
    TimeSpan PollInterval);

/// <summary>
/// CARD-0108 S1 — the Codex submit contract, shared by both Codex adapters so they cannot fork.
///
/// <para><b>The defect this exists for.</b> Measured 2026-08-20 (codex-cli 0.147.0, modern ConPTY,
/// production <c>PtyAgentRunner</c>): the production submit path — body, 20 ms, a separate
/// <c>\r</c> — failed to submit <b>6 times out of 6</b> across two probe runs. The CR lands inside
/// Codex's paste-detection window and folds into a literal newline instead of acting as Enter, so
/// the body strands in a composer that then emits nothing at all: no rollout file is ever created
/// (it is created lazily by the first TURN) and the TUI is silent for as long as you care to
/// watch. <c>SendPromptAsync</c> reported success over that, and the 3 s quiet done-detector then
/// certified the non-turn as a completed turn. One extra Enter, ~4 s later, submitted 6/6.</para>
///
/// <para><b>The rule.</b> A submit is proven by a <c>UserPrompt</c> transcript row past a baseline
/// captured before the first keystroke, whose text carries this body
/// (<see cref="PromptSubmissionMatch.IsConfirmedBy"/> — CARD-0055's matcher, head window). Until
/// then the retry is <b>Enter only, never a re-type</b>: an Enter on an empty composer was measured
/// submitting nothing five times over (<c>CodexComposerCanaryTests</c> phase A), so a re-press
/// after a submit that actually landed is a no-op, while a re-type after one that landed would send
/// the body twice.</para>
///
/// <para><b>"No transcript yet" is not failure.</b> On a first turn the rollout is created by this
/// very submit and the tailer must then discover and bind it (250 ms locate poll, CARD-0006 rules
/// C1-C4). An empty transcript therefore polls as not-yet-confirmed for the whole window, and the
/// window must not be trimmed below ~20 s — the measured end-to-end lag is ≤2.8 s after the Enter
/// that actually submitted, but the binding sits behind it.</para>
///
/// <para><b>Where the transcript never becomes observable at all</b> — a refused or failed bind, a
/// transcript-disabled session, an adapter with no transcript source — the CARD-0055 degrade still
/// stands, but the named/card-launch path is no longer a blind return the moment the window expires
/// (CARD-0133 S1b-A). After the last Enter, this looks twice <see cref="CodexMcpBoot.AbsentSettle"/>
/// apart: <see cref="CodexWorkingIndicator"/> on either look is degraded success; the body still
/// visible on <b>both</b> looks throws <see cref="PromptDeliveryException"/> with
/// <see cref="PromptDeliveryException.ComposerMayHoldBody"/>; neither (body gone, no Working) is
/// today's blind return. A transcript that produces rows but never one confirming this body is a
/// live pipeline saying the body did not submit, and that throws on the transcript-live arm
/// unchanged.</para>
/// </summary>
internal static class CodexSubmitConfirmation
{
    /// <param name="body">The prompt, exactly as it will be typed.</param>
    /// <param name="baselineSequence">Transcript LastSequence read before the first keystroke.</param>
    /// <param name="sendLine">Types the body and its separate delayed CR (the unchanged production path).</param>
    /// <param name="pressEnter">Writes a bare CR. Never re-types the body.</param>
    /// <param name="readTranscript">
    /// Returns the session's transcript rows, or null when the transcript is not observable at all.
    /// Pass null for an adapter that has no transcript source — it takes the degraded path directly.
    /// </param>
    /// <param name="snapshotScreen">Rendered screen, for the look that decides <see cref="PromptDeliveryException.ComposerMayHoldBody"/>.</param>
    /// <param name="log">Non-fatal delivery events (an Enter re-press, the degraded path). Warning level.</param>
    /// <exception cref="PromptDeliveryException">
    /// The transcript is demonstrably live and never confirmed this body, or (CARD-0133 S1b-A)
    /// no row was ever seen and the composer still shows the body after two post-Enter looks.
    /// </exception>
    public static async Task SubmitAsync(
        string body,
        long baselineSequence,
        Func<CancellationToken, Task> sendLine,
        Func<CancellationToken, Task> pressEnter,
        Func<CancellationToken, Task<IReadOnlyList<SessionRunnerTranscriptEvent>?>>? readTranscript,
        Func<CancellationToken, Task<string>> snapshotScreen,
        CodexSubmitOptions options,
        Action<string>? log,
        CancellationToken ct)
    {
        await sendLine(ct);

        if (readTranscript is null)
        {
            log?.Invoke(
                "Codex submit sent blind: this adapter has no transcript source, so the CARD-0108 "
                + "confirm loop cannot run. The prompt may be stranded in the composer with no signal.");
            return;
        }

        var deadline = DateTime.UtcNow + options.ConfirmTimeout;
        var extraEnters = Math.Max(0, options.ExtraEnterAttempts);
        var anyRowEverSeen = false;

        for (var attempt = 0; ; attempt++)
        {
            var window = attempt >= extraEnters
                ? deadline - DateTime.UtcNow
                : Min(options.ReEnterInterval, deadline - DateTime.UtcNow);

            var (confirmed, sawRows) = await PollForConfirmationAsync(
                body, baselineSequence, readTranscript, window, options.PollInterval, ct);
            anyRowEverSeen |= sawRows;
            if (confirmed)
                return;

            if (attempt >= extraEnters || DateTime.UtcNow >= deadline)
                break;

            log?.Invoke(
                $"Codex submit not confirmed by a transcript row after "
                + $"{options.ReEnterInterval.TotalSeconds:F0}s (Enter {attempt + 1} of {extraEnters} "
                + "re-presses); pressing Enter again. The body is NOT re-typed — a folded CR leaves "
                + "the composer holding it.");
            await pressEnter(ct);
        }

        if (!anyRowEverSeen)
        {
            await ConcludeUnobservableSubmitAsync(
                body, snapshotScreen, extraEnters, baselineSequence, options, log, ct);
            return;
        }

        var screen = await SafeScreenAsync(snapshotScreen, ct);
        var stillVisible = ComposerStillShows(screen, body);

        throw new PromptDeliveryException(
            $"Codex prompt ({body.Length} chars) was typed but no UserPrompt transcript row past "
            + $"sequence {baselineSequence} carried it within {options.ConfirmTimeout.TotalSeconds:F0}s "
            + $"across {extraEnters + 1} Enter press(es), while the transcript was live. "
            + (stillVisible
                ? StillShowsSentence
                : "The body is no longer visible on screen. ")
            + "Screen tail: " + Tail(screen, 400),
            composerMayHoldBody: stillVisible);
    }

    /// <summary>
    /// CARD-0133 S1b-A: no transcript row was ever seen. Two looks
    /// <see cref="CodexMcpBoot.AbsentSettle"/> apart decide among Working (degraded success),
    /// body still standing on both (throw, composer may hold it), and neither (blind return).
    /// Two looks so a single stale frame that still shows the body cannot fail a launch on its own.
    /// </summary>
    private static async Task ConcludeUnobservableSubmitAsync(
        string body,
        Func<CancellationToken, Task<string>> snapshotScreen,
        int extraEnters,
        long baselineSequence,
        CodexSubmitOptions options,
        Action<string>? log,
        CancellationToken ct)
    {
        var first = await SafeScreenAsync(snapshotScreen, ct);
        if (CodexWorkingIndicator.IsVisible(first))
        {
            log?.Invoke("Codex submit confirmed by Working indicator; transcript never bound");
            return;
        }

        await Task.Delay(CodexMcpBoot.AbsentSettle, ct);

        var second = await SafeScreenAsync(snapshotScreen, ct);
        if (CodexWorkingIndicator.IsVisible(second))
        {
            log?.Invoke("Codex submit confirmed by Working indicator; transcript never bound");
            return;
        }

        if (ComposerStillShows(first, body) && ComposerStillShows(second, body))
        {
            throw new PromptDeliveryException(
                $"Codex prompt ({body.Length} chars) was typed but no UserPrompt transcript row past "
                + $"sequence {baselineSequence} carried it within {options.ConfirmTimeout.TotalSeconds:F0}s "
                + $"across {extraEnters + 1} Enter press(es), and the transcript never produced a row. "
                + StillShowsSentence
                + "Screen tail: " + Tail(second, 400),
                composerMayHoldBody: true);
        }

        log?.Invoke(
            $"Codex submit could not be confirmed in {options.ConfirmTimeout.TotalSeconds:F0}s and "
            + "this session produced NO transcript rows at all, so there is nothing to confirm "
            + "against — treating the delivery as blind (pre-CARD-0108 behaviour). A bound "
            + "transcript is what makes this verifiable; its absence is already a "
            + "TranscriptBindFailed-class fault. Body still visible on screen: "
            + ComposerStillShows(second, body));
    }

    /// <summary>
    /// Polls the transcript for the confirming row. Reports separately whether ANY row was observed
    /// at all — that is what separates "the pipeline says it did not submit" from "there is no
    /// pipeline", and the two failures deserve opposite answers.
    /// </summary>
    private static async Task<(bool Confirmed, bool SawRows)> PollForConfirmationAsync(
        string body,
        long baselineSequence,
        Func<CancellationToken, Task<IReadOnlyList<SessionRunnerTranscriptEvent>?>> readTranscript,
        TimeSpan window,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + window;
        var sawRows = false;

        while (true)
        {
            var rows = await readTranscript(ct);
            if (rows is { Count: > 0 })
            {
                sawRows = true;
                if (rows.Any(r => r.Sequence > baselineSequence
                                  && r.Kind == TranscriptKinds.UserPrompt
                                  && PromptSubmissionMatch.IsConfirmedBy(body, r.Text)))
                {
                    return (true, true);
                }
            }

            if (DateTime.UtcNow >= deadline)
                return (false, sawRows);

            try { await Task.Delay(pollInterval, ct); }
            catch (OperationCanceledException) { return (false, sawRows); }
        }
    }

    /// <summary>
    /// Does the rendered screen still carry the body's head? Whitespace-free on both sides because
    /// the composer wraps at the window width and may collapse a paste, and only a short head
    /// fragment because that is all a wrapped composer row reliably shows. This is a LOOK, not a
    /// proof: false means "no reason to think the composer holds it", never "the composer is empty".
    /// </summary>
    internal static bool ComposerStillShows(string? screen, string body)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrWhiteSpace(body))
            return false;

        var head = Squash(PromptSubmissionMatch.Normalize(body));
        if (head.Length < PromptSubmissionMatch.MinMatchChars)
            return false;
        if (head.Length > HeadLookChars)
            head = head[..HeadLookChars];

        return Squash(PromptSubmissionMatch.Normalize(screen)).Contains(head, StringComparison.Ordinal);
    }

    private const int HeadLookChars = 40;

    private const string StillShowsSentence =
        "The composer STILL SHOWS the body, so it is holding an unsubmitted prompt: "
        + "re-typing would splice a second copy onto the first. ";

    private static string Squash(string text) => text.Replace(" ", "", StringComparison.Ordinal);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static async Task<string> SafeScreenAsync(
        Func<CancellationToken, Task<string>> snapshotScreen, CancellationToken ct)
    {
        try { return await snapshotScreen(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ""; }
    }

    private static string Tail(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "<empty>" : s.Length <= n ? s : s[^n..];
}
