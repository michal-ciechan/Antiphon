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
/// Deleting a project used to hit the <c>Board -&gt; Project</c> FK, which is
/// <see cref="DeleteBehavior.Restrict"/>: EF raised a <c>DbUpdateException</c> that no handler
/// mapped, so the API answered 500 with a Postgres error string (GitHub issue #2).
///
/// These tests pin the replacement contract: a project that owns things refuses politely and says
/// what it owns, a forced delete takes the whole board subtree with it, and deleting the last
/// board takes the now-empty project with it.
/// </summary>
[Category("Integration")]
[NotInParallel("Board")]
public class ProjectDeletionTests
{
    // ---------------------------------------------------------------------
    // Refusing, rather than exploding
    // ---------------------------------------------------------------------

    [Test]
    public async Task Deleting_a_project_that_owns_a_board_is_refused_instead_of_exploding()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            await harness.Boards.CreateAsync(new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);

            // The bug: this used to surface as DbUpdateException -> HTTP 500.
            await Should.ThrowAsync<ConflictException>(() =>
                harness.Projects.DeleteAsync(project.Id, force: false, CancellationToken.None));

            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task The_refusal_names_what_is_attached_so_the_dialog_can_warn()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            await SeedCardAsync(board, "CARD-1", CardStatus.Backlog);
            await SeedCardAsync(board, "CARD-2", CardStatus.InProgress);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Projects.DeleteAsync(project.Id, force: false, CancellationToken.None));

            ex.Message.ShouldContain("1 board");
            ex.Message.ShouldContain("2 card");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task An_empty_project_deletes_without_force()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();

            await harness.Projects.DeleteAsync(project.Id, force: false, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------------------------------------------------------------------
    // What a forced delete actually takes with it
    // ---------------------------------------------------------------------

    [Test]
    public async Task Force_delete_removes_the_whole_board_subtree()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            var card = await SeedCardAsync(board, "CARD-1", CardStatus.InProgress);
            var session = await SeedSessionAsync(card.Id, SessionStatus.Stopped);

            await harness.Projects.DeleteAsync(project.Id, force: true, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeFalse();
            (await verify.Boards.AnyAsync(b => b.Id == board.Id)).ShouldBeFalse();
            (await verify.BoardColumns.AnyAsync(c => c.BoardId == board.Id)).ShouldBeFalse();
            (await verify.Cards.AnyAsync(c => c.Id == card.Id)).ShouldBeFalse();
            (await verify.AgentSessions.AnyAsync(s => s.Id == session.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>
    /// Agents outlive the boards they were working: <c>Agent.BoardId</c> is SetNull for a reason.
    /// The issue asks for a warning about them, not for their destruction.
    /// </summary>
    [Test]
    public async Task Force_delete_detaches_agents_rather_than_deleting_them()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            var card = await SeedCardAsync(board, "CARD-1", CardStatus.InProgress);
            var agent = await SeedAgentAsync(tempRoot, board.Id, card.Id);

            await harness.Projects.DeleteAsync(project.Id, force: true, CancellationToken.None);

            await using var verify = CreateContext();
            var stored = await verify.Agents.SingleAsync(a => a.Id == agent.Id);
            stored.BoardId.ShouldBeNull();
            stored.CurrentCardId.ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Force_delete_leaves_other_projects_untouched()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var doomed = await SeedProjectAsync(tempRoot);
            var keeper = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            await harness.Boards.CreateAsync(new CreateBoardRequest(doomed.Id, "Doomed"), CancellationToken.None);
            var keptBoard = await harness.Boards.CreateAsync(
                new CreateBoardRequest(keeper.Id, "Kept"), CancellationToken.None);
            var keptCard = await SeedCardAsync(keptBoard, "KEEP-1", CardStatus.Backlog);

            await harness.Projects.DeleteAsync(doomed.Id, force: true, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == keeper.Id)).ShouldBeTrue();
            (await verify.Boards.AnyAsync(b => b.Id == keptBoard.Id)).ShouldBeTrue();
            (await verify.Cards.AnyAsync(c => c.Id == keptCard.Id)).ShouldBeTrue();
            (await verify.BoardColumns.CountAsync(c => c.BoardId == keptBoard.Id)).ShouldBe(4);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------------------------------------------------------------------
    // The impact report the confirm dialog reads
    // ---------------------------------------------------------------------

    [Test]
    public async Task Deletion_impact_counts_boards_cards_open_cards_agents_and_live_sessions()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            var open = await SeedCardAsync(board, "CARD-1", CardStatus.InProgress);
            await SeedCardAsync(board, "CARD-2", CardStatus.Done);
            await SeedCardAsync(board, "CARD-3", CardStatus.Canceled);
            await SeedSessionAsync(open.Id, SessionStatus.Running);
            await SeedSessionAsync(open.Id, SessionStatus.Stopped);
            await SeedAgentAsync(tempRoot, board.Id, open.Id);

            var impact = await harness.Projects.GetDeletionImpactAsync(project.Id, CancellationToken.None);

            impact.BoardCount.ShouldBe(1);
            impact.CardCount.ShouldBe(3);
            // Done and Canceled are settled; only the in-progress card is still outstanding.
            impact.OpenCardCount.ShouldBe(1);
            impact.RunningSessionCount.ShouldBe(1);
            impact.DetachedAgentCount.ShouldBe(1);
            impact.RequiresConfirmation.ShouldBeTrue();
            impact.CanDelete.ShouldBeTrue();
            impact.Blockers.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Deletion_impact_of_an_empty_project_asks_for_no_confirmation()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();

            var impact = await harness.Projects.GetDeletionImpactAsync(project.Id, CancellationToken.None);

            impact.ProjectName.ShouldBe(project.Name);
            impact.BoardCount.ShouldBe(0);
            impact.CardCount.ShouldBe(0);
            impact.RequiresConfirmation.ShouldBeFalse();
            impact.CanDelete.ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Deletion_impact_of_a_missing_project_is_not_found()
    {
        await using var harness = BuildHarness();

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.Projects.GetDeletionImpactAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // Orchestrator workflows block the delete outright
    // ---------------------------------------------------------------------

    /// <summary>
    /// Workflows hang cost-ledger entries off themselves via a Restrict FK — financial records that
    /// must not vanish behind a checkbox. They are reported as a blocker and force does not
    /// override them; the workflow has to be deleted through its own screen first.
    /// </summary>
    [Test]
    public async Task A_project_with_orchestrator_workflows_cannot_be_deleted_even_with_force()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await SeedWorkflowAsync(project.Id, tempRoot);
            await using var harness = BuildHarness();

            var impact = await harness.Projects.GetDeletionImpactAsync(project.Id, CancellationToken.None);
            impact.WorkflowCount.ShouldBe(1);
            impact.CanDelete.ShouldBeFalse();
            impact.Blockers.ShouldNotBeEmpty();

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Projects.DeleteAsync(project.Id, force: true, CancellationToken.None));
            ex.Message.ShouldContain("workflow");

            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------------------------------------------------------------------
    // Deleting a board, and the reverse cascade the issue asks for
    // ---------------------------------------------------------------------

    [Test]
    public async Task Deleting_a_board_removes_its_columns_and_cards()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            var keeper = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Keeper"), CancellationToken.None);
            var card = await SeedCardAsync(board, "CARD-1", CardStatus.Backlog);

            await harness.Boards.DeleteAsync(board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Boards.AnyAsync(b => b.Id == board.Id)).ShouldBeFalse();
            (await verify.BoardColumns.AnyAsync(c => c.BoardId == board.Id)).ShouldBeFalse();
            (await verify.Cards.AnyAsync(c => c.Id == card.Id)).ShouldBeFalse();
            (await verify.Boards.AnyAsync(b => b.Id == keeper.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Deleting_the_last_board_deletes_its_now_empty_project()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Only Board"), CancellationToken.None);

            var result = await harness.Boards.DeleteAsync(board.Id, CancellationToken.None);

            result.ProjectDeleted.ShouldBeTrue();
            result.ProjectId.ShouldBe(project.Id);
            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Deleting_one_of_two_boards_keeps_the_project()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var first = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "First"), CancellationToken.None);
            await harness.Boards.CreateAsync(new CreateBoardRequest(project.Id, "Second"), CancellationToken.None);

            var result = await harness.Boards.DeleteAsync(first.Id, CancellationToken.None);

            result.ProjectDeleted.ShouldBeFalse();
            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>
    /// "Last thing attached to it" means last thing — a workflow still pinned to the project keeps
    /// the project alive even once its final board is gone.
    /// </summary>
    [Test]
    public async Task Deleting_the_last_board_keeps_a_project_that_still_has_workflows()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await SeedWorkflowAsync(project.Id, tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Only Board"), CancellationToken.None);

            var result = await harness.Boards.DeleteAsync(board.Id, CancellationToken.None);

            result.ProjectDeleted.ShouldBeFalse();
            await using var verify = CreateContext();
            (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Deleting_a_missing_board_is_not_found()
    {
        await using var harness = BuildHarness();

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.Boards.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task Deleting_a_board_announces_the_change()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var project = await SeedProjectAsync(tempRoot);
            await using var harness = BuildHarness();
            var board = await harness.Boards.CreateAsync(
                new CreateBoardRequest(project.Id, "Delivery"), CancellationToken.None);
            harness.EventBus.Clear();

            await harness.Boards.DeleteAsync(board.Id, CancellationToken.None);

            harness.EventBus.PublishedEvents
                .Count(e => e.EventName == "BoardChanged")
                .ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------

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
            NullLogger<ProjectService>.Instance);
        return new Harness(db, projects, boards, eventBus);
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-project-delete-{Guid.NewGuid():N}");

    private static async Task<Project> SeedProjectAsync(string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            // Cleanup keys off this prefix, so every row a test makes is reachable afterwards.
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<Card> SeedCardAsync(BoardDetailDto board, string identifier, CardStatus status)
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
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<AgentSession> SeedSessionAsync(Guid cardId, SessionStatus status)
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
        return session;
    }

    private static async Task<Agent> SeedAgentAsync(string tempRoot, Guid boardId, Guid cardId)
    {
        var now = DateTime.UtcNow;
        var slug = Guid.NewGuid().ToString("N");
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"Agent {slug}",
            Slug = $"agent-{slug}",
            WorkingDirectory = Path.Combine(tempRoot, "agent"),
            BoardId = boardId,
            CurrentCardId = cardId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<Workflow> SeedWorkflowAsync(Guid projectId, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Template {Guid.NewGuid():N}",
            // Cleanup finds the template by this marker.
            Description = tempRoot,
            YamlDefinition = "name: Noop\nstages: []\n",
            IsBuiltIn = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = $"Workflow {Guid.NewGuid():N}",
            TemplateId = template.Id,
            ProjectId = projectId,
            Status = WorkflowStatus.Completed,
            InitialContext = "{}",
            GitBranchName = "feature/noop",
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var db = CreateContext();
        db.WorkflowTemplates.Add(template);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        return workflow;
    }

    /// <summary>
    /// Most tests here delete what they made; this is the safety net for the ones that assert a
    /// refusal, so a failing run cannot poison the shared database for the next one.
    /// </summary>
    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();

        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.CurrentCardId, (Guid?)null)
                .SetProperty(a => a.BoardId, (Guid?)null));

        await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.WorkingDirectory.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.Workflows.Where(w => projectIds.Contains(w.ProjectId)).ExecuteDeleteAsync();
        await db.WorkflowTemplates.Where(t => t.Description == tempRoot).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private sealed record Harness(
        AppDbContext Db,
        ProjectService Projects,
        BoardService Boards,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        // No test here exercises git connectivity; the dependency just has to be satisfiable.
        public HttpClient CreateClient(string name) => new();
    }
}
