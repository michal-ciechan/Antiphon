using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Private migrated database and separate server/runner paths for caller-input tests.</summary>
internal sealed class TaskInputSpillFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private readonly ServiceProvider _provider;
    public string ServerRoot { get; }
    public string RunnerParent { get; }
    public string RunnerRoot { get; }
    public Guid SessionId { get; }
    public Guid TaskId { get; }
    public AgentTaskReplyService Replies => _provider.GetRequiredService<AgentTaskReplyService>();
    public SessionMessageQueueService Queue => _provider.GetRequiredService<SessionMessageQueueService>();
    public RemoteSpillCourier Courier => _provider.GetRequiredService<RemoteSpillCourier>();
    public string ConnectionString => _schema.ConnectionString;

    private TaskInputSpillFixture(IsolatedTestSchema schema, ServiceProvider provider,
        string serverRoot, string runnerParent, string runnerRoot, Guid sessionId, Guid taskId)
    {
        _schema = schema;
        _provider = provider;
        ServerRoot = serverRoot;
        RunnerParent = runnerParent;
        RunnerRoot = runnerRoot;
        SessionId = sessionId;
        TaskId = taskId;
    }

    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    public static async Task<TaskInputSpillFixture> CreateAsync(
        AgentKind kind = AgentKind.Codex, bool remote = true,
        AgentTaskStatus status = AgentTaskStatus.Working)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var serverRoot = Directory.CreateTempSubdirectory("c888-server-").FullName;
        var runnerParent = Directory.CreateTempSubdirectory("c888-runner-").FullName;
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var runnerRoot = Path.Combine(runnerParent, "worktrees", $"task-{taskId.ToString("N")[..8]}");
        Directory.CreateDirectory(runnerRoot);
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "fake", AgentKind = kind,
                Status = SessionStatus.Running, Cwd = serverRoot,
                RunnerId = remote ? "server2" : null,
                RunnerStoreId = remote ? Guid.NewGuid() : null,
                RunnerCwd = remote ? runnerRoot : null,
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId,
                AgentSessionId = status == AgentTaskStatus.Queued ? null : sessionId,
                Title = "task input fixture", Goal = "Do the work.",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
                AgentKind = kind, ModelLevel = AgentModelLevel.High,
                Workspace = remote ? WorkspaceMode.Worktree : WorkspaceMode.Shared,
                WorkingDirectory = serverRoot, Status = status,
                CreatedAt = now, DispatchedAt = status == AgentTaskStatus.Queued ? null : now,
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RemoteSpillCourier>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskInputService>();
        var provider = services.BuildServiceProvider();
        return new TaskInputSpillFixture(schema, provider, serverRoot, runnerParent,
            runnerRoot, sessionId, taskId);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _schema.DisposeAsync();
        Directory.Delete(ServerRoot, recursive: true);
        Directory.Delete(RunnerParent, recursive: true);
    }
}
