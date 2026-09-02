using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0004 S1: load, render, compare, write, delete. Assertions are scoped to this test's
/// scratch directory and its own rows — the assembly shares one Postgres.
/// </summary>
[Category("Integration")]
public class CardTaskFileServiceTests
{
    [Test]
    public async Task First_sync_writes_n_plus_index_with_lf_and_second_sync_writes_nothing()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Antiphon");
        await world.AddCardAsync(board, "CARD-0001", "First");
        await world.AddCardAsync(board, "CARD-0002", "Second");

        var first = await world.SyncAsync(board.Id);

        first.WriteSkipReason.ShouldBeNull();
        first.Written.ShouldBe(3);
        first.Deleted.ShouldBe(0);
        first.Unchanged.ShouldBe(0);
        first.CommitSkipReason.ShouldBe("autocommit_disabled");
        first.CommitSha.ShouldBeNull();
        first.Directory.ShouldBe("docs/cards/antiphon");
        first.DryRun.ShouldBeFalse();

        var dir = world.BoardDir(repo, "antiphon");
        Directory.GetFiles(dir, "*.md").Length.ShouldBe(3);
        foreach (var file in Directory.GetFiles(dir, "*.md"))
            AssertLfNoBom(await File.ReadAllBytesAsync(file));

        var second = await world.SyncAsync(board.Id);
        second.Written.ShouldBe(0);
        second.Deleted.ShouldBe(0);
        second.Unchanged.ShouldBe(3);
        second.CommitSkipReason.ShouldBe("autocommit_disabled");
    }

    [Test]
    public async Task Title_edit_deletes_the_old_file_and_leaves_one_file_per_card()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Edit Board");
        var card = await world.AddCardAsync(board, "CARD-0001", "Old Title");
        await world.SyncAsync(board.Id);

        await world.RenameCardAsync(card.Id, "New Title");
        var result = await world.SyncAsync(board.Id);

        var dir = world.BoardDir(repo, "edit-board");
        var names = Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        names.ShouldBe([
            "CARD-0001-new-title.md",
            "INDEX.md",
        ]);
        File.Exists(Path.Combine(dir, "CARD-0001-old-title.md")).ShouldBeFalse();
        result.Deleted.ShouldBe(1);
        result.Written.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Archived_card_keeps_its_file_with_archived_keys_and_an_archived_index_group()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Archive Board");
        var card = await world.AddCardAsync(board, "CARD-0001", "Keep me");
        await world.ArchiveCardAsync(card.Id, "operator", "duplicate");

        var result = await world.SyncAsync(board.Id);
        result.Written.ShouldBe(2);

        var dir = world.BoardDir(repo, "archive-board");
        var body = await File.ReadAllTextAsync(Path.Combine(dir, "CARD-0001-keep-me.md"));
        body.ShouldContain("archived:");
        body.ShouldContain("archived_by: \"operator\"");
        body.ShouldContain("archived_reason: \"duplicate\"");
        var index = await File.ReadAllTextAsync(Path.Combine(dir, "INDEX.md"));
        index.ShouldContain("## Archived (1)");
        index.ShouldContain("1 card, 1 archived.");
    }

    [Test]
    public async Task Null_repository_path_skips_and_writes_nothing()
    {
        await using var world = new World();
        var project = await world.AddProjectAsync(localPath: null);
        var board = await world.AddBoardAsync(project.Id, "No Path");
        await world.AddCardAsync(board, "CARD-0001", "Ghost");

        var result = await world.SyncAsync(board.Id);

        result.WriteSkipReason.ShouldBe("no_repository_path");
        result.Directory.ShouldBeNull();
        result.Written.ShouldBe(0);
    }

    [Test]
    public async Task A_path_that_is_not_a_repo_skips_and_writes_nothing()
    {
        await using var world = new World();
        var dir = world.CreateNonRepoDir();
        var project = await world.AddProjectAsync(dir);
        var board = await world.AddBoardAsync(project.Id, "Not Git");
        await world.AddCardAsync(board, "CARD-0001", "Ghost");

        var result = await world.SyncAsync(board.Id);

        result.WriteSkipReason.ShouldBe("not_a_git_repository");
        Directory.Exists(Path.Combine(dir, "docs")).ShouldBeFalse();
    }

    [Test]
    public async Task Two_boards_in_one_project_each_with_card_0001_get_two_directories()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var a = await world.AddBoardAsync(project.Id, "Alpha");
        var b = await world.AddBoardAsync(project.Id, "Beta");
        await world.AddCardAsync(a, "CARD-0001", "From Alpha");
        await world.AddCardAsync(b, "CARD-0001", "From Beta");

        (await world.SyncAsync(a.Id)).Directory.ShouldBe("docs/cards/alpha");
        (await world.SyncAsync(b.Id)).Directory.ShouldBe("docs/cards/beta");

        File.Exists(Path.Combine(world.BoardDir(repo, "alpha"), "CARD-0001-from-alpha.md")).ShouldBeTrue();
        File.Exists(Path.Combine(world.BoardDir(repo, "beta"), "CARD-0001-from-beta.md")).ShouldBeTrue();
    }

    [Test]
    public async Task Stray_md_in_the_board_directory_is_deleted_and_txt_is_not()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Stray");
        await world.AddCardAsync(board, "CARD-0001", "Keep");
        var dir = world.BoardDir(repo, "stray");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "notes.md"), "stale");
        await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "keep me");

        var result = await world.SyncAsync(board.Id);

        result.Deleted.ShouldBe(1);
        File.Exists(Path.Combine(dir, "notes.md")).ShouldBeFalse();
        File.Exists(Path.Combine(dir, "notes.txt")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(dir, "notes.txt"))).ShouldBe("keep me");
    }

    [Test]
    public async Task Dry_run_reports_counts_and_writes_nothing()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Dry");
        await world.AddCardAsync(board, "CARD-0001", "One");

        var result = await world.SyncAsync(board.Id, dryRun: true);

        result.DryRun.ShouldBeTrue();
        result.Written.ShouldBe(2);
        result.Deleted.ShouldBe(0);
        result.CommitSkipReason.ShouldBe("dry_run");
        result.Directory.ShouldBe("docs/cards/dry");
        Directory.Exists(world.BoardDir(repo, "dry")).ShouldBeFalse();
    }

    [Test]
    public async Task A_crlf_rewrite_on_disk_is_rewritten_lf_and_counts_as_written()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var board = await world.AddBoardAsync(project.Id, "Crlf");
        await world.AddCardAsync(board, "CARD-0001", "One");
        await world.SyncAsync(board.Id);

        var path = Path.Combine(world.BoardDir(repo, "crlf"), "CARD-0001-one.md");
        var lf = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, lf.Replace("\n", "\r\n", StringComparison.Ordinal));

        var result = await world.SyncAsync(board.Id);

        result.Written.ShouldBe(1);
        result.Unchanged.ShouldBe(1, "INDEX.md is untouched");
        var bytes = await File.ReadAllBytesAsync(path);
        AssertLfNoBom(bytes);
    }

    [Test]
    public async Task Archived_board_and_archived_project_skip_without_touching_disk()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var liveProject = await world.AddProjectAsync(repo.Path);
        var archivedProject = await world.AddProjectAsync(repo.Path, archived: true);
        var archivedBoard = await world.AddBoardAsync(liveProject.Id, "Archived Board", archived: true);
        var boardOnArchivedProject = await world.AddBoardAsync(archivedProject.Id, "Orphan Board");
        await world.AddCardAsync(archivedBoard, "CARD-0001", "Hidden");
        await world.AddCardAsync(boardOnArchivedProject, "CARD-0001", "Hidden");

        (await world.SyncAsync(archivedBoard.Id)).WriteSkipReason.ShouldBe("board_archived");
        (await world.SyncAsync(boardOnArchivedProject.Id)).WriteSkipReason.ShouldBe("project_archived");
        Directory.Exists(Path.Combine(repo.Path, "docs")).ShouldBeFalse();
    }

    [Test]
    public async Task Colliding_board_slugs_suffix_the_later_board()
    {
        await using var world = new World();
        var repo = world.CreateRepo();
        var project = await world.AddProjectAsync(repo.Path);
        var first = await world.AddBoardAsync(project.Id, "Foo", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var later = await world.AddBoardAsync(project.Id, "Foo!!", createdAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await world.AddCardAsync(first, "CARD-0001", "A");
        await world.AddCardAsync(later, "CARD-0001", "B");

        (await world.SyncAsync(first.Id)).Directory.ShouldBe("docs/cards/foo");
        var laterDir = (await world.SyncAsync(later.Id)).Directory;
        laterDir.ShouldNotBe("docs/cards/foo");
        laterDir.ShouldStartWith("docs/cards/foo-");
        laterDir!.Length.ShouldBe("docs/cards/foo-".Length + 8);
    }

    private static void AssertLfNoBom(byte[] bytes)
    {
        bytes.Length.ShouldBeGreaterThan(0);
        if (bytes.Length >= 3)
            (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).ShouldBeFalse();
        Encoding.UTF8.GetString(bytes).ShouldNotContain("\r");
        bytes[^1].ShouldBe((byte)'\n');
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly List<Guid> _projectIds = [];
        private readonly List<Guid> _boardIds = [];
        private readonly List<Guid> _columnIds = [];
        private readonly List<Guid> _cardIds = [];
        private readonly List<ScratchGitRepo> _repos = [];
        private readonly List<string> _tempDirs = [];
        private readonly CardTaskFileSyncGate _gate = new();

        public ScratchGitRepo CreateRepo()
        {
            var repo = new ScratchGitRepo("c0004");
            _repos.Add(repo);
            return repo;
        }

        public string CreateNonRepoDir()
        {
            var dir = Directory.CreateTempSubdirectory("c0004-norepo").FullName;
            _tempDirs.Add(dir);
            return dir;
        }

        public string BoardDir(ScratchGitRepo repo, string slug) =>
            Path.Combine(repo.Path, "docs", "cards", slug);

        public async Task<Project> AddProjectAsync(string? localPath, bool archived = false)
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c0004-{Guid.NewGuid():N}"[..20],
                GitRepositoryUrl = "https://example.invalid/repo.git",
                LocalRepositoryPath = localPath,
                BaseBranch = "master",
                CreatedAt = now,
                UpdatedAt = now,
                ArchivedAt = archived ? now : null,
                ArchivedBy = archived ? "test" : null,
                ArchivedReason = archived ? "test" : null,
            };
            await using var db = CreateContext();
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            _projectIds.Add(project.Id);
            return project;
        }

        public async Task<Board> AddBoardAsync(
            Guid projectId, string name, bool archived = false, DateTime? createdAt = null)
        {
            var now = createdAt ?? DateTime.UtcNow;
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                CreatedAt = now,
                UpdatedAt = now,
                ArchivedAt = archived ? now : null,
                ArchivedBy = archived ? "test" : null,
                ArchivedReason = archived ? "test" : null,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await using var db = CreateContext();
            db.Boards.Add(board);
            db.BoardColumns.Add(column);
            await db.SaveChangesAsync();
            _boardIds.Add(board.Id);
            _columnIds.Add(column.Id);
            board.Columns.Add(column);
            return board;
        }

        public async Task<Card> AddCardAsync(Board board, string identifier, string title)
        {
            var columnId = board.Columns.Count > 0
                ? board.Columns.First().Id
                : throw new InvalidOperationException("AddBoardAsync must run first so the board has a column.");
            var now = DateTime.UtcNow;
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = columnId,
                Identifier = identifier,
                Title = title,
                Description = $"body of {identifier}",
                Status = CardStatus.Backlog,
                Priority = 2,
                LabelsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            };
            await using var db = CreateContext();
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            _cardIds.Add(card.Id);
            return card;
        }

        public async Task RenameCardAsync(Guid cardId, string title)
        {
            await using var db = CreateContext();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.Title = title;
            card.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task ArchiveCardAsync(Guid cardId, string by, string reason)
        {
            await using var db = CreateContext();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.ArchivedAt = DateTime.UtcNow;
            card.ArchivedBy = by;
            card.ArchivedReason = reason;
            await db.SaveChangesAsync();
        }

        public async Task<Antiphon.Server.Application.Dtos.CardFileSyncBoardResult> SyncAsync(
            Guid boardId, bool dryRun = false)
        {
            await using var db = CreateContext();
            var service = new CardTaskFileService(
                db,
                _gate,
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<CardTaskFileService>.Instance);
            return await service.SyncBoardAsync(boardId, dryRun, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.ExternalIssueRefs.Where(r => _cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.CardRevisions.Where(r => _cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.Cards.Where(c => _cardIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => _columnIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Boards.Where(b => _boardIds.Contains(b.Id)).ExecuteDeleteAsync();
            await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();

            foreach (var repo in _repos)
                repo.Dispose();
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
