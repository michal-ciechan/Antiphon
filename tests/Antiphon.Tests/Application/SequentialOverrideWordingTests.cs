using Antiphon.Server.Application.Exceptions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0569. The 409 text, the API doc, the delegate comments, and the orchestrator
/// bundle must state the axis and same-stage rule. The old "user asked for parallel
/// work this turn" sentence and the opt-in worktree collision sentence must not return.
/// </summary>
[Category("Unit")]
public sealed class SequentialOverrideWordingTests
{
    private const string UserAsked = "if the user asked for parallel work this turn";

    private const string AxisCoda =
        "Re-send with ignoreConcurrencyLimit=true only when axis=absolute and no listed "
        + "occupant is in the stage you are dispatching; a same-stage collision defers.";

    private const string ApiRetry =
        "only when `population` is `open`, `canOverride` is true, the axis is `absolute`, and no listed "
        + "occupant is in the stage you are dispatching; a same-stage collision defers.";

    private const string DelegateAxis =
        "the axis is absolute with no occupant in the stage you are dispatching";

    private const string DelegateDefer = "A same-stage collision still defers.";

    private const string WorktreeDefault =
        "Fresh workers already use a worktree; -Shared opts out of that default.";

    private const string StageWorktree = "each in its own -Worktree.";

    private const string OptInWorktree = "pass -Worktree so they cannot overwrite";

    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    [Test]
    public void coda_states_the_axis_rule_not_the_user_asked_sentence()
    {
        var coda = ConcurrencyLimitException.Coda;
        coda.ShouldNotContain(UserAsked);
        coda.ShouldNotContain("sequentially");
        coda.ShouldBe(AxisCoda);
        var infected = coda.Replace(AxisCoda, UserAsked, StringComparison.Ordinal);
        Should.Throw<ShouldAssertException>(() => infected.ShouldNotContain(UserAsked));
        Should.Throw<ShouldAssertException>(() => infected.ShouldBe(AxisCoda));
    }

    [Test]
    public void api_doc_states_the_axis_rule_not_the_user_asked_sentence()
    {
        var doc = Read("docs", "antiphon-api.md");
        doc.ShouldNotContain(UserAsked);
        doc.ShouldContain(ApiRetry);
        var infected = doc.Replace(ApiRetry, UserAsked, StringComparison.Ordinal);
        Should.Throw<ShouldAssertException>(() => infected.ShouldNotContain(UserAsked));
        Should.Throw<ShouldAssertException>(() => infected.ShouldContain(ApiRetry));
    }

    [Test]
    public void delegate_comments_name_the_axis_rule_not_sequential_by_default()
    {
        var script = Read("scripts", "delegate.ps1");
        script.ShouldNotContain("sequential-by-default");
        script.ShouldNotContain("user asked for parallel work");
        script.ShouldContain(DelegateAxis);
        script.ShouldContain(DelegateDefer);
        foreach (var ch in script)
            ((int)ch).ShouldBeLessThan(128, "delegate.ps1 stays ASCII");
        var infected = script.Replace(
            DelegateAxis,
            "sequential-by-default and only when the user asked for parallel work",
            StringComparison.Ordinal);
        Should.Throw<ShouldAssertException>(() => infected.ShouldNotContain("sequential-by-default"));
        Should.Throw<ShouldAssertException>(() => infected.ShouldContain(DelegateAxis));
    }

    [Test]
    public void bundle_states_the_worktree_default_beside_stage_policy()
    {
        var bundle = Read("server", "Bundles", "orchestrator.md");
        bundle.ShouldNotContain(OptInWorktree);
        bundle.ShouldNotContain("working directory by default");
        bundle.ShouldContain(WorktreeDefault);
        bundle.ShouldContain(StageWorktree);
        var infected = bundle.Replace(WorktreeDefault, OptInWorktree, StringComparison.Ordinal);
        Should.Throw<ShouldAssertException>(() => infected.ShouldNotContain(OptInWorktree));
        Should.Throw<ShouldAssertException>(() => infected.ShouldContain(WorktreeDefault));
        infected.ShouldContain(StageWorktree);
    }
}
