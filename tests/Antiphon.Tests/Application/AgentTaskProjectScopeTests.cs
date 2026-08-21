using Antiphon.Server.Application.Dtos;
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
/// CARD-0115 S1: a task's project is captured from durable caller provenance once, rather than
/// guessed from its working directory. Each test gets its own migrated schema because task rows are
/// intentionally durable and the assembly's normal Postgres container is shared.
/// </summary>
[Category("Integration")]
public class AgentTaskProjectScopeTests
{
    [Test]
    public async Task a_card_bound_session_token_uses_the_cards_board_project_before_its_owning_agent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (cardProject, cardBoard) = await SeedProjectBoardAsync(db);
        var (_, agentBoard) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, cardBoard.Id);
        var (session, token) = await SeedSessionAsync(db, workspace.Path, card.Id);
        await SeedStandingAgentAsync(db, agentBoard.Id, session.Id, workspace.Path);

        var service = CreateService(db);
        var caller = await service.AuthenticateAsync(token, CancellationToken.None);
        var created = await service.CreateAsync(Request(workspace.Path), caller, CancellationToken.None);

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        task.ProjectId.ShouldBe(cardProject.Id);
        (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == created.Id)).Detail
            .ShouldContain($"project scope: {cardProject.Id}");
    }

    [Test]
    public async Task a_cardless_standing_agents_session_token_uses_the_agents_board_project()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (project, board) = await SeedProjectBoardAsync(db);
        var (session, token) = await SeedSessionAsync(db, workspace.Path, cardId: null);
        await SeedStandingAgentAsync(db, board.Id, session.Id, workspace.Path);

        var created = await CreateService(db).CreateAsync(
            Request(workspace.Path),
            await CreateService(db).AuthenticateAsync(token, CancellationToken.None),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).ProjectId.ShouldBe(project.Id);
    }

    [Test]
    public async Task a_tokenless_ui_task_has_no_project_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();

        var created = await CreateService(db).CreateAsync(
            Request(workspace.Path), new AgentTaskService.Caller(null, null, workspace.Path), CancellationToken.None);

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        task.ProjectId.ShouldBeNull();
        (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == created.Id)).Detail
            .ShouldNotContain("project scope:");
    }

    [Test]
    public async Task a_child_inherits_its_parent_scope_even_when_it_works_in_a_different_directory()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var parentWorkspace = new TempWorkspace();
        using var childWorkspace = new TempWorkspace();
        var (project, _) = await SeedProjectBoardAsync(db);
        var parentToken = Guid.NewGuid().ToString("N");
        var parent = await SeedTaskAsync(db, parentWorkspace.Path, project.Id, token: parentToken);
        var service = CreateService(db, [childWorkspace.Path]);

        var created = await service.CreateAsync(
            Request(childWorkspace.Path),
            await service.AuthenticateAsync(parentToken, CancellationToken.None),
            CancellationToken.None);

        var child = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        child.ProjectId.ShouldBe(project.Id);
        child.WorkingDirectory.ShouldBe(childWorkspace.Path);
    }

    [Test]
    public async Task a_merge_conflict_child_copies_its_parents_project_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (project, _) = await SeedProjectBoardAsync(db);
        var parent = await SeedTaskAsync(db, workspace.Path, project.Id, worktree: true);

        var merge = await CreateService(db).CreateMergeTaskAsync(parent, ["conflicted.cs"], CancellationToken.None);

        merge.ShouldNotBeNull();
        merge!.ProjectId.ShouldBe(project.Id);
        db.ChangeTracker.Entries<AgentTaskEvent>().Single().Entity.Detail
            .ShouldContain($"project scope: {project.Id}");
    }

    [Test]
    public async Task retrying_a_row_keeps_its_original_project_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (project, _) = await SeedProjectBoardAsync(db);
        var task = await SeedTaskAsync(db, workspace.Path, project.Id, status: AgentTaskStatus.Failed);

        await CreateService(db).RetryAsync(task.Id, CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).ProjectId.ShouldBe(project.Id);
    }

    private static CreateAgentTaskRequest Request(string workingDirectory) => new(
        Goal: "Capture project provenance.",
        Kind: AgentTaskKind.Worker,
        Role: AgentTaskRole.Docs,
        WorkingDirectory: workingDirectory);

    private static AgentTaskService CreateService(AppDbContext db, IReadOnlyList<string>? allowedRoots = null) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = allowedRoots?.ToList() ?? [] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<(Project Project, Board Board)> SeedProjectBoardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"scope-project-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/scope.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Scope board {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board);
        await db.SaveChangesAsync();
        return (project, board);
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db, Guid boardId)
    {
        var now = DateTime.UtcNow;
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            StateKey = $"scope-{Guid.NewGuid():N}",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            BoardColumnId = column.Id,
            Identifier = $"SCOPE-{Guid.NewGuid():N}"[..18],
            Title = "Scope card",
            Description = "Scope test.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<(AgentSession Session, string Token)> SeedSessionAsync(
        AppDbContext db, string workingDirectory, Guid? cardId)
    {
        var now = DateTime.UtcNow;
        var token = Guid.NewGuid().ToString("N");
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            DelegationTokenHash = AgentTaskService.HashToken(token),
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return (session, token);
    }

    private static async Task SeedStandingAgentAsync(
        AppDbContext db, Guid boardId, Guid sessionId, string workingDirectory)
    {
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"scope-agent-{Guid.NewGuid():N}"[..30],
            Slug = $"scope-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = workingDirectory,
            Details = "Scope test standing agent.",
            BoardId = boardId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string workingDirectory,
        Guid? projectId,
        AgentTaskStatus status = AgentTaskStatus.Queued,
        string? token = null,
        bool worktree = false)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Depth = 0,
            Title = "Scoped parent",
            Goal = "Seeded scoped task.",
            Kind = AgentTaskKind.Orchestrator,
            Role = AgentTaskRole.Custom,
            ProjectId = projectId,
            ModelLevel = AgentModelLevel.High,
            WorkingDirectory = workingDirectory,
            RepoPath = worktree ? workingDirectory : null,
            WorktreePath = worktree ? workingDirectory : null,
            WorktreeBranch = worktree ? "scope-branch" : null,
            MergeTargetRef = worktree ? "main" : null,
            Status = status,
            TokenHash = token is null ? null : AgentTaskService.HashToken(token),
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-task-scope").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
