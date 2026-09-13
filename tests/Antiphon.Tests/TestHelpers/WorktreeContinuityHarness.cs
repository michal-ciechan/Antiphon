using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class WorktreeContinuityHarness : IAsyncDisposable
{
    private WorktreeContinuityHarness(
        ScratchGitRepo repo, IsolatedTestSchema schema, ServiceProvider services, Card card, Project project)
    {
        Repo = repo;
        Schema = schema;
        Services = services;
        Card = card;
        Project = project;
    }

    public ScratchGitRepo Repo { get; }
    public IsolatedTestSchema Schema { get; }
    public ServiceProvider Services { get; }
    public Card Card { get; }
    public Project Project { get; }
    public RecordingFailingWorktreeManager? FailingWorktrees { get; init; }

    public static async Task<WorktreeContinuityHarness> CreateAsync(
        GitSettings? gitSettings = null,
        string prefix = "c442",
        TimeProvider? clock = null,
        GitProcessGate? gate = null,
        bool failWorktreeCreate = false,
        DelegationSettings? delegation = null)
    {
        var repo = new ScratchGitRepo(prefix);
        await repo.CommitFileAsync("README.md", "base\n");
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c442-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c442.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c442",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0442", Title = "continuity", Description = "c442",
            CreatedAt = now, UpdatedAt = now,
        };
        seed.AddRange(project, board, column, card);
        await seed.SaveChangesAsync();

        var settings = gitSettings ?? new GitSettings { WorktreeAddTimeoutSeconds = 180 };
        settings.WorktreeBasePath = repo.WorktreeRoot;
        if (settings.WorktreeAddTimeoutSeconds <= 0)
            settings.WorktreeAddTimeoutSeconds = 180;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(delegation ?? new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        if (gate is not null)
            services.AddSingleton(gate);
        services.AddDelegationWorktreeGraph(settings);
        if (failWorktreeCreate)
        {
            services.Replace(ServiceDescriptor.Singleton<IWorktreeManager>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<WorktreeManager>(sp);
                return new RecordingFailingWorktreeManager(inner);
            }));
        }

        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddSingleton<AreaMapLoader>();
        services.AddScoped<AgentTaskPipelineStatusService>();
        services.AddScoped<SubscriptionUsageReader>();
        services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
        services.AddScoped<SubscriptionQuotaGate>();
        services.AddScoped<DelegationOpenGate>();
        services.AddScoped<RoutingPinService>();
        var provider = services.BuildServiceProvider();
        return new WorktreeContinuityHarness(repo, schema, provider, card, project)
        {
            FailingWorktrees = failWorktreeCreate
                ? provider.GetRequiredService<IWorktreeManager>() as RecordingFailingWorktreeManager
                : null,
        };
    }

    public AppDbContext CreateDb() =>
        new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public AgentTaskService.Caller Caller(AgentTask? parent = null, string? directory = null) =>
        new(parent, null, directory ?? Repo.Path);

    public async Task TickAsync(CancellationToken ct = default)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
    }

    public AgentTaskService Tasks() => Services.GetRequiredService<AgentTaskService>();

    public async Task<AgentTaskCreatedDto> CreateNextAsync(
        AgentTaskWorktreeBaseMode mode = AgentTaskWorktreeBaseMode.Auto,
        Guid? baseTask = null,
        string? mergeTarget = null,
        AgentTaskRole role = AgentTaskRole.Code,
        AgentTask? parent = null,
        string? card = "CARD-0442",
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var request = new CreateAgentTaskRequest(
            "next",
            Role: role,
            Workspace: WorkspaceMode.Worktree,
            Card: card,
            MergeTargetRef: mergeTarget,
            WorkingDirectory: workingDirectory)
        {
            FreshWorktree = mode == AgentTaskWorktreeBaseMode.Target,
            WorktreeBaseTask = mode == AgentTaskWorktreeBaseMode.Task
                ? baseTask?.ToString("D")
                : null,
        };
        return await Tasks().CreateAsync(request, Caller(parent), ct);
    }

    public async Task<AgentTask> SeedSucceededAsync(
        string commitMessage, string markerFile, string marker, AgentTaskRole role = AgentTaskRole.Code,
        Guid? cardId = null, DateTime? completedAt = null, AgentTaskStatus status = AgentTaskStatus.Succeeded,
        string? fromBranch = null, string? mergeTarget = null, string? repoPath = null,
        Guid? followUpOf = null, string? worktreePath = null)
    {
        await using var db = CreateDb();
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        var gitPath = repoPath ?? Repo.Path;
        if (!string.IsNullOrEmpty(fromBranch))
            await ScratchGitRepo.GitInAsync(gitPath, "checkout", fromBranch);
        else
            await ScratchGitRepo.GitInAsync(gitPath, "checkout", "master");
        (await ScratchGitRepo.GitInAsync(gitPath, "checkout", "-b", branch)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(gitPath, markerFile), marker);
        (await ScratchGitRepo.GitInAsync(gitPath, "add", markerFile)).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(gitPath, "commit", "-m", commitMessage)).Ok.ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(gitPath, "rev-parse", "HEAD")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(gitPath, "checkout", "master")).Ok.ShouldBeTrue();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = commitMessage, Goal = commitMessage,
            Role = role, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = gitPath, RepoPath = gitPath,
            CardId = cardId ?? Card.Id, WorktreeBranch = branch, WorktreeBaseSha = sha,
            WorktreePath = worktreePath, MergeTargetRef = mergeTarget,
            FollowUpOfTaskId = followUpOf,
            Status = status, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = completedAt ?? DateTime.UtcNow.AddHours(-1),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task<(AgentTask A, AgentTask X)> SeedTwoTipsAsync()
    {
        var a = await SeedSucceededAsync("A", "code-a.txt", "A\n", completedAt: DateTime.UtcNow.AddMinutes(-1));
        var x = await SeedSucceededAsync("X", "x.txt", "X\n", completedAt: DateTime.UtcNow.AddMinutes(-5));
        return (a, x);
    }

    public async Task<(AgentTask A, AgentTask B, AgentTask X, AgentTask Y)> SeedFourTipsAsync()
    {
        var a = await SeedSucceededAsync("A", "code-a.txt", "A\n", completedAt: DateTime.UtcNow);
        var b = await SeedSucceededAsync("B", "code-b.txt", "B\n", fromBranch: a.WorktreeBranch,
            completedAt: DateTime.UtcNow.AddHours(-3));
        var x = await SeedSucceededAsync("X", "x.txt", "X\n", completedAt: DateTime.UtcNow.AddHours(-2));
        var y = await SeedSucceededAsync("Y", "y.txt", "Y\n", fromBranch: x.WorktreeBranch,
            completedAt: DateTime.UtcNow.AddHours(-1));
        return (a, b, x, y);
    }

    public async Task<AgentTask> SeedMergeRangeAsync()
    {
        await Repo.GitAsync("checkout", "-b", "left");
        await File.WriteAllTextAsync(Path.Combine(Repo.Path, "left.txt"), "L\n");
        await Repo.GitAsync("add", "left.txt");
        await Repo.GitAsync("commit", "-m", "L");
        await Repo.GitAsync("checkout", "master");
        await Repo.GitAsync("checkout", "-b", "right");
        await File.WriteAllTextAsync(Path.Combine(Repo.Path, "right.txt"), "R\n");
        await Repo.GitAsync("add", "right.txt");
        await Repo.GitAsync("commit", "-m", "R");
        await Repo.GitAsync("checkout", "left");
        await Repo.GitAsync("merge", "--no-ff", "right", "-m", "merge");
        var sha = (await Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await Repo.GitAsync("checkout", "master");
        await File.WriteAllTextAsync(Path.Combine(Repo.Path, "unrelated.txt"), "U\n");
        await Repo.GitAsync("add", "unrelated.txt");
        await Repo.GitAsync("commit", "-m", "unrelated");
        await Repo.GitAsync("cherry-pick", sha + "^1");
        await Repo.GitAsync("cherry-pick", sha + "^2");
        await using var db = CreateDb();
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "merge", Goal = "merge",
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = Repo.Path, RepoPath = Repo.Path,
            CardId = Card.Id, WorktreeBranch = "left", WorktreeBaseSha = sha,
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-2), CompletedAt = DateTime.UtcNow.AddHours(-1),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task SeedLandedAsync(Guid taskId, AgentTaskEventType type = AgentTaskEventType.Landed)
    {
        await using var db = CreateDb();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, Type = type, Detail = type.ToString(), At = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task SetLandRequestedAsync(Guid taskId, DateTime? at = null)
    {
        await using var db = CreateDb();
        var live = await db.AgentTasks.FindAsync(taskId);
        live!.LandRequestedAt = at ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<AgentTask> ReloadAsync(Guid id)
    {
        await using var db = CreateDb();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public async Task AssertNoLaunchAsync(Guid taskId)
    {
        await using var db = CreateDb();
        var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        row.WorktreePath.ShouldBeNull();
        row.AgentSessionId.ShouldBeNull();
        row.Status.ShouldNotBe(AgentTaskStatus.Dispatched);
        row.Status.ShouldNotBe(AgentTaskStatus.Working);
        var expected = Path.Combine(Repo.WorktreeRoot, $"card-task-{DelegationReportFormatter.Short(taskId)}");
        Directory.Exists(expected).ShouldBeFalse();
        (await db.AgentSessions.CountAsync(s => s.Id == row.AgentSessionId)).ShouldBe(0);
    }

    public AgentTask NewQueued(AgentTaskWorktreeBaseMode mode = AgentTaskWorktreeBaseMode.Auto, Guid? requested = null)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id, RootTaskId = id, Title = "next", Goal = "continue",
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = Repo.Path, RepoPath = Repo.Path,
            CardId = Card.Id, Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow, WorktreeBaseMode = mode, RequestedWorktreeBaseTaskId = requested,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Schema.DisposeAsync();
        Repo.Dispose();
    }
}

internal sealed class RecordingFailingWorktreeManager : IWorktreeManager
{
    private readonly IWorktreeManager _inner;

    public RecordingFailingWorktreeManager(IWorktreeManager inner) => _inner = inner;

    public List<string> BaseRefs { get; } = [];
    public int CreateCalls { get; private set; }

    public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
    {
        CreateCalls++;
        BaseRefs.Add(baseRef);
        throw new InvalidOperationException("injected worktree add failure");
    }

    public Task<WorktreeInfo> CreateAsync(
        string repoPath, string cardId, string baseRef, RepositoryLease lease, CancellationToken ct) =>
        CreateAsync(repoPath, cardId, baseRef, ct);

    public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
        _inner.ListAsync(repoPath, ct);

    public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
        _inner.RemoveAsync(repoPath, worktreePath, ct);

    public Task TouchAsync(string worktreePath, CancellationToken ct) =>
        _inner.TouchAsync(worktreePath, ct);

    public Task<int> PruneStaleAsync(CancellationToken ct) =>
        _inner.PruneStaleAsync(ct);
}
