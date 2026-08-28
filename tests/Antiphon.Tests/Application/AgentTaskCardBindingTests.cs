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
/// CARD-0040 S1: which card a delegated task is about. The convention already existed in prose —
/// 397 of 627 live task titles begin with <c>CARD-nnnn</c> — and these pin the rules that turn it
/// into a durable row: explicit beats inherited beats title, identifiers are unique per BOARD so
/// ambiguity binds nothing, and a Check row never binds at all.
///
/// <para>Each test takes its own migrated schema: task and card rows are deliberately durable and
/// the assembly's Postgres container is shared, so an unscoped query would be asserting about other
/// tests' data as well as its own.</para>
/// </summary>
[Category("Integration")]
public class AgentTaskCardBindingTests
{
    [Test]
    public async Task an_explicit_card_guid_binds()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0040");

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with { Card = card.Id.ToString() },
            Manual(workspace.Path),
            CancellationToken.None);

        created.CardId.ShouldBe(card.Id);
        created.CardIdentifier.ShouldBe("CARD-0040");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(card.Id);
    }

    [Test]
    public async Task an_explicit_identifier_binds_inside_the_callers_board_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, callerBoard) = await SeedProjectBoardAsync(db);
        var (_, otherBoard) = await SeedProjectBoardAsync(db);
        var wanted = await SeedCardAsync(db, callerBoard.Id, "CARD-0007");
        var decoy = await SeedCardAsync(db, otherBoard.Id, "CARD-0007");
        var (session, token) = await SeedSessionAsync(db, workspace.Path, wanted.Id);
        await SeedStandingAgentAsync(db, callerBoard.Id, session.Id, workspace.Path);

        var service = CreateService(db, workspace);
        var created = await service.CreateAsync(
            // "#7" is one of card.ps1's accepted spellings — the binder normalises through the
            // same CardService.TryCanonicalIdentifier the card API uses.
            Request(workspace.Path) with { Card = "#7" },
            await service.AuthenticateAsync(token, CancellationToken.None),
            CancellationToken.None);

        created.CardId.ShouldBe(wanted.Id);
        created.CardId.ShouldNotBe(decoy.Id);
    }

    [Test]
    public async Task an_explicit_card_that_resolves_to_nothing_is_refused()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedProjectBoardAsync(db);

        var failure = await Should.ThrowAsync<ValidationException>(async () =>
            await CreateService(db, workspace).CreateAsync(
                Request(workspace.Path) with { Card = "CARD-9999" },
                Manual(workspace.Path),
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        (await db.AgentTasks.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task a_title_leading_with_an_identifier_binds()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0083");

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with { Title = "CARD-0083 S2 build the sweep" },
            Manual(workspace.Path),
            CancellationToken.None);

        created.CardIdentifier.ShouldBe("CARD-0083");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(card.Id);
        (await EventsFor(db, created.Id)).ShouldContain(e => e.Detail.Contains("bound to CARD-0083"));
    }

    [Test]
    public async Task a_title_naming_several_cards_binds_the_first_and_warns_about_the_rest()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var first = await SeedCardAsync(db, board.Id, "CARD-0177");
        await SeedCardAsync(db, board.Id, "CARD-0178");

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with { Title = "CARD-0177 and CARD-0178 and CARD-0179 in one pass" },
            Manual(workspace.Path),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(first.Id);
        created.Warning.ShouldNotBeNull();
        created.Warning!.ShouldContain("CARD-0178");
        created.Warning.ShouldContain("CARD-0179");
        var warnings = (await EventsFor(db, created.Id)).Where(e => e.Type == AgentTaskEventType.Warning).ToList();
        warnings.ShouldHaveSingleItem();
        warnings[0].Detail.ShouldContain("bound to CARD-0177");
    }

    [Test]
    public async Task an_identifier_on_two_boards_with_no_scope_binds_nothing_and_says_so()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, boardA) = await SeedProjectBoardAsync(db, name: "Antiphon");
        var (_, boardB) = await SeedProjectBoardAsync(db, name: "Gym Stat");
        var cardA = await SeedCardAsync(db, boardA.Id, "CARD-0005");
        var cardB = await SeedCardAsync(db, boardB.Id, "CARD-0005");

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with { Title = "CARD-0005 do the thing" },
            Manual(workspace.Path),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBeNull();
        created.Warning.ShouldNotBeNull();
        created.Warning!.ShouldContain("CARD-0005");
        created.Warning.ShouldContain("matches 2 cards");
        created.Warning.ShouldContain("Antiphon");
        created.Warning.ShouldContain("Gym Stat");
        created.Warning.ShouldContain(cardA.Id.ToString());
        created.Warning.ShouldContain(cardB.Id.ToString());
        (await EventsFor(db, created.Id)).ShouldContain(e => e.Type == AgentTaskEventType.Warning);
    }

    [Test]
    public async Task a_child_inherits_its_parents_card()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0083");
        var token = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(db, workspace.Path, cardId: card.Id, token: token);

        var service = CreateService(db, workspace);
        var created = await service.CreateAsync(
            Request(workspace.Path) with { Title = "Run the suite" },
            await service.AuthenticateAsync(token, CancellationToken.None),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(card.Id);
    }

    [Test]
    public async Task a_child_titled_with_another_card_overrides_the_inherited_one()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var parentCard = await SeedCardAsync(db, board.Id, "CARD-0083");
        var otherCard = await SeedCardAsync(db, board.Id, "CARD-0084");
        var token = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(db, workspace.Path, cardId: parentCard.Id, token: token);

        var service = CreateService(db, workspace);
        var created = await service.CreateAsync(
            Request(workspace.Path) with { Title = "CARD-0084 S1 the other card" },
            await service.AuthenticateAsync(token, CancellationToken.None),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(otherCard.Id);
    }

    [Test]
    public async Task a_follow_up_inherits_the_earlier_tasks_card()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0033");
        var agent = await SeedAgentAsync(db, workspace.Path);
        var prior = await SeedTaskAsync(db, workspace.Path, cardId: card.Id, agentId: agent.Id);

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with
            {
                Title = "One more pass",
                FollowUpOnTask = prior.Id.ToString(),
            },
            Manual(workspace.Path),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBe(card.Id);
    }

    [Test]
    public async Task a_merge_task_inherits_the_conflicted_tasks_card()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0063");
        var conflicted = await SeedTaskAsync(db, workspace.Path, cardId: card.Id, worktree: true);

        var merge = await CreateService(db, workspace)
            .CreateMergeTaskAsync(conflicted, ["conflicted.cs"], CancellationToken.None);

        merge.ShouldNotBeNull();
        merge!.CardId.ShouldBe(card.Id);
    }

    [Test]
    public async Task a_check_task_never_binds_even_when_its_title_names_a_card()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        await SeedCardAsync(db, board.Id, "CARD-0040");

        var created = await CreateService(db, workspace).CreateAsync(
            Request(workspace.Path) with
            {
                Role = AgentTaskRole.Check,
                Title = "CARD-0040 check #1 on task 242a7647",
            },
            Manual(workspace.Path),
            CancellationToken.None);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).CardId.ShouldBeNull();
        created.CardIdentifier.ShouldBeNull();
    }

    [Test]
    public async Task the_summary_exposes_the_card_id_and_identifier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var (_, board) = await SeedProjectBoardAsync(db);
        var card = await SeedCardAsync(db, board.Id, "CARD-0110");

        var service = CreateService(db, workspace);
        var created = await service.CreateAsync(
            Request(workspace.Path) with { Title = "CARD-0110 land the fix" },
            Manual(workspace.Path),
            CancellationToken.None);

        var detail = await service.GetAsync(created.Id, CancellationToken.None);
        detail.Summary.CardId.ShouldBe(card.Id);
        detail.Summary.CardIdentifier.ShouldBe("CARD-0110");

        var listed = await service.ListAsync(null, null, includeChecks: false, CancellationToken.None);
        listed.ShouldContain(t => t.Id == created.Id && t.CardIdentifier == "CARD-0110");
    }

    [Test]
    public async Task a_history_window_hides_old_terminal_tasks_but_keeps_old_open_work()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var since = DateTime.UtcNow.AddDays(-7);
        var oldTerminal = TaskRow(AgentTaskStatus.Succeeded, since.AddDays(-1), since.AddDays(-1));
        var oldBlocked = TaskRow(AgentTaskStatus.Blocked, since.AddDays(-21), null);
        var recentTerminal = TaskRow(AgentTaskStatus.Failed, since.AddMinutes(1), since.AddMinutes(1));
        db.AgentTasks.AddRange(oldTerminal, oldBlocked, recentTerminal);
        await db.SaveChangesAsync();

        var listed = await CreateService(db, workspace).ListAsync(
            rootId: null, statuses: null, includeChecks: false, since, CancellationToken.None);

        listed.Select(task => task.Id).ShouldNotContain(oldTerminal.Id);
        listed.Select(task => task.Id).ShouldContain(oldBlocked.Id);
        listed.Select(task => task.Id).ShouldContain(recentTerminal.Id);
    }

    [Test]
    public async Task fleet_summary_matches_the_unfiltered_delegations_list()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var root = Guid.NewGuid();
        db.AgentTasks.AddRange(
            TaskRow(AgentTaskStatus.Working, DateTime.UtcNow, null, root, costUsd: 0.05m),
            TaskRow(AgentTaskStatus.Blocked, DateTime.UtcNow, null, root, costUsd: 0.01m),
            TaskRow(AgentTaskStatus.Succeeded, DateTime.UtcNow, DateTime.UtcNow, costUsd: 1.23m));
        await db.SaveChangesAsync();

        var service = CreateService(db, workspace);
        var full = await service.ListAsync(null, null, includeChecks: false, CancellationToken.None);
        var summary = await service.GetListSummaryAsync(CancellationToken.None);

        summary.Runs.ShouldBe(full.Select(task => task.RootTaskId).Distinct().Count());
        summary.Active.ShouldBe(full.Count(task => task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working));
        summary.Blocked.ShouldBe(full.Count(task => task.Status == AgentTaskStatus.Blocked));
        summary.TotalCostUsd.ShouldBe(full.Sum(task => task.CostUsd));
        foreach (var group in full.GroupBy(task => task.Status))
            summary.ByStatus[group.Key.ToString()].ShouldBe(group.Count());
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static CreateAgentTaskRequest Request(string workingDirectory) => new(
        Goal: "Bind this task to a card.",
        Kind: AgentTaskKind.Orchestrator,
        Role: AgentTaskRole.Custom,
        WorkingDirectory: workingDirectory);

    private static AgentTaskService.Caller Manual(string workingDirectory) => new(null, null, workingDirectory);

    private static AgentTask TaskRow(
        AgentTaskStatus status,
        DateTime createdAt,
        DateTime? completedAt,
        Guid? rootTaskId = null,
        decimal costUsd = 0m)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId ?? id,
            Title = "List test task",
            Goal = "Exercise the list projection.",
            WorkingDirectory = "C:\\list-test",
            Status = status,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            CostUsd = costUsd,
        };
    }

    private static AgentTaskService CreateService(AppDbContext db, TempWorkspace workspace) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<List<AgentTaskEvent>> EventsFor(AppDbContext db, Guid taskId) =>
        await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId).ToListAsync();

    private static async Task<(Project Project, Board Board)> SeedProjectBoardAsync(
        AppDbContext db, string? name = null)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"bind-project-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/bind.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = name ?? $"Bind board {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board);
        await db.SaveChangesAsync();
        return (project, board);
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db, Guid boardId, string identifier)
    {
        var now = DateTime.UtcNow;
        var column = await db.BoardColumns.FirstOrDefaultAsync(c => c.BoardId == boardId);
        if (column is null)
        {
            column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.BoardColumns.Add(column);
            await db.SaveChangesAsync();
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} test card",
            Description = "Binding test.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Cards.Add(card);
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
            Name = $"bind-agent-{Guid.NewGuid():N}"[..30],
            Slug = $"bind-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = workingDirectory,
            Details = "Binding test standing agent.",
            BoardId = boardId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Agent> SeedAgentAsync(AppDbContext db, string workingDirectory)
    {
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"bind-pool-{Guid.NewGuid():N}"[..30],
            Slug = $"pool-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = workingDirectory,
            Details = "Binding test pool agent.",
            ModelLevel = AgentModelLevel.High,
            Kind = AgentKind.ClaudeCode,
            IsPoolDelegate = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string workingDirectory,
        Guid? cardId,
        string? token = null,
        Guid? agentId = null,
        bool worktree = false)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Depth = 0,
            Title = "Seeded parent",
            Goal = "Seeded task.",
            Kind = AgentTaskKind.Orchestrator,
            Role = AgentTaskRole.Custom,
            CardId = cardId,
            AgentId = agentId,
            ModelLevel = AgentModelLevel.High,
            WorkingDirectory = workingDirectory,
            RepoPath = worktree ? workingDirectory : null,
            WorktreePath = worktree ? workingDirectory : null,
            WorktreeBranch = worktree ? "bind-branch" : null,
            MergeTargetRef = worktree ? "main" : null,
            Status = AgentTaskStatus.Queued,
            TokenHash = token is null ? null : AgentTaskService.HashToken(token),
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-card-bind").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
