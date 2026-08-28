using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0217 S9: boards and projects gain the same archive/unarchive hide as cards. List endpoints
/// exclude archived rows by default; archive refuses a live agent, live session or open task.
/// </summary>
[Category("Integration")]
[NotInParallel("Board")]
public class BoardProjectArchiveTests
{
    [Test]
    public async Task Archiving_a_board_hides_it_from_the_default_list_and_unarchive_restores_it()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Proj {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Archive Board {marker}"), CancellationToken.None);

        var archived = await harness.Boards.ArchiveAsync(
            board.Id, new ArchiveBoardRequest("Test residue.", "operator"), CancellationToken.None);

        archived.ArchivedAt.ShouldNotBeNull();
        archived.ArchivedReason.ShouldBe("Test residue.");
        archived.ArchivedBy.ShouldBe("operator");

        var hidden = await harness.Boards.GetAllAsync(CancellationToken.None);
        hidden.ShouldNotContain(b => b.Id == board.Id);

        var shown = await harness.Boards.GetAllAsync(includeArchived: true, CancellationToken.None);
        shown.ShouldContain(b => b.Id == board.Id);

        var restored = await harness.Boards.UnarchiveAsync(
            board.Id, new UnarchiveBoardRequest("Needed after all."), CancellationToken.None);
        restored.ArchivedAt.ShouldBeNull();
        restored.ArchivedReason.ShouldBeNull();

        var listed = await harness.Boards.GetAllAsync(CancellationToken.None);
        listed.ShouldContain(b => b.Id == board.Id);

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Archiving_a_project_hides_it_and_its_boards_from_the_default_lists()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Hide {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Child Board {marker}"), CancellationToken.None);

        var archived = await harness.Projects.ArchiveAsync(
            project.Id, new ArchiveProjectRequest("Test residue."), CancellationToken.None);
        archived.ArchivedAt.ShouldNotBeNull();

        (await harness.Projects.GetAllAsync(CancellationToken.None))
            .ShouldNotContain(p => p.Id == project.Id);
        (await harness.Projects.GetAllAsync(includeArchived: true, CancellationToken.None))
            .ShouldContain(p => p.Id == project.Id);
        (await harness.Boards.GetAllAsync(CancellationToken.None))
            .ShouldNotContain(b => b.Id == board.Id);
        (await harness.Boards.GetAllAsync(includeArchived: true, CancellationToken.None))
            .ShouldContain(b => b.Id == board.Id);

        await harness.Projects.UnarchiveAsync(
            project.Id, new UnarchiveProjectRequest("Keep it."), CancellationToken.None);

        (await harness.Projects.GetAllAsync(CancellationToken.None))
            .ShouldContain(p => p.Id == project.Id);
        (await harness.Boards.GetAllAsync(CancellationToken.None))
            .ShouldContain(b => b.Id == board.Id);

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Archive_refuses_a_board_with_an_agent_attached()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Agent {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Agent Board {marker}"), CancellationToken.None);
        await SeedAgentAsync(board.Id, $"agent-{marker}");

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            harness.Boards.ArchiveAsync(
                board.Id, new ArchiveBoardRequest("Residue."), CancellationToken.None));
        ex.Message.ShouldContain("agent");

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Archive_refuses_a_board_with_a_live_session()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Sess {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Session Board {marker}"), CancellationToken.None);
        var card = await SeedCardAsync(board, $"CARD-{marker[..8]}");
        await SeedSessionAsync(card.Id, SessionStatus.Running);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            harness.Boards.ArchiveAsync(
                board.Id, new ArchiveBoardRequest("Residue."), CancellationToken.None));
        ex.Message.ShouldContain("live session");

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Archive_refuses_a_board_with_a_non_terminal_task()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Task {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Task Board {marker}"), CancellationToken.None);
        var card = await SeedCardAsync(board, $"CARD-{marker[..8]}");
        await SeedOpenTaskAsync(card.Id, project.Id);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            harness.Boards.ArchiveAsync(
                board.Id, new ArchiveBoardRequest("Residue."), CancellationToken.None));
        ex.Message.ShouldContain("non-terminal task");

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task A_stopped_session_does_not_block_archive()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Stopped {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Stopped Board {marker}"), CancellationToken.None);
        var card = await SeedCardAsync(board, $"CARD-{marker[..8]}");
        await SeedSessionAsync(card.Id, SessionStatus.Stopped);

        var archived = await harness.Boards.ArchiveAsync(
            board.Id, new ArchiveBoardRequest("Residue."), CancellationToken.None);
        archived.ArchivedAt.ShouldNotBeNull();

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Archive_requires_a_reason_and_refuses_a_second_archive()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Twice {marker}");
        var board = await harness.Boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Twice Board {marker}"), CancellationToken.None);

        var blank = await Should.ThrowAsync<ValidationException>(() =>
            harness.Boards.ArchiveAsync(
                board.Id, new ArchiveBoardRequest("  "), CancellationToken.None));
        blank.Errors.ShouldContainKey("Reason");

        await harness.Boards.ArchiveAsync(
            board.Id, new ArchiveBoardRequest("Once."), CancellationToken.None);
        var twice = await Should.ThrowAsync<ConflictException>(() =>
            harness.Boards.ArchiveAsync(
                board.Id, new ArchiveBoardRequest("Twice."), CancellationToken.None));
        twice.Message.ShouldContain("already archived");

        var notArchived = await Should.ThrowAsync<ConflictException>(() =>
            harness.Projects.UnarchiveAsync(
                project.Id, new UnarchiveProjectRequest("Was not archived."), CancellationToken.None));
        notArchived.Message.ShouldContain("is not archived");

        await CleanupAsync(project.Id);
    }

    [Test]
    public async Task Creating_a_board_on_an_archived_project_is_refused()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using var harness = BuildHarness();
        var project = await SeedProjectAsync($"Archive Closed {marker}");
        await harness.Projects.ArchiveAsync(
            project.Id, new ArchiveProjectRequest("Residue."), CancellationToken.None);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, $"Late Board {marker}"), CancellationToken.None));
        ex.Message.ShouldContain("archived");

        await CleanupAsync(project.Id);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness()
    {
        var db = CreateContext();
        var eventBus = new MockEventBus();
        var boards = new BoardService(db, eventBus, TimeProvider.System);
        var projects = new ProjectService(
            db,
            new StubHttpClientFactory(),
            Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance,
            eventBus: eventBus);
        return new Harness(db, projects, boards, eventBus);
    }

    private static async Task<Project> SeedProjectAsync(string name)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-archive-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<Card> SeedCardAsync(BoardDetailDto board, string identifier)
    {
        var column = board.Columns.First(c => c.StateKey == "backlog");
        var now = DateTime.UtcNow;
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"Card {identifier}",
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task SeedAgentAsync(Guid boardId, string slug)
    {
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            WorkingDirectory = Path.GetTempPath(),
            BoardId = boardId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSessionAsync(Guid cardId, SessionStatus status)
    {
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            DefinitionName = "fake",
            AgentKind = AgentKind.Raw,
            Status = status,
            Cwd = Path.GetTempPath(),
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now
        };
        await using var db = CreateContext();
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
    }

    private static async Task SeedOpenTaskAsync(Guid cardId, Guid projectId)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Open residue task",
            Goal = "Stay open.",
            Status = AgentTaskStatus.Dispatched,
            CardId = cardId,
            ProjectId = projectId,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = now
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(Guid projectId)
    {
        await using var db = CreateContext();
        var boardIds = await db.Boards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.AgentTasks.Where(t => t.ProjectId == projectId || (t.CardId != null && cardIds.Contains(t.CardId.Value)))
            .ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.BoardId != null && boardIds.Contains(a.BoardId.Value)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
    }

    private sealed class Harness(
        AppDbContext db,
        ProjectService projects,
        BoardService boards,
        MockEventBus eventBus) : IAsyncDisposable
    {
        public ProjectService Projects { get; } = projects;
        public BoardService Boards { get; } = boards;
        public MockEventBus EventBus { get; } = eventBus;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
