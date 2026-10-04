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
    public async Task C1017_DiscoverPostDoneAttempts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (cardId, doneId) = await SeedAsync(schema);
        var taskId = await SeedTaskAsync(schema, cardId);
        await DoneAsync(schema, cardId, doneId);
        await using (var first = Connect(schema))
        {
            var ids = await new CardWorktreeCleanupService(first, TimeProvider.System).DiscoverAsync(cardId, CancellationToken.None);
            ids.ShouldHaveSingleItem();
            var endpoint = await first.CardWorktreeCleanupEndpoints.SingleAsync();
            endpoint.State = CardWorktreeCleanupEndpointState.Complete;
            await first.SaveChangesAsync();
        }
        await using (var writer = Connect(schema))
        {
            var task = await writer.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Attempt = 2;
            task.Status = AgentTaskStatus.Working;
            await writer.SaveChangesAsync();
        }
        await using (var restarted = Connect(schema))
            await new CardWorktreeCleanupService(restarted, TimeProvider.System).DiscoverAsync(cardId, CancellationToken.None);
        await using var observer = Connect(schema);
        var targetAttempts = await observer.CardWorktreeCleanupTargets.OrderBy(t => t.TaskAttempt).Select(t => t.TaskAttempt).ToArrayAsync();
        targetAttempts.ShouldBe(new[] { 1, 2 });
        (await observer.AgentTasks.SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Working);
        (await observer.CardWorktreeCleanupEndpoints.CountAsync(e => e.OperationId != null)).ShouldBe(0);
    }

    [Test]
    public async Task C1017_ExactCardBinding()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (cardId, doneId) = await SeedAsync(schema);
        var (foreignCardId, _) = await SeedAsync(schema);
        var ownTaskId = await SeedTaskAsync(schema, cardId);
        var foreignTaskId = await SeedTaskAsync(schema, foreignCardId);
        await DoneAsync(schema, cardId, doneId);
        await using (var writer = Connect(schema))
            await new CardWorktreeCleanupService(writer, TimeProvider.System).DiscoverAsync(cardId, CancellationToken.None);
        await using var observer = Connect(schema);
        var targets = await observer.CardWorktreeCleanupTargets.Select(t => t.TaskId).ToArrayAsync();
        targets.ShouldNotContain(foreignTaskId);
        targets.ShouldBe(new[] { ownTaskId });
    }

    [Test]
    public async Task C1017_DuplicateDoneIsIdempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (cardId, doneId) = await SeedAsync(schema);
        await SeedTaskAsync(schema, cardId);
        await DoneAsync(schema, cardId, doneId);
        await using (var simulateOldVersion = Connect(schema))
        {
            // A historical Done revision whose callback was absent still needs discovery.
            simulateOldVersion.CardWorktreeCleanups.RemoveRange(await simulateOldVersion.CardWorktreeCleanups.ToListAsync());
            await simulateOldVersion.SaveChangesAsync();
        }
        await using var first = Connect(schema);
        await using var second = Connect(schema);
        var arrived = 0;
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Barrier(CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrived) == 2) bothReady.TrySetResult();
            await bothReady.Task.WaitAsync(ct);
        }
        await Task.WhenAll(
            new CardWorktreeCleanupService(first, TimeProvider.System) { BeforeInventorySaveAsync = Barrier }.DiscoverAsync(cardId, CancellationToken.None),
            new CardWorktreeCleanupService(second, TimeProvider.System) { BeforeInventorySaveAsync = Barrier }.DiscoverAsync(cardId, CancellationToken.None));
        await using var observer = Connect(schema);
        var generationRows = await observer.CardWorktreeCleanups.ToListAsync();
        generationRows.Count.ShouldBe(1);
        (await observer.CardWorktreeCleanupTargets.CountAsync()).ShouldBe(1);
        (await observer.CardWorktreeCleanupEndpoints.CountAsync()).ShouldBe(1);
        (await observer.CardWorktreeCleanupEndpoints.CountAsync(e => e.OperationId != null)).ShouldBe(0);
    }

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

    private static async Task DoneAsync(IsolatedTestSchema schema, Guid cardId, Guid doneId)
    {
        await using var db = Connect(schema);
        var card = await db.Cards.SingleAsync(c => c.Id == cardId);
        await Cards(db).MoveAsync(cardId, new MoveCardRequest(doneId, card.ConcurrencyToken, "completed"), CancellationToken.None);
    }

    private static async Task<Guid> SeedTaskAsync(IsolatedTestSchema schema, Guid cardId)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), "c1017-" + cardId.ToString("N"), "card-task-" + id.ToString("N")[..8]);
        await using var db = Connect(schema);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, CardId = cardId, Title = "CARD-1017 cleanup", Goal = "fixture",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            RepoPath = Path.GetDirectoryName(path), WorktreePath = path, WorkingDirectory = path,
            WorktreeBranch = "feat/card-task-" + id.ToString("N")[..8], Attempt = 1,
            Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow.AddHours(-5),
            CompletedAt = DateTime.UtcNow.AddHours(-3), Result = "fixture report", ReplyTo = AgentTaskReplyTo.None
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<(Guid CardId, Guid DoneId)> SeedAsync(IsolatedTestSchema schema)
    {
        await using var db = Connect(schema);
        var project = new Project { Id = Guid.NewGuid(), Name = "cleanup fixture", GitRepositoryUrl = "https://example.test/c1017.git", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var board = new Board { Id = Guid.NewGuid(), Project = project, Name = "cleanup fixture", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
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
