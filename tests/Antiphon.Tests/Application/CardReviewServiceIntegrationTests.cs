using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
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
[NotInParallel("CardReview")]
public class CardReviewServiceIntegrationTests
{
    [Test]
    public async Task CardDiff_returns_worktree_changes_from_real_git_worktree()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var repoPath = Path.Combine(tempRoot, "repo");
            var worktreePath = Path.Combine(tempRoot, "worktree");
            await CreateRepoWithWorktreeChangeAsync(repoPath, worktreePath);
            var graph = NewGraph(tempRoot, repoPath, worktreePath);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.CurrentWorktreeId = graph.Worktree.Id;
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(db, tempRoot, new GitService(NullLogger<GitService>.Instance), new FakeGitHubService());

            var diff = await harness.Service.GetDiffAsync(graph.Card.Id, CancellationToken.None);

            diff.BaseBranch.ShouldBe("main");
            diff.HeadBranch.ShouldBe("feat/card-CARD-0001");
            var readme = diff.Files.Single(file => file.Filename == "README.md");
            readme.Patch.ShouldContain("+changed");
            diff.Files.ShouldContain(file => file.Filename == "new-file.txt"
                && file.Patch.Contains("+untracked", StringComparison.Ordinal));
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task CommentApi_post_routes_to_active_session_stdin()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(db, tempRoot, new MockGitService(), new FakeGitHubService());
            harness.Runtime.Register(session.Id, adapter);

            var result = await harness.Service.PostCommentAsync(
                graph.Card.Id,
                new CardCommentRequest("Please tighten this branch.", "src/App.tsx", 42, "new"),
                CancellationToken.None);

            result.SessionId.ShouldBe(session.Id);
            adapter.SentInput.ShouldContain("[channel]");
            adapter.SentInput.ShouldContain("Review comment for CARD-0001");
            adapter.SentInput.ShouldContain("src/App.tsx new line 42");
            adapter.SentInput.ShouldContain("Please tighten this branch.");
            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "ChannelMessage");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task CommentApi_without_running_session_spawns_agent_with_line_range_prompt()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.CurrentWorktreeId = graph.Worktree.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "COMMENT_ACK" };
            await using var harness = BuildHarness(
                db,
                tempRoot,
                new MockGitService(),
                new FakeGitHubService(),
                adapters: [adapter]);

            var result = await harness.Service.PostCommentAsync(
                graph.Card.Id,
                new CardCommentRequest("Please revise this paragraph.", "README.md", 10, "new", 12),
                CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            result.SessionId.ShouldNotBe(Guid.Empty);
            result.FormattedMessage.ShouldContain("README.md new lines 10-12");
            adapter.Started.ShouldBeTrue();
            adapter.SentPrompt.ShouldContain("Review comment for CARD-0001");
            adapter.SentPrompt.ShouldContain("README.md new lines 10-12");
            adapter.SentPrompt.ShouldContain("Please revise this paragraph.");

            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.BoardColumn)
                .SingleAsync(c => c.Id == graph.Card.Id);
            card.Status.ShouldBe(CardStatus.Review);
            card.BoardColumn.StateKey.ShouldBe("review");
            card.OwnerSessionId.ShouldBe(result.SessionId);
            var attempt = await verify.RunAttempts.SingleAsync(a => a.CardId == graph.Card.Id);
            attempt.Phase.ShouldBe(RunPhase.Succeeded);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task CardPrApi_open_pushes_branch_and_creates_pr()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot, githubEnabled: true);
            graph.Project.BaseBranch = "develop";
            graph.Worktree.BaseRef = "release/1";
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.CurrentWorktreeId = graph.Worktree.Id;
            await db.SaveChangesAsync();
            var attempt = NewAttempt(graph.Card, graph.Worktree);
            db.RunAttempts.Add(attempt);
            await db.SaveChangesAsync();
            var git = new MockGitService();
            var github = new FakeGitHubService { CreatedPullRequestNumber = 42 };
            await using var harness = BuildHarness(db, tempRoot, git, github);

            var result = await harness.Service.OpenPullRequestAsync(graph.Card.Id, CancellationToken.None);

            result.PrNumber.ShouldBe(42);
            result.Owner.ShouldBe("example");
            result.Repo.ShouldBe("antiphon-card-review");
            result.Branch.ShouldBe(graph.Worktree.Branch);
            result.BaseBranch.ShouldBe("release/1");
            git.Operations.ShouldContain(o => o.Method == "CommitAllChanges"
                && o.RepoPath == graph.Worktree.Path
                && o.Detail == "CARD-0001: Review card");
            github.PushedBranches.ShouldContain((graph.Worktree.Path, graph.Worktree.Branch));
            github.CreatedPullRequests.Single().TargetBranch.ShouldBe("release/1");
            github.CreatedPullRequests.Single().Body.ShouldContain("Review card");
            github.CreatedPullRequests.Single().Body.ShouldContain("Phase: Succeeded");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task CardPrApi_returns_existing_pr_without_creating_duplicate()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot, githubEnabled: true);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.CurrentWorktreeId = graph.Worktree.Id;
            await db.SaveChangesAsync();
            var git = new MockGitService();
            var github = new FakeGitHubService
            {
                ExistingPullRequest = new PullRequestInfo(
                    99,
                    "Existing review",
                    "open",
                    "main",
                    "https://github.example/pr/99")
            };
            await using var harness = BuildHarness(db, tempRoot, git, github);

            var result = await harness.Service.OpenPullRequestAsync(graph.Card.Id, CancellationToken.None);

            result.PrNumber.ShouldBe(99);
            result.Created.ShouldBeFalse();
            result.PrUrl.ShouldBe("https://github.example/pr/99");
            github.CreatedPullRequests.ShouldBeEmpty();
            github.PushedBranches.ShouldContain((graph.Worktree.Path, graph.Worktree.Branch));
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task CardPrApi_rejects_when_github_is_disabled_globally()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot, githubEnabled: true);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            graph.Card.CurrentWorktreeId = graph.Worktree.Id;
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(
                db,
                tempRoot,
                new MockGitService(),
                new FakeGitHubService(),
                githubEnabled: false);

            var exception = await Should.ThrowAsync<ConflictException>(
                () => harness.Service.OpenPullRequestAsync(graph.Card.Id, CancellationToken.None));
            exception.Message.ShouldContain("disabled globally");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public void PrDescription_includes_card_title_and_attempt_summary()
    {
        var card = new Card
        {
            Identifier = "CARD-0007",
            Title = "Ship review flow",
            Description = "Wire diff review to PRs."
        };
        var attempt = new RunAttempt
        {
            AttemptNumber = 3,
            Phase = RunPhase.Succeeded,
            StartedAt = new DateTime(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 5, 16, 10, 5, 0, DateTimeKind.Utc),
            ExitCode = 0
        };

        var body = CardReviewService.BuildPullRequestBody(card, attempt);

        body.ShouldContain("CARD-0007: Ship review flow");
        body.ShouldContain("Wire diff review to PRs.");
        body.ShouldContain("Attempt: 3");
        body.ShouldContain("Phase: Succeeded");
        body.ShouldContain("Exit code: 0");
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        AppDbContext db,
        string tempRoot,
        IGitService gitService,
        IGitHubService gitHubService,
        bool githubEnabled = true,
        IReadOnlyList<IAgentProtocolAdapter>? adapters = null)
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
        services.AddSingleton(gitService);
        services.AddSingleton<IGitService>(_ => gitService);
        services.AddSingleton(gitHubService);
        services.AddSingleton<IGitHubService>(_ => gitHubService);
        services.AddSingleton<IOptions<GithubSettings>>(Options.Create(new GithubSettings { Enabled = githubEnabled }));
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
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters ?? []));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardService>();
        services.AddScoped<AgentChannelService>();
        services.AddScoped<CardReviewService>();
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            provider.GetRequiredService<AgentSessionRuntime>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus,
            scope.ServiceProvider.GetRequiredService<CardReviewService>());
    }

    private static Graph NewGraph(
        string tempRoot,
        string? repoPath = null,
        string? worktreePath = null,
        bool githubEnabled = false)
    {
        var now = DateTime.UtcNow;
        repoPath ??= Path.Combine(tempRoot, "repo");
        worktreePath ??= Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(worktreePath);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Review Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://github.com/example/antiphon-card-review.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            GitHubIntegrationEnabled = githubEnabled,
            CreatedAt = now,
            UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Review Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.Internal,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var active = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "in-progress",
            Name = "In Progress",
            ColumnOrder = 0,
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        var review = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "review",
            Name = "Review",
            ColumnOrder = 1,
            CardStatus = CardStatus.Review,
            IsActive = false,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(active);
        board.Columns.Add(review);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = review.Id,
            Identifier = "CARD-0001",
            Title = "Review card",
            Description = "Open a PR for this card.",
            Status = CardStatus.Review,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = review
        };
        board.Cards.Add(card);
        review.Cards.Add(card);
        var worktree = new Worktree
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            RepoPath = repoPath,
            Path = worktreePath,
            Branch = "feat/card-CARD-0001",
            BaseRef = "main",
            Status = WorktreeStatus.Active,
            CreatedAt = now,
            LastTouchedAt = now,
            Card = card
        };
        card.Worktrees.Add(worktree);
        return new Graph(project, board, card, worktree);
    }

    private static AgentSession NewSession(Card card)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            WorktreeId = card.CurrentWorktreeId,
            DefinitionName = "reviewer",
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = card.CurrentWorktree?.Path ?? "D:/worktree",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card
        };
    }

    private static RunAttempt NewAttempt(Card card, Worktree worktree)
    {
        var now = DateTime.UtcNow;
        return new RunAttempt
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            WorktreeId = worktree.Id,
            AttemptNumber = 1,
            Phase = RunPhase.Succeeded,
            Prompt = "review card",
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            CompletedAt = now,
            ExitCode = 0,
            Card = card,
            Worktree = worktree
        };
    }

    private static async Task CreateRepoWithWorktreeChangeAsync(string repoPath, string worktreePath)
    {
        Directory.CreateDirectory(repoPath);
        await RunGitAsync(repoPath, "init");
        await RunGitAsync(repoPath, "config", "user.email", "tests@example.test");
        await RunGitAsync(repoPath, "config", "user.name", "Antiphon Tests");
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "original\n");
        await RunGitAsync(repoPath, "add", "README.md");
        await RunGitAsync(repoPath, "commit", "-m", "Initial commit");
        await RunGitAsync(repoPath, "branch", "-M", "main");
        await RunGitAsync(repoPath, "worktree", "add", "-b", "feat/card-CARD-0001", worktreePath, "main");
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "README.md"), "original\nchanged\n");
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "new-file.txt"), "untracked\n");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {stdout}{stderr}");
    }

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        var sessionIds = await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).Select(s => s.Id).ToListAsync();
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates.SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null));
        await db.RunAttempts.Where(a => cardIds.Contains(a.CardId)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-review-tests-{Guid.NewGuid():N}");

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private sealed record Graph(Project Project, Board Board, Card Card, Worktree Worktree);

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        AgentSessionRuntime Runtime,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus,
        CardReviewService Service) : IAsyncDisposable
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

    private sealed class FakeGitHubService : IGitHubService
    {
        public int CreatedPullRequestNumber { get; set; } = 7;
        public PullRequestInfo? ExistingPullRequest { get; set; }
        public List<(string RepoPath, string Branch)> PushedBranches { get; } = [];
        public List<CreatedPullRequest> CreatedPullRequests { get; } = [];

        public Task<int> CreatePullRequestAsync(string owner, string repo, string sourceBranch, string targetBranch, string title, string body, CancellationToken ct)
        {
            CreatedPullRequests.Add(new CreatedPullRequest(owner, repo, sourceBranch, targetBranch, title, body));
            return Task.FromResult(CreatedPullRequestNumber);
        }

        public Task PushBranchAsync(string repoPath, string branchName, CancellationToken ct)
        {
            PushedBranches.Add((repoPath, branchName));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(string owner, string repo, int prNumber, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PullRequestComment>>([]);

        public Task<PullRequestStatus> GetPullRequestStatusAsync(string owner, string repo, int prNumber, CancellationToken ct) =>
            Task.FromResult(new PullRequestStatus("success", []));

        public Task<PullRequestDetail> GetPullRequestDetailAsync(string owner, string repo, int prNumber, CancellationToken ct) =>
            Task.FromResult(new PullRequestDetail(prNumber, "open", false, "abc", "main", "feature"));

        public Task<IReadOnlyList<GitHubRepoDto>> GetRepositoriesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitHubRepoDto>>([]);

        public Task<IReadOnlyList<GitHubBranchDto>> GetBranchesAsync(string owner, string repo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitHubBranchDto>>([]);

        public Task<PullRequestInfo?> FindPullRequestForBranchAsync(string owner, string repo, string headBranch, CancellationToken ct) =>
            Task.FromResult(ExistingPullRequest);

        public Task<(bool Success, string? Login, string? Error)> CheckConnectivityAsync(CancellationToken ct) =>
            Task.FromResult((true, (string?)"tests", (string?)null));
    }

    private sealed record CreatedPullRequest(
        string Owner,
        string Repo,
        string SourceBranch,
        string TargetBranch,
        string Title,
        string Body);
}
