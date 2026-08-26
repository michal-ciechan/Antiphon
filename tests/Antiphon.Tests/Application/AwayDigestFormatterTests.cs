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
    public void Quiet_and_idle_empty_shapes_are_distinct()
    {
        AwayDigestFormatter.FormatDigest(Empty(), new DigestSettings(), DateTimeOffset.UtcNow).ShouldBeNull();
        AwayDigestFormatter.FormatQuiet(Empty() with { Running = new AwayDigestRunningDto(2, null, null, null) }, DateTimeOffset.UtcNow)
            .ShouldContain("2 running");
    }

    private static AwayDigestDto Empty() => new(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, false, [], [], [], [],
        new AwayDigestRunningDto(0, null, null, null), new AwayDigestSpendDto(0, 0, 0), []);
}
