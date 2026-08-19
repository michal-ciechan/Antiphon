using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Board")]
public class BoardServiceIntegrationTests
{
    [Test]
    public async Task Board_create_card_and_detail_round_trip_returns_ordered_columns_and_labels()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "E08 Board", "Browser-driven work", 2),
                CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Wire board UI", "Build the usable board", 3, ["ui", "e08"]),
                CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Frontend Claude",
                    Path.Combine(tempRoot, "agent-workspace"),
                    DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id,
                new AssignAgentCardRequest(card.Id),
                CancellationToken.None);

            var detail = await harness.BoardService.GetByIdAsync(board.Id, CancellationToken.None);

            detail.Columns.Select(c => c.StateKey).ShouldBe(["backlog", "in-progress", "review", "done"]);
            detail.Columns.Single(c => c.StateKey == "in-progress").IsActive.ShouldBeTrue();
            detail.Columns.Single(c => c.StateKey == "done").IsTerminal.ShouldBeTrue();
            var backlogCard = detail.Columns.Single(c => c.StateKey == "backlog").Cards.Single();
            backlogCard.Id.ShouldBe(card.Id);
            backlogCard.Labels.ShouldBe(["ui", "e08"]);
            backlogCard.Sessions.ShouldBeEmpty();
            backlogCard.AssignedAgentId.ShouldBe(agent.Id);
            backlogCard.AssignedAgentName.ShouldBe("Frontend Claude");
            backlogCard.AgentQueuePosition.ShouldBe(1);
            backlogCard.ActiveWorkflowRunId.ShouldNotBeNull();
            backlogCard.WorkflowRunStatus.ShouldBe(CardWorkflowRunStatus.Queued);
            backlogCard.CurrentWorkflowStageName.ShouldBe("Implement");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Card_move_rejects_column_from_another_board()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);
            var boardA = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "A"), CancellationToken.None);
            var boardB = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "B"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                boardA.Id,
                new CreateCardRequest(null, "Stay on board A"),
                CancellationToken.None);
            var foreignColumn = boardB.Columns.Single(c => c.StateKey == "in-progress");

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.MoveAsync(
                    card.Id,
                    new MoveCardRequest(foreignColumn.Id, card.ConcurrencyToken),
                    CancellationToken.None));

            ex.Errors[nameof(MoveCardRequest.BoardColumnId)].Single()
                .ShouldBe("Target column belongs to a different board.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0005 regression pin: the allocator used to be count+1, so removing any card shifted the
    // sequence backwards — delete CARD-0002 from a three-card board and the next create handed out
    // CARD-0003, which was already taken. Identifiers are cited in commit messages and docs, so
    // allocation is now max-suffix+1. (Deleting the CURRENT HIGHEST card still frees its number;
    // only CARD-0019's archive-instead-of-delete closes that, and this test says so by scope.)
    [Test]
    public async Task Card_identifier_allocation_skips_a_freed_number_instead_of_reusing_it()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Identifier board"), CancellationToken.None);

            var first = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "First"), CancellationToken.None);
            var second = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Second"), CancellationToken.None);
            var third = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Third"), CancellationToken.None);
            first.Identifier.ShouldBe("CARD-0001");
            second.Identifier.ShouldBe("CARD-0002");
            third.Identifier.ShouldBe("CARD-0003");

            await using (var deleteDb = CreateContext())
            {
                var row = await deleteDb.Cards.SingleAsync(c => c.Id == second.Id);
                deleteDb.Cards.Remove(row);
                await deleteDb.SaveChangesAsync();
            }

            var fourth = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Fourth"), CancellationToken.None);

            // count+1 would have produced CARD-0003 here — a duplicate of a live card.
            fourth.Identifier.ShouldBe("CARD-0004");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Card_move_rejects_stale_concurrency_token()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);
            var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Stale Token"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Detect stale drag"),
                CancellationToken.None);
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.MoveAsync(
                    card.Id,
                    new MoveCardRequest(activeColumn.Id, Guid.NewGuid()),
                    CancellationToken.None));

            ex.Message.ShouldContain("modified by another operation");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Moving_card_to_active_column_queues_interactive_session_and_moves_success_to_review()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "E08_MOVE_OK" };
            await using var harness = BuildHarness(tempRoot, [adapter]);
            var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Spawn Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Start from drag"),
                CancellationToken.None);
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(activeColumn.Id, card.ConcurrencyToken, Spawn: true),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            moved.Card.Status.ShouldBe(CardStatus.InProgress);
            moved.Card.OwnerSessionId.ShouldNotBeNull();
            // The spawn is no longer a side effect the caller has to infer.
            moved.SpawnedSessionId.ShouldBe(moved.Card.OwnerSessionId);
            moved.SpawnSuppressed.ShouldBeFalse();
            adapter.Started.ShouldBeTrue();
            adapter.Killed.ShouldBeFalse();
            adapter.Disposed.ShouldBeFalse();
            await using var verify = CreateContext();
            var storedCard = await verify.Cards
                .Include(c => c.BoardColumn)
                .SingleAsync(c => c.Id == card.Id);
            storedCard.Status.ShouldBe(CardStatus.Review);
            storedCard.BoardColumn.StateKey.ShouldBe("review");
            storedCard.OwnerSessionId.ShouldBe(moved.Card.OwnerSessionId);
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == moved.Card.OwnerSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            var attempt = await verify.RunAttempts.SingleAsync(a => a.AgentSessionId == session.Id);
            attempt.Phase.ShouldBe(RunPhase.Succeeded);
            (await verify.RetrySchedules.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
            harness.EventBus.PublishedEvents
                .Count(e => e.Group is null && e.EventName == "CardChanged")
                .ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0051: the move used to spawn unconditionally and return a bare CardDto, so a scripted
    // PATCH that only meant to file a card where it belongs started an agent and said nothing.
    // Default-off, and the result says out loud that it moved into an active column with nobody
    // on it — the alternative is discovering a dead session later, or nothing at all.
    [Test]
    public async Task Moving_into_an_active_column_without_asking_starts_nothing_and_reports_that()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "NEVER_RUNS" };
            await using var harness = BuildHarness(tempRoot, [adapter]);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "No Spawn Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Filed where it belongs, not started"),
                CancellationToken.None);
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(activeColumn.Id, card.ConcurrencyToken, "Belongs here; not starting it yet."),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            moved.SpawnSuppressed.ShouldBeTrue();
            moved.SpawnedSessionId.ShouldBeNull();
            moved.Card.Status.ShouldBe(CardStatus.InProgress);
            moved.Card.OwnerSessionId.ShouldBeNull();
            adapter.Started.ShouldBeFalse();

            await using var verify = CreateContext();
            // Scoped to THIS card: the assembly shares one Postgres and other suites spawn freely.
            (await verify.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
            var storedCard = await verify.Cards.Include(c => c.BoardColumn).SingleAsync(c => c.Id == card.Id);
            storedCard.BoardColumn.StateKey.ShouldBe("in-progress");
            storedCard.OwnerSessionId.ShouldBeNull();
            storedCard.AutoDispatchHeldAt.ShouldNotBeNull();
            moved.Card.AutoDispatchHeldAt.ShouldNotBeNull();

            // Explicit start lifts the hold and claims a session.
            var spawned = await harness.CardService.SpawnAsync(
                card.Id,
                new SpawnCardRequest("fake", 100, 24),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            spawned.SessionId.ShouldNotBe(Guid.Empty);
            await using var afterSpawn = CreateContext();
            var released = await afterSpawn.Cards.SingleAsync(c => c.Id == card.Id);
            released.AutoDispatchHeldAt.ShouldBeNull();
            (await afterSpawn.AgentSessions.CountAsync(s => s.CardId == card.Id && s.Id == spawned.SessionId))
                .ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0087: a hold is only meaningful while the card sits in an active column with no work
    // started. Leaving clears it so a later re-entry is a new decision, not a stale timestamp.
    [Test]
    public async Task Moving_a_held_card_off_active_and_back_in_reapplies_the_hold()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Hold re-entry board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Hold, leave, come back"),
                CancellationToken.None);
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");
            var backlogColumn = board.Columns.Single(c => c.StateKey == "backlog");

            var held = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(activeColumn.Id, card.ConcurrencyToken, "Parked in progress."),
                CancellationToken.None);
            held.SpawnSuppressed.ShouldBeTrue();
            held.Card.AutoDispatchHeldAt.ShouldNotBeNull();

            var offActive = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(backlogColumn.Id, held.Card.ConcurrencyToken, "Back to the pile."),
                CancellationToken.None);
            offActive.Card.AutoDispatchHeldAt.ShouldBeNull();
            offActive.SpawnSuppressed.ShouldBeFalse();

            var backIn = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(activeColumn.Id, offActive.Card.ConcurrencyToken, "Back in, still not starting."),
                CancellationToken.None);
            backIn.SpawnSuppressed.ShouldBeTrue();
            backIn.Card.AutoDispatchHeldAt.ShouldNotBeNull();

            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.AutoDispatchHeldAt.ShouldNotBeNull();
            stored.OwnerSessionId.ShouldBeNull();
            (await verify.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Explicit_spawn_from_backlog_moves_successful_interactive_session_to_review()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "E08_SPAWN_OK" };
            await using var harness = BuildHarness(tempRoot, [adapter]);
            var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Explicit Spawn"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Spawn from modal"),
                CancellationToken.None);

            var result = await harness.CardService.SpawnAsync(
                card.Id,
                new SpawnCardRequest("fake", 100, 24),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            result.SessionId.ShouldNotBe(Guid.Empty);
            adapter.Started.ShouldBeTrue();
            adapter.Cols.ShouldBe(100);
            adapter.Rows.ShouldBe(24);
            await using var verify = CreateContext();
            var storedCard = await verify.Cards
                .Include(c => c.BoardColumn)
                .SingleAsync(c => c.Id == card.Id);
            storedCard.Status.ShouldBe(CardStatus.Review);
            storedCard.BoardColumn.StateKey.ShouldBe("review");
            storedCard.OwnerSessionId.ShouldBe(result.SessionId);
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            session.Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Queued_spawn_failure_releases_claim_and_publishes_card_changed()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot, []);
            var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Failed Spawn"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Spawn failure"),
                CancellationToken.None);

            var result = await harness.CardService.SpawnAsync(
                card.Id,
                new SpawnCardRequest("fake", 100, 24),
                CancellationToken.None);

            // WaitForIdleAsync settles without rethrowing launch faults (they're logged and
            // persisted); the failure outcome is asserted via DB/event state below.
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            await using var verify = CreateContext();
            var storedCard = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            storedCard.OwnerSessionId.ShouldBeNull();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            harness.EventBus.PublishedEvents
                .Count(e => e.Group is null && e.EventName == "CardChanged" && HasPayloadValue(e.Payload, "cardId", card.Id))
                .ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(string tempRoot, IReadOnlyList<IAgentProtocolAdapter> adapters)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe") } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        // CardService now depends on AgentReviewCheckpointService (files-review checkpoints);
        // register it and its GitWorkspaceService dep alongside, as ReviewLoopTests does.
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus);
    }

    private static Project NewProject(string tempRoot)
    {
        var repoPath = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var now = DateTime.UtcNow;
        return new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static WorkflowTemplate NewWorkflowTemplate(string tempRoot)
    {
        var now = DateTime.UtcNow;
        return new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Agent Template {Guid.NewGuid():N}",
            Description = tempRoot,
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
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-board-tests-{Guid.NewGuid():N}");

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var workflowTemplateIds = await db.WorkflowTemplates
            .Where(t => t.Description == tempRoot)
            .Select(t => t.Id)
            .ToListAsync();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
        {
            await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
            return;
        }

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
            .Select(s => s.Id)
            .ToListAsync();
        var workflowRunIds = await db.CardWorkflowRuns
            .Where(r => cardIds.Contains(r.CardId))
            .Select(r => r.Id)
            .ToListAsync();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot)
                || (a.CurrentCardId != null && cardIds.Contains(a.CurrentCardId.Value))
                || db.Cards.Any(c => cardIds.Contains(c.Id) && c.AssignedAgentId == a.Id)
                || db.CardWorkflowRuns.Any(r => workflowRunIds.Contains(r.Id) && r.AgentId == a.Id))
            .Select(a => a.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();
        var worktreeIds = await db.Worktrees
            .Where(w => cardIds.Contains(w.CardId))
            .Select(w => w.Id)
            .ToListAsync();

        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        await db.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(a => a.CurrentCardId, (Guid?)null));
        await db.CardWorkflowRuns
            .Where(r => workflowRunIds.Contains(r.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.CurrentStageId, (Guid?)null));
        await db.CardWorkflowStages.Where(s => workflowRunIds.Contains(s.CardWorkflowRunId)).ExecuteDeleteAsync();
        await db.CardWorkflowRuns.Where(r => workflowRunIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => worktreeIds.Contains(w.Id)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktree/session directories.
        }
    }

    private static bool HasPayloadValue<T>(object payload, string propertyName, T expected)
    {
        var value = payload.GetType().GetProperty(propertyName)?.GetValue(payload);
        return value is T typed && EqualityComparer<T>.Default.Equals(typed, expected);
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        BoardService BoardService,
        CardService CardService,
        AgentService AgentService,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for dispatch.");
        }
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;
        private readonly List<WorktreeInfo> _worktrees = [];

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            _worktrees.Add(info);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>(_worktrees.ToList());

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
