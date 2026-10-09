using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S6: the owner sentences, verbatim, in <c>docs/session-runtime-invariants.md</c> (all
/// five, each followed by its pins), with the detection and reported-not-recovered sentences
/// repeated in <c>docs/orchestration-loop.md</c> and <c>docs/agent-kinds.md</c>; the retired
/// CARD-0312 restart/latch promise and the CARD-1151 <c>AlwaysOnExceptionSentence</c> absent from
/// all three; and the minutes the detection sentence names checked against the policy's own
/// clocks at the shipped defaults. A documentation pin, not behavioural proof: every pin is a
/// <c>nameof</c>, so renaming a pinned test without the document breaks the build. Design V-13 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// </summary>
[Category("Unit")]
public class StandingBootDocumentationTests
{
    internal const string DetectionSentence =
        "A taskless AlwaysOn boot stall is detection, never a stop (CARD-1156, operator decision "
        + "option A): the boot reply watchdog records at most one Warning receipt per episode "
        + "(accepted generation, launch clock and boot prompt sequence) from the boot due "
        + "(`Delegation:BootModelWaitDeadlineMinutes`, 8 minutes) and at most one Error receipt from "
        + "the operator threshold (20 minutes with defaults), each in its own context and "
        + "transaction, keeps the watch armed, and never stops, kills, fails, restarts, latches or "
        + "types into the session or writes its supervision state.";

    internal const string AttentionSentence =
        "Its attention row is projected from the current boot facts, not from the receipts: Warning "
        + "from the boot due, Error from the operator threshold or a recorded Error receipt of the "
        + "same episode, and gone once the model replies, the session ends or a new launch or prompt "
        + "opens a new episode.";

    internal const string UnknownSentence =
        "Unknown evidence keeps the session: a policy input that cannot be read positively "
        + "(provider delivery support, Grok rules, session row, generation, owner, task, prompt or "
        + "reply) records nothing, an open task on the session (Queued, Dispatched, Working or "
        + "Blocked) records nothing, a boot wait `<= 0` turns the watch off, and a missing runtime, "
        + "a failed transcript pull or an absent runner listing still records from the stored "
        + "transcript.";

    internal const string LegacyDiagnosticSentence =
        "A live session that no AlwaysOn agent points at and no Dispatched or Working task owns "
        + "keeps the generic `bootSeq=` diagnostic: one Warning incident per boot prompt, then the "
        + "watch is disarmed; it stops nothing and writes no supervision state either.";

    internal const string ReportedSentence =
        "A hung taskless AlwaysOn boot is reported, not recovered: the session keeps running and "
        + "keeps its seat until the model replies, an operator acts or the process exits; CARD-0079 "
        + "remains the only automatic stop of a Working session, and no deadline releases the seat.";

    /// <summary>
    /// The CARD-1151 S4 repair sentence (Review 04808159 F1) that CARD-1156 makes false. It was
    /// <c>BootStallDocumentationTests.AlwaysOnExceptionSentence</c>; design reversal 4 replaces it.
    /// </summary>
    internal const string RetiredAlwaysOnExceptionSentence =
        "A taskless AlwaysOn session is the exception: the boot reply watchdog still raises its "
        + "incident and stops the session for the existing standing-agent restart ladder "
        + "(`BootReplyWatchdogService`, unchanged by CARD-1151; CARD-1156).";

    /// <summary>The CARD-0312 S4 promises the design names, absent from every owner document.</summary>
    private static readonly string[] RetiredOwnerPromises =
    [
        "stops the session for the existing standing-agent restart ladder",
        "two consecutive probe-driven restarts",
        "A human StartAsync clears the latch",
    ];

    /// <summary>Restart-ladder wording, absent from the three changed standing sections.</summary>
    private static readonly string[] RetiredSectionWording =
    [
        "restart ladder",
        "latches off",
        "latches it off",
        "stopped restarting",
        "kills the session",
        "the watchdog stops",
        "still stops",
    ];

    private static readonly string[] OwnerSentences =
        [DetectionSentence, AttentionSentence, UnknownSentence, LegacyDiagnosticSentence, ReportedSentence];

    [Test]
    public Task C1156_Docs_name_detection_clocks_custody_and_compaction_exception()
    {
        var runtime = Read("docs/session-runtime-invariants.md");
        var loop = Read("docs/orchestration-loop.md");
        var kinds = Read("docs/agent-kinds.md");

        // The owner carries all five sentences, each followed by its pins.
        Pinned(runtime, DetectionSentence,
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody)),
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Episodes_deduplicate_and_reopen_only_for_new_identity)),
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Telemetry_faults_preserve_custody_and_future_writes)),
            Pin(nameof(BootReplyWatchdogTests),
                nameof(BootReplyWatchdogTests.boot_silence_preserves_existing_failure_history_without_creating_a_latch)));
        Pinned(runtime, AttentionSentence,
            Pin(nameof(StandingBootAttentionTests),
                nameof(StandingBootAttentionTests.C1156_Current_boot_attention_survives_optional_history)),
            Pin(nameof(StandingBootAttentionTests),
                nameof(StandingBootAttentionTests.C1156_Positive_resolution_clears_only_the_current_episode)),
            Pin(nameof(StandingBootAttentionTests),
                nameof(StandingBootAttentionTests.C1156_Recorded_operator_stage_survives_clock_rollback)));
        Pinned(runtime, UnknownSentence,
            Pin(nameof(StandingBootWatchPolicyTests),
                nameof(StandingBootWatchPolicyTests.C1156_Emission_requires_each_positive_condition)),
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Fresh_evidence_revokes_stale_emission)),
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery)));
        Pinned(runtime, LegacyDiagnosticSentence,
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Evidence_variants_never_authorize_recovery)));
        Pinned(runtime, ReportedSentence,
            $"`{nameof(CheckCompactionRecoveryFlowTests)}`",
            Pin(nameof(StandingBootWatchdogTests),
                nameof(StandingBootWatchdogTests.C1156_Working_boot_keeps_its_session_and_supervisor_custody)));

        // The operator-facing loop doc and the Grok note repeat the two sentences an operator acts on.
        foreach (var (owner, text) in new[] { ("loop", loop), ("agent-kinds", kinds) })
        {
            text.ShouldContain(DetectionSentence, Case.Sensitive, $"{owner}: detection sentence");
            text.ShouldContain(ReportedSentence, Case.Sensitive, $"{owner}: reported-not-recovered sentence");
        }

        // The retired promises are gone from every owner.
        foreach (var (owner, text) in new[] { ("runtime", runtime), ("loop", loop), ("agent-kinds", kinds) })
        {
            text.ShouldNotContain(RetiredAlwaysOnExceptionSentence, Case.Sensitive, $"{owner}: CARD-1151 AlwaysOn exception");
            foreach (var promise in RetiredOwnerPromises)
                text.ShouldNotContain(promise, Case.Sensitive, $"{owner}: retired promise '{promise}'");
        }

        // No restart-ladder wording in the three changed standing sections.
        var sections = new[]
        {
            ("runtime", Section(runtime,
                "- **A hung taskless AlwaysOn boot is reported, not recovered** (CARD-1156).",
                "<!-- CARD-0254 preserved source begins -->")),
            ("loop", Section(loop,
                "**A taskless AlwaysOn session showing only its boot prompt is reported, not recovered (CARD-1156).**",
                "**A session past the general deadline is Failed")),
            ("agent-kinds", Section(kinds,
                "- **Grok Build 1.0.13 has no first-token timeout",
                "- **Grok's own diagnostics, for a human")),
        };
        foreach (var (owner, section) in sections)
        {
            foreach (var wording in RetiredSectionWording)
                section.ShouldNotContain(wording, Case.Insensitive, $"{owner}: retired wording '{wording}'");
        }

        // The clocks the sentence names are the ones the policy computes at the shipped defaults.
        var defaults = new DelegationSettings();
        defaults.BootModelWaitDeadlineMinutes.ShouldBe(8, "the sentence names an 8-minute boot due");
        var promptAt = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var facts = StandingBootWatchPolicy.Facts(
            promptAt, promptAt, promptSequence: 1, promptAt,
            defaults.BootModelWaitDeadlineMinutes, defaults.ModelWaitDeadlineMinutes);
        (facts.BootDueAt - promptAt).ShouldBe(TimeSpan.FromMinutes(8), "boot due at the defaults");
        (facts.OperatorDueAt - promptAt).ShouldBe(TimeSpan.FromMinutes(20), "operator threshold at the defaults");
        StandingBootWatchPolicy.DueStage(facts, facts.BootDueAt)
            .ShouldBe(StandingBootWatchPolicy.Stage.Detected, "the Warning receipt's stage from the boot due");
        StandingBootWatchPolicy.DueStage(facts, facts.OperatorDueAt)
            .ShouldBe(StandingBootWatchPolicy.Stage.NeedsOperator, "the Error receipt's stage from the operator threshold");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Review f5b1d580 F-1: the <c>AttentionKind.LivenessProbeFailed</c> summary describes the
    /// standing row as current-facts attention that can render without any incident and claims no
    /// delivery, and describes the legacy <c>bootSeq=</c> incident projection separately. The
    /// retired summary (a transcript-confirmed delivery, projected from incidents) is rejected.
    /// </summary>
    [Test]
    public Task C1156_LivenessProbeFailed_summary_claims_no_delivery_and_names_both_projections()
    {
        var summary = EnumSummary(Read("server/Application/Dtos/AttentionDtos.cs"), "LivenessProbeFailed = 27,");

        foreach (var sentence in new[]
        {
            "Neither projection below matches the prompt against the intended request, so neither row "
            + "is a delivery verdict.",
            "For a taskless AlwaysOn session the row is projected from the session's current facts at read "
            + "time: the age of the latest prompt record on its current launch against the boot due "
            + "(Warning) and the operator threshold (Error), with no qualifying boot-model reply on that "
            + "launch (as decided by <c>BootReplyWatch.HasModelReplySinceAsync</c>, which ignores Grok "
            + "rules-turn responses).",
            "It renders with or without a saved <c>standingBoot:v1;</c> incident;",
            "It makes no claim about delivery: the latest prompt record may be a queued one, and the row "
            + "labels it so.",
            "For a live session that no AlwaysOn agent points at and no open task owns, the row is "
            + "projected from a <c>bootSeq=</c>",
        })
        {
            summary.ShouldContain(sentence, Case.Sensitive, $"LivenessProbeFailed summary: {sentence}");
        }

        foreach (var retired in new[]
        {
            "transcript-confirmed",
            "was delivered",
            "rung 5 of the delivery evidence ladder",
            "Projected from open",
            // Review 5906dbf4 F-1: an initialized Grok session's rules-turn rows are excluded, so the
            // summary must not enumerate row kinds as if any such row resolved the episode.
            "assistant, thinking, tool or turn-end",
        })
        {
            summary.ShouldNotContain(retired, Case.Insensitive, $"LivenessProbeFailed summary: retired '{retired}'");
        }

        return Task.CompletedTask;
    }

    /// <summary>The XML summary immediately above <paramref name="member"/>, its <c>///</c> lines joined by spaces.</summary>
    private static string EnumSummary(string text, string member)
    {
        var at = text.IndexOf(member, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"member missing: {member}");
        var lines = text[..at].Split('\n');
        var summary = new List<string>();
        for (var i = lines.Length - 2; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("///", StringComparison.Ordinal))
                break;
            summary.Insert(0, line[3..].Trim());
        }

        summary.ShouldNotBeEmpty($"no summary above {member}");
        return string.Join(" ", summary);
    }

    private static string Pin(string type, string method) => $"`{type}.{method}`";

    /// <summary>The sentence verbatim, then each pin before the next owner sentence or bullet.</summary>
    private static void Pinned(string text, string sentence, params string[] pins)
    {
        var at = text.IndexOf(sentence, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"owner sentence missing: {sentence}");
        var after = text[(at + sentence.Length)..];
        var end = after.IndexOf("\n- ", StringComparison.Ordinal);
        var nextSentence = OwnerSentences
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
