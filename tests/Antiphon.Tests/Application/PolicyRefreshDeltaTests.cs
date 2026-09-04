using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0334 S2 — stamp-line delta is keys and versions only, never bundle text.</summary>
[Category("Unit")]
public class PolicyRefreshDeltaTests
{
    [Test]
    public void Format_names_changed_added_and_removed_bundle_keys_and_changed_files()
    {
        var delta = PolicyRefreshDelta.Format(
            launchedBundles: "orchestrator v26dea68f, style-brief v664a6353",
            currentBundles: "orchestrator v3c1f0a9e, board-api v51981dbe",
            launchedFiles: "AGENTS.md v11111111, docs/orchestration-loop.md v22222222",
            currentFiles: "AGENTS.md v33333333");

        delta.ShouldContain("orchestrator v26dea68f → v3c1f0a9e");
        delta.ShouldContain("board-api added v51981dbe");
        delta.ShouldContain("style-brief removed");
        delta.ShouldContain("AGENTS.md, docs/orchestration-loop.md changed");
        foreach (var bundle in InstructionBundles.All.Values)
            delta.ShouldNotContain(bundle.Text);
    }

    [Test]
    public void Format_is_empty_when_stamps_match()
    {
        PolicyRefreshDelta.Format(
            "orchestrator v26dea68f", "orchestrator v26dea68f",
            "AGENTS.md v11111111", "AGENTS.md v11111111")
            .ShouldBe("");
    }

    [Test]
    public void PolicyRefreshResumeBody_never_contains_bundle_text()
    {
        var delta = PolicyRefreshDelta.Format(
            "", InstructionBundles.Get(InstructionBundles.Orchestrator).Stamp,
            "AGENTS.md v00000000", "AGENTS.md v11111111");
        var body = ChannelPreamble.PolicyRefreshResumeBody(delta);

        body.ShouldContain("orchestrator added v");
        body.ShouldContain("AGENTS.md changed");
        body.ShouldContain(ChannelContracts.NoReplyToken);
        foreach (var bundle in InstructionBundles.All.Values)
            body.ShouldNotContain(bundle.Text);
    }
}
