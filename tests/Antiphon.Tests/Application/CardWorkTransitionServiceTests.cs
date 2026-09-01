using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0040 S2: the sweep that moves a card from the delegated work bound to it.
/// </summary>
/// <remarks>
/// <para><b>[NotInParallel] with NO group key.</b> This drives a GLOBAL sweep, and a group key is
/// exactly the mistake <c>AgentSupervisionTests</c> made: it serialised the suite only against
/// itself while other suites' sweeps ticked concurrently. Every test additionally takes its own
/// migrated schema, so the sweep cannot see another test's cards at all, and every assertion is
/// scoped to rows the test created.</para>
/// </remarks>
[Category("Integration")]
[NotInParallel]
public class CardWorkTransitionServiceTests
{
    [Test]
    public async Task a_dispatched_task_moves_its_backlog_card_to_in_progress()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var task = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);

        var moved = await world.ReadCardAsync(card.Id);
        moved.Status.ShouldBe(CardStatus.InProgress);
        // The hold is what stops the orchestrator tick spawning a card session on top of the
        // delegate that caused the move (CARD-0087).
        moved.AutoDispatchHeldAt.ShouldNotBeNull();

        var revision = await world.LatestMoveAsync(card.Id);
        revision.EditedBy.ShouldBe("card-transitions");
        revision.ToStatus.ShouldBe(CardStatus.InProgress);
        revision.Reason.ShouldNotBeNull();
        revision.Reason!.ShouldContain(DelegationReportFormatter.Short(task.Id));
        revision.Reason.ShouldContain("dispatched against this card");

        // Structurally incapable of spawning: it calls ApplyColumnMove, not MoveAsync (CARD-0051).
        (await world.SessionCountForAsync(card.Id)).ShouldBe(0);
        world.CardChangedFor(card.Id).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task a_settled_task_with_nothing_else_open_moves_the_card_to_review_and_dequeues_it()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress, assignedAgentId: agent.Id, queuePosition: 1);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Succeeded, dispatchedAt: world.Now.AddMinutes(-10), completedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);

        var moved = await world.ReadCardAsync(card.Id);
        moved.Status.ShouldBe(CardStatus.Review);
        // A Review landing is finished from its agent's perspective — leaving it at queue head is
        // the CARD-0001 respawn loop.
        moved.AssignedAgentId.ShouldBeNull();
        moved.AgentQueuePosition.ShouldBeNull();
        moved.AutoDispatchHeldAt.ShouldBeNull();

        var revision = await world.LatestMoveAsync(card.Id);
        revision.ToStatus.ShouldBe(CardStatus.Review);
        revision.Reason!.ShouldContain("settled Succeeded; no other task is open");
    }

    [Test]
    public async Task a_settle_with_a_sibling_still_open_leaves_the_card_in_progress()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Succeeded, dispatchedAt: world.Now.AddMinutes(-20), completedAt: world.Now);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Working, dispatchedAt: world.Now.AddMinutes(-5));

        (await world.ScanAsync()).ShouldBe(0);

        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.InProgress);
        (await world.MoveCountAsync(card.Id)).ShouldBe(0);
    }

    [Test]
    public async Task a_blocked_task_counts_as_open()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Blocked, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);

        // The card is still being worked — by whoever answers the question.
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.InProgress);
    }

    [Test]
    public async Task a_failed_or_canceled_last_task_moves_the_card_nowhere()
    {
        await using var world = await World.CreateAsync();
        var failedCard = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(
            failedCard.Id, AgentTaskStatus.Failed, dispatchedAt: world.Now.AddMinutes(-9), completedAt: world.Now);
        var canceledCard = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(
            canceledCard.Id, AgentTaskStatus.Canceled, dispatchedAt: world.Now.AddMinutes(-9), completedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(0);

        (await world.ReadCardAsync(failedCard.Id)).Status.ShouldBe(CardStatus.InProgress);
        (await world.ReadCardAsync(canceledCard.Id)).Status.ShouldBe(CardStatus.Backlog);
        (await world.MoveCountAsync(failedCard.Id)).ShouldBe(0);
        (await world.MoveCountAsync(canceledCard.Id)).ShouldBe(0);
    }

    [Test]
    public async Task a_succeeded_after_an_earlier_failure_still_reaches_review()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Failed, dispatchedAt: world.Now.AddMinutes(-40), completedAt: world.Now.AddMinutes(-30));
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Succeeded, dispatchedAt: world.Now.AddMinutes(-20), completedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);

        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Review);
    }

    [Test]
    public async Task a_backlog_card_whose_only_evidence_is_a_settle_goes_straight_to_review()
    {
        await using var world = await World.CreateAsync();
        // The CARD-0069 shape: the work ran as a delegated task and the card never left Backlog.
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Succeeded, dispatchedAt: world.Now.AddMinutes(-30), completedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);

        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Review);
        var moves = await world.MovesAsync(card.Id);
        // One move, not two: never routed via In Progress.
        moves.Count.ShouldBe(1);
        moves[0].FromStatus.ShouldBe(CardStatus.Backlog);
        moves[0].ToStatus.ShouldBe(CardStatus.Review);
    }

    [Test]
    public async Task a_human_move_newer_than_the_evidence_is_never_overridden_and_the_next_dispatch_moves_it_again()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.InProgress);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Succeeded, dispatchedAt: world.Now.AddMinutes(-60), completedAt: world.Now.AddMinutes(-30));
        // The human's word, recorded AFTER the settle: "needs another pass".
        await world.SeedHumanMoveAsync(card.Id, CardStatus.InProgress, world.Now.AddMinutes(-10));

        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.InProgress);

        // A newer dispatch IS newer than the human's word, so it speaks again. Move the card to
        // Backlog first so there is somewhere to move it FROM.
        await world.SeedHumanMoveAsync(card.Id, CardStatus.Backlog, world.Now.AddMinutes(-5), apply: true);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.InProgress);
    }

    [Test]
    public async Task cards_the_sweep_must_not_touch_are_left_alone()
    {
        await using var world = await World.CreateAsync();
        var needsDecision = await world.SeedCardAsync(CardStatus.NeedsDecision);
        var done = await world.SeedCardAsync(CardStatus.Done);
        var canceled = await world.SeedCardAsync(CardStatus.Canceled);
        var archived = await world.SeedCardAsync(CardStatus.Backlog, archived: true);
        var owned = await world.SeedCardAsync(CardStatus.Backlog, owned: true);
        foreach (var card in new[] { needsDecision, done, canceled, archived, owned })
            await world.SeedTaskAsync(card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(0);

        (await world.ReadCardAsync(needsDecision.Id)).Status.ShouldBe(CardStatus.NeedsDecision);
        (await world.ReadCardAsync(done.Id)).Status.ShouldBe(CardStatus.Done);
        (await world.ReadCardAsync(canceled.Id)).Status.ShouldBe(CardStatus.Canceled);
        (await world.ReadCardAsync(archived.Id)).Status.ShouldBe(CardStatus.Backlog);
        // The RunAttempt / card-spawn path owns this one and keeps it (CARD-0040 §4).
        (await world.ReadCardAsync(owned.Id)).Status.ShouldBe(CardStatus.Backlog);
    }

    [Test]
    public async Task check_tasks_are_ignored()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now, role: AgentTaskRole.Check);

        (await world.ScanAsync()).ShouldBe(0);

        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Backlog);
    }

    [Test]
    public async Task a_second_sweep_over_unchanged_rows_writes_nothing()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(1);
        // Its own Move row is now the card's last word, and it is newer than the evidence.
        (await world.ScanAsync()).ShouldBe(0);

        (await world.MoveCountAsync(card.Id)).ShouldBe(1);
    }

    [Test]
    public async Task the_sweep_does_nothing_when_it_is_disabled()
    {
        await using var world = await World.CreateAsync(enabled: false);
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now);

        (await world.ScanAsync()).ShouldBe(0);

        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Backlog);
        (await world.MoveCountAsync(card.Id)).ShouldBe(0);
    }

    // ---- harness -------------------------------------------------------------------------------

    /// <summary>
    /// One migrated schema, one project, one board with the four columns, and the services the
    /// sweep needs. The isolated schema is what makes a GLOBAL sweep safe to run in a shared
    /// container: there are no other tests' cards in it to move.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private readonly string _tempRoot;
        private int _cardNumber;

        private World(IsolatedTestSchema schema, ServiceProvider provider, string tempRoot,
            MockEventBus eventBus, Guid boardId)
        {
            _schema = schema;
            _provider = provider;
            _tempRoot = tempRoot;
            EventBus = eventBus;
            BoardId = boardId;
        }

        public MockEventBus EventBus { get; }

        public Guid BoardId { get; }

        public DateTime Now { get; } = DateTime.UtcNow;

        public static async Task<World> CreateAsync(bool enabled = true)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-card-transitions-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(schema.ConnectionString, npgsql =>
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
                SessionLogPath = Path.Combine(tempRoot, "session-logs"),
            }));
            services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
            {
                InternalTrackerRepositoryPathPrefix = tempRoot,
            }));
            services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
            services.AddSingleton<IOptions<CardWorkTransitionSettings>>(
                Options.Create(new CardWorkTransitionSettings { Enabled = enabled }));
            services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                new TransitionOptionsMonitor<AgentRegistrySettings>(new AgentRegistrySettings
                {
                    DefaultDefinition = "fake",
                    Definitions =
                    {
                        ["fake"] = new AgentDefinition
                        {
                            Kind = "Raw",
                            Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        },
                    },
                }));
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
            services.AddSingleton<IAgentProtocolAdapterFactory>(new NoAdapterFactory());
            services.AddSingleton<IWorkspaceHookRunner>(
                new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
            services.AddScoped<WorkspaceHookService>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddScoped<AgentSessionService>();
            services.AddScoped<AgentSessionLaunchComposer>();
            services.AddScoped<RetryScheduler>();
            services.AddScoped<ExternalTrackerSyncService>();
            services.AddSingleton<OrchestratorControlState>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddScoped<OrchestratorService>();
            services.AddScoped<CardWorkflowRunFactory>();
            services.AddScoped<AgentService>();
            services.AddSingleton<IDirectoryWriter>(
                new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                    new System.IO.Abstractions.FileSystem()));
            services.AddScoped<BoardService>();
            services.AddGitWorkspaceService();
            services.AddScoped<AgentReviewCheckpointService>();
            services.AddScoped<CardService>();
            services.AddScoped<CardWorkTransitionService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var boardId = await SeedBoardAsync(provider, tempRoot);
            return new World(schema, provider, tempRoot, eventBus, boardId);
        }

        private static async Task<Guid> SeedBoardAsync(ServiceProvider provider, string tempRoot)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Transition project",
                GitRepositoryUrl = "https://example.test/transitions.git",
                LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Transitions",
                MaxConcurrentSessions = 1,
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

        public async Task<int> ScanAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CardWorkTransitionService>()
                .ScanAsync(CancellationToken.None);
        }

        public async Task<Card> SeedCardAsync(
            CardStatus status,
            bool archived = false,
            bool owned = false,
            Guid? assignedAgentId = null,
            int? queuePosition = null)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == status);

            Guid? ownerSessionId = null;
            if (owned)
            {
                var session = new AgentSession
                {
                    Id = Guid.NewGuid(),
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = _tempRoot,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = Now,
                    StartedAt = Now,
                    LastSeenAt = Now,
                };
                db.AgentSessions.Add(session);
                await db.SaveChangesAsync();
                ownerSessionId = session.Id;
            }

            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = BoardId,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{Interlocked.Increment(ref _cardNumber):0000}",
                Title = $"Transition card {status}",
                Description = "Sweep test.",
                Status = status,
                OwnerSessionId = ownerSessionId,
                AssignedAgentId = assignedAgentId,
                AgentQueuePosition = queuePosition,
                ArchivedAt = archived ? Now : null,
                ArchivedReason = archived ? "Sweep test." : null,
                // Older than any evidence a test seeds, so the edge trigger fires unless a test
                // deliberately records a newer human move.
                CreatedAt = Now.AddDays(-30),
                UpdatedAt = Now.AddDays(-30),
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task<Agent> SeedAgentAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"transition-{Guid.NewGuid():N}"[..24],
                Slug = $"tr-{Guid.NewGuid():N}"[..18],
                WorkingDirectory = _tempRoot,
                Details = "Sweep test agent.",
                BoardId = BoardId,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();
            return agent;
        }

        public async Task<AgentTask> SeedTaskAsync(
            Guid cardId,
            AgentTaskStatus status,
            DateTime? dispatchedAt = null,
            DateTime? completedAt = null,
            AgentTaskRole role = AgentTaskRole.Code)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Depth = 0,
                Title = "Bound task",
                Goal = "Sweep test task.",
                Kind = AgentTaskKind.Worker,
                Role = role,
                CardId = cardId,
                ModelLevel = AgentModelLevel.High,
                WorkingDirectory = _tempRoot,
                Status = status,
                DispatchedAt = dispatchedAt,
                CompletedAt = completedAt,
                CreatedAt = dispatchedAt ?? Now,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        /// <summary>
        /// Records what a human's <c>card.ps1 move</c> leaves behind: a Move revision at a chosen
        /// time. <paramref name="apply"/> also lands the card in that column, for the cases where
        /// the sweep then has somewhere to move it from.
        /// </summary>
        public async Task SeedHumanMoveAsync(Guid cardId, CardStatus to, DateTime at, bool apply = false)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == to);

            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                RevisionNumber = ++card.RevisionCount,
                Kind = CardRevisionKind.Move,
                FromColumnId = card.BoardColumnId,
                ToColumnId = column.Id,
                FromStatus = card.Status,
                ToStatus = to,
                Reason = "needs another pass",
                CreatedAt = at,
            });
            if (apply)
            {
                card.BoardColumnId = column.Id;
                card.Status = to;
            }

            await db.SaveChangesAsync();
        }

        public async Task<Card> ReadCardAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Cards.AsNoTracking().FirstAsync(c => c.Id == cardId);
        }

        public async Task<List<CardRevision>> MovesAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.CardRevisions.AsNoTracking()
                .Where(r => r.CardId == cardId && r.Kind == CardRevisionKind.Move && r.EditedBy == "card-transitions")
                .OrderBy(r => r.RevisionNumber)
                .ToListAsync();
        }

        public async Task<int> MoveCountAsync(Guid cardId) => (await MovesAsync(cardId)).Count;

        public async Task<CardRevision> LatestMoveAsync(Guid cardId) => (await MovesAsync(cardId))[^1];

        public async Task<int> SessionCountForAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AgentSessions.AsNoTracking().CountAsync(s => s.CardId == cardId);
        }

        public int CardChangedFor(Guid cardId) => EventBus.PublishedEvents
            .Count(e => e.EventName == "CardChanged" && e.Payload.ToString()!.Contains(cardId.ToString()));

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>No test here spawns a session; asking for an adapter is a bug in the test.</summary>
    private sealed class NoAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("The sweep must never launch an agent session.");
    }

    /// <summary>An automated move cannot spawn, so it can never reach a worktree either.</summary>
    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new InvalidOperationException("The sweep must never create a worktree.");

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class TransitionOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TransitionOptionsMonitor(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
