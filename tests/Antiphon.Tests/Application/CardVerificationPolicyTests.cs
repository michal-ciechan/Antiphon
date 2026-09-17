using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Land = Antiphon.Tests.Application.ExternalTrackerSyncLandingColumnTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-1 / G-5..G-9. Per-role card verification policy through the production card content
/// PATCH, revision history, board-scoped task binding, tracker synchronization and the CLI-generated
/// migration — every case on an owned database.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardVerificationPolicyTests
{
    [Test]
    public async Task C544_PolicyRevision()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await CardHarness.CreateAsync(schema);
        var created = await harness.Cards.CreateAsync(harness.BoardId, new CreateCardRequest(null, "Policy card"), CancellationToken.None);
        created.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "new-card-code-default");
        created.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "new-card-review-default");

        var rows = new (string Row, CardVerificationPolicy? Code, CardVerificationPolicy? Review,
            CardVerificationPolicy ExpectCode, CardVerificationPolicy ExpectReview,
            CardVerificationPolicy SupersededCode, CardVerificationPolicy SupersededReview)[]
        {
            ("both", CardVerificationPolicy.AllowInterim, CardVerificationPolicy.AllowInterim,
                CardVerificationPolicy.AllowInterim, CardVerificationPolicy.AllowInterim,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly),
            ("code-only", CardVerificationPolicy.FullOnly, null,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.AllowInterim,
                CardVerificationPolicy.AllowInterim, CardVerificationPolicy.AllowInterim),
            ("review-only", null, CardVerificationPolicy.FullOnly,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.AllowInterim),
            ("neither", null, null,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly,
                CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly),
        };
        var token = created.ConcurrencyToken;
        foreach (var (row, code, review, expectCode, expectReview, supersededCode, supersededReview) in rows)
        {
            var updated = await harness.Cards.UpdateContentAsync(created.Id, new UpdateCardContentRequest(token,
                $"C544 {row}", Title: row == "neither" ? "Policy card renamed" : null,
                CodeVerificationPolicy: code, ReviewVerificationPolicy: review), CancellationToken.None);
            token = updated.ConcurrencyToken;
            updated.CodeVerificationPolicy.ShouldBe(expectCode, row);
            updated.ReviewVerificationPolicy.ShouldBe(expectReview, row);
            var fetched = await harness.Cards.GetByIdAsync(created.Id, CancellationToken.None);
            fetched.CodeVerificationPolicy.ShouldBe(expectCode, row + " GET");
            fetched.ReviewVerificationPolicy.ShouldBe(expectReview, row + " GET");
            var revision = (await harness.Cards.GetRevisionsAsync(created.Id, CancellationToken.None))
                .First(r => r.Kind == CardRevisionKind.ContentEdit);
            revision.Reason.ShouldBe($"C544 {row}", row);
            revision.CodeVerificationPolicy.ShouldBe(supersededCode, row + " superseded code");
            revision.ReviewVerificationPolicy.ShouldBe(supersededReview, row + " superseded review");
        }

        // Unknown string and numeric values reach 422 validation, never a bind 400 or a silent write.
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        foreach (var (row, payload) in new[]
                 {
                     ("unknown-string", $$"""{"concurrencyToken":"{{token}}","reason":"bad","codeVerificationPolicy":"Sometimes"}"""),
                     ("numeric", $$"""{"concurrencyToken":"{{token}}","reason":"bad","reviewVerificationPolicy":1}"""),
                 })
        {
            var request = JsonSerializer.Deserialize<UpdateCardContentRequest>(payload, json).ShouldNotBeNull(row);
            var error = await Should.ThrowAsync<ValidationException>(
                () => harness.Cards.UpdateContentAsync(created.Id, request, CancellationToken.None), row);
            error.StatusCode.ShouldBe(422, row);
            var unchanged = await harness.Cards.GetByIdAsync(created.Id, CancellationToken.None);
            unchanged.ConcurrencyToken.ShouldBe(token, row + ": no write");
        }
    }

    [Test]
    public async Task C544_StalePolicyEdit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await CardHarness.CreateAsync(schema);
        var created = await harness.Cards.CreateAsync(harness.BoardId, new CreateCardRequest(null, "Stale card"), CancellationToken.None);
        var stale = created.ConcurrencyToken;
        var first = await harness.Cards.UpdateContentAsync(created.Id, new UpdateCardContentRequest(stale, "title first",
            Title: "Stale card v2"), CancellationToken.None);
        var before = await harness.Cards.GetByIdAsync(created.Id, CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => harness.Cards.UpdateContentAsync(created.Id,
            new UpdateCardContentRequest(stale, "stale opt-in", CodeVerificationPolicy: CardVerificationPolicy.AllowInterim,
                ReviewVerificationPolicy: CardVerificationPolicy.AllowInterim), CancellationToken.None), "stale-token");
        error.StatusCode.ShouldBe(409, "stale-token");
        var after = await harness.Cards.GetByIdAsync(created.Id, CancellationToken.None);
        after.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "stale-token code");
        after.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "stale-token review");
        after.RevisionCount.ShouldBe(before.RevisionCount, "stale-token revisions");
        after.ConcurrencyToken.ShouldBe(first.ConcurrencyToken, "stale-token token");
    }

    [Test]
    public async Task C544_BoardIsolation()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        Guid otherCardId;
        Guid scopedSession;
        await using (var db = world.CreateContext())
        {
            var now = DateTime.UtcNow;
            var otherBoard = new Board
            {
                Id = Guid.NewGuid(), ProjectId = world.Project.Id, Name = $"C544 other {Guid.NewGuid():N}",
                MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = otherBoard.Id, StateKey = "backlog", Name = "Backlog", ColumnOrder = 0,
                CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
            };
            var other = new Card
            {
                Id = Guid.NewGuid(), BoardId = otherBoard.Id, BoardColumnId = column.Id, Identifier = world.Card.Identifier,
                Title = "same identifier, other board", CreatedAt = now, UpdatedAt = now,
            };
            otherCardId = other.Id;
            scopedSession = Guid.NewGuid();
            var session = C544World.Session(scopedSession, "c544-board-b-caller", now);
            session.CardId = other.Id;
            db.AddRange(otherBoard, column, other, session);
            await db.SaveChangesAsync();
        }

        // Board-B-scoped caller naming the shared identifier binds board B's FullOnly card.
        var caller = new AgentTaskService.Caller(null, scopedSession, world.Repo.Path, ProjectId: world.Project.Id);
        var request = world.InterimReview(baseline.Id) with { Card = world.Card.Identifier };
        var error = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(request, caller), "board-b-scope");
        error.Code.ShouldBe(InterimVerificationPolicy.InterimDisallowedCode, $"board-b-scope: {error.Message}");
        (await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim))).ShouldBe(0, "board-b-scope");

        await using var verify = world.CreateContext();
        var boardA = await verify.Cards.AsNoTracking().SingleAsync(c => c.Id == world.Card.Id);
        boardA.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.AllowInterim, "board-a untouched");
        boardA.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.AllowInterim, "board-a untouched");
        boardA.RevisionCount.ShouldBe(0, "board-a untouched");
        var boardB = await verify.Cards.AsNoTracking().SingleAsync(c => c.Id == otherCardId);
        boardB.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "board-b FullOnly");
    }

    [Test]
    public async Task C544_TrackerPreservesPolicy()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [Land.Issue("acme/app#3", "#3", "Imported", "body")]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            Guid cardId;
            await using (var mutate = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var imported = await mutate.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
                imported.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "import default code");
                imported.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "import default review");
                cardId = imported.Id;
                imported.CodeVerificationPolicy = CardVerificationPolicy.AllowInterim;
                imported.ReviewVerificationPolicy = CardVerificationPolicy.AllowInterim;
                await mutate.SaveChangesAsync();
            }

            // An existing-card update (title + priority change) and a brand-new import in one pass.
            tracker.Candidates =
            [
                Land.Issue("acme/app#3", "#3", "Renamed on GitHub", "new body", priority: 5, labels: ["priority:critical"]),
                Land.Issue("acme/app#4", "#4", "Second import", "body"),
            ];
            await using (var sync2 = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                await Land.NewSut(sync2, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var existing = await verify.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId);
            existing.Title.ShouldBe("Renamed on GitHub", "sync did update the existing card");
            existing.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.AllowInterim, "existing code preserved");
            existing.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.AllowInterim, "existing review preserved");
            var second = await verify.Cards.AsNoTracking().SingleAsync(c => c.BoardId == graph.Board.Id && c.Id != cardId);
            second.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "new import code");
            second.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, "new import review");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch (IOException) { }
        }
    }

    [Test]
    public async Task C544_MigrationKeepsUnknown()
    {
        const string previous = "20260914233700_AddQueuedMessageDeliveryGeneration";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var legacyOutcome = Guid.NewGuid();
        Guid cardId;
        await using (var seed = await CardHarness.CreateAsync(schema))
            cardId = (await seed.Cards.CreateAsync(seed.BoardId, new CreateCardRequest(null, "Legacy card"), CancellationToken.None)).Id;

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(previous);
            (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(previous, "downgraded to the preceding migration");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "StageOutcomes" ("Id", "Stage", "Outcome", "Source", "DurationSeconds", "Detail", "RecordedAt",
                    "ReviewedSourceSha", "SubjectTaskId")
                VALUES ({legacyOutcome}, {(int)OrchestrationStage.Review}, {(int)StageOutcomeKind.Clean},
                    {(int)StageOutcomeSource.Delegate}, 5, 'legacy clean', {DateTime.UtcNow},
                    {new string('a', 40)}, {Guid.NewGuid()})
                """);
        }

        for (var pass = 1; pass <= 2; pass++)
        {
            var row = pass == 1 ? "upgrade" : "rerun";
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.Database.MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty(row);
            var outcome = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == legacyOutcome);
            outcome.OrdinaryScopeCompleted.ShouldBeNull(row + ": historical scope stays Unknown/null");
            outcome.CommissionedRound.ShouldBeNull(row + ": historical round stays null");
            outcome.VerificationProfileVersion.ShouldBeNull(row + ": historical profile stays null");
            var card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId);
            card.CodeVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, row + ": card code FullOnly");
            card.ReviewVerificationPolicy.ShouldBe(CardVerificationPolicy.FullOnly, row + ": card review FullOnly");
            (await db.StageOutcomes.CountAsync(o => o.OrdinaryScopeCompleted == VerificationScope.Full))
                .ShouldBe(0, row + ": nothing backfilled to Full");
        }
    }

    /// <summary>The production card service graph on an owned database (mirrors CardCorrectionIntegrationTests).</summary>
    private sealed class CardHarness : IAsyncDisposable
    {
        private ServiceProvider _provider = null!;
        private IServiceScope _scope = null!;
        private string _root = "";
        public CardService Cards { get; private set; } = null!;
        public Guid BoardId { get; private set; }

        public static async Task<CardHarness> CreateAsync(IsolatedTestSchema schema)
        {
            var harness = new CardHarness { _root = Path.Combine(Path.GetTempPath(), $"antiphon-c544-card-{Guid.NewGuid():N}") };
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
            var bus = new MockEventBus();
            services.AddSingleton(bus);
            services.AddSingleton<IEventBus>(bus);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000, KillGraceMs = 100, SignalRMaxChunkChars = 16 * 1024,
                ReplayBufferMaxChars = 128 * 1024, SessionLogPath = Path.Combine(harness._root, "session-logs"),
            }));
            services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
            {
                InternalTrackerRepositoryPathPrefix = harness._root,
            }));
            services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
            services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(
                new AgentRegistrySettings
                {
                    DefaultDefinition = "fake",
                    Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe") } },
                }));
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
            services.AddSingleton<IAgentProtocolAdapterFactory>(new NoAdapterFactory());
            services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
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
            services.AddSingleton<IDirectoryWriter>(new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
            services.AddScoped<BoardService>();
            services.AddGitWorkspaceService();
            services.AddScoped<AgentReviewCheckpointService>();
            services.AddScoped<CardService>();
            services.AddLogging();
            harness._provider = services.BuildServiceProvider();
            harness._scope = harness._provider.CreateScope();
            harness.Cards = harness._scope.ServiceProvider.GetRequiredService<CardService>();

            var repo = Path.Combine(harness._root, "repo");
            Directory.CreateDirectory(repo);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = $"C544 {Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/repo.git",
                LocalRepositoryPath = repo, BaseBranch = "main", CreatedAt = now, UpdatedAt = now,
            };
            var db = harness._scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var board = await harness._scope.ServiceProvider.GetRequiredService<BoardService>()
                .CreateAsync(new CreateBoardRequest(project.Id, "C544 board"), CancellationToken.None);
            harness.BoardId = board.Id;
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _provider.DisposeAsync();
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }
    }

    private sealed class NoAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("No agent session should be launched by these tests.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new InvalidOperationException("No worktree should be created by these tests.");
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
