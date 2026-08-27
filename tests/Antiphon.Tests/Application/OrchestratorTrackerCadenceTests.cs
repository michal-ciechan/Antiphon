using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0166 S1: read-only tracker sync on the orchestrator tick is cadence-gated.
/// Bidirectional writes are structurally absent from OrchestratorService (no DI registration).
/// </summary>
[Category("Integration")]
[NotInParallel]
public class OrchestratorTrackerCadenceTests
{
    [Test]
    public async Task TrackerSyncIntervalMinutes_30_calls_SyncAsync_once_for_two_ticks_inside_window()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = CreateExternalBoard(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var tracker = new CountingIssueTracker(TrackerKind.GitHubIssues, [
                new TrackedIssue(
                    "acme/app#1",
                    "#1",
                    "Cadence issue",
                    "body",
                    "open",
                    0,
                    [],
                    [],
                    "https://github.test/acme/app/issues/1",
                    """{"number":1}""")
            ]);
            await using var harness = BuildHarness(
                tempRoot,
                clock,
                trackerSyncIntervalMinutes: 30,
                issueTrackers: [tracker]);

            await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            tracker.FetchCandidatesCalls.ShouldBe(1);

            clock.Advance(TimeSpan.FromMinutes(5));
            await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            tracker.FetchCandidatesCalls.ShouldBe(1);

            clock.Advance(TimeSpan.FromMinutes(30));
            await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            tracker.FetchCandidatesCalls.ShouldBe(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task TrackerSyncIntervalMinutes_0_preserves_every_tick_sync()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = CreateExternalBoard(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var tracker = new CountingIssueTracker(TrackerKind.GitHubIssues, [
                new TrackedIssue(
                    "acme/app#2",
                    "#2",
                    "Every tick",
                    "body",
                    "open",
                    0,
                    [],
                    [],
                    "https://github.test/acme/app/issues/2",
                    """{"number":2}""")
            ]);
            await using var harness = BuildHarness(
                tempRoot,
                clock,
                trackerSyncIntervalMinutes: 0,
                issueTrackers: [tracker]);

            await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            await harness.Orchestrator.PollTickAsync(CancellationToken.None);
            tracker.FetchCandidatesCalls.ShouldBe(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public void OrchestratorService_does_not_take_a_bidirectional_sync_dependency()
    {
        // Construction/DI pin: the write half must stay off the tick. When
        // TrackerBidirectionalSyncService lands in S4 it must NOT appear on this constructor.
        var ctor = typeof(OrchestratorService).GetConstructors().Single();
        ctor.GetParameters()
            .Select(p => p.ParameterType.Name)
            .ShouldNotContain("TrackerBidirectionalSyncService");
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot,
        TimeProvider clock,
        int trackerSyncIntervalMinutes,
        IReadOnlyList<IIssueTracker> issueTrackers)
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
        services.AddSingleton(clock);
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
            PollIntervalSeconds = 30,
            MaxDispatchesPerTick = 10,
            DefaultCols = 120,
            DefaultRows = 30,
            InternalTrackerRepositoryPathPrefix = tempRoot,
            TrackerSyncIntervalMinutes = trackerSyncIntervalMinutes
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                Definitions =
                {
                    ["fake"] = new AgentDefinition
                    {
                        Kind = "Raw",
                        Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe")
                    }
                }
            }));
        services.AddSingleton<AgentRegistry>();
        foreach (var issueTracker in issueTrackers)
            services.AddSingleton(issueTracker);
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        // No adapters queued — this board has no dispatchable cards; sync is the only path exercised.
        services.AddSingleton<IAgentProtocolAdapterFactory>(new EmptyAdapterFactory());
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        // OrchestratorService depends on AgentSessionLaunchComposer since 1b1b667 (2026-08-26);
        // this harness's copy of that registration was missed at the time.
        services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<OrchestratorService>());
    }

    private static Graph CreateExternalBoard(string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Cadence Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Cadence Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.GitHubIssues,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var column = new BoardColumn
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
        board.Columns.Add(column);

        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = """
                ---
                tracker:
                  kind: github_issues
                  repository: acme/app
                  active_states: [open]
                ---
                Work on {{ issue.identifier }}.
                """,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        });

        return new Graph(project, board);
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-tracker-cadence-{Guid.NewGuid():N}");

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();

        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    private sealed record Graph(Project Project, Board Board);

    private sealed class Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        OrchestratorService Orchestrator) : IAsyncDisposable
    {
        public OrchestratorService Orchestrator { get; } = Orchestrator;

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class CountingIssueTracker(TrackerKind kind, IReadOnlyList<TrackedIssue> issues) : IIssueTracker
    {
        public TrackerKind Kind { get; } = kind;
        public int FetchCandidatesCalls { get; private set; }

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config,
            CancellationToken ct)
        {
            FetchCandidatesCalls++;
            return Task.FromResult(issues);
        }

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config,
            IReadOnlyList<string> states,
            CancellationToken ct) =>
            Task.FromResult(issues);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config,
            IReadOnlyList<string> externalIds,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>(
                issues.Where(i => externalIds.Contains(i.ExternalId, StringComparer.Ordinal)).ToList());
    }

    private sealed class FakeWorktreeManager(string worktreeRoot) : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(worktreeRoot);
            var worktreePath = Path.Combine(worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class EmptyAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("Cadence tests must not dispatch.");
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
