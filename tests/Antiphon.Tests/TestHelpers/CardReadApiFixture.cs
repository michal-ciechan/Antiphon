using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

public sealed class CardReadApiFixture : AntiphonWebAppFactory
{
    private Guid _projectId;
    private int _sequence;

    public override async Task ResetAsync()
    {
        await base.ResetAsync();
        _sequence = 0;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"Card read {_sequence++} {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/cards.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"card-read-{Guid.NewGuid():N}"),
            BaseBranch = "main", CreatedAt = now, UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
    }

    public async Task<BoardDetailDto> BoardAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().CreateAsync(
            new CreateBoardRequest(_projectId, $"Card read board {Guid.NewGuid():N}"), CancellationToken.None);
    }

    public async Task<IReadOnlyList<Card>> CardsAsync(Guid boardId, int count, string? marker = null,
        bool archived = false, DateTime? firstAt = null)
    {
        await using var db = Writer();
        var column = await db.BoardColumns.Where(c => c.BoardId == boardId && c.StateKey == "backlog")
            .Select(c => c.Id).SingleAsync();
        var start = firstAt ?? DateTime.UtcNow.AddDays(-2);
        var cards = Enumerable.Range(0, count).Select(i => new Card
        {
            Id = Guid.NewGuid(), BoardId = boardId, BoardColumnId = column,
            Identifier = $"CR-{_sequence++:D6}", Title = marker is null ? $"Card {i}" : $"{marker} {i}",
            Description = "body", CreatedAt = start.AddMinutes(-1), UpdatedAt = start.AddSeconds(-i),
            ArchivedAt = archived ? start.AddDays(1) : null
        }).ToList();
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
        return cards;
    }

    public AppDbContext Writer() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    public async Task<IReadOnlyList<Guid>> OracleAsync(Guid boardId, bool includeArchived = false)
    {
        await using var db = Writer();
        var query = db.Cards.Where(c => c.BoardId == boardId);
        if (!includeArchived) query = query.Where(c => c.ArchivedAt == null);
        return await query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id)
            .Select(c => c.Id).ToListAsync();
    }

    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty) return;
        await using var db = Writer();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).ExecuteDeleteAsync();
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }
}
