using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class WorktreeContinuityHarness : IAsyncDisposable
{
    private WorktreeContinuityHarness(ScratchGitRepo repo, IsolatedTestSchema schema, ServiceProvider services, Card card)
    {
        Repo = repo;
        Schema = schema;
        Services = services;
        Card = card;
    }

    public ScratchGitRepo Repo { get; }
    public IsolatedTestSchema Schema { get; }
    public ServiceProvider Services { get; }
    public Card Card { get; }

    public static async Task<WorktreeContinuityHarness> CreateAsync(
        GitSettings? gitSettings = null,
        string prefix = "c442")
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

        var settings = gitSettings ?? new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot,
            WorktreeAddTimeoutSeconds = 180,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
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
        services.AddDelegationWorktreeGraph(settings);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return new WorktreeContinuityHarness(repo, schema, services.BuildServiceProvider(), card);
    }

    public AppDbContext CreateDb() =>
        new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public AgentTaskService.Caller Caller() => new(null, null, Repo.Path);

    public async Task<AgentTask> SeedSucceededAsync(
        string commitMessage, string markerFile, string marker, AgentTaskRole role = AgentTaskRole.Code,
        Guid? cardId = null, DateTime? completedAt = null, AgentTaskStatus status = AgentTaskStatus.Succeeded,
        string? fromBranch = null)
    {
        await using var db = CreateDb();
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        if (!string.IsNullOrEmpty(fromBranch))
            await Repo.GitAsync("checkout", fromBranch);
        await Repo.GitAsync("checkout", "-b", branch);
        await File.WriteAllTextAsync(Path.Combine(Repo.Path, markerFile), marker);
        await Repo.GitAsync("add", markerFile);
        await Repo.GitAsync("commit", "-m", commitMessage);
        var sha = (await Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await Repo.GitAsync("checkout", "master");
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = commitMessage, Goal = commitMessage,
            Role = role, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = Repo.Path, RepoPath = Repo.Path,
            CardId = cardId ?? Card.Id, WorktreeBranch = branch, WorktreeBaseSha = sha,
            Status = status, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = completedAt ?? DateTime.UtcNow.AddHours(-1),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
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
