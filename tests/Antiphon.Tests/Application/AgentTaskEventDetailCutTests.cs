using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1710: an event Detail cut at 4000 chars must not split a surrogate pair. Half a pair is
/// not valid UTF-8, and Npgsql's strict encoder then fails the whole event save.
/// </summary>
[Category("Unit")]
public class AgentTaskEventDetailCutTests
{
    private static readonly UTF8Encoding Strict = new(false, true);
    private const string Emoji = "\U0001F600";

    [Test]
    public void New_event_cut_keeps_an_emoji_across_the_boundary_whole()
    {
        var detail = new string('a', 3999) + Emoji + " tail";
        char.IsHighSurrogate(detail[3999]).ShouldBeTrue("the old [..4000] cut would end on a lone high surrogate");

        var cut = AgentTaskReplyService.NewEvent(Guid.NewGuid(), AgentTaskEventType.Warning, detail, DateTime.UtcNow).Detail;

        Should.NotThrow(() => Strict.GetBytes(cut));
        cut.ShouldBe(new string('a', 3999));
    }

    [Test]
    public void New_event_keeps_short_details_and_cuts_plain_text_at_4000()
    {
        static string Cut(string d) =>
            AgentTaskReplyService.NewEvent(Guid.NewGuid(), AgentTaskEventType.Warning, d, DateTime.UtcNow).Detail;

        Cut("short " + Emoji).ShouldBe("short " + Emoji);
        var exact = new string('b', 3998) + Emoji;
        Cut(exact).ShouldBe(exact);
        Cut(new string('c', 5000)).ShouldBe(new string('c', 4000));
    }

    [Test]
    public void Committed_detail_keeps_a_short_file_list_as_the_plain_join()
    {
        AgentTaskReplyService.CommittedDetail("abc1234", ["a.cs", "docs/b.md"]).ShouldBe("abc1234 a.cs, docs/b.md");
        var twenty = Enumerable.Range(0, 20).Select(i => $"src/File{i:D2}.cs").ToList();
        AgentTaskReplyService.CommittedDetail("abc1234", twenty).ShouldBe("abc1234 " + string.Join(", ", twenty));
    }

    [Test]
    public void Committed_detail_bounds_many_long_unicode_names()
    {
        var files = Enumerable.Range(0, 300)
            .Select(i => $"u/{i:D3}" + string.Concat(Enumerable.Repeat(Emoji, 150)) + ".md").ToList();
        string.Join(", ", files).Length.ShouldBeGreaterThan(4000);

        var detail = AgentTaskReplyService.CommittedDetail("abc1234", files);

        detail.Length.ShouldBeLessThanOrEqualTo("abc1234 ".Length + AgentTaskLandingProtocol.ConflictListMaxLength);
        Should.NotThrow(() => Strict.GetBytes(detail));
        detail.ShouldStartWith("abc1234 u/000");
        detail.ShouldMatch(@"\.\.\. and \d+ more$");
    }

    [Test]
    public void Interrupt_stamp_cut_keeps_an_emoji_across_the_boundary_whole()
    {
        var requestId = Guid.NewGuid();
        var stamp = MidTurnInterruptStamp.Format("written", "delivered", requestId);
        var human = new string('h', 3999 - stamp.Length - 1) + Emoji + " tail";
        var refined = new AgentTaskEvent { Detail = human };
        char.IsHighSurrogate((stamp + "\n" + human)[3999]).ShouldBeTrue("the old [..4000] cut would end on a lone high surrogate");

        AgentTaskReplyService.StampInterrupt(refined, "written", "delivered", requestId);

        Should.NotThrow(() => Strict.GetBytes(refined.Detail));
        refined.Detail.ShouldBe(stamp + "\n" + new string('h', 3999 - stamp.Length - 1));
    }
}
