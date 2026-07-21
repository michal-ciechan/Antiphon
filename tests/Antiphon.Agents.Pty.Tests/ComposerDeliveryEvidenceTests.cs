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
}
