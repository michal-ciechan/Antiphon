using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardDoneWorktreeCleanupTests
{
    [Test]
    public async Task C1017_DoneSaveIsAtomic()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (cardId, doneId) = await SeedAsync(schema);
        await using (var writer = Connect(schema))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            var card = await writer.Cards.SingleAsync(c => c.Id == cardId);
            await Cards(writer).MoveAsync(cardId, new MoveCardRequest(doneId, card.ConcurrencyToken, "completed"), CancellationToken.None);
            await using var observer = Connect(schema);
            (await observer.Cards.SingleAsync(c => c.Id == cardId)).Status.ShouldBe(CardStatus.Review);
            (await ReadObligationsAsync(schema, cardId)).ShouldBeEmpty();
            await transaction.RollbackAsync();
        }
        await using (var observer = Connect(schema))
        {
            (await observer.Cards.SingleAsync(c => c.Id == cardId)).Status.ShouldBe(CardStatus.Review);
            (await observer.CardRevisions.CountAsync(r => r.CardId == cardId)).ShouldBe(0);
            (await ReadObligationsAsync(schema, cardId)).ShouldBeEmpty();
        }
        await using (var writer = Connect(schema))
        {
            var card = await writer.Cards.SingleAsync(c => c.Id == cardId);
            await Cards(writer).MoveAsync(cardId, new MoveCardRequest(doneId, card.ConcurrencyToken, "completed"), CancellationToken.None);
        }
        // A new context/connection observes the actual card-service commit, not tracked entities.
        await using var restarted = Connect(schema);
        var revision = await restarted.CardRevisions.SingleAsync(r => r.CardId == cardId && r.ToStatus == CardStatus.Done);
        var committedObligations = await ReadObligationsAsync(schema, cardId);
        committedObligations.ShouldHaveSingleItem().ShouldBe(revision.Id);
        (await restarted.Cards.SingleAsync(c => c.Id == cardId)).Status.ShouldBe(CardStatus.Done);
    }

    private static AppDbContext Connect(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static CardService Cards(AppDbContext db) =>
        new(db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!);

    private static async Task<(Guid CardId, Guid DoneId)> SeedAsync(IsolatedTestSchema schema)
    {
        await using var db = Connect(schema);
        var board = new Board { Id = Guid.NewGuid(), Name = "cleanup fixture", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var review = new BoardColumn { Id = Guid.NewGuid(), Board = board, StateKey = "review", Name = "Review", CardStatus = CardStatus.Review, ColumnOrder = 0 };
        var done = new BoardColumn { Id = Guid.NewGuid(), Board = board, StateKey = "done", Name = "Done", CardStatus = CardStatus.Done, IsTerminal = true, ColumnOrder = 1 };
        board.Columns.Add(review);
        board.Columns.Add(done);
        var card = new Card { Id = Guid.NewGuid(), Board = board, BoardColumn = review, Identifier = "CARD-1017", Title = "cleanup fixture", Status = CardStatus.Review,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid() };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return (card.Id, done.Id);
    }

    private static async Task<List<Guid>> ReadObligationsAsync(IsolatedTestSchema schema, Guid cardId)
    {
        await using var connection = new NpgsqlConnection(schema.ConnectionString);
        await connection.OpenAsync();
        // Baseline compatibility: the current defect has no obligation table at all.
        // Absence means zero obligations; an existing table must provide real persisted rows.
        await using var present = new NpgsqlCommand("SELECT to_regclass('\"CardWorktreeCleanups\"') IS NOT NULL", connection);
        if ((bool)(await present.ExecuteScalarAsync())! == false) return [];
        await using var query = new NpgsqlCommand("SELECT \"DoneRevisionId\" FROM \"CardWorktreeCleanups\" WHERE \"CardId\" = @card", connection);
        query.Parameters.AddWithValue("card", cardId);
        await using var rows = await query.ExecuteReaderAsync();
        var result = new List<Guid>();
        while (await rows.ReadAsync()) result.Add(rows.GetGuid(0));
        return result;
    }
}
