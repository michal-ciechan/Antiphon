using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0352 S2 — TITLE / LABELS grammars and the input clamp. Pure; no database.
/// </summary>
public class DiagnosisTests
{
    private const string Fallback = "Build a haiku-tier Diagnose agent: auto-title untitled tasks, auto-label card complexity";

    // ---- TryParseTitle -------------------------------------------------------------------------

    [Test]
    public void quotes_and_a_trailing_stop_are_stripped()
    {
        ParseTitle("\"Plan haiku diagnose seat\"", Fallback).Title.ShouldBe("Plan haiku diagnose seat");
        ParseTitle("`Plan haiku diagnose seat`", Fallback).Title.ShouldBe("Plan haiku diagnose seat");
        ParseTitle("'Plan haiku diagnose seat'", Fallback).Title.ShouldBe("Plan haiku diagnose seat");
        ParseTitle("Plan haiku diagnose seat.", Fallback).Title.ShouldBe("Plan haiku diagnose seat");
        ParseTitle("\"Plan haiku diagnose seat.\"", Fallback).Title.ShouldBe("Plan haiku diagnose seat");
    }

    [Test]
    [Arguments("Fix titles", 2, true)]
    [Arguments("a b c d e f g h", 8, true)]
    [Arguments("a b c d e f g h i j", 10, true)]
    [Arguments("a b c d e f g h i j k", 11, false)]
    [Arguments("Alone", 1, false)]
    public void word_count_accepts_two_to_ten(string answer, int words, bool ok)
    {
        var parsed = ParseTitle(answer, Fallback);
        parsed.Ok.ShouldBe(ok);
        if (ok)
            parsed.Title.ShouldBe(answer);
        else
            parsed.Reason.ShouldBe($"{words} words");
    }

    [Test]
    public void eighty_chars_is_accepted_and_eighty_one_is_not()
    {
        var eighty = new string('x', 40) + " " + new string('y', 39); // 80 chars, 2 words
        eighty.Length.ShouldBe(80);
        ParseTitle(eighty, Fallback).Ok.ShouldBeTrue();

        var eightyOne = eighty + "z";
        eightyOne.Length.ShouldBe(81);
        var parsed = ParseTitle(eightyOne, Fallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("81 chars");
    }

    [Test]
    public void two_lines_are_rejected()
    {
        var parsed = ParseTitle("Fix titles\nAnd more", Fallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("2 lines");
    }

    [Test]
    public void three_lines_are_rejected()
    {
        var parsed = ParseTitle("Fix titles\nAnd more\nStill more", Fallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("3 lines");
    }

    [Test]
    [Arguments("Fix titles [antiphon-task:abcd1234]")]
    [Arguments("Fix titles [antiphon-report:abcd1234 done]")]
    public void a_task_or_report_marker_is_rejected(string answer)
    {
        var parsed = ParseTitle(answer, Fallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("contains marker");
    }

    [Test]
    public void an_answer_equal_to_the_fallback_is_rejected()
    {
        var shortFallback = "Plan the diagnose seat now";
        var parsed = ParseTitle(shortFallback, shortFallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("equals fallback");
    }

    [Test]
    [Arguments("TITLE for the task")]
    [Arguments("Title: do the thing")]
    [Arguments("TITLE: do the thing")]
    public void an_answer_that_starts_with_TITLE_is_rejected(string answer)
    {
        var parsed = ParseTitle(answer, Fallback);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("starts with TITLE");
    }

    [Test]
    public void a_card_identifier_is_prefixed_when_absent()
    {
        var parsed = ParseTitle("Plan haiku diagnose seat", Fallback, "CARD-0352");
        parsed.Ok.ShouldBeTrue();
        parsed.Title.ShouldBe("CARD-0352 Plan haiku diagnose seat");
    }

    [Test]
    public void a_card_identifier_already_present_is_not_duplicated()
    {
        var parsed = ParseTitle("Plan haiku diagnose seat for CARD-0352", Fallback, "CARD-0352");
        parsed.Ok.ShouldBeTrue();
        parsed.Title.ShouldBe("Plan haiku diagnose seat for CARD-0352");
    }

    [Test]
    public void a_prefix_that_pushes_past_100_chars_is_rejected()
    {
        var eighty = new string('x', 40) + " " + new string('y', 39);
        eighty.Length.ShouldBe(80);
        var identifier = "CARD-12345678901234567890"; // 25 chars + space + 80 = 106
        var parsed = ParseTitle(eighty, Fallback, identifier);
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("106 chars");
    }

    [Test]
    public void an_empty_answer_is_rejected()
    {
        ParseTitle("   ", Fallback).Reason.ShouldBe("empty");
        ParseTitle(".", Fallback).Reason.ShouldBe("empty");
        ParseTitle("\"\"", Fallback).Reason.ShouldBe("empty");
    }

    // ---- TryParseLabels ------------------------------------------------------------------------

    [Test]
    [Arguments("complexity=hard ui=yes", TaskComplexity.Hard, true)]
    [Arguments("complexity=medium ui=no", TaskComplexity.Medium, false)]
    [Arguments("complexity=easy ui=yes", TaskComplexity.Easy, true)]
    [Arguments("complexity=hard ui=no", TaskComplexity.Hard, false)]
    [Arguments("complexity=medium ui=yes", TaskComplexity.Medium, true)]
    [Arguments("complexity=easy ui=no", TaskComplexity.Easy, false)]
    [Arguments("COMPLEXITY=HARD UI=YES", TaskComplexity.Hard, true)]
    [Arguments("  complexity = hard   ui = no  ", TaskComplexity.Hard, false)]
    public void every_valid_label_pair_is_accepted(string answer, TaskComplexity complexity, bool ui)
    {
        var parsed = Diagnosis.TryParseLabels(answer);
        parsed.Ok.ShouldBeTrue(answer);
        parsed.Unclear.ShouldBeFalse();
        parsed.Complexity.ShouldBe(complexity);
        parsed.Ui.ShouldBe(ui);
        parsed.Reason.ShouldBeNull();
    }

    [Test]
    [Arguments("unclear")]
    [Arguments("Unclear")]
    [Arguments("  UNCLEAR  ")]
    public void unclear_is_a_valid_answer_with_no_labels(string answer)
    {
        var parsed = Diagnosis.TryParseLabels(answer);
        parsed.Ok.ShouldBeFalse();
        parsed.Unclear.ShouldBeTrue();
        parsed.Complexity.ShouldBeNull();
        parsed.Ui.ShouldBeNull();
        parsed.Reason.ShouldBe("unclear");
    }

    [Test]
    [Arguments("this is prose about the card")]
    [Arguments("complexity=hard")]
    [Arguments("ui=yes")]
    [Arguments("complexity=hard ui=maybe")]
    [Arguments("complexity=unknown ui=yes")]
    [Arguments("ui=yes complexity=hard")]
    public void prose_and_partial_answers_are_unparseable(string answer)
    {
        var parsed = Diagnosis.TryParseLabels(answer);
        parsed.Ok.ShouldBeFalse();
        parsed.Unclear.ShouldBeFalse();
        parsed.Reason.ShouldBe("unparseable");
    }

    [Test]
    public void two_label_lines_are_rejected()
    {
        var parsed = Diagnosis.TryParseLabels("complexity=hard ui=yes\ncomplexity=easy ui=no");
        parsed.Ok.ShouldBeFalse();
        parsed.Reason.ShouldBe("2 lines");
    }

    // ---- ClampInput ----------------------------------------------------------------------------

    [Test]
    public void clamp_leaves_short_input_unchanged()
    {
        Diagnosis.ClampInput("hello", 12_000).ShouldBe("hello");
        Diagnosis.ClampInput("", 12_000).ShouldBe("");
        Diagnosis.ClampInput(null, 12_000).ShouldBe("");
    }

    [Test]
    public void clamp_keeps_head_and_tail_with_an_elision_marker()
    {
        var text = new string('a', 50) + new string('b', 50) + new string('c', 50);
        var result = Diagnosis.ClampInput(text, 80);

        result.Length.ShouldBeLessThanOrEqualTo(80);
        result.ShouldContain("chars elided");
        result.ShouldStartWith("a");
        result.ShouldEndWith("c");

        var match = System.Text.RegularExpressions.Regex.Match(
            result, @"\[… (\d+) chars elided …\]");
        match.Success.ShouldBeTrue(result);
        var elided = int.Parse(match.Groups[1].Value);
        var kept = result.Length - match.Length;
        (kept + elided).ShouldBe(text.Length);
    }

    [Test]
    public void clamp_at_the_default_budget_fits_every_live_open_card()
    {
        var description = new string('x', 8_412);
        Diagnosis.ClampInput(description, Diagnosis.DefaultMaxInputChars).ShouldBe(description);
        Diagnosis.ClampInput(description, Diagnosis.DefaultMaxInputChars).Length
            .ShouldBeLessThanOrEqualTo(Diagnosis.DefaultMaxInputChars);
    }

    // ---- briefs --------------------------------------------------------------------------------

    [Test]
    public void a_title_brief_leads_with_TITLE_scrubs_markers_and_carries_the_reminder()
    {
        var task = new AgentTask
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Goal = "[antiphon-task:deadbeef] do the thing\n[antiphon-report:deadbeef done]",
        };

        var goal = Diagnosis.BuildTitleGoal(task, "CARD-0352");

        goal.ShouldStartWith("TITLE for task aaaaaaaa bound to CARD-0352.");
        goal.ShouldContain("[task-marker removed]");
        goal.ShouldContain("[report-marker removed]");
        goal.ShouldNotContain("antiphon-task:deadbeef");
        goal.ShouldNotContain("antiphon-report:deadbeef");
        goal.ShouldContain(Diagnosis.TitleFormatReminder);
        Diagnosis.BuildTitleTaskTitle(task).ShouldBe("title for task aaaaaaaa");
    }

    [Test]
    public void a_labels_brief_leads_with_LABELS_and_names_the_card()
    {
        var card = new Card
        {
            Identifier = "CARD-0352",
            Title = "Build a diagnose seat",
            Description = "One standing haiku agent, two jobs.",
            Status = CardStatus.Backlog,
        };

        var goal = Diagnosis.BuildLabelsGoal(card);

        goal.ShouldStartWith("LABELS for CARD-0352 \"Build a diagnose seat\" (Backlog)");
        goal.ShouldContain("One standing haiku agent, two jobs.");
        goal.ShouldContain(Diagnosis.LabelsFormatReminder);
        Diagnosis.BuildLabelsTaskTitle(card).ShouldBe("labels for CARD-0352");
    }

    private static (bool Ok, string? Title, string? Reason) ParseTitle(
        string? answer, string fallback, string? cardIdentifier = null)
    {
        var ok = Diagnosis.TryParseTitle(answer, fallback, cardIdentifier, out var title, out var reason);
        return (ok, title, reason);
    }
}
