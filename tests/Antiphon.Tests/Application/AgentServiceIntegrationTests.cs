using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentServiceIntegrationTests
{
    [Test]
    public async Task CreateAsync_persists_agent_with_default_auto_pick_policy()
    {
        await using var db = CreateContext();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);

        var created = await service.CreateAsync(
            new CreateAgentRequest("Frontend Claude", "D:/src/app"),
            CancellationToken.None);

        created.Name.ShouldBe("Frontend Claude");
        created.Slug.ShouldBe("frontend-claude");
        created.WorkingDirectory.ShouldBe("D:/src/app");
        created.AssignmentPolicy.ShouldBe(AgentAssignmentPolicy.AutoPick);
        created.Status.ShouldBe(AgentStatus.Idle);
        created.Queue.ShouldBeEmpty();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Name.ShouldBe("Frontend Claude");
        stored.AssignmentPolicy.ShouldBe(AgentAssignmentPolicy.AutoPick);
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentChanged").ShouldBeTrue();
    }

    [Test]
    public async Task AssignCardAsync_assigns_card_to_next_queue_position_and_snapshots_default_workflow()
    {
        await using var db = CreateContext();
        var graph = CreateGraph();
        db.Add(graph.Template);
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var agent = await service.CreateAsync(
            new CreateAgentRequest("Frontend Claude", "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);

        var detail = await service.AssignCardAsync(
            agent.Id,
            new AssignAgentCardRequest(graph.CardA.Id),
            CancellationToken.None);

        detail.Queue.Single().CardId.ShouldBe(graph.CardA.Id);
        detail.Queue.Single().QueuePosition.ShouldBe(1);
        detail.Queue.Single().WorkflowStatus.ShouldBe(CardWorkflowRunStatus.Queued);
        detail.Queue.Single().CurrentStageName.ShouldBe("Implement");

        await using var verify = CreateContext();
        var storedCard = await verify.Cards
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.Stages)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .SingleAsync(c => c.Id == graph.CardA.Id);
        storedCard.AssignedAgentId.ShouldBe(agent.Id);
        storedCard.AgentQueuePosition.ShouldBe(1);
        storedCard.ActiveWorkflowRunId.ShouldNotBeNull();
        storedCard.ActiveWorkflowRun!.CurrentStageId.ShouldNotBeNull();
        storedCard.ActiveWorkflowRun.CurrentStage!.Name.ShouldBe("Implement");
        storedCard.ActiveWorkflowRun.WorkflowDefinitionSnapshot.ShouldContain("name: One Shot");
        storedCard.ActiveWorkflowRun.Stages
            .OrderBy(s => s.StageOrder)
            .Select(s => s.Name)
            .ShouldBe(["Implement", "Human Review"]);
        WorkflowDefinitionParser
            .ParseYamlDefinition(storedCard.ActiveWorkflowRun.WorkflowDefinitionSnapshot)
            .Stages
            .Select(s => s.Name)
            .ShouldBe(["Implement", "Human Review"]);
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentQueueChanged").ShouldBeTrue();
        eventBus.PublishedEvents.Any(e => e.EventName == "CardChanged").ShouldBeTrue();
    }

    [Test]
    public async Task AssignCardAsync_concurrent_assignments_create_unique_queue_positions()
    {
        await using (var seed = CreateContext())
        {
            var graph = CreateGraph();
            seed.Add(graph.Template);
            seed.Add(graph.Project);
            await seed.SaveChangesAsync();
            var agent = await CreateService(seed, new MockEventBus()).CreateAsync(
                new CreateAgentRequest("Concurrent Claude", "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
                CancellationToken.None);

            await using var workerA = CreateContext();
            await using var workerB = CreateContext();
            var assignA = CreateService(workerA, new MockEventBus()).AssignCardAsync(
                agent.Id,
                new AssignAgentCardRequest(graph.CardA.Id),
                CancellationToken.None);
            var assignB = CreateService(workerB, new MockEventBus()).AssignCardAsync(
                agent.Id,
                new AssignAgentCardRequest(graph.CardB.Id),
                CancellationToken.None);

            await Task.WhenAll(assignA, assignB);

            await using var verify = CreateContext();
            var positions = await verify.Cards
                .Where(c => c.AssignedAgentId == agent.Id)
                .OrderBy(c => c.AgentQueuePosition)
                .Select(c => c.AgentQueuePosition)
                .ToListAsync();
            positions.ShouldBe([1, 2]);
        }
    }

    [Test]
    public async Task ReorderQueueAsync_rewrites_positions_without_cross_agent_cards()
    {
        await using var db = CreateContext();
        var graph = CreateGraph(includeThirdCard: true);
        db.Add(graph.Template);
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var agentA = await service.CreateAsync(
            new CreateAgentRequest("Frontend Claude", "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);
        var agentB = await service.CreateAsync(
            new CreateAgentRequest("Backend Claude", "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);
        await service.AssignCardAsync(agentA.Id, new AssignAgentCardRequest(graph.CardA.Id), CancellationToken.None);
        await service.AssignCardAsync(agentA.Id, new AssignAgentCardRequest(graph.CardB.Id), CancellationToken.None);
        await service.AssignCardAsync(agentB.Id, new AssignAgentCardRequest(graph.CardC!.Id), CancellationToken.None);
        eventBus.Clear();

        var detail = await service.ReorderQueueAsync(
            agentA.Id,
            new ReorderAgentQueueRequest([graph.CardB.Id, graph.CardC.Id, graph.CardA.Id]),
            CancellationToken.None);

        detail.Queue.Select(c => c.CardId).ShouldBe([graph.CardB.Id, graph.CardA.Id]);
        detail.Queue.Select(c => c.QueuePosition).ShouldBe([1, 2]);
        await using var verify = CreateContext();
        var otherAgentCard = await verify.Cards.SingleAsync(c => c.Id == graph.CardC.Id);
        otherAgentCard.AssignedAgentId.ShouldBe(agentB.Id);
        otherAgentCard.AgentQueuePosition.ShouldBe(1);
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentQueueChanged").ShouldBeTrue();
        eventBus.PublishedEvents.Any(e => e.EventName == "CardChanged").ShouldBeTrue();
    }

    [Test]
    public async Task RemoveCardAsync_clears_assignment_and_active_workflow_run()
    {
        await using var db = CreateContext();
        var graph = CreateGraph();
        db.Add(graph.Template);
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var agent = await service.CreateAsync(
            new CreateAgentRequest("Frontend Claude", "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);
        await service.AssignCardAsync(agent.Id, new AssignAgentCardRequest(graph.CardA.Id), CancellationToken.None);
        await service.AssignCardAsync(agent.Id, new AssignAgentCardRequest(graph.CardB.Id), CancellationToken.None);
        eventBus.Clear();

        await service.RemoveCardAsync(agent.Id, graph.CardA.Id, CancellationToken.None);

        await using var verify = CreateContext();
        var storedCard = await verify.Cards.SingleAsync(c => c.Id == graph.CardA.Id);
        var shiftedCard = await verify.Cards.SingleAsync(c => c.Id == graph.CardB.Id);
        storedCard.AssignedAgentId.ShouldBeNull();
        storedCard.AgentQueuePosition.ShouldBeNull();
        storedCard.ActiveWorkflowRunId.ShouldBeNull();
        shiftedCard.AgentQueuePosition.ShouldBe(1);
        (await verify.CardWorkflowRuns.CountAsync(r => r.CardId == graph.CardA.Id)).ShouldBe(1);
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentQueueChanged").ShouldBeTrue();
        eventBus.PublishedEvents
            .Where(e => e.EventName == "CardChanged")
            .ShouldContain(e => HasPayloadValue(e.Payload, "cardId", graph.CardA.Id));
        eventBus.PublishedEvents
            .Where(e => e.EventName == "CardChanged")
            .ShouldContain(e => HasPayloadValue(e.Payload, "cardId", graph.CardB.Id));
    }

    [Test]
    public async Task CreateAsync_rejects_blank_name_and_working_directory()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(new CreateAgentRequest(" ", " "), CancellationToken.None));

        ex.Errors["Name"].Single().ShouldBe("Agent name is required.");
        ex.Errors["WorkingDirectory"].Single().ShouldBe("Working directory is required.");
    }

    [Test]
    public async Task ReorderQueueAsync_rejects_null_card_ids()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.ReorderQueueAsync(Guid.NewGuid(), new ReorderAgentQueueRequest(null!), CancellationToken.None));

        ex.Errors["CardIds"].Single().ShouldBe("Card ids are required.");
    }

    private static AgentService CreateService(AppDbContext db, IEventBus eventBus)
    {
        return new AgentService(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            eventBus,
            TimeProvider.System);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static TestGraph CreateGraph(bool includeThirdCard = false)
    {
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

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Agent Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.com/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Agent Board",
            Description = "Queue work",
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 2,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            IsActive = false,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(backlog);

        var cardA = NewCard(board, backlog, "CARD-0001", "Build queue UI", now);
        var cardB = NewCard(board, backlog, "CARD-0002", "Wire queue API", now);
        board.Cards.Add(cardA);
        board.Cards.Add(cardB);

        Card? cardC = null;
        if (includeThirdCard)
        {
            cardC = NewCard(board, backlog, "CARD-0003", "Keep backend isolated", now);
            board.Cards.Add(cardC);
        }

        return new TestGraph(template, project, cardA, cardB, cardC);
    }

    private static Card NewCard(Board board, BoardColumn column, string identifier, string title, DateTime now)
    {
        return new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = title,
            Description = title,
            Priority = 1,
            LabelsJson = "[]",
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
    }

    private static bool HasPayloadValue<T>(object payload, string propertyName, T expected)
    {
        var value = payload.GetType().GetProperty(propertyName)?.GetValue(payload);
        return value is T typed && EqualityComparer<T>.Default.Equals(typed, expected);
    }

    private sealed record TestGraph(
        WorkflowTemplate Template,
        Project Project,
        Card CardA,
        Card CardB,
        Card? CardC);
}
