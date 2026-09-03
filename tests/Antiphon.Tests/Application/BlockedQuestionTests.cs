using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0033 S1: isolate the trailing question from the report that preceded it.</summary>
[Category("Unit")]
public class BlockedQuestionTests
{
    [Test]
    public void a_trailing_question_paragraph_is_the_question_and_the_rest_is_context()
    {
        var report = """
            Added Fizz(int) and wired the parser.

            142 passed, 0 failed.

            Buzz throws on negatives — should Fizz match that?
            """;

        AgentTaskReplyService.LooksLikeAQuestion(report).ShouldBeTrue();
        BlockedQuestion.TryExtract(report, out var question, out var context).ShouldBeTrue();
        question.ShouldBe("Buzz throws on negatives — should Fizz match that?");
        context.ShouldBe("Added Fizz(int) and wired the parser.\n\n142 passed, 0 failed.");
    }

    [Test]
    public void a_two_line_question_paragraph_keeps_both_lines()
    {
        var report = "I finished the parser.\n\nShould we accept negatives\non the public API?";

        BlockedQuestion.TryExtract(report, out var question, out var context).ShouldBeTrue();
        question.ShouldBe("Should we accept negatives\non the public API?");
        context.ShouldBe("I finished the parser.");
    }

    [Test]
    public void a_question_that_is_the_whole_report_has_no_context()
    {
        BlockedQuestion.TryExtract("Should I accept negative inputs?", out var question, out var context)
            .ShouldBeTrue();
        question.ShouldBe("Should I accept negative inputs?");
        context.ShouldBeNull();
    }

    [Test]
    public void crlf_reports_extract_the_same_as_lf()
    {
        var report = "Added Fizz(int).\r\n\r\nBuzz throws on negatives — should Fizz match that?";

        BlockedQuestion.TryExtract(report, out var question, out var context).ShouldBeTrue();
        question.ShouldBe("Buzz throws on negatives — should Fizz match that?");
        context.ShouldBe("Added Fizz(int).");
    }

    [Test]
    public void a_report_with_no_trailing_question_is_not_extracted()
    {
        BlockedQuestion.TryExtract("142 passed, 0 failed. Build clean.", out _, out _)
            .ShouldBeFalse();
    }

    [Test]
    public void blocked_event_detail_carries_the_question()
    {
        var detail = BlockedQuestion.BlockedEventDetail(
            "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?");
        detail.ShouldBe("Delegate asked: Buzz throws on negatives — should Fizz match that?");
    }

    [Test]
    public void replied_event_detail_carries_origin_round_and_text()
    {
        var detail = BlockedQuestion.RepliedEventDetail(AnswerOrigin.Web, 2, "yes, accept negatives");
        detail.ShouldBe("Answered via Web (round 2): yes, accept negatives");
        var (answer, origin) = BlockedQuestion.AnswerFromEventDetail(detail);
        answer.ShouldBe("yes, accept negatives");
        origin.ShouldBe(AnswerOrigin.Web);
    }
}
