using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Pure unit tests for the delivery-evidence predicate (CI tier — no PTY, no Claude). The
/// scenario shapes mirror what <c>ClaudeComposerRenderCanaryTests</c> observed on real Claude:
/// verbatim short lines, suffix-only huge lines with mid-token wrapping, and the two
/// non-deterministic multi-line renderings (prefix + placeholder vs tail-only).
/// </summary>
public class ComposerDeliveryEvidenceTests
{
    private const string IdleScreen = "❯ Try \"how do I log an error?\"\n──────────\n  ⏵⏵ bypass permissions";

    [Test]
    public void Short_body_rendered_verbatim_is_evidence()
    {
        var after = IdleScreen + "\n❯ ship the release notes";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, "ship the release notes").ShouldBeTrue();
    }

    [Test]
    public void Unchanged_screen_is_not_evidence()
    {
        ComposerDeliveryEvidence.IsVisible(IdleScreen, IdleScreen, "ship the release notes").ShouldBeFalse();
    }

    [Test]
    public void Huge_single_line_matches_on_the_visible_suffix_only()
    {
        var body = string.Concat(Enumerable.Range(0, 400).Select(i => $"wall{i:D4} ")) + "ENDMARKERZULU";
        // Screen shows only the tail near the cursor (start scrolled out of the viewport).
        var after = IdleScreen + "\n❯ wall0396 wall0397 wall0398\n  wall0399 ENDMARKERZULU───";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Wrapping_that_splits_tokens_across_rows_still_matches()
    {
        var body = string.Concat(Enumerable.Range(0, 400).Select(i => $"wall{i:D4} ")) + "ENDMARKERZULU";
        // Observed on real Claude: rows wrap mid-token and are trimmed of trailing spaces —
        // "wall039" + newline + "9 ENDMARK" + newline + "ERZULU".
        var after = IdleScreen + "\n❯ wall0398 wall039\n9 ENDMARK\nERZULU";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Ghost_rows_interleaved_in_the_wrapped_tail_still_match()
    {
        // Observed on real Claude mid-scroll captures: stale prompt-hint or border rows appear
        // INSIDE the wrapped composer text. A contiguous substring match dies on this — the
        // windowed quorum match must not (this exact shape failed the first headed run).
        var body = string.Concat(Enumerable.Range(0, 300).Select(i => $"filler{i:D4} "))
            + "reply with the single word PONG and nothing else.";
        var after = IdleScreen
            + "\n❯ filler0296 filler0297 reply with the single wo"
            + "\n❯ Try \"how does <filepath> work?\""    // ghost row splits the tail
            + "\n──────────────────────────────"          // border row splits it again
            + "\nrd PONG and nothing else.";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Multi_line_prefix_plus_paste_placeholder_is_evidence()
    {
        var body = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line{i:D2} of the wall"));
        // Rendering variant 1: first lines + "[Pasted text #1 +31 lines]", tail hidden.
        var after = IdleScreen + "\n❯ line00 of the wall\n  line01 of the wall\n  [Pasted text #1 +31 lines]";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Multi_line_tail_only_rendering_is_evidence()
    {
        var body = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line{i:D2} of the wall"));
        // Rendering variant 2: tail lines visible, no placeholder, start hidden.
        var after = IdleScreen + "\n❯ line38 of the wall\n  line39 of the wall";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Paste_placeholder_already_in_history_does_not_count()
    {
        // A previously SUBMITTED paste leaves "[Pasted text ...]" in the transcript area. A new
        // delivery must not treat that stale placeholder as evidence.
        var beforeWithHistory = "> [Pasted text #1 +10 lines]\n" + IdleScreen;
        var unchanged = beforeWithHistory;
        var body = "first\nsecond\nthird";
        ComposerDeliveryEvidence.IsVisible(beforeWithHistory, unchanged, body).ShouldBeFalse();

        // But a NEW placeholder appearing on top of the old one does count.
        var withNewPlaceholder = beforeWithHistory + "\n❯ [Pasted text #2 +2 lines]";
        ComposerDeliveryEvidence.IsVisible(beforeWithHistory, withNewPlaceholder, body).ShouldBeTrue();
    }

    /// <summary>
    /// The modern-pseudoconsole case (CARD-0037), and the one that would have broken delivery
    /// verification outright: with the bracketed-paste markers actually reaching the TUI, a large
    /// body is PASTED, and the composer shows the placeholder and NOTHING of the body — no head, no
    /// tail, no fragment of a line. Head-or-tail matching finds nothing, so without the placeholder
    /// arm every large delivery on the paste path would report "no composer evidence", withhold its
    /// Enter, revert the message and kill an always-on session as wedged.
    /// </summary>
    [Test]
    public void A_collapsed_paste_showing_none_of_the_body_is_still_evidence()
    {
        var body = string.Join("\n", Enumerable.Range(0, 3_200).Select(i => $"Q{i:D5} of the pasted wall"));
        var after = IdleScreen + "\n❯ [Pasted text #1 +3199 lines]";

        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, body).ShouldBeTrue(
            "the composer collapsed the paste, so the body is not on the screen at all — the "
            + "placeholder IS the evidence");
    }

    /// <summary>
    /// Why the placeholder is matched by its #N and not by counting occurrences: the screen is the
    /// VISIBLE rows, and a tall paste pushes the previous placeholder off the top as it renders its
    /// own. The count is then unchanged and a perfectly good delivery has no evidence. Claude
    /// numbers pastes per session and never reuses an index, so the index does not have this hole.
    /// </summary>
    [Test]
    public void A_new_placeholder_that_scrolled_the_old_one_away_is_still_evidence()
    {
        var body = string.Join("\n", Enumerable.Range(0, 900).Select(i => $"Q{i:D5} second delivery"));
        var before = IdleScreen + "\n❯ [Pasted text #7 +120 lines]";
        var after = IdleScreen + "\n❯ [Pasted text #8 +899 lines]"; // #7 scrolled out of the viewport

        ComposerDeliveryEvidence.IsVisible(before, after, body).ShouldBeTrue(
            "one placeholder before, one after — the COUNT is unchanged, and only the index says a "
            + "new paste landed");
    }

    /// <summary>The other side of it: the SAME placeholder still on screen is not a new delivery.</summary>
    [Test]
    public void The_same_placeholder_index_is_not_evidence_of_a_new_paste()
    {
        var before = IdleScreen + "\n❯ [Pasted text #7 +120 lines]";
        var body = string.Join("\n", Enumerable.Range(0, 900).Select(i => $"Q{i:D5} second delivery"));

        ComposerDeliveryEvidence.IsVisible(before, before, body).ShouldBeFalse();
    }

    [Test]
    public void Body_shorter_than_fragment_length_matches_whole()
    {
        var after = IdleScreen + "\n❯ ok";
        ComposerDeliveryEvidence.IsVisible(IdleScreen, after, "ok").ShouldBeTrue();
        ComposerDeliveryEvidence.IsVisible(IdleScreen, IdleScreen + "\n❯ nope", "ok").ShouldBeFalse();
    }

    [Test]
    public void Whitespace_only_body_is_trivially_visible()
    {
        ComposerDeliveryEvidence.IsVisible(IdleScreen, IdleScreen, "  \n\t ").ShouldBeTrue();
    }

    [Test]
    public void Empty_screen_is_never_evidence()
    {
        ComposerDeliveryEvidence.IsVisible(IdleScreen, "", "ship it").ShouldBeFalse();
    }

    [Test]
    public void Body_consumed_when_the_tail_fragment_is_visible()
    {
        var body = "HEAD-" + new string('a', 50) + "-TAIL-UNIQUE";
        var after = IdleScreen + "\n❯ " + body[^ComposerDeliveryEvidence.FragmentSpan..];

        ComposerDeliveryEvidence.BodyConsumed(IdleScreen, after, body).ShouldBeTrue();
    }

    [Test]
    public void Body_consumed_is_false_when_only_the_head_fragment_is_visible()
    {
        var body = "HEAD-UNIQUE-" + new string('a', 50) + new string('z', 50);
        var after = IdleScreen + "\n❯ " + body[..ComposerDeliveryEvidence.FragmentSpan];

        ComposerDeliveryEvidence.BodyConsumed(IdleScreen, after, body).ShouldBeFalse(
            "the head can arrive while the tail is still in flight, so it must not release Enter");
    }

    [Test]
    public void Body_consumed_when_a_new_placeholder_index_appears()
    {
        var before = IdleScreen + "\n❯ [Pasted text #7 +120 lines]";
        var after = IdleScreen + "\n❯ [Pasted text #8 +899 lines]";

        ComposerDeliveryEvidence.BodyConsumed(before, after, "body hidden by the collapsed paste")
            .ShouldBeTrue();
    }

    [Test]
    public void Body_consumed_is_false_for_a_pre_existing_placeholder_index_alone()
    {
        var screen = IdleScreen + "\n❯ [Pasted text #7 +120 lines]";

        ComposerDeliveryEvidence.BodyConsumed(screen, screen, "body hidden by the collapsed paste")
            .ShouldBeFalse();
    }
}
