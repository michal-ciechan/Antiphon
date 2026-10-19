using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

public class SubmitEvidenceTests
{
    private const string Body = "the unique head fragment proves this composer held our body";

    [Test]
    public void C907_current_working_is_positive_without_an_emptied_composer()
    {
        var before = "› " + Body;
        // Labelled ghost echo keeps the emptied-composer OR arm false.
        var held = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.V0160Running, before);
        held.Contains(Body, StringComparison.Ordinal).ShouldBeTrue("C907 held-body precondition");
        SubmitEvidence.IsPositive(SubmitEvidenceKind.Codex, before, held, Body)
            .ShouldBeTrue("C907 captured working despite still-visible body");
        var idleHeld = CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.V0160Idle, before);
        SubmitEvidence.IsPositive(SubmitEvidenceKind.Codex, before, idleHeld, Body)
            .ShouldBeFalse("C907 body retained without working is insufficient");
    }

    [Test]
    public void Codex_working_indicator_is_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Codex,
            $"› {Body}",
            "• Working (0s • esc to interrupt)",
            Body).ShouldBeTrue();
    }

    [Test]
    public void Codex_emptied_composer_is_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Codex,
            $"› {Body}",
            "› Improve documentation in @filename",
            Body).ShouldBeTrue();
    }

    [Test]
    public void Codex_body_still_visible_is_not_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Codex,
            $"› {Body}",
            $"› {Body}\nredraw frame",
            Body).ShouldBeFalse();
    }

    [Test]
    public void Codex_one_shot_IsPositive_is_true_on_empty_after_body()
    {
        // Documents the hole the queue must not latch (CARD-0299): a single empty snapshot
        // looks positive. WaitForTranscriptConfirmAsync requires PostEvidenceSettleMs of
        // consecutive emptied frames and re-checks HeadFragmentIsVisible at the deadline.
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Codex,
            $"› {Body}",
            "",
            Body).ShouldBeTrue();
    }

    [Test]
    public void Standard_kind_has_no_screen_only_positive_predicate()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Standard,
            $"› {Body}",
            "• Working (0s • esc to interrupt)",
            Body).ShouldBeFalse();
    }

    [Test]
    public void Grok_emptied_composer_is_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Grok,
            $"› {Body}",
            "› grok",
            Body).ShouldBeTrue();
    }

    [Test]
    public void Grok_working_indicator_with_body_still_visible_is_not_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Grok,
            $"› {Body}",
            $"› {Body}\n• Working (0s • esc to interrupt)",
            Body).ShouldBeFalse();
    }

    [Test]
    public void Grok_body_still_visible_is_not_positive_submit_evidence()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Grok,
            $"› {Body}",
            $"› {Body}\nredraw frame",
            Body).ShouldBeFalse();
    }
}
