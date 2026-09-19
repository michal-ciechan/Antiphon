using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class CodexReadyTrackerTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1000);

    [Test]
    public void Ready_requires_the_full_settle_interval()
    {
        var tracker = new CodexReadyTracker(Settle);
        var ready = CodexStartupScreen.Classify(CodexStartupFixtures.P3);
        tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse("R-17: first observation");
        tracker.Observe(ready, TimeSpan.FromMilliseconds(999))
            .ShouldBeFalse("R-17: Ready.ShouldBeFalse() at settle-minus-one");
    }

    [Test]
    public void No_mcp_and_completed_mcp_both_require_only_the_positive_settle()
    {
        var noMcp = new CodexReadyTracker(Settle);
        var ready = CodexStartupScreen.Classify(CodexStartupFixtures.P3);
        noMcp.Observe(ready, TimeSpan.Zero).ShouldBeFalse();
        noMcp.Observe(ready, TimeSpan.FromMilliseconds(999)).ShouldBeFalse("V-2: false at 999");
        noMcp.Observe(ready, TimeSpan.FromMilliseconds(1000)).ShouldBeTrue("V-2: true at 1,000 on a new sample");

        var delayed = new CodexReadyTracker(Settle);
        var loading = CodexStartupScreen.Classify(
            CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading"));
        var mcp = CodexStartupScreen.Classify(CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Starting MCP servers (1/3): cua_repl"));
        delayed.Observe(loading, TimeSpan.FromMilliseconds(2000)).ShouldBeFalse("V-2: loading to 2,000");
        delayed.Observe(mcp, TimeSpan.FromMilliseconds(2050)).ShouldBeFalse("V-2: MCP at 2,050");
        delayed.Observe(ready, TimeSpan.FromMilliseconds(12_000)).ShouldBeFalse("V-2: positive at 12,000 starts settle");
        delayed.Observe(ready, TimeSpan.FromMilliseconds(12_999)).ShouldBeFalse();
        delayed.Observe(ready, TimeSpan.FromMilliseconds(13_000)).ShouldBeTrue("V-2: earliest success 13,000");
        delayed.McpEverSeen.ShouldBeTrue();
    }

    [Test]
    public void Every_blocker_and_incomplete_frame_restarts_settling()
    {
        var tracker = new CodexReadyTracker(Settle);
        var ready = CodexStartupScreen.Classify(CodexStartupFixtures.P3);
        var loading = CodexStartupScreen.Classify(
            CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading"));
        tracker.Observe(ready, TimeSpan.Zero);
        tracker.Observe(ready, TimeSpan.FromMilliseconds(999));
        tracker.Observe(loading, TimeSpan.FromMilliseconds(1000)).ShouldBeFalse();
        tracker.Observe(ready, TimeSpan.FromMilliseconds(1000)).ShouldBeFalse("R-19: new candidate");
        tracker.Observe(ready, TimeSpan.FromMilliseconds(1999))
            .ShouldBeFalse("R-19: Ready.ShouldBeFalse() at the old candidate deadline");
    }

    [Test]
    public void Relevant_layout_changes_restart_settling()
    {
        var tracker = new CodexReadyTracker(Settle);
        var a = CodexStartupScreen.Classify(CodexStartupFixtures.P3);
        var b = CodexStartupScreen.Classify(
            CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "other-model high"));
        var c = CodexStartupScreen.Classify(
            CodexStartupFixtures.ReplaceComposer(CodexStartupFixtures.P3, "› " + CodexStartupScreen.HintWriteTests));
        var d = CodexStartupScreen.Classify(
            CodexStartupFixtures.ReplaceFooter(CodexStartupFixtures.P3, "  other-model high · D:\\work"));
        tracker.Observe(a, TimeSpan.Zero);
        tracker.Observe(b, TimeSpan.FromMilliseconds(999)).ShouldBeFalse("R-20: model change");
        tracker.Observe(c, TimeSpan.FromMilliseconds(1998)).ShouldBeFalse("R-20: hint change");
        tracker.Observe(d, TimeSpan.FromMilliseconds(2997))
            .ShouldBeFalse("R-20: Ready.ShouldBeFalse() after model/hint/footer changes at settle-minus-one");
    }
}
