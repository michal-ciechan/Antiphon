using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class CardFilePrivacyWorld(string? connectionString = null) : IAsyncDisposable
{
    public ScratchGitRepo Repo { get; } = new("c408");
    public CardTaskFileSyncGate Gate { get; } = new();
    public Guid ProjectId { get; } = Guid.NewGuid();
    public Guid BoardId { get; } = Guid.NewGuid();
    public Guid ColumnId { get; } = Guid.NewGuid();
    public string DirectoryPath => Path.Combine(Repo.Path, "docs/cards/board");
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(connectionString));

    public async Task InitializeAsync(bool enabledBoard = true, RepositoryVisibility visibility = RepositoryVisibility.Public)
    {
        await Repo.CommitFileAsync("seed", "seed\n");
        await File.WriteAllTextAsync(Path.Combine(Repo.Path, ".gitignore"),
            "# BEGIN ANTIPHON CARD FILES\n/docs/cards/*\n!/docs/cards/board/\n# END ANTIPHON CARD FILES\n");
        await using var db = Db();
        db.Projects.Add(new Project { Id = ProjectId, Name = $"c408-{ProjectId:N}", LocalRepositoryPath = Repo.Path,
            GitRepositoryUrl = "https://example.invalid/c408.git", RepositoryVisibility = visibility,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Boards.Add(new Board { Id = BoardId, ProjectId = ProjectId, Name = "Board", SyncCardFiles = enabledBoard,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.BoardColumns.Add(new BoardColumn { Id = ColumnId, BoardId = BoardId, Name = "Backlog", StateKey = "backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task<Guid> AddCardAsync(string identifier = "CARD-0001", string title = "Public",
        string notes = "C408_NOTE_CURRENT", CardFileVisibility visibility = CardFileVisibility.Inherit)
    {
        await using var db = Db();
        var card = new Card { Id = Guid.NewGuid(), BoardId = BoardId, BoardColumnId = ColumnId,
            Identifier = identifier, Title = title, Description = "C408_PUBLIC_BODY", PrivateNotes = notes,
            CardFileVisibility = visibility, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    public CardTaskFileService Service(AppDbContext db, bool autoCommit = false, bool enabled = true,
        Antiphon.Server.Application.Interfaces.ICardFileRepository? repository = null) => new(db, Gate,
        new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), NullLogger<CardTaskFileService>.Instance,
        repository ?? new Antiphon.Server.Infrastructure.Git.CardFileRepository(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<Antiphon.Server.Infrastructure.Git.CardFileRepository>.Instance),
        Options.Create(new CardFileSyncSettings { AutoCommit = autoCommit, Enabled = enabled, IntervalSeconds = 0 }));

    public async Task<CardFileSyncBoardResult> SyncAsync(bool autoCommit = false, bool dryRun = false)
    {
        await using var db = Db();
        return await Service(db, autoCommit).SyncBoardAsync(BoardId, dryRun);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = Db();
        var boardIds = await db.Boards.Where(b => b.ProjectId == ProjectId).Select(b => b.Id).ToListAsync();
        await db.CardRevisions.Where(r => boardIds.Contains(r.Card.BoardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => boardIds.Contains(r.Card.BoardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == ProjectId).ExecuteDeleteAsync();
        Repo.Dispose();
    }
}
