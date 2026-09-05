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

/// <summary>CARD-0098 S4: <see cref="CardService.ReorderBoardCardsAsync"/>.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class BoardCardOrderIntegrationTests
{
    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public BoardCardOrderIntegrationTests(AntiphonWebAppFactory factory) => _factory = factory;

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
    public async Task Listed_cards_come_first_per_cell_and_unlisted_keep_their_order()
    {
        var (board, cards) = await SeedAsync(
            "Bulk order board",
            ("A", CardImportance.Normal),
            ("B", CardImportance.Normal),
            ("C", CardImportance.Normal));
        var a = cards[0];
        var b = cards[1];
        var c = cards[2];

        var result = await ReorderAsync(board.Id, new ReorderBoardCardsRequest(
            [new ReorderBoardCardEntry(c.Identifier), new ReorderBoardCardEntry(a.Identifier)],
            "C then A at the top."));

        result.SkippedHumanRated.ShouldBeEmpty();
        var ordered = await PositionsAsync(board.Id);
        ordered.Select(p => p.Id).ShouldBe([c.Id, a.Id, b.Id]);
        ordered.Select(p => p.Position).ShouldBe([1, 2, 3]);
    }

    [Test]
    public async Task Human_rated_axis_changes_are_skipped_unless_overridden()
    {
        var (board, cards) = await SeedAsync(
            "Bulk skip board",
            ("Human", CardImportance.High),
            ("Auto", null));
        var human = cards[0];
        var auto = cards[1];
        auto.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
        human.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Human);

        var skipped = await ReorderAsync(board.Id, new ReorderBoardCardsRequest(
            [
                new ReorderBoardCardEntry(human.Identifier, CardImportance.Low),
                new ReorderBoardCardEntry(auto.Identifier, CardImportance.High)
            ],
            "Try to demote the human one."));
        skipped.SkippedHumanRated.ShouldHaveSingleItem();
        skipped.SkippedHumanRated[0].Id.ShouldBe(human.Id);
        (await GetAsync(human.Id)).Importance.ShouldBe(CardImportance.High);
        (await GetAsync(auto.Id)).Importance.ShouldBe(CardImportance.High);

        var forced = await ReorderAsync(board.Id, new ReorderBoardCardsRequest(
            [new ReorderBoardCardEntry(human.Identifier, CardImportance.Low)],
            "Operator override.",
            OverrideHumanRatings: true));
        forced.SkippedHumanRated.ShouldBeEmpty();
        (await GetAsync(human.Id)).Importance.ShouldBe(CardImportance.Low);
    }

    [Test]
    public async Task A_bad_ref_is_atomic_and_writes_a_reorder_revision_per_listed_card()
    {
        var (board, cards) = await SeedAsync(
            "Bulk atomic board",
            ("A", CardImportance.Normal),
            ("B", CardImportance.Normal));
        var a = cards[0];

        await Should.ThrowAsync<ValidationException>(() =>
            ReorderAsync(board.Id, new ReorderBoardCardsRequest(
                [new ReorderBoardCardEntry(a.Identifier), new ReorderBoardCardEntry("CARD-9999")],
                "Should not land.")));

        (await GetAsync(a.Id)).Position.ShouldBeNull();
        (await RevisionsAsync(a.Id)).ShouldBeEmpty();

        var ok = await ReorderAsync(board.Id, new ReorderBoardCardsRequest(
            [new ReorderBoardCardEntry(cards[1].Identifier), new ReorderBoardCardEntry(a.Identifier)],
            "B then A."));
        ok.Cards.Count.ShouldBe(2);
        var history = await RevisionsAsync(a.Id);
        history.ShouldHaveSingleItem();
        history[0].Kind.ShouldBe(CardRevisionKind.Reorder);
        history[0].Reason.ShouldContain("B then A.");
    }

    private async Task<ReorderBoardCardsResult> ReorderAsync(Guid boardId, ReorderBoardCardsRequest request)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>()
            .ReorderBoardCardsAsync(boardId, request, CancellationToken.None);
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
        var rows = await db.Cards.AsNoTracking()
            .Where(c => c.BoardColumnId == columnId && c.ArchivedAt == null)
            .ToListAsync();
        return rows.OrderBy(c => CardRanking.OrderKey(c, now)).Select(c => (c.Id, c.Position)).ToList();
    }

    private async Task<(BoardDetailDto Board, List<CardDto> Cards)> SeedAsync(
        string boardName,
        params (string Title, CardImportance? Importance)[] titles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Bulk Order Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-bulk-order-{Guid.NewGuid():N}"),
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
