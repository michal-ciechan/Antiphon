using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0355: the headed canary may read only <c>[ui].follow_up_behavior</c>. Pin the parser
/// so a comment, another table, or a missing key cannot be mistaken for steer — and so we
/// never need to dump the rest of the operator config to know which arm we are on.
/// </summary>
[Category("Unit")]
public class GkSessionFollowUpBehaviorTests
{
    [Test]
    public void Absent_file_or_key_is_vendor_default_queue()
    {
        GkSession.ParseFollowUpBehavior([]).ShouldBe("queue");
        GkSession.ParseFollowUpBehavior(["[ui]", "simple_mode = true"]).ShouldBe("queue");
        GkSession.ParseFollowUpBehavior(["[models]", "default = \"grok-4.5\""]).ShouldBe("queue");
    }

    [Test]
    public void Reads_queue_and_steer_from_the_ui_table_only()
    {
        GkSession.ParseFollowUpBehavior(["[ui]", "follow_up_behavior = \"queue\""]).ShouldBe("queue");
        GkSession.ParseFollowUpBehavior(["[ui]", "follow_up_behavior = 'steer'"]).ShouldBe("steer");
        GkSession.ParseFollowUpBehavior(
            ["[other]", "follow_up_behavior = \"steer\"", "[ui]", "simple_mode = true"])
            .ShouldBe("queue");
    }

    [Test]
    public void Strips_inline_comments_and_ignores_hash_lines()
    {
        GkSession.ParseFollowUpBehavior(
            [
                "# follow_up_behavior = \"steer\"",
                "[ui]",
                "follow_up_behavior = \"queue\"  # mid-turn follow-ups",
            ])
            .ShouldBe("queue");
    }
}
