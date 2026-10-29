using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// The C540 crash cut rejects a committed dispatch-base projection and names every other warning.
/// A bare Warning count is the wrong guard: quota and capacity-consume events share that type.
/// </summary>
[Category("Unit")]
public sealed class DispatchCrashWarningTests
{
    private static readonly Guid IntentId = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid OtherId = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    [Test]
    public void An_event_id_equal_to_an_unmaterialized_intent_is_a_projection()
    {
        DispatchWaitDiagnostic.IsUnmaterializedProjection(IntentId, "capacity consume refused: boom", [IntentId])
            .ShouldBeTrue();
    }

    [Test]
    [Arguments("task abcd branched from master without CARD-0540's kept branch c540-containing.")]
    [Arguments("sibling containment was evaluated against master; the worktree was cut from project-default (DefaultBranch).")]
    [Arguments("configured default branch 'project-default' did not resolve; falling back to HEAD.")]
    [Arguments("Same-card warning inspection incomplete: git_inspection_error; 4 raw.")]
    [Arguments("Worktree base changed after create preview: Continue abc -> Target (Git/card state changed).")]
    [Arguments("Worktree base preview could not be compared with launch; inspect task provenance.")]
    [Arguments("[dispatch-base aabb task=ccdd warning=eeff]\nkept out of the detail path")]
    public void Dispatch_base_detail_is_a_projection_even_with_a_fresh_event_id(string detail)
    {
        DispatchWaitDiagnostic.IsUnmaterializedProjection(OtherId, detail, [IntentId]).ShouldBeTrue();
        DispatchWaitDiagnostic.IsDispatchBaseDetail(detail).ShouldBeTrue();
    }

    [Test]
    [Arguments("capacity consume refused: 40001")]
    [Arguments("capacity non-postgres commit fault")]
    [Arguments("dispatched on Grok at 12% remaining, resets in 3h (quota gate was passed/overridden at create)")]
    [Arguments("launch enqueue refused after the committed claim: busy; the Starting session awaits interrupted-launch recovery")]
    [Arguments(null)]
    [Arguments("")]
    public void A_non_projection_warning_stays_outside_the_crash_cut(string? detail)
    {
        DispatchWaitDiagnostic.IsUnmaterializedProjection(OtherId, detail, [IntentId]).ShouldBeFalse();
    }

    [Test]
    public void The_failure_text_names_every_observed_warning()
    {
        var text = DispatchWaitDiagnostic.FormatWarnings(
        [
            new(OtherId, "capacity consume refused: boom"),
            new(IntentId, "task abcd branched from master without CARD-0540's kept branch c540-a."),
        ]);
        text.ShouldContain(OtherId.ToString("N") + ":capacity consume refused: boom");
        text.ShouldContain(IntentId.ToString("N") + ":task abcd branched from master");
        text.ShouldContain("kept branch c540-a");
    }
}
