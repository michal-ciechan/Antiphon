using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskPipelineStatusTests
{
    /// <summary>
    /// CARD-0100 T-16 (V-6, G-18, G-19). A Backlog card with an open blocker leaves the Investigate
    /// candidates and is listed under <c>blockedItems</c> with its open blockers; a card whose only
    /// blocker is Done stays a candidate. With nothing blocked the backlog serializes as before.
    /// </summary>
    [Test]
    public async Task C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db);
        var board = seed.Board(1);
        var card1 = seed.Card(1, board);
        var card2 = seed.Card(2, board);
        var card3 = seed.Card(3, board);
        var card4 = seed.Card(4, board, CardStatus.Done);
        db.Add(C100BlockedBy(card2, card1));
        db.Add(C100BlockedBy(card3, card4));
        await seed.SaveAsync();

        var backlog = (await C557Read(db)).InvestigateBacklog;

        backlog.Total.ShouldBe(2);
        backlog.Items.Select(i => i.CardId).ShouldBe([card1.Id, card3.Id]);
        backlog.Blocked.ShouldBe(1);
        var held = backlog.BlockedItems.ShouldNotBeNull().ShouldHaveSingleItem();
        held.CardId.ShouldBe(card2.Id);
        held.BoardId.ShouldBe(board);
        held.Identifier.ShouldBe("CARD-0002");
        held.Title.ShouldBe("Card 2");
        held.BlockedBy.ShouldBe(["CARD-0001"]);

        JsonSerializer.Serialize(new AgentTaskPipelineBacklogDto(0, []), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .ShouldBe("""{"total":0,"items":[]}""");
    }

    private static CardRelationship C100BlockedBy(Card blocked, Card blocker) => new()
    {
        Id = Guid.NewGuid(),
        CardId = blocked.Id,
        RelatedCardId = blocker.Id,
        Kind = CardRelationshipKind.BlockedBy,
        CreatedAt = C557Now,
    };
}
