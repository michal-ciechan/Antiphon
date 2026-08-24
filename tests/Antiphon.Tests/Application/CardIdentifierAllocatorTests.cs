using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0175: the identifier sequence moved out of <c>CardService.NextIdentifierAsync</c> so a
/// batch of tracker imports gets N distinct numbers in one save. The parse is unchanged and these
/// pin that: highest-suffix+1, archived rows counted, unparseable suffixes ignored.
/// </summary>
[Category("Unit")]
public class CardIdentifierAllocatorTests
{
    [Test]
    public void Next_is_one_past_the_highest_suffix_not_the_count()
    {
        var allocator = CardIdentifierAllocator.ForIdentifiers(["CARD-0007", "CARD-0003"]);

        allocator.Next().ShouldBe("CARD-0008");
    }

    [Test]
    public void An_archived_rows_number_is_still_taken()
    {
        // CARD-0005: archived cards are filtered at the READ site, never by a global query filter,
        // precisely so their numbers stay spent here. Same list, archived or not.
        var allocator = CardIdentifierAllocator.ForIdentifiers(["CARD-0001", "CARD-0042"]);

        allocator.Next().ShouldBe("CARD-0043");
    }

    [Test]
    public void A_hash_key_is_ignored_and_a_foreign_prefix_still_counts()
    {
        // Documents the parse, which this card did not change: the suffix after the LAST '-'.
        // "#12" has no '-', so it never contributed to the sequence - which is exactly why the
        // eleven imported cards did not consume CARD numbers while they were named "#3".."#13".
        CardIdentifierAllocator.ForIdentifiers(["CARD-0002", "#12"]).Next().ShouldBe("CARD-0003");
        CardIdentifierAllocator.ForIdentifiers(["CARD-0002", "ANT-9"]).Next().ShouldBe("CARD-0010");
    }

    [Test]
    public void Three_calls_on_one_instance_are_three_consecutive_distinct_values()
    {
        var allocator = CardIdentifierAllocator.ForIdentifiers(["CARD-0175"]);

        var allocated = new[] { allocator.Next(), allocator.Next(), allocator.Next() };

        allocated.ShouldBe(["CARD-0176", "CARD-0177", "CARD-0178"]);
        allocated.Distinct().Count().ShouldBe(3);
    }

    [Test]
    public void An_empty_board_starts_at_one()
    {
        CardIdentifierAllocator.ForIdentifiers([]).Next().ShouldBe("CARD-0001");
        CardIdentifierAllocator.ForIdentifiers([null, string.Empty]).Next().ShouldBe("CARD-0001");
    }
}
