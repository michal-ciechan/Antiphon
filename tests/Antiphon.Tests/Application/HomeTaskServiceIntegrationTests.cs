using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
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

/// <summary>
/// CARD-0002 S1: the read-only home-rail projection. Each test takes its own migrated schema so
/// assertions can name the rows they created (<c>items.Single(i => i.Id == mine.Id)</c>) without
/// colliding with the shared Postgres container. Nothing here sweeps global state, so the class
/// is not <c>[NotInParallel]</c>.
/// </summary>
[Category("Integration")]
public class HomeTaskServiceIntegrationTests
{
    [Test]
    public async Task a_needs_decision_card_is_needs_human_with_decision_reason()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.NeedsDecision);

        var item = (await world.GetAsync()).Items.Single(i => i.Id == card.Id);

        item.Source.ShouldBe(HomeTaskSource.Card);
        item.Group.ShouldBe(HomeTaskGroup.NeedsHuman);
        item.HumanReason.ShouldBe(HomeTaskHumanReason.Decision);
        item.State.ShouldBe("NeedsDecision");
        item.Identifier.ShouldBe(card.Identifier);
        item.Key.ShouldBe($"card:{card.Id:N}");
    }

    [Test]
    public async Task a_waiting_for_human_review_workflow_is_needs_human_gate_and_stage_beats_role()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Working, role: AgentTaskRole.Code);
        await world.SeedWorkflowRunAsync(card.Id, CardWorkflowRunStatus.WaitingForHumanReview, "Human gate");

        var item = (await world.GetAsync()).Items.Single(i => i.Id == card.Id);

        item.Group.ShouldBe(HomeTaskGroup.NeedsHuman);
        item.HumanReason.ShouldBe(HomeTaskHumanReason.Gate);
        item.Stage.ShouldBe("Human gate");
        item.WorkflowRunStatus.ShouldBe(CardWorkflowRunStatus.WaitingForHumanReview);
    }

    [Test]
    public async Task an_in_progress_card_with_a_blocked_bound_task_is_needs_human_question()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        var task = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Blocked, role: AgentTaskRole.Code);

        var dto = await world.GetAsync();
        var item = dto.Items.Single(i => i.Id == card.Id);

        item.Group.ShouldBe(HomeTaskGroup.NeedsHuman);
        item.HumanReason.ShouldBe(HomeTaskHumanReason.Question);
        item.Worker.ShouldNotBeNull();
        item.Worker!.Status.ShouldBe(AgentTaskStatus.Blocked);
        item.Worker.TaskId.ShouldBe(task.Id);
        dto.Items.Any(i => i.Id == task.Id).ShouldBeFalse();
    }

    [Test]
    public async Task an_in_progress_card_with_a_working_bound_task_is_running_then_next_after_settle()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress, startedAt: world.Now);
        var task = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Working, role: AgentTaskRole.Code,
            agentName: "task-code", dispatchedAt: world.Now);

        var running = (await world.GetAsync()).Items.Single(i => i.Id == card.Id);
        running.Group.ShouldBe(HomeTaskGroup.Running);
        running.Stage.ShouldBe("Code");
        running.Worker.ShouldNotBeNull();
        running.Worker!.AgentName.ShouldBe("task-code");
        running.Worker.Status.ShouldBe(AgentTaskStatus.Working);

        await world.SettleTaskAsync(task.Id, AgentTaskStatus.Succeeded);

        var after = await world.GetAsync();
        var next = after.Items.Single(i => i.Id == card.Id);
        next.Group.ShouldBe(HomeTaskGroup.Next);
        next.Worker.ShouldNotBeNull();
        next.Worker!.Status.ShouldBe(AgentTaskStatus.Succeeded);
        after.Items.Any(i => i.Id == task.Id).ShouldBeFalse();
    }

    [Test]
    public async Task a_review_card_is_review_and_backlog_cards_are_next_ordered_by_priority()
    {
        await using var world = await World.CreateAsync();
        var review = await world.SeedCardAsync(CardStatus.Review);
        var later = await world.SeedCardAsync(CardStatus.Backlog, priority: 5, createdAt: world.Now.AddMinutes(-1));
        var sooner = await world.SeedCardAsync(CardStatus.Backlog, priority: 1, createdAt: world.Now.AddMinutes(-10));

        var items = (await world.GetAsync()).Items.ToList();
        var reviewItem = items.Single(i => i.Id == review.Id);
        reviewItem.Group.ShouldBe(HomeTaskGroup.Review);
        reviewItem.HumanReason.ShouldBe(HomeTaskHumanReason.Review);
        reviewItem.State.ShouldBe("Review");

        var first = items.Single(i => i.Id == sooner.Id);
        var second = items.Single(i => i.Id == later.Id);
        first.Group.ShouldBe(HomeTaskGroup.Next);
        second.Group.ShouldBe(HomeTaskGroup.Next);
        items.IndexOf(first).ShouldBeLessThan(items.IndexOf(second));
    }

    [Test]
    public async Task unbound_tasks_group_by_status_and_a_bound_task_is_only_a_worker()
    {
        await using var world = await World.CreateAsync();
        var queued = await world.SeedTaskAsync(cardId: null, AgentTaskStatus.Queued);
        var working = await world.SeedTaskAsync(cardId: null, AgentTaskStatus.Working);
        var blocked = await world.SeedTaskAsync(cardId: null, AgentTaskStatus.Blocked);
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        var bound = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Working, role: AgentTaskRole.Plan);

        var dto = await world.GetAsync();
        dto.Items.Single(i => i.Id == queued.Id).Group.ShouldBe(HomeTaskGroup.Next);
        dto.Items.Single(i => i.Id == working.Id).Group.ShouldBe(HomeTaskGroup.Running);
        var blockedItem = dto.Items.Single(i => i.Id == blocked.Id);
        blockedItem.Group.ShouldBe(HomeTaskGroup.NeedsHuman);
        blockedItem.HumanReason.ShouldBe(HomeTaskHumanReason.Question);
        blockedItem.Source.ShouldBe(HomeTaskSource.Delegation);

        dto.Items.Any(i => i.Id == bound.Id).ShouldBeFalse();
        dto.Items.Single(i => i.Id == card.Id).Worker!.TaskId.ShouldBe(bound.Id);
    }

    [Test]
    public async Task check_role_rows_are_never_an_item_and_never_a_worker()
    {
        await using var world = await World.CreateAsync();
        var unboundCheck = await world.SeedTaskAsync(
            cardId: null, AgentTaskStatus.Working, role: AgentTaskRole.Check);
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var boundCheck = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Working, role: AgentTaskRole.Check);

        var dto = await world.GetAsync();
        dto.Items.Any(i => i.Id == unboundCheck.Id).ShouldBeFalse();
        dto.Items.Any(i => i.Id == boundCheck.Id).ShouldBeFalse();
        var cardItem = dto.Items.Single(i => i.Id == card.Id);
        cardItem.Worker.ShouldBeNull();
        cardItem.Group.ShouldBe(HomeTaskGroup.Next);
    }

    [Test]
    public async Task the_done_window_keeps_yesterday_and_drops_thirty_days_and_carries_read_fields()
    {
        await using var world = await World.CreateAsync();
        var oldCard = await world.SeedCardAsync(
            CardStatus.Done, completedAt: world.Now.AddDays(-30));
        var recentCard = await world.SeedCardAsync(
            CardStatus.Done, completedAt: world.Now.AddDays(-1));
        var oldTask = await world.SeedTaskAsync(
            cardId: null, AgentTaskStatus.Succeeded, completedAt: world.Now.AddDays(-30));
        var recentTask = await world.SeedTaskAsync(
            cardId: null, AgentTaskStatus.Succeeded,
            completedAt: world.Now.AddDays(-1),
            readAt: world.Now.AddHours(-2),
            deliverablePath: "docs/superpowers/plans/recent.md",
            deliverableRef: "feat/recent");

        var dto = await world.GetAsync();
        dto.Items.Any(i => i.Id == oldCard.Id).ShouldBeFalse();
        dto.Items.Any(i => i.Id == oldTask.Id).ShouldBeFalse();

        var cardItem = dto.Items.Single(i => i.Id == recentCard.Id);
        cardItem.Group.ShouldBe(HomeTaskGroup.Done);
        cardItem.State.ShouldBe("Done");

        var taskItem = dto.Items.Single(i => i.Id == recentTask.Id);
        taskItem.Group.ShouldBe(HomeTaskGroup.Done);
        taskItem.ReadAt.ShouldNotBeNull();
        taskItem.ReadAt!.Value.ShouldBe(recentTask.ReadAt!.Value, TimeSpan.FromMilliseconds(1));
        taskItem.DeliverablePath.ShouldBe("docs/superpowers/plans/recent.md");
        taskItem.DeliverableRef.ShouldBe("feat/recent");
    }

    [Test]
    public async Task directory_fields_come_from_the_project_or_the_task_and_are_null_without_history()
    {
        await using var world = await World.CreateAsync();
        var withPath = await world.SeedCardAsync(CardStatus.Backlog);
        var worktreePath = Path.Combine(world.TempRoot, "wt");
        await world.SeedWorktreeAsync(withPath.Id, worktreePath);

        var noPathBoard = await world.SeedBoardAsync(localRepositoryPath: null);
        var noHistory = await world.SeedCardAsync(CardStatus.Backlog, boardId: noPathBoard);

        var task = await world.SeedTaskAsync(
            cardId: null, AgentTaskStatus.Queued,
            workingDirectory: @"C:\src\task-cwd",
            repoPath: @"C:\src\task-repo",
            worktreePath: @"C:\src\task-wt");

        var dto = await world.GetAsync();
        var pathItem = dto.Items.Single(i => i.Id == withPath.Id);
        pathItem.WorkingDirectory.ShouldBe(world.RepoPath);
        pathItem.WorktreePath.ShouldBe(worktreePath);

        dto.Items.Single(i => i.Id == noHistory.Id).WorkingDirectory.ShouldBeNull();

        var taskItem = dto.Items.Single(i => i.Id == task.Id);
        taskItem.WorkingDirectory.ShouldBe(@"C:\src\task-cwd");
        taskItem.RepoPath.ShouldBe(@"C:\src\task-repo");
        taskItem.WorktreePath.ShouldBe(@"C:\src\task-wt");
    }

    [Test]
    public async Task needs_you_orders_decision_before_question_and_running_orders_newer_dispatch_first()
    {
        await using var world = await World.CreateAsync();
        var questionCard = await world.SeedCardAsync(
            CardStatus.InProgress, updatedAt: world.Now.AddDays(-10));
        await world.SeedTaskAsync(
            questionCard.Id, AgentTaskStatus.Blocked, dispatchedAt: world.Now.AddDays(-10));
        var decisionCard = await world.SeedCardAsync(
            CardStatus.NeedsDecision, updatedAt: world.Now.AddHours(-1));

        var olderRun = await world.SeedCardAsync(CardStatus.InProgress, startedAt: world.Now.AddHours(-5));
        await world.SeedTaskAsync(olderRun.Id, AgentTaskStatus.Working, dispatchedAt: world.Now.AddHours(-5));
        var newerRun = await world.SeedCardAsync(CardStatus.InProgress, startedAt: world.Now.AddHours(-1));
        await world.SeedTaskAsync(newerRun.Id, AgentTaskStatus.Working, dispatchedAt: world.Now.AddHours(-1));

        var items = (await world.GetAsync()).Items.ToList();
        var decision = items.Single(i => i.Id == decisionCard.Id);
        var question = items.Single(i => i.Id == questionCard.Id);
        items.IndexOf(decision).ShouldBeLessThan(items.IndexOf(question));

        var newer = items.Single(i => i.Id == newerRun.Id);
        var older = items.Single(i => i.Id == olderRun.Id);
        items.IndexOf(newer).ShouldBeLessThan(items.IndexOf(older));
    }

    [Test]
    public async Task a_closed_card_carries_terminal_reason_and_an_open_card_does_not()
    {
        await using var world = await World.CreateAsync();
        var closed = await world.SeedCardAsync(
            CardStatus.Done, completedAt: world.Now.AddDays(-1),
            terminalReason: "Shipped as the plan.");
        var open = await world.SeedCardAsync(CardStatus.InProgress);

        var dto = await world.GetAsync();
        dto.Items.Single(i => i.Id == closed.Id).TerminalReason.ShouldBe("Shipped as the plan.");
        dto.Items.Single(i => i.Id == open.Id).TerminalReason.ShouldBeNull();
    }

    [Test]
    public async Task Get_home_tasks_returns_200_and_serialises_enums_as_strings()
    {
        await using var factory = new AntiphonWebAppFactory();
        Guid cardId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"Home tasks {Guid.NewGuid():N}"[..24],
                GitRepositoryUrl = "https://example.test/home-tasks.git",
                LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-home-{Guid.NewGuid():N}"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Home",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = "CARD-0002",
                Title = "Home tasks HTTP smoke",
                Status = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board, column, card);
            await db.SaveChangesAsync();
            cardId = card.Id;
        }

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/home/tasks");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.ShouldContain("\"source\":\"Card\"");
        json.ShouldNotContain("\"source\":0");
        json.ShouldContain("\"group\":\"Next\"");

        var dto = JsonSerializer.Deserialize<HomeTasksDto>(json, Json)!;
        var item = dto.Items.Single(i => i.Id == cardId);
        item.Source.ShouldBe(HomeTaskSource.Card);
        item.Group.ShouldBe(HomeTaskGroup.Next);
        item.State.ShouldBe("Backlog");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private int _cardNumber;

        private World(IsolatedTestSchema schema, ServiceProvider provider, string tempRoot, Guid boardId, string repoPath)
        {
            _schema = schema;
            _provider = provider;
            TempRoot = tempRoot;
            BoardId = boardId;
            RepoPath = repoPath;
        }

        public string TempRoot { get; }
        public Guid BoardId { get; }
        public string RepoPath { get; }
        public DateTime Now { get; } = DateTime.UtcNow;

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-home-tasks-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(schema.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<HomeTaskService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var (boardId, repoPath) = await SeedDefaultBoardAsync(provider, tempRoot);
            return new World(schema, provider, tempRoot, boardId, repoPath);
        }

        private static async Task<(Guid BoardId, string RepoPath)> SeedDefaultBoardAsync(
            ServiceProvider provider, string tempRoot)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repoPath = Path.Combine(tempRoot, "repo");
            var boardId = await InsertBoardAsync(db, "Home tasks project", repoPath);
            return (boardId, repoPath);
        }

        public async Task<Guid> SeedBoardAsync(string? localRepositoryPath)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await InsertBoardAsync(db, $"Home tasks project {Guid.NewGuid():N}"[..32], localRepositoryPath);
        }

        private static async Task<Guid> InsertBoardAsync(AppDbContext db, string projectName, string? localRepositoryPath)
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                GitRepositoryUrl = "https://example.test/home-tasks.git",
                LocalRepositoryPath = localRepositoryPath,
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Home",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board);

            var columns = new (string Key, string Name, CardStatus Status, bool Active, bool Terminal)[]
            {
                ("backlog", "Backlog", CardStatus.Backlog, false, false),
                ("in-progress", "In Progress", CardStatus.InProgress, true, false),
                ("review", "Review", CardStatus.Review, false, false),
                ("needs-decision", "Needs decision", CardStatus.NeedsDecision, false, false),
                ("done", "Done", CardStatus.Done, false, true),
                ("canceled", "Canceled", CardStatus.Canceled, false, true),
            };
            for (var i = 0; i < columns.Length; i++)
            {
                var (key, name, status, active, terminal) = columns[i];
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = Guid.NewGuid(),
                    BoardId = board.Id,
                    StateKey = key,
                    Name = name,
                    ColumnOrder = i,
                    CardStatus = status,
                    IsActive = active,
                    IsTerminal = terminal,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
            return board.Id;
        }

        public async Task<HomeTasksDto> GetAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<HomeTaskService>()
                .GetAsync(CancellationToken.None);
        }

        public async Task<Card> SeedCardAsync(
            CardStatus status,
            int priority = 0,
            DateTime? createdAt = null,
            DateTime? updatedAt = null,
            DateTime? startedAt = null,
            DateTime? completedAt = null,
            Guid? boardId = null,
            Guid? assignedAgentId = null,
            string? terminalReason = null)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var targetBoard = boardId ?? BoardId;
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == targetBoard && c.CardStatus == status);
            var at = createdAt ?? Now.AddDays(-30);
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = targetBoard,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{Interlocked.Increment(ref _cardNumber):0000}",
                Title = $"Home card {status}",
                Status = status,
                Importance = (CardImportance)priority,
                AssignedAgentId = assignedAgentId,
                CreatedAt = at,
                UpdatedAt = updatedAt ?? at,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                TerminalReason = terminalReason,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task<AgentTask> SeedTaskAsync(
            Guid? cardId,
            AgentTaskStatus status,
            AgentTaskRole role = AgentTaskRole.Code,
            DateTime? dispatchedAt = null,
            DateTime? completedAt = null,
            DateTime? createdAt = null,
            DateTime? readAt = null,
            string? deliverablePath = null,
            string? deliverableRef = null,
            string? agentName = null,
            string? workingDirectory = null,
            string? repoPath = null,
            string? worktreePath = null)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Depth = 0,
                Title = role == AgentTaskRole.Check ? "Check row" : "Home task",
                Goal = "Home tasks projection.",
                Kind = AgentTaskKind.Worker,
                Role = role,
                CardId = cardId,
                ModelLevel = AgentModelLevel.High,
                WorkingDirectory = workingDirectory ?? TempRoot,
                RepoPath = repoPath,
                WorktreePath = worktreePath,
                Status = status,
                AgentName = agentName,
                DispatchedAt = dispatchedAt,
                CompletedAt = completedAt,
                CreatedAt = createdAt ?? dispatchedAt ?? Now,
                ReadAt = readAt,
                DeliverablePath = deliverablePath,
                DeliverableRef = deliverableRef,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SettleTaskAsync(Guid taskId, AgentTaskStatus status)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.FirstAsync(t => t.Id == taskId);
            task.Status = status;
            task.CompletedAt = Now;
            await db.SaveChangesAsync();
        }

        public async Task SeedWorkflowRunAsync(Guid cardId, CardWorkflowRunStatus status, string stageName)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"home-wf-{Guid.NewGuid():N}"[..24],
                Slug = $"hw-{Guid.NewGuid():N}"[..18],
                WorkingDirectory = TempRoot,
                Details = "Home tasks workflow agent.",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.Agents.Add(agent);

            var run = new CardWorkflowRun
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                AgentId = agent.Id,
                WorkflowName = "Home gate",
                WorkflowDefinitionSnapshot = "name: home",
                Status = status,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.CardWorkflowRuns.Add(run);
            await db.SaveChangesAsync();

            var stage = new CardWorkflowStage
            {
                Id = Guid.NewGuid(),
                CardWorkflowRunId = run.Id,
                StageOrder = 0,
                Name = stageName,
                ExecutorType = "agent",
                Status = CardWorkflowStageStatus.WaitingForHumanReview,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.CardWorkflowStages.Add(stage);
            await db.SaveChangesAsync();

            run.CurrentStageId = stage.Id;
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.ActiveWorkflowRunId = run.Id;
            await db.SaveChangesAsync();
        }

        public async Task SeedWorktreeAsync(Guid cardId, string path)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var worktree = new Worktree
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                RepoPath = RepoPath,
                Path = path,
                Branch = "feat/home",
                BaseRef = "master",
                CreatedAt = Now,
                LastTouchedAt = Now,
            };
            db.Worktrees.Add(worktree);
            await db.SaveChangesAsync();

            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.CurrentWorktreeId = worktree.Id;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            try { Directory.Delete(TempRoot, recursive: true); }
            catch (IOException) { }
        }
    }
}
