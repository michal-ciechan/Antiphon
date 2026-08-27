using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

public class SubmitEvidenceTests
{
    private const string Body = "the unique head fragment proves this composer held our body";

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
    public void Standard_kind_has_no_screen_only_positive_predicate()
    {
        SubmitEvidence.IsPositive(
            SubmitEvidenceKind.Standard,
            $"› {Body}",
            "• Working (0s • esc to interrupt)",
            Body).ShouldBeFalse();
    }
}
