using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class CodexV0160StartupTests
{
    [Test]
    public void C907_running_frame_blocks_startup()
    {
        var observation = CodexStartupScreen.Classify(CodexStartupFixtures.V0160Running);
        observation.IsReady.ShouldBeFalse("C907 active frame");
        observation.Reason.ShouldBe(CodexStartupReason.Working);
    }

    [Test]
    public void C907_idle_frame_requires_full_settle_after_working()
    {
        var ready = CodexStartupScreen.Classify(CodexStartupFixtures.V0160Idle);
        ready.Reason.ShouldBe(CodexStartupReason.Ready, "C907 captured current idle");
        var active = CodexStartupScreen.Classify(CodexStartupFixtures.V0160Running);
        var tracker = new CodexReadyTracker(TimeSpan.FromSeconds(1));
        tracker.Observe(ready, TimeSpan.Zero).ShouldBeFalse("C907 first sample");
        tracker.Observe(active, TimeSpan.FromMilliseconds(999)).ShouldBeFalse("C907 blocker resets");
        tracker.Observe(ready, TimeSpan.FromMilliseconds(1000)).ShouldBeFalse("C907 new candidate");
        tracker.Observe(ready, TimeSpan.FromMilliseconds(1999)).ShouldBeFalse("C907 new settle minus one");
        tracker.Observe(ready, TimeSpan.FromMilliseconds(2000)).ShouldBeTrue("C907 full settle");
        var immediate = new CodexReadyTracker(TimeSpan.Zero);
        immediate.Observe(ready, TimeSpan.Zero).ShouldBeFalse("C907 zero settle still needs two samples");
        immediate.Observe(ready, TimeSpan.Zero).ShouldBeTrue("C907 second completed sample");
    }

    [Test]
    public void C907_footer_only_ready_needs_complete_current_layout()
    {
        var idle = CodexStartupFixtures.V0160Idle;
        CodexStartupScreen.Classify(idle).IsReady.ShouldBeTrue("C907 captured warning chip allowed");
        foreach (var (label, derived) in new[]
        {
            ("no banner", idle.Replace("OpenAI Codex", "unidentified program", StringComparison.Ordinal)),
            ("occupied composer", CodexStartupFixtures.ReplaceComposer(idle, "› unfinished draft")),
            ("context only", CodexStartupFixtures.ReplaceFooter(idle, "100% context left")),
            ("clipped footer", CodexStartupFixtures.ClipFooter(idle)),
            ("invalid model", CodexStartupFixtures.ReplaceFooter(idle, "other-model low · /tmp/c907")),
            ("invalid effort", CodexStartupFixtures.ReplaceFooter(idle, "GPT-6.1-Sol invalid · /tmp/c907")),
            ("invalid path", CodexStartupFixtures.ReplaceFooter(idle, "GPT-6.1-Sol low · nowhere"))
        })
        {
            var observation = CodexStartupScreen.Classify(derived);
            observation.IsReady.ShouldBeFalse("C907 derived " + label);
            observation.Reason.ShouldBe(CodexStartupReason.Unknown, "C907 derived " + label);
        }
    }

    [Test]
    public void C907_running_queue_footer_blocks_ready()
    {
        var captured = File.ReadAllText(Path.Combine(CodexStartupFixtures.DirectoryPath, "v0160-queue-draft.txt"));
        captured.Contains("tab to queue message", StringComparison.Ordinal).ShouldBeTrue("C907 captured footer token");
        CodexStartupScreen.Classify(captured).Reason.ShouldBe(CodexStartupReason.Working,
            "C907 captured both-active precedence");

        // Derived independent witness: the draft capture lacks an idle composer/model footer.
        // Transplant its exact queue token into the captured idle frame's bottom footer.
        const string token = "tab to queue message";
        var idle = CodexStartupFixtures.V0160Idle;
        var lines = idle.Split('\n');
        var footer = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        lines[footer] += " " + token;
        var queueOnly = string.Join('\n', lines);
        CodexWorkingIndicator.IsVisible(queueOnly).ShouldBeFalse("C907 independent queue witness");
        var observation = CodexStartupScreen.Classify(queueOnly);
        observation.IsReady.ShouldBeFalse("C907 queue token independently blocks");
        observation.Reason.ShouldBe(CodexStartupReason.QueueMode, "C907 earlier classifier arms absent");
        queueOnly.Replace(" " + token, "", StringComparison.Ordinal).ShouldBe(idle,
            "C907 only the queue token differs");
        CodexStartupScreen.Classify(idle).Reason.ShouldBe(CodexStartupReason.Ready,
            "C907 removing only the queue token restores positive Ready");
    }
}
