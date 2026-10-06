using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>One migrated PostgreSQL clone shared by every scope, including recreated DI.</summary>
internal sealed class BlockedTaskParkFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private readonly List<string> _roots = [];
    private BridgeQueueHarness _harness = null!;
    public ParkCommitCut Cut { get; } = new();
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid BlockEventId { get; private set; } = Guid.NewGuid();
    public Guid CardId { get; } = Guid.NewGuid();
    public Guid SessionId { get; private set; }
    public Guid AgentId { get; private set; }
    public BlockedTaskParkingOptions Options { get; } = new();
    public DateTime Now { get; } = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
    internal IServiceProvider Services => _harness.Provider;

    private BlockedTaskParkFixture(IsolatedTestSchema schema) => _schema = schema;

    public static async Task<BlockedTaskParkFixture> CreateAsync(bool report = true, bool session = true,
        bool sourceLanding = false)
    {
        var f = new BlockedTaskParkFixture(await TestDbFixture.CreateIsolatedSchemaAsync());
        try
        {
            await f.StartAsync();
            f.SessionId = f._harness.SessionId;
            f.AgentId = f._harness.AgentId;
            await using var db = f.Db();
            var project = new Project { Id = Guid.NewGuid(), Name = "park-state", CreatedAt = f.Now, UpdatedAt = f.Now,
                LocalRepositoryPath = f._harness.TempRoot, GitRepositoryUrl = "https://example.test/park.git" };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "park-state", CreatedAt = f.Now, UpdatedAt = f.Now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "In Progress",
                StateKey = "in-progress", CardStatus = CardStatus.InProgress, IsActive = true, CreatedAt = f.Now, UpdatedAt = f.Now };
            db.AddRange(project, board, column, new Card { Id = f.CardId, BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-0001", Title = "retained park", Status = CardStatus.InProgress,
                CreatedAt = f.Now, UpdatedAt = f.Now });
            var worker = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            worker.RunnerId = "fixture";
            worker.RunnerStoreId = Guid.NewGuid();
            worker.RunnerCwd = f._harness.TempRoot;
            worker.StartedAt = f.Now;
            Guid? sourceLandingId = null;
            if (sourceLanding)
            {
                var ownerId = Guid.NewGuid();
                sourceLandingId = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = ownerId, RootTaskId = ownerId, Title = "source owner", Goal = "source custody fixture",
                    Status = AgentTaskStatus.Succeeded, CreatedAt = f.Now, CompletedAt = f.Now
                });
                // Only a valid custody reference is needed for the unconditional exclusion.
                // Do not seed a successful landing/publication receipt or bypass EF immutability.
                db.AgentTaskLandings.Add(new AgentTaskLanding
                {
                    Id = sourceLandingId.Value, TaskId = ownerId, Phase = LandPhase.Inspected,
                    OriginalSourceSha = new string('a', 40), CreatedAt = f.Now, UpdatedAt = f.Now
                });
            }
            db.AgentTasks.Add(new AgentTask
            {
                Id = f.TaskId, RootTaskId = f.TaskId, AgentId = f.AgentId,
                AgentSessionId = session ? f.SessionId : null, CardId = f.CardId,
                Title = "park custody", Goal = "retain full context", Status = AgentTaskStatus.Blocked,
                Attempt = 1, Workspace = WorkspaceMode.Worktree, WorktreePath = f._harness.TempRoot,
                RemoteWorktreePath = f._harness.TempRoot, WorktreeBranch = "feat/park-state",
                WorktreeBaseSha = new string('a', 40), RunnerId = session ? "fixture" : null,
                SourceLandingOperationId = sourceLandingId,
                SourceLandingSha = sourceLanding ? new string('a', 40) : null,
                CreatedAt = f.Now, DispatchedAt = f.Now.AddMinutes(1),
                CompletedAt = report ? f.Now.AddMinutes(2) : null,
                Result = report ? "Stored full blocked report with final canary" : null,
                ResultFilePath = report ? Path.Combine(f._harness.TempRoot, "report.md") : null,
                FailureReason = report ? "Question awaiting reply" : "Routing/quota hold"
            });
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = f.BlockEventId, AgentTaskId = f.TaskId,
                AgentSessionId = session ? f.SessionId : null, Type = AgentTaskEventType.Blocked,
                Detail = "Original block history", At = f.Now.AddMinutes(2) });
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Sequence = 42, Kind = "AssistantText", Text = "Historical transcript canary", CreatedAt = f.Now });
            await db.SaveChangesAsync();
            if (report) await File.WriteAllTextAsync(Path.Combine(f._harness.TempRoot, "report.md"), "Full retained report bytes");
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    private async Task StartAsync()
    {
        _harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = _schema.ConnectionString, PreserveDatabaseOnDispose = true,
            AttachSessionId = SessionId == Guid.Empty ? null : SessionId,
            AttachAgentId = AgentId == Guid.Empty ? null : AgentId,
            AlwaysOn = false, ConfigureDbContext = b => b.AddInterceptors(Cut),
            ConfigureServices = services =>
            {
                services.AddDelegationWorktreeGraph();
                services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
                services.AddScoped<BlockedTaskParkingService>();
                services.AddOptions<CardWorkTransitionSettings>();
                services.AddScoped<CardWorkTransitionService>();
            }
        });
        _roots.Add(_harness.TempRoot);
    }

    public async Task RestartAsync()
    {
        await _harness.DisposeAsync();
        await StartAsync();
    }

    public async Task<T> RunAsync<T>(Func<BlockedTaskParkingService, AppDbContext, Task<T>> action)
    {
        await using var scope = _harness.Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<Guid?> RegisterAsync()
    {
        await using var db = Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
        return await RunAsync((s, _) => s.RegisterAsync(TaskId, task.Attempt, BlockEventId, task.ConcurrencyToken, default));
    }

    public async Task<bool> AdvanceAsync(Guid id, AgentTaskParkState next)
    {
        await using var db = Db();
        var row = await db.AgentTaskParks.SingleAsync(p => p.Id == id);
        return await RunAsync((s, _) => s.PersistStateAsync(id, row.Revision, row.State, next, "park_test_boundary", default));
    }

    public async Task NextEpisodeAsync(bool attempt)
    {
        await using var db = Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
        task.ConcurrencyToken = Guid.NewGuid();
        if (attempt) task.Attempt++;
        else
        {
            BlockEventId = Guid.NewGuid();
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = BlockEventId, AgentTaskId = TaskId,
                Type = AgentTaskEventType.Blocked, Detail = "Second block", At = Now.AddMinutes(3) });
        }
        await db.SaveChangesAsync();
    }

    public async Task<int> ScanCardsAsync()
    {
        await using var scope = _harness.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CardWorkTransitionService>().ScanAsync(default);
    }

    public async ValueTask DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
        await _schema.DisposeAsync();
        foreach (var root in _roots.Distinct())
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    internal sealed class ParkCommitCut : DbTransactionInterceptor
    {
        public bool Fail { get; set; }
        public TaskCompletionSource? Entered { get; set; }
        public TaskCompletionSource? Continue { get; set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Entered is not null)
            {
                Entered.TrySetResult();
                await Continue!.Task.WaitAsync(cancellationToken);
            }
            if (Fail) throw new InvalidOperationException("park_commit_cut");
            return result;
        }
    }
}
