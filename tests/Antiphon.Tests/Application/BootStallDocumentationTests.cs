using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S4: the owner sentences of the plan, verbatim, in
/// <c>docs/session-runtime-invariants.md</c> (all four) and <c>docs/orchestration-loop.md</c> (the
/// two operator-facing ones), each pinned to its named test; and the absence of any remaining
/// boot kill/retry promise in the changed delegate boot-stall sections. A documentation pin, not
/// behavioural proof.
///
/// <para>Sentence three is the decision-Q-1 option-B form. The plan's option-A sentence ("Only
/// positively non-Working, absent and terminal evidence permits the narrow existing boot
/// failure/retry outcome") describes an outcome option B deleted, so writing it verbatim would
/// document behaviour the code no longer has. Every pin is a <c>nameof</c>, so renaming a pinned
/// test without the document breaks the build, not just this assertion.</para>
/// </summary>
[Category("Unit")]
public class BootStallDocumentationTests
{
    internal const string DetectionSentence =
        "A transcript-confirmed boot prompt with no model reply is detection only while the session "
        + "is Working, runner-listed, or safety evidence is unknown. At eight minutes it records "
        + "BootStallDetected; at the bounded operator threshold (20 minutes with defaults) it asks for "
        + "an operator decision without failure, retry, input, stop or seat release.";

    internal const string DeadlinesSentence =
        "The general and role deadlines do not terminalize that unresolved boot episode; positive "
        + "model progress returns it to ordinary deadline policy.";

    internal const string NoAutomaticOutcomeSentence =
        "No boot evidence permits an automatic failure or retry: under option B the narrow automatic "
        + "boot failure/retry is retired, a terminal or missing session row stays with the "
        + "dead-session reconciler's existing policy, a Pending brief stays with the delivery "
        + "watchdog, a human Retry is the only retry, and S1's pristine absent-launch hold still "
        + "takes precedence.";

    internal const string CompactionSentence =
        "CARD-0079 is the only automatic Working stop authorization; boot-stall detection does not "
        + "call it. Parking is default-off and provides no release deadline for an input-waiting "
        + "session.";

    private static readonly string[] RetiredPromises =
    [
        "kills the session (it produced nothing",
        "the session is killed (it produced nothing",
        "kills the session because it provably produced nothing",
        "nothing is lost",
        "retried once",
        "retries it\n  once",
        "fails the task with",
        "is failed with",
        "put that alias on an AutoDetected hold",
    ];

    [Test]
    public async Task C1151_Docs_describe_detection_and_only_compaction_exception()
    {
        var runtime = Read("docs/session-runtime-invariants.md");
        var loop = Read("docs/orchestration-loop.md");

        // The owner carries all four sentences, each followed by its pins.
        Pinned(runtime, DetectionSentence,
            Pin(nameof(BootStallWorkingTickCharacterizationTests),
                nameof(BootStallWorkingTickCharacterizationTests.Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing)),
            Pin(nameof(BootStallDetectionTests),
                nameof(BootStallDetectionTests.C1151_Operator_escalation_preserves_the_attempt)));
        Pinned(runtime, DeadlinesSentence,
            Pin(nameof(BootStallDetectionTests),
                nameof(BootStallDetectionTests.C1151_Boot_protection_survives_all_deadlines)));
        Pinned(runtime, NoAutomaticOutcomeSentence,
            Pin(nameof(BootStallDetectionTests),
                nameof(BootStallDetectionTests.C1151_Listed_or_unknown_session_is_untouched)),
            Pin(nameof(BootStallDetectionTests),
                nameof(BootStallDetectionTests.C1151_Explicit_retry_retains_operator_semantics)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1149_Absent_launch_is_blocked_with_original_input)));
        Pinned(runtime, CompactionSentence,
            $"`{nameof(CheckCompactionRecoveryFlowTests)}`",
            Pin(nameof(BootStallDetectionTests),
                nameof(BootStallDetectionTests.C1151_Detection_does_not_release_or_park)));

        // The operator-facing loop doc repeats the two sentences an orchestrator acts on and
        // points at the owner.
        loop.ShouldContain(DetectionSentence, Case.Sensitive, "loop: detection sentence");
        loop.ShouldContain(DeadlinesSentence, Case.Sensitive, "loop: deadlines sentence");

        // No kill/retry promise survives in either changed section.
        var runtimeSection = Section(runtime,
            "- **A delivered boot prompt with no assistant row is a PROVIDER STALL",
            "<!-- CARD-0254 preserved source begins -->");
        runtimeSection.ShouldContain("- **A boot stall is detection, never a stop**", Case.Sensitive);
        var loopSection = Section(loop,
            "**A session showing only its own prompt, and WORKING, is a provider stall",
            "**A session past the general deadline is Failed");
        foreach (var (owner, section) in new[] { ("runtime", runtimeSection), ("loop", loopSection) })
        {
            foreach (var promise in RetiredPromises)
                section.ShouldNotContain(promise, Case.Sensitive, $"{owner}: retired promise '{promise}'");
            section.ShouldNotContain("ProviderUnresponsive", Case.Sensitive, $"{owner}: no boot failure code");
        }

        // The Grok note no longer says the deadline ends a hung provider call.
        Read("docs/agent-kinds.md").ShouldNotContain(
            "Antiphon's boot-turn deadline is what ends this", Case.Sensitive, "agent-kinds: retired promise");
    }

    private static string Pin(string type, string method) => $"`{type}.{method}`";

    /// <summary>The sentence verbatim, then each pin before the next owner sentence or bullet.</summary>
    private static void Pinned(string text, string sentence, params string[] pins)
    {
        var at = text.IndexOf(sentence, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"owner sentence missing: {sentence}");
        var after = text[(at + sentence.Length)..];
        var end = after.IndexOf("\n- ", StringComparison.Ordinal);
        var nextSentence = new[] { DetectionSentence, DeadlinesSentence, NoAutomaticOutcomeSentence, CompactionSentence }
            .Select(s => after.IndexOf(s, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (nextSentence >= 0 && (end < 0 || nextSentence < end))
            end = nextSentence;
        var tail = end < 0 ? after : after[..end];
        foreach (var pin in pins)
            tail.ShouldContain(pin, Case.Sensitive, $"pin {pin} must follow: {sentence}");
    }

    private static string Section(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, $"section start missing: {start}");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, $"section end missing: {end}");
        return text[from..to];
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relative)).Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Antiphon.sln")) || File.Exists(Path.Combine(dir, "docs", "orchestration-loop.md")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }
}
