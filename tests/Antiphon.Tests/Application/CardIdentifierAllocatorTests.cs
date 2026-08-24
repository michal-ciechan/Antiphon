using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CardIdentifierAllocatorTests
{
    [Test]
    public void Highest_plus_one_across_existing_card_identifiers()
    {
        var allocator = CardIdentifierAllocator.FromIdentifiers(["CARD-0007", "CARD-0003"]);
        allocator.Next().ShouldBe("CARD-0008");
    }

    [Test]
    public void Archived_shaped_rows_are_counted()
    {
        // Archive does not drop the identifier from the sequence — same parse as live rows.
        var allocator = CardIdentifierAllocator.FromIdentifiers(["CARD-0004", "CARD-0012"]);
        allocator.Next().ShouldBe("CARD-0013");
    }

    [Test]
    public void Hash_N_is_ignored_and_ANT_N_is_counted()
    {
        // Documents the parse: no dash → ignored; last-dash suffix that parses is counted.
        var allocator = CardIdentifierAllocator.FromIdentifiers(["#12", "ANT-9", "CARD-0003"]);
        allocator.Next().ShouldBe("CARD-0010");
    }

    [Test]
    public void Next_three_times_on_one_instance_is_consecutive_and_distinct()
    {
        var allocator = CardIdentifierAllocator.FromIdentifiers([]);
        var first = allocator.Next();
        var second = allocator.Next();
        var third = allocator.Next();
        first.ShouldBe("CARD-0001");
        second.ShouldBe("CARD-0002");
        third.ShouldBe("CARD-0003");
        new[] { first, second, third }.Distinct().Count().ShouldBe(3);
    }
}
