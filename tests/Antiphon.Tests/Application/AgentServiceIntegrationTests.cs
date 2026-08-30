using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.FileSystem;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var agentName = $"Frontend Claude {suffix}";

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/app"),
            CancellationToken.None);

        created.Name.ShouldBe(agentName);
        created.Slug.ShouldBe($"frontend-claude-{suffix}");
        created.WorkingDirectory.ShouldBe("D:/src/app");
        created.AssignmentPolicy.ShouldBe(AgentAssignmentPolicy.AutoPick);
        created.Status.ShouldBe(AgentStatus.Idle);
        created.Queue.ShouldBeEmpty();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Name.ShouldBe(agentName);
        stored.AssignmentPolicy.ShouldBe(AgentAssignmentPolicy.AutoPick);
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentChanged").ShouldBeTrue();
    }

    [Test]
    public async Task CreateAsync_applies_bundle_keys_and_system_prompt_append()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                UniqueAgentName("Bundled At Birth"),
                "D:/src/bundled",
                BundleKeys: [InstructionBundles.Orchestrator, InstructionBundles.BoardApi],
                SystemPromptAppend: "You watch the board."),
            CancellationToken.None);

        created.SystemPromptAppend.ShouldBe("You watch the board.");
        created.AttachedBundleKeys.ShouldBe([InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
    }

    [Test]
    public async Task CreateAsync_unknown_bundle_key_is_422()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateAgentRequest(
                    UniqueAgentName("Bad Bundle"),
                    "D:/src/bad-bundle",
                    BundleKeys: ["not-a-bundle"]),
                CancellationToken.None));
        ex.Errors.Values.SelectMany(v => v).ShouldContain(m => m.Contains("not-a-bundle"));
    }

    [Test]
    public async Task CreateAsync_on_an_unknown_directory_creates_a_project_and_a_board_named_after_it()
    {
        await using var db = CreateContext();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var workingDirectory = $"D:/src/{Guid.NewGuid():N}";
        var agentName = UniqueAgentName("Board Owner");

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, workingDirectory),
            CancellationToken.None);

        created.BoardId.ShouldNotBeNull();
        created.BoardName.ShouldBe(Path.GetFileName(workingDirectory));

        await using var verify = CreateContext();
        var board = await verify.Boards
            .Include(b => b.Columns)
            .Include(b => b.Project)
            .SingleAsync(b => b.Id == created.BoardId!.Value);
        board.Name.ShouldBe(Path.GetFileName(workingDirectory));
        board.Project.LocalRepositoryPath.ShouldBe(workingDirectory);
        board.Columns
            .Select(c => c.StateKey)
            .OrderBy(s => s)
            .ShouldBe(["backlog", "done", "in-progress", "needs-decision", "review"]);
        eventBus.PublishedEvents.Any(e => e.EventName == "BoardChanged").ShouldBeTrue();
    }

    [Test]
    public async Task CreateAsync_reuses_the_projects_only_board_for_shared_working_directory()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var workingDirectory = $"D:/src/{Guid.NewGuid():N}";

        var first = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("First Owner"), workingDirectory),
            CancellationToken.None);
        var second = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Second Owner"), workingDirectory),
            CancellationToken.None);

        await using var verify = CreateContext();
        var firstBoard = await verify.Boards.SingleAsync(b => b.Id == first.BoardId!.Value);
        second.BoardId.ShouldBe(firstBoard.Id);
        (await verify.Boards.CountAsync(b => b.ProjectId == firstBoard.ProjectId)).ShouldBe(1);
    }

    [Test]
    public async Task CreateAsync_with_boardId_links_and_creates_nothing()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Explicit board project {Guid.NewGuid():N}",
            LocalRepositoryPath = $"D:/src/{Guid.NewGuid():N}",
            GitRepositoryUrl = string.Empty,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            Name = "Chosen board",
            Description = string.Empty,
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.Boards.Add(board);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var created = await CreateService(db, new MockEventBus()).CreateAsync(
            new CreateAgentRequest(
                UniqueAgentName("Explicit board agent"),
                $"D:/src/worktree-{Guid.NewGuid():N}",
                BoardId: board.Id),
            CancellationToken.None);

        created.BoardId.ShouldBe(board.Id);
        await using var verify = CreateContext();
        (await verify.Projects.CountAsync(p => p.Id == project.Id)).ShouldBe(1);
        (await verify.Boards.CountAsync(b => b.ProjectId == project.Id)).ShouldBe(1);
    }

    [Test]
    public async Task CreateAsync_without_boardId_links_to_the_projects_only_board()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var directory = $"D:/src/{Guid.NewGuid():N}";
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Inherited board project {Guid.NewGuid():N}",
            LocalRepositoryPath = directory,
            GitRepositoryUrl = string.Empty,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            Name = "Only board",
            Description = string.Empty,
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.Boards.Add(board);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var created = await CreateService(db, new MockEventBus()).CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Inherited board agent"), directory),
            CancellationToken.None);

        created.BoardId.ShouldBe(board.Id);
        await using var verify = CreateContext();
        (await verify.Boards.CountAsync(b => b.ProjectId == project.Id)).ShouldBe(1);
    }

    [Test]
    public async Task CreateAsync_without_boardId_refuses_when_the_project_has_several_boards()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var directory = $"D:/src/{Guid.NewGuid():N}";
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Ambiguous board project {Guid.NewGuid():N}",
            LocalRepositoryPath = directory,
            GitRepositoryUrl = string.Empty,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            project.Boards.Add(new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = name,
                Description = string.Empty,
                TrackerKind = TrackerKind.Internal,
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            CreateService(db, new MockEventBus()).CreateAsync(
                new CreateAgentRequest(UniqueAgentName("Ambiguous board agent"), directory),
                CancellationToken.None));

        ex.Errors[nameof(CreateAgentRequest.BoardId)].Single().ShouldContain("Alpha, Beta");
    }

    [Test]
    public async Task CreateAsync_matches_an_existing_project_path_regardless_of_separator_and_case()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var leaf = Guid.NewGuid().ToString("N");
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Normalised path project {leaf}",
            LocalRepositoryPath = $"D:/SRC/{leaf}",
            GitRepositoryUrl = string.Empty,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
            Name = "Normalised board",
            Description = string.Empty,
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        project.Boards.Add(board);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var created = await CreateService(db, new MockEventBus()).CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Normalised path agent"), $"d:\\src\\{leaf.ToUpperInvariant()}\\"),
            CancellationToken.None);

        created.BoardId.ShouldBe(board.Id);
        await using var verify = CreateContext();
        (await verify.Projects.CountAsync(p => p.Id == project.Id)).ShouldBe(1);
    }

    [Test]
    public async Task UpdateAsync_changes_default_board()
    {
        await using var db = CreateContext();
        var graph = CreateGraph();
        db.Add(graph.Template);
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        var service = CreateService(db, new MockEventBus());
        var agent = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Editable Claude"), "D:/src/app"),
            CancellationToken.None);
        var targetBoardId = graph.CardA.BoardId;
        agent.BoardId.ShouldNotBe(targetBoardId);

        var updated = await service.UpdateAsync(
            agent.Id,
            new UpdateAgentRequest(
                agent.Name,
                agent.WorkingDirectory,
                "edited details",
                agent.DefaultWorkflowTemplateId,
                AgentAssignmentPolicy.Paused,
                targetBoardId),
            CancellationToken.None);

        updated.BoardId.ShouldBe(targetBoardId);
        updated.BoardName.ShouldBe("Agent Board");
        updated.Details.ShouldBe("edited details");
        updated.AssignmentPolicy.ShouldBe(AgentAssignmentPolicy.Paused);

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).BoardId.ShouldBe(targetBoardId);
    }

    [Test]
    public async Task UpdateAsync_with_null_board_keeps_the_agents_board()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var agent = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Sticky Board Claude"), "D:/src/app"),
            CancellationToken.None);
        agent.BoardId.ShouldNotBeNull();

        // An update that omits the board (settings save from an older client, partial PATCH)
        // must leave the default board in place — clearing it orphaned Add-Work routing.
        var updated = await service.UpdateAsync(
            agent.Id,
            new UpdateAgentRequest(
                agent.Name,
                agent.WorkingDirectory,
                "edited",
                agent.DefaultWorkflowTemplateId,
                agent.AssignmentPolicy,
                BoardId: null),
            CancellationToken.None);

        updated.BoardId.ShouldBe(agent.BoardId);

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).BoardId.ShouldBe(agent.BoardId);
    }

    [Test]
    public async Task EnsureAgentBoardsAsync_relinks_the_agents_original_orphaned_board()
    {
        await using var db = CreateContext();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var workingDirectory = $"D:/src/{Guid.NewGuid():N}";
        var agentName = UniqueAgentName("Boardless Claude");
        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, workingDirectory),
            CancellationToken.None);

        // Simulate the old update path clearing the link; the original board still exists.
        await using (var setup = CreateContext())
        {
            var row = await setup.Agents.SingleAsync(a => a.Id == created.Id);
            // Legacy per-agent boards were named after their agent. New-agent boards use the
            // directory leaf, but the backfill must still recover the legacy shape.
            (await setup.Boards.SingleAsync(b => b.Id == created.BoardId!.Value)).Name = agentName;
            row.BoardId = null;
            await setup.SaveChangesAsync();
        }

        await using var backfillDb = CreateContext();
        var backfilled = await CreateService(backfillDb, eventBus).EnsureAgentBoardsAsync(CancellationToken.None);

        backfilled.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        // ADOPTED, not duplicated: the same-named unclaimed board in the project is re-linked.
        stored.BoardId.ShouldBe(created.BoardId);
    }

    [Test]
    public async Task EnsureAgentBoardsAsync_creates_a_board_when_no_orphaned_original_exists()
    {
        await using var db = CreateContext();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var workingDirectory = $"D:/src/{Guid.NewGuid():N}";
        var agentName = UniqueAgentName("Fresh Board Claude");
        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, workingDirectory),
            CancellationToken.None);

        // Sever the link AND rename the original board so no adoption candidate matches.
        await using (var setup = CreateContext())
        {
            var row = await setup.Agents.SingleAsync(a => a.Id == created.Id);
            row.BoardId = null;
            var oldBoard = await setup.Boards.SingleAsync(b => b.Id == created.BoardId!.Value);
            oldBoard.Name = "Repurposed Board";
            await setup.SaveChangesAsync();
        }

        await using var backfillDb = CreateContext();
        await CreateService(backfillDb, eventBus).EnsureAgentBoardsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.Agents.Include(a => a.Board).SingleAsync(a => a.Id == created.Id);
        stored.BoardId.ShouldNotBeNull();
        stored.BoardId.ShouldNotBe(created.BoardId);
        stored.Board!.Name.ShouldBe(agentName);
        stored.Board.ProjectId.ShouldNotBe(Guid.Empty);

        // Idempotent: a second run finds nothing to do for this agent.
        await using var secondDb = CreateContext();
        (await secondDb.Agents.AnyAsync(a => a.Id == created.Id && a.BoardId == null)).ShouldBeFalse();
    }

    [Test]
    public async Task EnsureAgentBoardsAsync_leaves_pool_delegates_boardless()
    {
        await using var db = CreateContext();
        var workingDirectory = $"D:/src/{Guid.NewGuid():N}/worktrees/card-task-deadbeef";
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = UniqueAgentName("task-deadbeef"),
            Slug = $"task-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = workingDirectory,
            Details = "Pool delegate under test.",
            Status = AgentStatus.Idle,
            IsPoolDelegate = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        await CreateService(db, new MockEventBus()).EnsureAgentBoardsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).BoardId.ShouldBeNull();
        (await verify.Projects.AnyAsync(p => p.LocalRepositoryPath == workingDirectory)).ShouldBeFalse();
        (await verify.Boards.AnyAsync(b => b.Name == agent.Name)).ShouldBeFalse();
    }

    [Test]
    public async Task UpdateAsync_rejects_unknown_board()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var agent = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Strict Claude"), "D:/src/app"),
            CancellationToken.None);

        await Should.ThrowAsync<NotFoundException>(() =>
            service.UpdateAsync(
                agent.Id,
                new UpdateAgentRequest(
                    agent.Name,
                    agent.WorkingDirectory,
                    null,
                    null,
                    agent.AssignmentPolicy,
                    Guid.NewGuid()),
                CancellationToken.None));
    }

    [Test]
    public async Task DeleteAsync_removes_agent_unassigns_cards_and_drops_runs()
    {
        await using var db = CreateContext();
        var graph = CreateGraph();
        db.Add(graph.Template);
        db.Add(graph.Project);
        await db.SaveChangesAsync();
        var eventBus = new MockEventBus();
        var service = CreateService(db, eventBus);
        var agent = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Doomed Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);
        await service.AssignCardAsync(agent.Id, new AssignAgentCardRequest(graph.CardA.Id), CancellationToken.None);
        eventBus.Clear();

        await service.DeleteAsync(agent.Id, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agent.Id)).ShouldBeFalse();
        var card = await verify.Cards.SingleAsync(c => c.Id == graph.CardA.Id);
        card.AssignedAgentId.ShouldBeNull();
        card.AgentQueuePosition.ShouldBeNull();
        card.ActiveWorkflowRunId.ShouldBeNull();
        (await verify.CardWorkflowRuns.AnyAsync(r => r.AgentId == agent.Id)).ShouldBeFalse();
        eventBus.PublishedEvents.Any(e => e.EventName == "AgentChanged").ShouldBeTrue();
        eventBus.PublishedEvents
            .Where(e => e.EventName == "CardChanged")
            .ShouldContain(e => HasPayloadValue(e.Payload, "cardId", graph.CardA.Id));
    }

    [Test]
    public async Task DeleteAsync_rejects_unknown_agent()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());

        await Should.ThrowAsync<NotFoundException>(() =>
            service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
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
            new CreateAgentRequest(UniqueAgentName("Frontend Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
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
                new CreateAgentRequest(UniqueAgentName("Concurrent Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
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
            new CreateAgentRequest(UniqueAgentName("Frontend Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
            CancellationToken.None);
        var agentB = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Backend Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
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
            new CreateAgentRequest(UniqueAgentName("Frontend Claude"), "D:/src/app", DefaultWorkflowTemplateId: graph.Template.Id),
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

    [Test]
    public async Task CreateAsync_with_CreateWorkingDirectory_true_creates_missing_directory()
    {
        await using var db = CreateContext();
        var mockFs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var writer = new FileSystemDirectoryWriter(mockFs);
        var service = CreateService(db, new MockEventBus(), writer);
        var agentName = UniqueAgentName("Mkdir Claude");

        mockFs.Directory.Exists("D:/src/newdir").ShouldBeFalse();

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/newdir", CreateWorkingDirectory: true),
            CancellationToken.None);

        mockFs.Directory.Exists("D:/src/newdir").ShouldBeTrue();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.WorkingDirectory.ShouldBe("D:/src/newdir");
    }

    [Test]
    public async Task EnsureWorkingDirectoryAsync_unknown_agent_is_not_found()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());

        await Should.ThrowAsync<NotFoundException>(() =>
            service.EnsureWorkingDirectoryAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task EnsureWorkingDirectoryAsync_creates_missing_directory_and_is_idempotent()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-ensure-dir-{Guid.NewGuid():N}");
        try
        {
            Directory.Exists(missing).ShouldBeFalse();
            await using var db = CreateContext();
            var writer = new FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem());
            var service = CreateService(db, new MockEventBus(), writer);
            var created = await service.CreateAsync(
                new CreateAgentRequest(UniqueAgentName("Ensure Dir"), missing),
                CancellationToken.None);

            var first = await service.EnsureWorkingDirectoryAsync(created.Id, CancellationToken.None);
            first.WorkingDirectory.ShouldBe(missing);
            Directory.Exists(missing).ShouldBeTrue();

            var second = await service.EnsureWorkingDirectoryAsync(created.Id, CancellationToken.None);
            second.WorkingDirectory.ShouldBe(missing);
            Directory.Exists(missing).ShouldBeTrue();
        }
        finally
        {
            try
            {
                if (Directory.Exists(missing))
                    Directory.Delete(missing, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Test]
    public async Task CreateAsync_with_flag_false_does_not_create_directory()
    {
        await using var db = CreateContext();
        var mockFs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var writer = new FileSystemDirectoryWriter(mockFs);
        var service = CreateService(db, new MockEventBus(), writer);
        var agentName = UniqueAgentName("NoMkdir Claude");

        var created = await service.CreateAsync(
            new CreateAgentRequest(agentName, "D:/src/skipdir", CreateWorkingDirectory: false),
            CancellationToken.None);

        mockFs.Directory.Exists("D:/src/skipdir").ShouldBeFalse();

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.WorkingDirectory.ShouldBe("D:/src/skipdir");
    }

    // ── Feature 004: Working on the agent DTOs means mid-turn RIGHT NOW ──────────────────────
    // AgentService projects the transcript-derived working signal
    // (SessionMessageQueueService.IsWorkingAsync) onto the agent list/detail, gated on a RUNNING
    // live session. The rule's exclusion matrix (interrupt markers, local slash-commands, compact
    // boundaries) is pinned at the queue tier by SessionMessageQueueServiceTests — these tests pin
    // the agent-tier PROJECTION only, plus one shared-rule canary (interrupt marker).

    // The headline regression from the 004 investigation: every started agent read "Working"
    // forever because the card rendered the lifecycle latch. The two fields answer different
    // questions and must be able to disagree.
    [Test]
    public async Task Idle_live_session_reports_working_false_while_lifecycle_status_stays_started()
    {
        var (agentId, sessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        try
        {
            await InsertTranscriptAsync(sessionId, TranscriptKinds.AssistantText, "did the thing");
            await InsertTranscriptAsync(sessionId, TranscriptKinds.TurnEnd, null);

            await using var db = CreateContext();
            var summary = (await CreateService(db, new MockEventBus()).GetAllAsync(CancellationToken.None))
                .Single(a => a.Id == agentId);

            summary.Working.ShouldBeFalse("the last turn ended — the agent is idle at the prompt");
            summary.Status.ShouldBe(AgentStatus.Running, "the lifecycle latch still says 'started'");
            summary.LiveSession.ShouldNotBeNull();
        }
        finally
        {
            await CleanupSessionsAsync(sessionId);
        }
    }

    [Test]
    public async Task Mid_turn_live_session_reports_working_true()
    {
        var (agentId, sessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        try
        {
            // Activity with no TurnEnd after it = mid-turn.
            await InsertTranscriptAsync(sessionId, TranscriptKinds.AssistantText, "working on it");

            await using var db = CreateContext();
            var summary = (await CreateService(db, new MockEventBus()).GetAllAsync(CancellationToken.None))
                .Single(a => a.Id == agentId);

            summary.Working.ShouldBeTrue();
        }
        finally
        {
            await CleanupSessionsAsync(sessionId);
        }
    }

    // Pins the gate in AgentService.IsSessionWorkingAsync: a session that is not RUNNING must
    // never report working, even when its (stale) transcript looks mid-turn — a dead session's
    // last recorded activity is history, not work.
    [Test]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopped)]
    public async Task Non_running_live_session_never_reports_working(SessionStatus sessionStatus)
    {
        var (agentId, sessionId) = await SeedStartedAgentAsync(sessionStatus);
        try
        {
            await InsertTranscriptAsync(sessionId, TranscriptKinds.AssistantText, "mid-turn when it died");

            await using var db = CreateContext();
            var summary = (await CreateService(db, new MockEventBus()).GetAllAsync(CancellationToken.None))
                .Single(a => a.Id == agentId);

            summary.Working.ShouldBeFalse($"a {sessionStatus} session must not read as working");
        }
        finally
        {
            await CleanupSessionsAsync(sessionId);
        }
    }

    [Test]
    public async Task Agent_without_a_live_session_reports_working_false()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Sessionless Claude"), "D:/src/app"),
            CancellationToken.None);

        var summary = (await service.GetAllAsync(CancellationToken.None)).Single(a => a.Id == created.Id);
        summary.Working.ShouldBeFalse();
        summary.LiveSession.ShouldBeNull();

        var detail = await service.GetByIdAsync(created.Id, CancellationToken.None);
        detail.Working.ShouldBeFalse();
    }

    // Detail must ride the same projection as the list, and the agent tier must ride the SAME
    // hardened rule as the queue tier — one canary case (interrupt marker = turn end) proves the
    // shared code path; the full exclusion matrix stays pinned in SessionMessageQueueServiceTests.
    [Test]
    public async Task Detail_working_matches_summary_and_an_interrupt_marker_reads_idle()
    {
        var (agentId, sessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        try
        {
            await InsertTranscriptAsync(sessionId, TranscriptKinds.AssistantText, "working on it");

            await using (var db = CreateContext())
            {
                var service = CreateService(db, new MockEventBus());
                (await service.GetByIdAsync(agentId, CancellationToken.None)).Working
                    .ShouldBeTrue("detail must agree with the mid-turn summary");
            }

            // Esc / rejected tool call: a USER marker entry, NO TurnEnd — the marker IS the end.
            await InsertTranscriptAsync(
                sessionId, TranscriptKinds.UserPrompt, "[Request interrupted by user for tool use]");

            await using (var db = CreateContext())
            {
                var service = CreateService(db, new MockEventBus());
                (await service.GetAllAsync(CancellationToken.None)).Single(a => a.Id == agentId)
                    .Working.ShouldBeFalse("the interrupt marker ends the turn at the agent tier too");
                (await service.GetByIdAsync(agentId, CancellationToken.None)).Working.ShouldBeFalse();
            }
        }
        finally
        {
            await CleanupSessionsAsync(sessionId);
        }
    }

    // Catches any future batched-query grouping bug: working must be computed per agent, not
    // smeared across the list.
    [Test]
    public async Task List_projection_computes_working_per_agent()
    {
        var (busyAgentId, busySessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        var (idleAgentId, idleSessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        try
        {
            await InsertTranscriptAsync(busySessionId, TranscriptKinds.AssistantText, "working on it");
            await InsertTranscriptAsync(idleSessionId, TranscriptKinds.AssistantText, "done");
            await InsertTranscriptAsync(idleSessionId, TranscriptKinds.TurnEnd, null);

            await using var db = CreateContext();
            var all = await CreateService(db, new MockEventBus()).GetAllAsync(CancellationToken.None);

            all.Single(a => a.Id == busyAgentId).Working.ShouldBeTrue();
            all.Single(a => a.Id == idleAgentId).Working.ShouldBeFalse();
        }
        finally
        {
            await CleanupSessionsAsync(busySessionId, idleSessionId);
        }
    }

    [Test]
    public async Task CreateAsync_with_a_Codex_profile_stores_Kind_Codex_and_a_Grok_profile_stores_Kind_Grok()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");

        var createdCodex = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Codex Kind"), "D:/src/codex-kind", TuiProfileId: codex.Id),
            CancellationToken.None);
        var createdGrok = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Grok Kind"), "D:/src/grok-kind", TuiProfileId: grok.Id),
            CancellationToken.None);

        await using var verify = CreateContext();
        var storedCodex = await verify.Agents.SingleAsync(a => a.Id == createdCodex.Id);
        storedCodex.Kind.ShouldBe(AgentKind.Codex);
        storedCodex.TuiProfileId.ShouldBe(codex.Id);
        var storedGrok = await verify.Agents.SingleAsync(a => a.Id == createdGrok.Id);
        storedGrok.Kind.ShouldBe(AgentKind.Grok);
        storedGrok.TuiProfileId.ShouldBe(grok.Id);
    }

    [Test]
    public async Task UpdateAsync_moving_between_claude_and_codex_profiles_moves_Kind_with_the_profile()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var claude = await SeedProfileAsync(db, AgentKind.ClaudeCode, "Claude");
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Profile Mover"), "D:/src/profile-mover", TuiProfileId: claude.Id),
            CancellationToken.None);

        await using (var verify = CreateContext())
            (await verify.Agents.SingleAsync(a => a.Id == created.Id)).Kind.ShouldBe(AgentKind.ClaudeCode);

        var toCodex = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                TuiProfileId: codex.Id),
            CancellationToken.None);

        await using (var verify = CreateContext())
        {
            var stored = await verify.Agents.SingleAsync(a => a.Id == toCodex.Id);
            stored.Kind.ShouldBe(AgentKind.Codex);
            stored.TuiProfileId.ShouldBe(codex.Id);
        }

        await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                TuiProfileId: claude.Id),
            CancellationToken.None);

        await using (var verify = CreateContext())
        {
            var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
            stored.Kind.ShouldBe(AgentKind.ClaudeCode);
            stored.TuiProfileId.ShouldBe(claude.Id);
        }
    }

    [Test]
    public async Task CreateAsync_with_null_tui_profile_and_no_installation_default_leaves_Kind_untouched()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
        (await db.AgentTuiProfiles.AnyAsync()).ShouldBeFalse();
        var service = CreateService(db, new MockEventBus());

        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Unprofiled"), "D:/src/unprofiled", TuiProfileId: null),
            CancellationToken.None);

        var stored = await db.Agents.SingleAsync(a => a.Id == created.Id);
        stored.TuiProfileId.ShouldBeNull();
        stored.Kind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task GetById_and_list_return_Kind_from_the_attached_Codex_profile()
    {
        // CARD-0139 T1 / D5: Kind is on both DTOs so a mismatch is visible without hand-written SQL.
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Visible Codex"), "D:/src/visible-codex", TuiProfileId: codex.Id),
            CancellationToken.None);

        created.Kind.ShouldBe(AgentKind.Codex);
        created.TuiProfileId.ShouldBe(codex.Id);

        var listed = (await service.GetAllAsync(CancellationToken.None))
            .Single(a => a.Id == created.Id);
        listed.Kind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task UpdateAsync_with_null_Kind_leaves_the_stored_value_unchanged()
    {
        // CARD-0139 T3. Null = leave unchanged, matching BoardId / ReplyStyle / LaunchEnv.
        new UpdateAgentRequest("A", "C:\\tmp", null, null, AgentAssignmentPolicy.AutoPick)
            .Kind.ShouldBeNull();

        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Sticky Kind"), "D:/src/sticky-kind", TuiProfileId: codex.Id),
            CancellationToken.None);
        created.Kind.ShouldBe(AgentKind.Codex);

        var updated = await service.UpdateAsync(
            created.Id,
            Patch(created, details: "edited without touching kind"),
            CancellationToken.None);

        updated.Kind.ShouldBe(AgentKind.Codex);
        updated.Details.ShouldBe("edited without touching kind");
        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == created.Id)).Kind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task UpdateAsync_Kind_on_a_pool_delegate_is_refused_even_when_it_agrees()
    {
        // CARD-0139 T5 / D3 — the dangerous edit. Refused unconditionally; the row is unchanged.
        await using var db = CreateContext();
        var created = await CreateService(db, new MockEventBus()).CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Pool Kind"), "D:/src/pool-kind"),
            CancellationToken.None);

        AgentKind originalKind;
        await using (var setup = CreateContext())
        {
            originalKind = (await setup.Agents.SingleAsync(a => a.Id == created.Id)).Kind;
            await setup.Agents.Where(a => a.Id == created.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsPoolDelegate, true));
        }

        await using var updateDb = CreateContext();
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            CreateService(updateDb, new MockEventBus()).UpdateAsync(
                created.Id,
                Patch(created, kind: originalKind),
                CancellationToken.None));
        ex.Code.ShouldBe("agent_kind_pool_delegate");
        ex.StatusCode.ShouldBe(409);
        ex.Message.ShouldContain("pool delegate");

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(originalKind);
        stored.IsPoolDelegate.ShouldBeTrue();
    }

    [Test]
    public async Task UpdateAsync_Kind_disagreeing_with_the_attached_profile_is_refused()
    {
        // CARD-0139 T6 / D2 rule 3b.
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Mismatch Kind"), "D:/src/mismatch-kind", TuiProfileId: codex.Id),
            CancellationToken.None);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateAsync(
                created.Id,
                Patch(created, kind: AgentKind.ClaudeCode),
                CancellationToken.None));
        ex.Code.ShouldBe("agent_kind_profile_mismatch");
        ex.StatusCode.ShouldBe(409);
        ex.Message.ShouldContain("Codex");
        ex.Message.ShouldContain("ClaudeCode");
        ex.Message.ShouldContain("runner profile");

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(AgentKind.Codex);
        stored.TuiProfileId.ShouldBe(codex.Id);
    }

    [Test]
    public async Task UpdateAsync_Kind_agreeing_with_the_attached_profile_succeeds_as_a_noop()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Agree Kind"), "D:/src/agree-kind", TuiProfileId: codex.Id),
            CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.Id,
            Patch(created, kind: AgentKind.Codex),
            CancellationToken.None);

        updated.Kind.ShouldBe(AgentKind.Codex);
        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(AgentKind.Codex);
        stored.TuiProfileId.ShouldBe(codex.Id);
    }

    [Test]
    public async Task UpdateAsync_re_sending_the_existing_tuiProfileId_re_syncs_a_corrupted_Kind()
    {
        // CARD-0139 T6 / D6 — the in-place correction. No dedicated resync endpoint.
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Resync Kind"), "D:/src/resync-kind", TuiProfileId: codex.Id),
            CancellationToken.None);

        await using (var setup = CreateContext())
        {
            await setup.Agents.Where(a => a.Id == created.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Kind, AgentKind.ClaudeCode));
            (await setup.Agents.SingleAsync(a => a.Id == created.Id)).Kind.ShouldBe(AgentKind.ClaudeCode);
        }

        await using var updateDb = CreateContext();
        var updated = await CreateService(updateDb, new MockEventBus()).UpdateAsync(
            created.Id,
            Patch(created, tuiProfileId: created.TuiProfileId),
            CancellationToken.None);

        updated.Kind.ShouldBe(AgentKind.Codex);
        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(AgentKind.Codex);
        stored.TuiProfileId.ShouldBe(codex.Id);
    }

    [Test]
    public async Task UpdateAsync_changing_profile_to_codex_and_asserting_Kind_is_checked_against_the_new_profile()
    {
        // CARD-0139 T7 / D2 application order. Red if the Kind check runs before ApplyTuiSelectionAsync:
        // asserting Codex would be refused against the still-attached Claude profile, and asserting
        // ClaudeCode would be accepted then overwritten by the Codex sync.
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var claude = await SeedProfileAsync(db, AgentKind.ClaudeCode, "Claude");
        var codex = await SeedProfileAsync(db, AgentKind.Codex, "Codex");
        var created = await service.CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Order Kind"), "D:/src/order-kind", TuiProfileId: claude.Id),
            CancellationToken.None);

        var toCodex = await service.UpdateAsync(
            created.Id,
            Patch(created, kind: AgentKind.Codex, tuiProfileId: codex.Id),
            CancellationToken.None);
        toCodex.Kind.ShouldBe(AgentKind.Codex);
        toCodex.TuiProfileId.ShouldBe(codex.Id);

        var reset = await service.UpdateAsync(
            created.Id,
            Patch(created, tuiProfileId: claude.Id),
            CancellationToken.None);
        reset.Kind.ShouldBe(AgentKind.ClaudeCode);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateAsync(
                created.Id,
                Patch(created, kind: AgentKind.ClaudeCode, tuiProfileId: codex.Id),
                CancellationToken.None));
        ex.Code.ShouldBe("agent_kind_profile_mismatch");

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(AgentKind.ClaudeCode);
        stored.TuiProfileId.ShouldBe(claude.Id);
    }

    [Test]
    public async Task UpdateAsync_Kind_on_a_no_profile_non_pool_agent_writes_it()
    {
        await using var db = CreateContext();
        var created = await CreateService(db, new MockEventBus()).CreateAsync(
            new CreateAgentRequest(UniqueAgentName("Free Kind"), "D:/src/free-kind"),
            CancellationToken.None);

        await using (var setup = CreateContext())
        {
            await setup.Agents.Where(a => a.Id == created.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.TuiProfileId, (Guid?)null));
        }

        await using var updateDb = CreateContext();
        var updated = await CreateService(updateDb, new MockEventBus()).UpdateAsync(
            created.Id,
            Patch(created, kind: AgentKind.Grok),
            CancellationToken.None);

        updated.Kind.ShouldBe(AgentKind.Grok);
        updated.TuiProfileId.ShouldBeNull();
        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.Kind.ShouldBe(AgentKind.Grok);
        stored.TuiProfileId.ShouldBeNull();
        stored.IsPoolDelegate.ShouldBeFalse();
    }

    /// <summary>
    /// A started agent as AgentControlService leaves it: lifecycle latch set, PersistentSessionId
    /// pointing at a seeded session in the given state. Transcript starts empty (= idle).
    /// </summary>
    private static async Task<(Guid AgentId, Guid SessionId)> SeedStartedAgentAsync(SessionStatus sessionStatus)
    {
        Guid agentId;
        await using (var db = CreateContext())
        {
            var created = await CreateService(db, new MockEventBus()).CreateAsync(
                new CreateAgentRequest(UniqueAgentName("Working Projection Claude"), "D:/src/app"),
                CancellationToken.None);
            agentId = created.Id;
        }

        var sessionId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = null,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = sessionStatus,
                Cwd = $"D:/tmp/agent-working-tests/{sessionId:N}",
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.PersistentSessionId = sessionId.ToString("D");
            agent.Status = AgentStatus.Running; // the "started" lifecycle latch, as Start sets it
            await db.SaveChangesAsync();
        }

        return (agentId, sessionId);
    }

    private static async Task InsertTranscriptAsync(Guid sessionId, string kind, string? text)
    {
        await using var db = CreateContext();
        var baseSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = baseSeq + 1,
            Kind = kind,
            Text = text,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // Transcript entries cascade-delete with the session.
    private static async Task CleanupSessionsAsync(params Guid[] sessionIds)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
    }

    [Test]
    public async Task T5_exact_model_on_a_blank_field_profile_is_refused_and_clearing_succeeds()
    {
        await using var db = CreateContext();
        var service = CreateService(db, new MockEventBus());
        var profile = await SeedProfileAsync(db, AgentKind.Grok, "GKP", modelArgumentName: null);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                UniqueAgentName("Blank Field Grok"),
                "D:/src/gkp",
                TuiProfileId: profile.Id),
            CancellationToken.None);
        created.ModelId.ShouldBeNull();

        var error = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateAgentRequest(
                    created.Name,
                    created.WorkingDirectory,
                    created.Details,
                    created.DefaultWorkflowTemplateId,
                    created.AssignmentPolicy,
                    TuiProfileId: profile.Id,
                    ModelId: "grok-4.6"),
                CancellationToken.None));
        error.Code.ShouldBe("model_argument_unsupported");
        error.StatusCode.ShouldBe(409);

        var cleared = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                TuiProfileId: profile.Id,
                ModelId: null),
            CancellationToken.None);
        cleared.ModelId.ShouldBeNull();
        cleared.TuiProfileId.ShouldBe(profile.Id);
    }

    private static async Task<AgentTuiProfile> SeedProfileAsync(
        AppDbContext db, AgentKind kind, string namePrefix, string? modelArgumentName = null)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"{namePrefix} {Guid.NewGuid():N}",
            Kind = kind,
            IsEnabled = true,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = "synthetic-wrapper",
            ArgumentsJson = "[]",
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = modelArgumentName,
            Guidance = string.Empty,
            CreatedAt = now,
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return profile;
    }

    private static UpdateAgentRequest Patch(
        AgentDetailDto agent,
        AgentKind? kind = null,
        Guid? tuiProfileId = null,
        string? details = null) =>
        new(
            agent.Name,
            agent.WorkingDirectory,
            details ?? agent.Details,
            agent.DefaultWorkflowTemplateId,
            agent.AssignmentPolicy,
            TuiProfileId: tuiProfileId,
            Kind: kind);

    [Test]
    public async Task Live_session_dto_carries_transcriptBinding_from_runner_metadata()
    {
        var (agentId, sessionId) = await SeedStartedAgentAsync(SessionStatus.Running);
        try
        {
            var runner = new BindingRunnerClient(sessionId, transcriptBound: false, transcriptUnboundReason: "awaiting-input");
            await using var db = CreateContext();
            var detail = await CreateService(db, new MockEventBus(), runnerClient: runner)
                .GetByIdAsync(agentId, CancellationToken.None);

            detail.LiveSession.ShouldNotBeNull();
            detail.LiveSession!.TranscriptBinding.ShouldBe("awaiting-input");

            var refusedRunner = new BindingRunnerClient(sessionId, transcriptBound: false, transcriptUnboundReason: "refused");
            await using var refusedDb = CreateContext();
            var refused = await CreateService(refusedDb, new MockEventBus(), runnerClient: refusedRunner)
                .GetByIdAsync(agentId, CancellationToken.None);
            refused.LiveSession!.TranscriptBinding.ShouldBe("unbound");

            var boundRunner = new BindingRunnerClient(sessionId, transcriptBound: true);
            await using var db2 = CreateContext();
            var bound = await CreateService(db2, new MockEventBus(), runnerClient: boundRunner)
                .GetByIdAsync(agentId, CancellationToken.None);
            bound.LiveSession!.TranscriptBinding.ShouldBe("bound");

            var oldRunner = new BindingRunnerClient(sessionId, transcriptBound: null);
            await using var oldDb = CreateContext();
            var old = await CreateService(oldDb, new MockEventBus(), runnerClient: oldRunner)
                .GetByIdAsync(agentId, CancellationToken.None);
            old.LiveSession!.TranscriptBinding.ShouldBeNull();
        }
        finally
        {
            await CleanupSessionsAsync(sessionId);
        }
    }

    private static AgentService CreateService(
        AppDbContext db,
        IEventBus eventBus,
        IDirectoryWriter? directoryWriter = null,
        ISessionRunnerClient? runnerClient = null)
    {
        return new AgentService(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            eventBus,
            TimeProvider.System,
            directoryWriter ?? new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance,
            runnerClient: runnerClient);
    }

    private sealed class BindingRunnerClient : ISessionRunnerClient
    {
        private readonly SessionRunnerSessionDto _session;

        public BindingRunnerClient(Guid sessionId, bool? transcriptBound, string? transcriptUnboundReason = null)
        {
            _session = new SessionRunnerSessionDto(
                sessionId,
                Pid: 1,
                StartedAt: DateTime.UtcNow,
                Status: "Running",
                ExitCode: null,
                ExitReason: AgentExitReason.Unknown,
                LastSequence: 0,
                TranscriptBound: transcriptBound,
                TranscriptBindHow: transcriptBound == true ? TranscriptBindMethods.Exact : null,
                TranscriptUnboundReason: transcriptUnboundReason);
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([_session]);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(_session);

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string UniqueAgentName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

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
