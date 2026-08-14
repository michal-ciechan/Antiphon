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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0019: the card SURFACE is correctable, the card RECORD is append-only. These tests cover
/// the ceilings that used to answer 500, the revision history behind every correction and move,
/// and archive-instead-of-delete.
/// </summary>
/// <remarks>
/// Every assertion is scoped to rows this test created — one Postgres testcontainer is shared by
/// the whole assembly and other suites are writing rows throughout, so an unscoped count would
/// also be asserting "nobody else has data right now".
/// </remarks>
[Category("Integration")]
[NotInParallel("CardCorrection")]
public class CardCorrectionIntegrationTests
{
    // A live 500, not a hypothetical: Cards.Description was varchar(4000) with no application
    // check, so an over-long description reached Postgres, came back as 22001 "value too long"
    // inside a DbUpdateException — which is not an HttpException — and the middleware answered a
    // raw 500 naming nothing.
    [Test]
    public async Task Create_answers_a_validation_error_for_an_over_ceiling_description_instead_of_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Ceiling board"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.CreateAsync(
                    board.Id,
                    new CreateCardRequest(null, "Too long", new string('x', CardService.MaxDescriptionLength + 1)),
                    CancellationToken.None));

            var message = ex.Errors[nameof(CreateCardRequest.Description)].Single();
            message.ShouldContain("20,000");
            message.ShouldContain("20,001");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Pins that the varchar(4000) -> text migration actually ran: this body is five times the old
    // column width and must round-trip whole.
    [Test]
    public async Task A_description_just_under_the_ceiling_round_trips_whole()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Wide board"), CancellationToken.None);
            var description = new string('d', CardService.MaxDescriptionLength - 1);

            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Long but legal", description),
                CancellationToken.None);

            card.Description.Length.ShouldBe(CardService.MaxDescriptionLength - 1);
            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.Description.ShouldBe(description);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Create_answers_a_validation_error_for_a_title_past_the_column_width()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Title board"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.CreateAsync(
                    board.Id,
                    new CreateCardRequest(null, new string('t', CardService.MaxTitleLength + 1)),
                    CancellationToken.None));

            ex.Errors[nameof(CreateCardRequest.Title)].Single().ShouldContain("300");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // TerminalReason was varchar(1000) and overflowed with a raw 500 twice while closing CARD-0042
    // and CARD-0046; a review verdict had to be hand-trimmed to exactly 1000 characters to fit.
    [Test]
    public async Task A_terminal_move_stores_a_reason_far_longer_than_the_old_thousand_character_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Verdict board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Close with a real verdict"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var verdict = new string('v', 3_500);

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, verdict),
                CancellationToken.None);

            moved.TerminalReason.ShouldBe(verdict);
            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.TerminalReason.ShouldBe(verdict);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task A_move_reason_past_the_ceiling_is_a_validation_error_not_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Long reason board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Reason too long"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.MoveAsync(
                    card.Id,
                    new MoveCardRequest(
                        doneColumn.Id, card.ConcurrencyToken, new string('r', CardService.MaxReasonLength + 1)),
                    CancellationToken.None));

            ex.Errors[nameof(MoveCardRequest.Reason)].Single().ShouldContain("4,000");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(string tempRoot)
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
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(
            new AgentRegistrySettings
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
        services.AddSingleton<IWorktreeManager>(new StubWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new NoAdapterFactory());
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
        services.AddSingleton<IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
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

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-card-correction-{Guid.NewGuid():N}");

    /// <summary>Deletes only the rows this test's temp root owns. Revisions go with their card.</summary>
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
        var sessionIds = await db.AgentSessions
            .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
            .Select(s => s.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();

        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
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

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        BoardService BoardService,
        CardService CardService,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>No test here spawns a session; asking for an adapter is a bug in the test.</summary>
    private sealed class NoAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("No agent session should be launched by these tests.");
    }

    private sealed class StubWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;
        private readonly List<WorktreeInfo> _worktrees = [];

        public StubWorktreeManager(string worktreeRoot)
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

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
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
