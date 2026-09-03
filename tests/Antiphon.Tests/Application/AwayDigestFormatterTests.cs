using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class AwayDigestFormatterTests
{
    [Test]
    public void Formats_five_rows_then_a_fold_and_honours_the_hard_cap()
    {
        var tasks = Enumerable.Range(1, 8).Select(i => new AwayDigestTaskDto(Guid.NewGuid(), $"abcd{i:0000}",
            new string('x', 200), "A deliberately long result sentence.", DateTime.UtcNow, 1m)).ToList();
        var digest = Empty() with { Finished = tasks };

        var text = AwayDigestFormatter.FormatDigest(digest, new DigestSettings { MaxChars = 3500 }, DateTimeOffset.UtcNow)!;

        text.ShouldContain("+3 more");
        text.Length.ShouldBeLessThanOrEqualTo(AwayDigestFormatter.MaxChars);
    }

    [Test]
    public void Ping_first_line_carries_the_short_task_id()
    {
        var id = Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000");
        var ping = AwayDigestFormatter.FormatPing(new AttentionItemDto(AttentionKind.BlockedQuestion, AlertSeverity.Critical,
            id, null, null, null, "Task", "Blocked", "Can I continue?", DateTime.UtcNow, 1m, []));

        ping.Split('\n')[0].ShouldBe("❓ task 1a2b3c4d needs an answer — Task");
    }

    [Test]
    public void the_ping_carries_the_question_not_the_report_head()
    {
        var id = Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000");
        var question = "Should I accept negative inputs?";
        var ping = AwayDigestFormatter.FormatPing(new AttentionItemDto(
            AttentionKind.BlockedQuestion, AlertSeverity.Critical,
            id, null, null, null, "Fizz", "Blocked — waiting on a human answer.",
            question, DateTime.UtcNow, 1.37m, [AttentionAction.Reply]));

        ping.ShouldContain(question);
        ping.ShouldNotContain(new string('a', 40));
    }

    [Test]
    public void Decision_ping_first_line_carries_the_card_identifier()
    {
        var ping = AwayDigestFormatter.FormatDecisionPing(new AttentionItemDto(AttentionKind.CardNeedsDecision,
            AlertSeverity.Critical, null, null, null, null, "CARD-0010 — Ship the release", "Needs a decision",
            "Choose the release train.", DateTime.UtcNow, null, [], Guid.NewGuid(), Guid.NewGuid()));

        ping.Split('\n')[0].ShouldBe("❓ CARD-0010 needs a decision — Ship the release");
    }

    [Test]
    public void Digest_lists_waiting_decisions_after_needs_you_and_folds_past_five()
    {
        var decisions = Enumerable.Range(1, 8).Select(i => new AwayDigestCardDto($"CARD-{i:0000}", "Choose", DateTime.UtcNow, "A question.")).ToList();
        IReadOnlyList<AwayDigestTaskDto> needsYou = [new AwayDigestTaskDto(Guid.NewGuid(), "1234abcd", "Blocked", "Need input.", DateTime.UtcNow, 0m)];
        var text = AwayDigestFormatter.FormatDigest(Empty() with { NeedsYou = needsYou, Decisions = decisions }, new DigestSettings(), DateTimeOffset.UtcNow)!;

        text.IndexOf("❗ Needs you", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("❓ Decisions", StringComparison.Ordinal));
        text.ShouldContain("+3 more");
    }

    [Test]
    public void A_waiting_decision_makes_a_digest_that_is_never_quiet()
    {
        var text = AwayDigestFormatter.FormatDigest(Empty() with
        {
            Decisions = [new AwayDigestCardDto("CARD-0010", "Choose", DateTime.UtcNow, "A question.")],
        }, new DigestSettings(), DateTimeOffset.UtcNow);

        text.ShouldNotBeNull();
        text.ShouldContain("❓ Decisions (1)");
    }

    [Test]
    public void Quiet_and_idle_empty_shapes_are_distinct()
    {
        AwayDigestFormatter.FormatDigest(Empty(), new DigestSettings(), DateTimeOffset.UtcNow).ShouldBeNull();
        AwayDigestFormatter.FormatQuiet(Empty() with { Running = new AwayDigestRunningDto(2, null, null, null) }, DateTimeOffset.UtcNow)
            .ShouldContain("2 running");
    }

    private static AwayDigestDto Empty() => new(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, false, [], [], [], [], [],
        new AwayDigestRunningDto(0, null, null, null), new AwayDigestSpendDto(0, 0, 0), []);
}
