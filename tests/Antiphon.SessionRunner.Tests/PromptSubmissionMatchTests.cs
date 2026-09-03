using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0055 slice 1: the C4 matcher, extracted so the server's delivery confirmation and the
/// runner's transcript binding share one set of rules.
///
/// The direction differs between the two callers and that is the point of the split:
/// C4 asks "does this transcript record's text appear in what we typed?" (needle = the record),
/// delivery confirmation asks "does this transcript record carry the body we typed?"
/// (needle = the body's HEAD). Both build the needle the same way, and neither may drift.
///
/// <see cref="SessionInputLog"/>'s own suites (TranscriptAdoptionSafetyTests, the tailer tests)
/// are deliberately untouched by the extraction — that they stay green IS the lockstep proof.
/// </summary>
public class PromptSubmissionMatchTests
{
    private const string Esc = "\u001b";

    // ---- normalization ------------------------------------------------------------------------

    [Test]
    public void Normalize_collapses_every_whitespace_run_to_one_space()
    {
        PromptSubmissionMatch.Normalize("deliver   this\t\tbody\n\nnow")
            .ShouldBe("deliver this body now");
    }

    [Test]
    public void Normalize_trims_leading_and_trailing_whitespace()
    {
        PromptSubmissionMatch.Normalize("\n  padded body  \n").ShouldBe("padded body");
    }

    // The delivery path rewrites line endings (ReplaceLineEndings("\n")) and the TUI re-wraps the
    // body before Claude persists it, so a CRLF body and its LF twin MUST normalize identically —
    // otherwise every Windows/Telegram-sourced body would fail to confirm and be re-Entered.
    [Test]
    public void Crlf_and_lf_bodies_normalize_identically_and_match_each_other()
    {
        var crlf = "first line of the message\r\nsecond line of the message";
        var lf = "first line of the message\nsecond line of the message";

        PromptSubmissionMatch.Normalize(crlf).ShouldBe(PromptSubmissionMatch.Normalize(lf));
        PromptSubmissionMatch.IsConfirmedBy(crlf, lf).ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy(lf, crlf).ShouldBeTrue();
    }

    [Test]
    public void Normalize_strips_bracketed_paste_wrappers_the_delivery_path_adds()
    {
        PromptSubmissionMatch.Normalize($"{Esc}[200~wrapped body text{Esc}[201~")
            .ShouldBe("wrapped body text");
    }

    [Test]
    public void Normalize_strips_csi_osc_and_two_char_escape_sequences()
    {
        PromptSubmissionMatch.Normalize($"{Esc}[1;31mred{Esc}[0m text").ShouldBe("red text");
        PromptSubmissionMatch.Normalize($"{Esc}]0;window title\atext after").ShouldBe("text after");
        PromptSubmissionMatch.Normalize($"{Esc}]0;st terminated{Esc}\\text after").ShouldBe("text after");
        PromptSubmissionMatch.Normalize($"{Esc}7saved cursor").ShouldBe("saved cursor");
    }

    // A truncated write ends mid-sequence; swallowing the remainder is the right call (there is no
    // text after it, only a broken control sequence).
    [Test]
    public void An_unterminated_escape_sequence_swallows_the_rest()
    {
        PromptSubmissionMatch.Normalize($"visible{Esc}[38;2;1").ShouldBe("visible");
    }

    // ---- the needle ---------------------------------------------------------------------------

    [Test]
    public void A_needle_is_the_head_window_not_the_tail()
    {
        var body = new string('a', 50) + new string('b', 300);

        PromptSubmissionMatch.TryBuildNeedle(body, out var needle).ShouldBeTrue();
        needle.Length.ShouldBe(PromptSubmissionMatch.MatchWindowChars);
        needle.ShouldStartWith("aaaa");
        needle.ShouldBe(PromptSubmissionMatch.Normalize(body)[..PromptSubmissionMatch.MatchWindowChars]);
    }

    [Test]
    public void Text_below_the_minimum_yields_no_needle()
    {
        PromptSubmissionMatch.TryBuildNeedle("Continue.", out _).ShouldBeFalse();
        PromptSubmissionMatch.TryBuildNeedle("", out _).ShouldBeFalse();
        PromptSubmissionMatch.TryBuildNeedle(null, out _).ShouldBeFalse();
        // Exactly at the threshold is identifiable.
        PromptSubmissionMatch.TryBuildNeedle(new string('x', PromptSubmissionMatch.MinMatchChars), out _)
            .ShouldBeTrue();
    }

    // ---- IsConfirmedBy: the delivery-confirmation direction -------------------------------------

    [Test]
    public void A_record_carrying_the_body_confirms_it()
    {
        PromptSubmissionMatch
            .IsConfirmedBy("run the integration tests and report back", "run the integration tests and report back")
            .ShouldBeTrue();
    }

    // THE 15c9150e shape, at the matcher level: a genuine new UserPrompt record arrived, but it
    // carried the PREVIOUS delivery's body. Arrival is not confirmation.
    [Test]
    public void A_record_carrying_a_different_body_does_not_confirm()
    {
        PromptSubmissionMatch
            .IsConfirmedBy("the note that was lost in the composer", "the stale note that Enter actually submitted")
            .ShouldBeFalse();
    }

    // Containment, not equality, and in one direction only: Claude may add framing around what we
    // typed, but a record that is merely a FRAGMENT of our body is not evidence our body arrived.
    [Test]
    public void Containment_runs_body_into_record_never_the_reverse()
    {
        const string body = "deliver this exact instruction to the delegate";

        PromptSubmissionMatch.IsConfirmedBy(body, $"<framing>{body}</framing>").ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy(body, "deliver this exact").ShouldBeFalse();
    }

    // The head window is what makes a clipped body fail to confirm. The pty's measured loss mode
    // keeps TAILS (CARD-0027), so a tail-anchored match would certify exactly the bodies that lost
    // their head — which is CARD-0024's gap, not something this matcher may paper over.
    [Test]
    public void A_body_whose_head_was_clipped_away_does_not_confirm()
    {
        var body = new string('h', 300) + "and here is the tail that survived the clip";
        var clipped = "and here is the tail that survived the clip";

        PromptSubmissionMatch.IsConfirmedBy(body, clipped).ShouldBeFalse();
    }

    // Only the head window is compared, so a 40 KB brief confirms on its opening frame — the rest
    // of the body is CARD-0024's problem (completeness), not this matcher's (identity).
    [Test]
    public void A_long_body_confirms_on_its_head_window_alone()
    {
        var head = "[Antiphon delegation brief] CARD-0055 slice 2 — transcript-confirmed delivery. ";
        var body = head + new string('z', 40_000);

        PromptSubmissionMatch.IsConfirmedBy(body, head + new string('z', 200)).ShouldBeTrue();
    }

    // The weak arm: an auto-continue "Continue." has no distinctive text, so the record's existence
    // is all the evidence there is. Weaker than a text match, strictly stronger than a screen redraw.
    [Test]
    public void A_body_too_short_to_identify_takes_the_weak_arm()
    {
        PromptSubmissionMatch.RequiresTextMatch("Continue.").ShouldBeFalse();
        PromptSubmissionMatch.IsConfirmedBy("Continue.", "something else entirely").ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy("Continue.", null).ShouldBeTrue();

        PromptSubmissionMatch.RequiresTextMatch("a body long enough to identify").ShouldBeTrue();
    }

    [Test]
    public void An_identifiable_body_is_never_confirmed_by_an_empty_record()
    {
        PromptSubmissionMatch.IsConfirmedBy("a body long enough to identify", null).ShouldBeFalse();
        PromptSubmissionMatch.IsConfirmedBy("a body long enough to identify", "").ShouldBeFalse();
    }

    // The delivery path wraps multi-line bodies in bracketed paste and normalizes line endings
    // before typing; the record Claude writes has neither. Both sides normalize, so it matches.
    [Test]
    public void A_paste_wrapped_multiline_body_confirms_against_the_plain_record()
    {
        var typed = $"{Esc}[200~line one of the note\nline two of the note{Esc}[201~";
        const string recorded = "line one of the note\nline two of the note";

        PromptSubmissionMatch.IsConfirmedBy(typed, recorded).ShouldBeTrue();
    }

    // ---- CARD-0080 S2: the whitespace-free arm, for a TUI that keeps no whitespace at all --------

    /// <summary>
    /// Grok's composer drops EVERY newline from typed and pasted input with NO separator (measured
    /// grok 1.0.5, CARD-0080 S1: 4450 chars sent → 4389 recorded, exactly the newline count). The
    /// spaced normalization keeps a space where the newline was, so without the whitespace-free
    /// second arm every multi-line delivery to Grok would fail to confirm — and CARD-0055 would
    /// then park messages and kill healthy always-on sessions.
    /// </summary>
    [Test]
    public void A_newline_dropped_join_still_confirms_the_multiline_body()
    {
        const string body = "line one of the channel reply\nline two of the channel reply\nline three";
        const string grokRecord = "line one of the channel replyline two of the channel replyline three";

        PromptSubmissionMatch.IsConfirmedBy(body, grokRecord).ShouldBeTrue();
        // Framing around the joined record (batch envelope etc.) must not break it either.
        PromptSubmissionMatch.IsConfirmedBy(body, "prefix " + grokRecord + " suffix").ShouldBeTrue();
    }

    // The widening must not weaken the 15c9150e protection: a record carrying DIFFERENT text still
    // fails, whitespace-free or not.
    [Test]
    public void The_whitespace_free_arm_still_rejects_a_different_body()
    {
        PromptSubmissionMatch.IsConfirmedBy(
                "the note that was lost in the composer\nsecond line",
                "the stale note that Enter actuallysubmitted")
            .ShouldBeFalse();
    }

    // A needle that is mostly whitespace shrinks below MinMatchChars when stripped — it may not
    // take the whitespace-free arm, or a near-weak match would be reported at text-match strength.
    [Test]
    public void A_mostly_whitespace_needle_does_not_degrade_to_a_near_weak_match()
    {
        const string body = "a b c d e f g h"; // 15 chars spaced (identifiable), 8 stripped (not)
        PromptSubmissionMatch.RequiresTextMatch(body).ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy(body, "xxabcdefghxx").ShouldBeFalse();
        // The spaced arm still works as before.
        PromptSubmissionMatch.IsConfirmedBy(body, "a b c d e f g h").ShouldBeTrue();
    }

    // ---- CARD-0056: a boot slash command confirms through its local-command wrapper ---------------

    /// <summary>
    /// A launch's <c>/remote-control</c> is not recorded verbatim: Claude wraps a local slash
    /// command in <c>&lt;command-name&gt;</c> tags. CARD-0056's boot late-confirm leans on that
    /// wrapper satisfying this matcher unchanged, so the wrapper is pinned here rather than a
    /// second matcher being invented for it. 15 chars clears <see cref="MinMatchChars"/>, which is
    /// what keeps it on the STRONG arm — the weak arm would confirm a slash command from any
    /// unrelated record.
    /// </summary>
    [Test]
    public void The_local_command_wrapper_confirms_the_slash_command_that_was_typed()
    {
        const string wrapper =
            "<command-name>/remote-control</command-name>\n"
            + "            <command-message>remote-control</command-message>\n"
            + "            <command-args></command-args>";

        PromptSubmissionMatch.RequiresTextMatch("/remote-control")
            .ShouldBeTrue("15 chars — identifiable by text, so the weak arm never applies");
        PromptSubmissionMatch.IsConfirmedBy("/remote-control", wrapper).ShouldBeTrue();
        PromptSubmissionMatch.IsConfirmedBy("/remote-control", "<command-name>/clear</command-name>")
            .ShouldBeFalse("a different command's wrapper is not our command");
    }

    // ---- CARD-0024: completeness is a second question, not a change to identity -----------------
    //
    // IsConfirmedBy stays the head-window identity matcher (C4 shares it). IsCompleteIn asks
    // whether the same record contains the FULL normalized body. Identity without completeness
    // is the measured clip: first-chunk-only and the 2026-08-10 head+tail splice both confirm
    // and both fail completeness.

    [Test]
    public void The_2026_08_10_head_and_tail_splice_identifies_but_is_not_complete()
    {
        // Measured: 5 471 queued, 379 recorded = src[0..246] + src[5339..5470], 5 × 1024 dropped.
        const int headKept = 247;
        const int tailStart = 5339;
        const int total = 5471;
        var body = new string('A', headKept) + new string('M', tailStart - headKept) + new string('Z', total - tailStart);
        var recorded = body[..headKept] + body[tailStart..];

        body.Length.ShouldBe(total);
        recorded.Length.ShouldBe(379);
        PromptSubmissionMatch.IsConfirmedBy(body, recorded).ShouldBeTrue("the surviving head is enough for identity");
        PromptSubmissionMatch.IsCompleteIn(body, recorded).ShouldBeFalse("both ends survived; the middle did not");
    }

    [Test]
    public void A_first_chunk_only_clip_identifies_but_is_not_complete()
    {
        var body = new string('h', 250) + new string('t', 800);
        var clipped = body[..250];

        PromptSubmissionMatch.IsConfirmedBy(body, clipped).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(body, clipped).ShouldBeFalse();
    }

    [Test]
    public void A_whole_body_is_complete()
    {
        const string body = "deliver this exact instruction to the delegate";
        PromptSubmissionMatch.IsConfirmedBy(body, body).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(body, body).ShouldBeTrue();
    }

    [Test]
    public void A_framed_whole_body_is_complete()
    {
        const string body = "deliver this exact instruction to the delegate";
        PromptSubmissionMatch.IsConfirmedBy(body, $"<framing>{body}</framing>").ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(body, $"<framing>{body}</framing>").ShouldBeTrue();
    }

    [Test]
    public void A_newline_dropped_join_is_complete()
    {
        const string body = "line one of the channel reply\nline two of the channel reply\nline three";
        const string grokRecord = "line one of the channel replyline two of the channel replyline three";

        PromptSubmissionMatch.IsConfirmedBy(body, grokRecord).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(body, grokRecord)
            .ShouldBeTrue("Grok drops newlines with no separator; completeness uses the same whitespace-free arm");
        PromptSubmissionMatch.IsCompleteIn(body, "prefix " + grokRecord + " suffix").ShouldBeTrue();
    }

    [Test]
    public void A_long_body_that_confirms_on_its_head_window_alone_is_not_complete()
    {
        var head = "[Antiphon delegation brief] CARD-0055 slice 2 — transcript-confirmed delivery. ";
        var body = head + new string('z', 40_000);

        PromptSubmissionMatch.IsConfirmedBy(body, head + new string('z', 200)).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(body, head + new string('z', 200)).ShouldBeFalse();
    }

    [Test]
    public void A_weak_match_body_is_vacuously_complete()
    {
        PromptSubmissionMatch.RequiresTextMatch("Continue.").ShouldBeFalse();
        PromptSubmissionMatch.IsCompleteIn("Continue.", "something else entirely").ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn("Continue.", null).ShouldBeTrue();
    }

    [Test]
    public void The_local_command_wrapper_is_complete_for_the_slash_command_that_was_typed()
    {
        const string wrapper =
            "<command-name>/remote-control</command-name>\n"
            + "            <command-message>remote-control</command-message>\n"
            + "            <command-args></command-args>";

        PromptSubmissionMatch.IsConfirmedBy("/remote-control", wrapper).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn("/remote-control", wrapper)
            .ShouldBeTrue("the wrapper contains the typed body, so completeness is a no-op there");
    }

    // ---- lockstep with C4 ------------------------------------------------------------------------

    // SessionInputLog must ask the extracted matcher the same question it used to answer itself:
    // same normalization, same thresholds, same head window. Its own C4 suites cover the behaviour;
    // this pins the delegation so a future divergence is visible here too.
    [Test]
    public void SessionInputLog_uses_the_shared_thresholds_and_normalization()
    {
        SessionInputLog.MinMatchChars.ShouldBe(PromptSubmissionMatch.MinMatchChars);
        SessionInputLog.MatchWindowChars.ShouldBe(PromptSubmissionMatch.MatchWindowChars);
        SessionInputLog.Normalize($"{Esc}[200~typed   body\r\nsecond line{Esc}[201~")
            .ShouldBe(PromptSubmissionMatch.Normalize("typed body\nsecond line"));

        var log = new SessionInputLog();
        log.Append($"{Esc}[200~Implement CARD-0055 slice one\n{Esc}[201~");

        log.MatchesRecordedInput("Implement CARD-0055 slice one").ShouldBeTrue();
        log.MatchesRecordedInput("Implement CARD-0006 slice one").ShouldBeFalse();
        log.MatchesRecordedInput("continue").ShouldBeFalse("below MinMatchChars: C4 refuses, it never guesses");
    }

    [Test]
    public void A_prefix_of_19_chars_distinguishes_two_otherwise_identical_long_prompts()
    {
        var ritual = new string('x', 195);
        var candidateA = "[session aaaaaaaa] " + ritual;
        var candidateB = "[session bbbbbbbb] " + ritual;

        var logA = new SessionInputLog();
        logA.Append(candidateA);
        var logB = new SessionInputLog();
        logB.Append(candidateB);

        logA.MatchesRecordedInput(candidateA).ShouldBeTrue();
        logA.MatchesRecordedInput(candidateB).ShouldBeFalse();
        logB.MatchesRecordedInput(candidateB).ShouldBeTrue();
        logB.MatchesRecordedInput(candidateA).ShouldBeFalse();
    }

    [Test]
    public void A_suffix_past_the_head_window_does_not()
    {
        // Trap: a suffix on a ≥200-char shared prefix is invisible to C4. Delivery must prefix.
        var shared = new string('x', PromptSubmissionMatch.MatchWindowChars);
        var candidateA = shared + " [session aaaaaaaa]";
        var candidateB = shared + " [session bbbbbbbb]";

        var logA = new SessionInputLog();
        logA.Append(candidateA);
        var logB = new SessionInputLog();
        logB.Append(candidateB);

        logA.MatchesRecordedInput(candidateB).ShouldBeTrue(
            "suffix sits past MatchWindowChars; C4 cannot tell the two apart");
        logB.MatchesRecordedInput(candidateA).ShouldBeTrue();
    }
}
