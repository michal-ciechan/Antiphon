using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0098 S2: <see cref="CardService.PlaceAsync"/> — relative placement, dense cell
/// renumbering, stale-order 409, unreachable-dueAt 422, and neighbour tokens left alone.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardReorderIntegrationTests
{
    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardReorderIntegrationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Top_bottom_before_and_after_renumber_the_cell_1_to_n()
    {
        var (board, cards) = await SeedAsync(
            "Reorder dense board",
            ("Alpha", CardImportance.Normal),
            ("Bravo", CardImportance.Normal),
            ("Charlie", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];
        var c = cards[2];

        var top = await PlaceAsync(c.Id, new PlaceCardRequest(c.ConcurrencyToken, Placement: CardPlacement.Top));
        top.Position.ShouldBe(1);
        await PositionsAsync(board.Id).ShouldBePositions((c.Id, 1), (a.Id, 2), (b.Id, 3));

        a = await GetAsync(a.Id);
        var bottom = await PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Placement: CardPlacement.Bottom));
        bottom.Position.ShouldBe(3);
        await PositionsAsync(board.Id).ShouldBePositions((c.Id, 1), (b.Id, 2), (a.Id, 3));

        b = await GetAsync(b.Id);
        c = await GetAsync(c.Id);
        await PlaceAsync(b.Id, new PlaceCardRequest(b.ConcurrencyToken, Before: c.Identifier));
        await PositionsAsync(board.Id).ShouldBePositions((b.Id, 1), (c.Id, 2), (a.Id, 3));

        a = await GetAsync(a.Id);
        c = await GetAsync(c.Id);
        await PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, After: c.Identifier));
        await PositionsAsync(board.Id).ShouldBePositions((b.Id, 1), (c.Id, 2), (a.Id, 3));
    }

    [Test]
    public async Task Stale_neighbours_are_a_409_and_a_token_mismatch_is_a_409()
    {
        var (_, cards) = await SeedAsync(
            "Reorder stale board",
            ("Alpha", CardImportance.Normal),
            ("Bravo", CardImportance.Normal),
            ("Charlie", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];
        var c = cards[2];
        await PlaceAsync(c.Id, new PlaceCardRequest(c.ConcurrencyToken, Placement: CardPlacement.Top));
        // Column order is now C, A, B — A and C are not adjacent to a phantom between them
        // once C is removed? After C is removed, A and B are adjacent. Asking for before=B
        // after=C (C is still in the column) is adjacent after removing A: C then B.
        // Asking for before=A after=B: after removal of... let's use A with before=B after=C
        // ordered without A is C, B — C then B, so after=C before=B is adjacent.
        // Stale pair: after=B before=C (wrong order / not adjacent as after-then-before).
        a = await GetAsync(a.Id);
        var stale = await Should.ThrowAsync<ConflictException>(() =>
            PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Before: c.Identifier, After: b.Identifier)));
        stale.Code.ShouldBe(CardService.CardOrderStaleCode);

        a = await GetAsync(a.Id);
        var mismatch = await Should.ThrowAsync<ConflictException>(() =>
            PlaceAsync(a.Id, new PlaceCardRequest(Guid.NewGuid(), Placement: CardPlacement.Top)));
        mismatch.Message.ShouldContain("modified by another operation");
    }

    [Test]
    public async Task Neighbours_tokens_are_not_rotated()
    {
        var (_, cards) = await SeedAsync(
            "Reorder token board",
            ("Alpha", CardImportance.Normal),
            ("Bravo", CardImportance.Normal),
            ("Charlie", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];
        var c = cards[2];
        var beforeTokens = await TokensAsync(b.Id, c.Id);

        await PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Before: b.Identifier, After: null));

        var afterTokens = await TokensAsync(b.Id, c.Id);
        afterTokens[b.Id].ShouldBe(beforeTokens[b.Id]);
        afterTokens[c.Id].ShouldBe(beforeTokens[c.Id]);
        var moved = await GetAsync(a.Id);
        moved.ConcurrencyToken.ShouldNotBe(a.ConcurrencyToken);
    }

    [Test]
    public async Task Cross_cell_adoption_writes_axes_and_one_reorder_revision()
    {
        var (_, cards) = await SeedAsync(
            "Reorder cross-cell board",
            ("High one", CardImportance.High),
            ("Normal one", CardImportance.Normal));
        var high = cards[0];
        var normal = cards[1];

        var placed = await PlaceAsync(
            high.Id,
            new PlaceCardRequest(high.ConcurrencyToken, Before: normal.Identifier, Reason: "Drop into Someday"));

        placed.Importance.ShouldBe(CardImportance.Normal);
        placed.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Human);
        placed.Urgency.ShouldBe(CardUrgency.Normal);
        placed.Position.ShouldBe(1);

        var history = await RevisionsAsync(high.Id);
        history.ShouldHaveSingleItem();
        history[0].Kind.ShouldBe(CardRevisionKind.Reorder);
        history[0].Importance.ShouldBe(CardImportance.High);
        history[0].Urgency.ShouldBe(CardUrgency.Normal);
        history[0].Reason.ShouldBe("Drop into Someday (placed before " + normal.Identifier + ")");
        history[0].EditedBy.ShouldBeNull();
    }

    [Test]
    public async Task Own_cell_edge_keeps_the_higher_cell()
    {
        var (_, cards) = await SeedAsync(
            "Reorder edge board",
            ("High one", CardImportance.High),
            ("High two", CardImportance.High),
            ("Normal one", CardImportance.Normal));
        var h1 = cards[0];
        var h2 = cards[1];
        var n1 = cards[2];

        var placed = await PlaceAsync(
            h2.Id,
            new PlaceCardRequest(h2.ConcurrencyToken, Before: n1.Identifier, After: h1.Identifier));

        placed.Importance.ShouldBe(CardImportance.High);
        placed.Position.ShouldBe(2);
        var n = await GetAsync(n1.Id);
        n.Importance.ShouldBe(CardImportance.Normal);
        n.Position.ShouldBeNull();
    }

    [Test]
    public async Task Due_at_that_escalates_out_of_the_target_cell_is_unreachable()
    {
        var (_, cards) = await SeedAsync(
            "Reorder due board",
            ("Normal one", CardImportance.Normal),
            ("Due soon", CardImportance.Normal));
        var normal = cards[0];
        var due = cards[1];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var when = DateTime.UtcNow.AddDays(2);
            await db.Cards.Where(c => c.Id == due.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DueAt, when));
        }

        due = await GetAsync(due.Id);
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            PlaceAsync(due.Id, new PlaceCardRequest(due.ConcurrencyToken, Before: normal.Identifier)));
        ex.Code.ShouldBe(CardService.CardPositionUnreachableCode);
        ex.Errors.Values.SelectMany(v => v).ShouldContain(m => m.Contains("due in") && m.Contains(normal.Identifier));
    }

    [Test]
    public async Task Archived_neighbour_and_off_board_ref_and_self_are_422()
    {
        var (board, cards) = await SeedAsync(
            "Reorder 422 board",
            ("Alpha", CardImportance.Normal),
            ("Bravo", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<CardService>();
            await service.ArchiveAsync(
                b.Id,
                new ArchiveCardRequest(b.ConcurrencyToken, "Filed away."),
                CancellationToken.None);
        }

        a = await GetAsync(a.Id);
        var archived = await Should.ThrowAsync<ValidationException>(() =>
            PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Before: b.Identifier)));
        archived.Errors.Values.SelectMany(v => v).ShouldContain(m => m.Contains("archived"));

        a = await GetAsync(a.Id);
        var self = await Should.ThrowAsync<ValidationException>(() =>
            PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Before: a.Identifier)));
        self.Errors.Values.SelectMany(v => v).ShouldContain(m => m.Contains("itself"));

        Guid otherBoardCardId;
        string otherIdentifier;
        using (var scope = _factory.Services.CreateScope())
        {
            var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
            var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
            var other = await boards.CreateAsync(
                new CreateBoardRequest(_projectId, "Other reorder board"), CancellationToken.None);
            var otherCard = await cardService.CreateAsync(
                other.Id, new CreateCardRequest(null, "Elsewhere"), CancellationToken.None);
            otherBoardCardId = otherCard.Id;
            otherIdentifier = otherCard.Identifier;
        }

        a = await GetAsync(a.Id);
        var offBoard = await Should.ThrowAsync<ValidationException>(() =>
            PlaceAsync(a.Id, new PlaceCardRequest(a.ConcurrencyToken, Before: otherIdentifier)));
        offBoard.Errors.ShouldNotBeEmpty();
        board.Id.ShouldNotBe(Guid.Empty);
        otherBoardCardId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Reorder_revision_reason_is_the_fact_when_the_caller_gave_none()
    {
        var (_, cards) = await SeedAsync(
            "Reorder reason board",
            ("Alpha", CardImportance.Normal),
            ("Bravo", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];

        await PlaceAsync(b.Id, new PlaceCardRequest(b.ConcurrencyToken, Placement: CardPlacement.Top));
        var history = await RevisionsAsync(b.Id);
        history.ShouldHaveSingleItem();
        history[0].Kind.ShouldBe(CardRevisionKind.Reorder);
        history[0].Reason.ShouldBe("top of cell");
        history[0].Position.ShouldBeNull();
    }

    private async Task<CardDto> PlaceAsync(Guid id, PlaceCardRequest request)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>()
            .PlaceAsync(id, request, CancellationToken.None);
    }

    private async Task<CardDto> GetAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>()
            .GetByIdAsync(id, CancellationToken.None);
    }

    private async Task<IReadOnlyList<CardRevisionDto>> RevisionsAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>()
            .GetRevisionsAsync(id, CancellationToken.None);
    }

    private async Task<List<(Guid Id, int? Position)>> PositionsAsync(Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columnId = await db.BoardColumns
            .Where(c => c.BoardId == boardId && c.CardStatus == CardStatus.Backlog)
            .Select(c => c.Id)
            .SingleAsync();
        var now = DateTime.UtcNow;
        var rows = await db.Cards
            .AsNoTracking()
            .Where(c => c.BoardColumnId == columnId && c.ArchivedAt == null)
            .ToListAsync();
        return rows
            .OrderBy(c => CardRanking.OrderKey(c, now))
            .Select(c => (c.Id, c.Position))
            .ToList();
    }

    private async Task<Dictionary<Guid, Guid>> TokensAsync(params Guid[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cards.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.ConcurrencyToken);
    }

    private async Task<(BoardDetailDto Board, List<CardDto> Cards)> SeedAsync(
        string boardName,
        params (string Title, CardImportance Importance)[] titles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Reorder Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-reorder-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, boardName), CancellationToken.None);
        var created = new List<CardDto>();
        foreach (var (title, importance) in titles)
        {
            created.Add(await cards.CreateAsync(
                board.Id,
                new CreateCardRequest(null, title, Importance: importance),
                CancellationToken.None));
        }

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < created.Count; i++)
        {
            var id = created[i].Id;
            var stamped = origin.AddMinutes(i);
            await db.Cards.Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, stamped));
        }

        var reloaded = new List<CardDto>();
        foreach (var card in created)
            reloaded.Add(await cards.GetByIdAsync(card.Id, CancellationToken.None));
        return (board, reloaded);
    }
}

file static class CardReorderPositionAssert
{
    public static async Task ShouldBePositions(
        this Task<List<(Guid Id, int? Position)>> actualTask,
        params (Guid Id, int? Position)[] expected)
    {
        var actual = await actualTask;
        actual.ShouldBe(expected.ToList());
    }
}
