using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0039 S3: a Critical/Now card created later still sorts ahead of a Low/Normal card on
/// every in-memory sort site (board columns and docs/cards index). Home and orchestrator
/// dispatch order are pinned in their own integration tests.
/// </summary>
[Category("Unit")]
public class CardRankingOrderTests
{
    [Test]
    public void Board_and_renderer_put_a_later_critical_now_ahead_of_low_normal()
    {
        var earlier = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddHours(2);
        var low = MakeCard("CARD-0001", CardImportance.Low, CardUrgency.Normal, earlier);
        var critical = MakeCard("CARD-0002", CardImportance.Critical, CardUrgency.Now, later);

        CardRanking.Rank(critical, later).ShouldBeLessThan(CardRanking.Rank(low, later));

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog
        };
        var project = new Project { Id = Guid.NewGuid(), Name = "P" };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = "B",
            Project = project,
            Columns = { column },
            Cards = { low, critical }
        };
        low.BoardColumnId = column.Id;
        critical.BoardColumnId = column.Id;

        var dto = BoardService.ToDetailDto(board, includeArchived: false);
        dto.Columns.Single().Cards.Select(c => c.Identifier).ToArray()
            .ShouldBe(["CARD-0002", "CARD-0001"]);

        var names = new Dictionary<Guid, string>
        {
            [low.Id] = "card-0001.md",
            [critical.Id] = "card-0002.md"
        };
        var index = CardTaskFileRenderer.RenderIndex("B", [low, critical], names);
        var criticalAt = index.IndexOf("CARD-0002", StringComparison.Ordinal);
        var lowAt = index.IndexOf("CARD-0001", StringComparison.Ordinal);
        criticalAt.ShouldBeLessThan(lowAt);
        index.ShouldContain("`critical`");
        index.ShouldContain("`now`");
    }

    [Test]
    public void Same_rank_later_created_with_position_1_sorts_first()
    {
        var earlier = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddHours(2);
        var older = MakeCard("CARD-0001", CardImportance.Normal, CardUrgency.Normal, earlier);
        var placed = MakeCard("CARD-0002", CardImportance.Normal, CardUrgency.Normal, later);
        placed.Position = 1;

        CardRanking.OrderKey(placed, later).ShouldBeLessThan(CardRanking.OrderKey(older, later));

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog
        };
        var project = new Project { Id = Guid.NewGuid(), Name = "P" };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = "B",
            Project = project,
            Columns = { column },
            Cards = { older, placed }
        };
        older.BoardColumnId = column.Id;
        placed.BoardColumnId = column.Id;

        var dto = BoardService.ToDetailDto(board, includeArchived: false);
        dto.Columns.Single().Cards.Select(c => c.Identifier).ToArray()
            .ShouldBe(["CARD-0002", "CARD-0001"]);
        dto.Columns.Single().Cards[0].Position.ShouldBe(1);
        dto.Columns.Single().Cards[1].Position.ShouldBeNull();
    }

    [Test]
    public void Position_is_ignored_across_different_ranks()
    {
        var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        var lowPlaced = MakeCard("CARD-0001", CardImportance.Low, CardUrgency.Normal, now);
        lowPlaced.Position = 1;
        var highUnplaced = MakeCard("CARD-0002", CardImportance.High, CardUrgency.Normal, now.AddHours(1));

        CardRanking.OrderKey(highUnplaced, now).ShouldBeLessThan(CardRanking.OrderKey(lowPlaced, now));
    }

    private static Card MakeCard(
        string identifier, CardImportance importance, CardUrgency urgency, DateTime created) =>
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            Title = identifier,
            Importance = importance,
            Urgency = urgency,
            Status = CardStatus.Backlog,
            CreatedAt = created,
            UpdatedAt = created
        };
}
