using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
public class KanbanPersistenceTests
{
    [Test]
    public async Task AppDbContext_round_trip_persists_board_card_session_worktree_attempt()
    {
        await using var db = CreateContext();
        var graph = CreateKanbanGraph();
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        graph.Card.OwnerSessionId = graph.Session.Id;
        graph.Card.CurrentWorktreeId = graph.Worktree.Id;
        graph.Card.ConcurrencyToken = Guid.NewGuid();
        graph.Card.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await using var verify = CreateContext();
        var card = await verify.Cards
            .Include(c => c.Board).ThenInclude(b => b.Columns)
            .Include(c => c.AgentSessions)
            .Include(c => c.RunAttempts).ThenInclude(a => a.TokenUsage)
            .Include(c => c.Worktrees)
            .Include(c => c.ExternalIssueRef)
            .Include(c => c.RetrySchedule)
            .SingleAsync(c => c.Identifier == graph.Card.Identifier);

        card.Board.Name.ShouldBe("Delivery");
        card.Board.Columns.Select(c => c.StateKey).ShouldBe(["backlog", "in-progress", "review", "done"], ignoreOrder: true);
        card.Status.ShouldBe(CardStatus.InProgress);
        card.AgentSessions.Single().DefinitionName.ShouldBe("raw-pwsh");
        card.OwnerSessionId.ShouldBe(card.AgentSessions.Single().Id);
        card.CurrentWorktreeId.ShouldBe(card.Worktrees.Single().Id);
        card.Worktrees.Single().Branch.ShouldBe($"feat/card-{graph.Card.Identifier}");
        card.RunAttempts.Single().BoardWorkflowDefinitionId.ShouldNotBeNull();
        card.RunAttempts.Single().TokenUsage!.TokensOut.ShouldBe(21);
        card.ExternalIssueRef!.ExternalKey.ShouldStartWith("LIN-");
        card.RetrySchedule!.MaxAttempts.ShouldBe(3);
    }

    [Test]
    public async Task AppDbContext_concurrency_token_allows_one_card_claim_winner()
    {
        await using var seed = CreateContext();
        var graph = CreateKanbanGraph();
        seed.Add(graph.Project);
        await seed.SaveChangesAsync();

        await using var workerA = CreateContext();
        await using var workerB = CreateContext();

        var cardA = await workerA.Cards.SingleAsync(c => c.Identifier == graph.Card.Identifier);
        var cardB = await workerB.Cards.SingleAsync(c => c.Identifier == graph.Card.Identifier);

        var sessionA = NewSession(cardA.Id, null, "session-a");
        var sessionB = NewSession(cardB.Id, null, "session-b");
        cardA.OwnerSessionId = sessionA.Id;
        cardA.ConcurrencyToken = Guid.NewGuid();
        cardA.UpdatedAt = DateTime.UtcNow;
        cardB.OwnerSessionId = sessionB.Id;
        cardB.ConcurrencyToken = Guid.NewGuid();
        cardB.UpdatedAt = DateTime.UtcNow;

        workerA.AgentSessions.Add(sessionA);
        workerB.AgentSessions.Add(sessionB);

        await workerA.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => workerB.SaveChangesAsync());
    }

    [Test]
    public async Task AppDbContext_round_trip_persists_agent_queue_and_card_workflow_run()
    {
        await using var db = CreateContext();
        var graph = CreateKanbanGraph();
        var now = DateTime.UtcNow;

        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Agent Template {Guid.NewGuid():N}",
            Description = "Agent queue test template",
            YamlDefinition = """
                name: One Shot
                description: Implement then review
                stages:
                  - name: Implement
                    executorType: agent
                    gateRequired: false
                  - name: Human Review
                    executorType: human
                    gateRequired: true
                """,
            IsBuiltIn = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Frontend Claude",
            Slug = $"frontend-claude-{Guid.NewGuid():N}",
            WorkingDirectory = "D:/src/app",
            Details = "Handles UI cards",
            DefaultWorkflowTemplateId = template.Id,
            AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now
        };

        var run = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = graph.Card.Id,
            AgentId = agent.Id,
            WorkflowTemplateId = template.Id,
            WorkflowName = "One Shot",
            WorkflowDefinitionSnapshot = template.YamlDefinition,
            Status = CardWorkflowRunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        var stage = new CardWorkflowStage
        {
            Id = Guid.NewGuid(),
            CardWorkflowRunId = run.Id,
            StageOrder = 0,
            Name = "Implement",
            ExecutorType = "agent",
            GateRequired = false,
            Status = CardWorkflowStageStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WorkflowTemplates.Add(template);
        db.Add(graph.Project);
        db.Agents.Add(agent);
        db.CardWorkflowRuns.Add(run);
        db.CardWorkflowStages.Add(stage);
        graph.Card.AssignedAgentId = agent.Id;
        graph.Card.AgentQueuePosition = 1;

        await db.SaveChangesAsync();
        graph.Card.ActiveWorkflowRunId = run.Id;
        await db.SaveChangesAsync();

        await using var verify = CreateContext();
        var stored = await verify.Cards
            .Include(c => c.AssignedAgent)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.Stages)
            .SingleAsync(c => c.Id == graph.Card.Id);

        stored.AssignedAgent!.Name.ShouldBe("Frontend Claude");
        stored.AgentQueuePosition.ShouldBe(1);
        stored.ActiveWorkflowRun!.WorkflowName.ShouldBe("One Shot");
        stored.ActiveWorkflowRun.Stages.Single().Name.ShouldBe("Implement");
    }

    [Test]
    public async Task AppDbContext_rejects_active_workflow_run_from_another_card()
    {
        await using var db = CreateContext();
        var graphA = CreateKanbanGraph();
        var graphB = CreateKanbanGraph();
        var now = DateTime.UtcNow;

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Backend Claude",
            Slug = $"backend-claude-{Guid.NewGuid():N}",
            WorkingDirectory = "D:/src/app",
            Details = "Handles backend cards",
            AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now
        };

        var runForCardB = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = graphB.Card.Id,
            AgentId = agent.Id,
            WorkflowName = "Card B Run",
            WorkflowDefinitionSnapshot = "name: Card B Run",
            Status = CardWorkflowRunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Add(graphA.Project);
        db.Add(graphB.Project);
        db.Agents.Add(agent);
        db.CardWorkflowRuns.Add(runForCardB);
        await db.SaveChangesAsync();

        graphA.Card.ActiveWorkflowRunId = runForCardB.Id;

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task AppDbContext_rejects_current_stage_from_another_workflow_run()
    {
        await using var db = CreateContext();
        var graphA = CreateKanbanGraph();
        var graphB = CreateKanbanGraph();
        var now = DateTime.UtcNow;

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Workflow Claude",
            Slug = $"workflow-claude-{Guid.NewGuid():N}",
            WorkingDirectory = "D:/src/app",
            Details = "Handles workflow cards",
            AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now
        };

        var runA = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = graphA.Card.Id,
            AgentId = agent.Id,
            WorkflowName = "Card A Run",
            WorkflowDefinitionSnapshot = "name: Card A Run",
            Status = CardWorkflowRunStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        };

        var runB = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = graphB.Card.Id,
            AgentId = agent.Id,
            WorkflowName = "Card B Run",
            WorkflowDefinitionSnapshot = "name: Card B Run",
            Status = CardWorkflowRunStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        };

        var stageForRunB = new CardWorkflowStage
        {
            Id = Guid.NewGuid(),
            CardWorkflowRunId = runB.Id,
            StageOrder = 0,
            Name = "Run B Stage",
            ExecutorType = "agent",
            GateRequired = false,
            Status = CardWorkflowStageStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Add(graphA.Project);
        db.Add(graphB.Project);
        db.Agents.Add(agent);
        db.CardWorkflowRuns.AddRange(runA, runB);
        db.CardWorkflowStages.Add(stageForRunB);
        await db.SaveChangesAsync();

        runA.CurrentStageId = stageForRunB.Id;

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Migration_KanbanInitial_creates_expected_tables()
    {
        await using var db = CreateContext();

        var tables = await db.Database.SqlQueryRaw<string>(
                """
                SELECT "table_name"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                """)
            .ToListAsync();

        foreach (var table in new[]
                 {
                     "Boards",
                     "BoardColumns",
                     "Cards",
                     "AgentSessions",
                     "RunAttempts",
                     "Worktrees",
                     "BoardWorkflowDefinitions",
                     "ExternalIssueRefs",
                     "RetrySchedules",
                     "TokenUsages"
                 })
        {
            tables.ShouldContain(table);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static KanbanGraph CreateKanbanGraph()
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.com/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Delivery",
            Description = "Kanban board",
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 2,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = NewColumn(board, "backlog", "Backlog", 0, CardStatus.Backlog, isActive: false, isTerminal: false);
        var inProgress = NewColumn(board, "in-progress", "In Progress", 1, CardStatus.InProgress, isActive: true, isTerminal: false);
        var review = NewColumn(board, "review", "Review", 2, CardStatus.Review, isActive: false, isTerminal: false);
        var done = NewColumn(board, "done", "Done", 3, CardStatus.Done, isActive: false, isTerminal: true);
        board.Columns.Add(backlog);
        board.Columns.Add(inProgress);
        board.Columns.Add(review);
        board.Columns.Add(done);

        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Default",
            Content = "---\nname: default\n---\nDo the work.",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);

        var cardIdentifier = $"CARD-{Guid.NewGuid():N}";
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = inProgress.Id,
            Identifier = cardIdentifier,
            Title = "Build persistence",
            Description = "Persist the kanban graph",
            Priority = 1,
            LabelsJson = """["backend","bdd"]""",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
            StartedAt = now,
            Board = board,
            BoardColumn = inProgress
        };
        board.Cards.Add(card);
        inProgress.Cards.Add(card);

        var worktree = new Worktree
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            RepoPath = "D:/repo",
            Path = $"D:/worktrees/{Guid.NewGuid():N}",
            Branch = $"feat/card-{cardIdentifier}",
            BaseRef = "main",
            Status = WorktreeStatus.Active,
            CreatedAt = now,
            LastTouchedAt = now,
            Card = card
        };
        card.Worktrees.Add(worktree);

        var session = NewSession(card.Id, worktree.Id, "raw-pwsh");
        session.Card = card;
        session.Worktree = worktree;
        card.AgentSessions.Add(session);
        worktree.AgentSessions.Add(session);

        var attempt = new RunAttempt
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AgentSessionId = session.Id,
            WorktreeId = worktree.Id,
            BoardWorkflowDefinitionId = definition.Id,
            AttemptNumber = 1,
            Phase = RunPhase.StreamingTurn,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            PhaseDurationsJson = "{}",
            Prompt = "Implement E04",
            Card = card,
            AgentSession = session,
            Worktree = worktree,
            BoardWorkflowDefinition = definition
        };
        card.RunAttempts.Add(attempt);
        session.RunAttempts.Add(attempt);
        worktree.RunAttempts.Add(attempt);

        attempt.TokenUsage = new TokenUsage
        {
            Id = Guid.NewGuid(),
            RunAttemptId = attempt.Id,
            TokensIn = 13,
            TokensOut = 21,
            CostUsd = 0.012345m,
            ModelName = "raw",
            CreatedAt = now,
            RunAttempt = attempt
        };

        var externalKey = $"LIN-{Guid.NewGuid():N}";
        card.ExternalIssueRef = new ExternalIssueRef
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            TrackerKind = TrackerKind.Linear,
            ExternalId = externalKey,
            ExternalKey = externalKey,
            Url = $"https://linear.example/{externalKey}",
            RawPayloadJson = "{}",
            LastSyncedAt = now,
            Card = card
        };

        card.RetrySchedule = new RetrySchedule
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AttemptCount = 1,
            MaxAttempts = 3,
            LastAttemptAt = now,
            LastError = "transient",
            Card = card
        };

        return new KanbanGraph(project, card, session, worktree);
    }

    private static BoardColumn NewColumn(
        Board board,
        string stateKey,
        string name,
        int order,
        CardStatus status,
        bool isActive,
        bool isTerminal)
    {
        var now = DateTime.UtcNow;
        return new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = stateKey,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = isActive,
            IsTerminal = isTerminal,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
    }

    private static AgentSession NewSession(Guid cardId, Guid? worktreeId, string definitionName)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            WorktreeId = worktreeId,
            DefinitionName = definitionName,
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = $"D:/worktrees/card-{cardId:N}",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now
        };
    }

    private sealed record KanbanGraph(Project Project, Card Card, AgentSession Session, Worktree Worktree);
}
