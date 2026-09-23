using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ClaudeCredentialProbeDispatcherTests
{
    [Test]
    public async Task Runner_bound_claude_with_a_logged_out_runner_fails_before_any_worktree()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (parentId, taskId) = await SeedAsync(schema, workspace.Path);
        var probe = new ProbeClient(new RunnerProviderAuthDto("claude", false, "none", null,
            DateTimeOffset.UtcNow, null));

        await CreateDispatcher(schema, probe, enabled: true).TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.AuthenticationRequired);
        task.FailureReason.ShouldContain("claude auth login");
        task.AgentSessionId.ShouldBeNull();
        task.WorktreePath.ShouldBeNull();
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.ProviderSignInRequired
            && i.FailureReason == "claude-home:server2:/state/claude")).ShouldBe(1);
        var notes = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentId).Select(m => m.Body).ToListAsync();
        notes.ShouldContain(n => n.Contains("claude auth login", StringComparison.Ordinal));
        probe.Requests.ShouldBe(1);
    }

    [Test]
    public async Task Probe_disabled_does_not_fail()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (_, taskId) = await SeedAsync(schema, workspace.Path);
        var probe = new ProbeClient(new RunnerProviderAuthDto("claude", false, "none", null,
            DateTimeOffset.UtcNow, null));

        await CreateDispatcher(schema, probe, enabled: false).TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.FailureCode.ShouldBeNull();
        probe.Requests.ShouldBe(0);
    }

    private static async Task<(Guid ParentId, Guid TaskId)> SeedAsync(IsolatedTestSchema schema, string path)
    {
        var now = DateTime.UtcNow;
        var parentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentId, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, Cwd = path, Cols = 120, Rows = 30,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "remote Claude", Goal = "reply",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, RunnerId = "server2", Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = parentId,
            CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return (parentId, taskId);
    }

    private static AgentTaskDispatcher CreateDispatcher(IsolatedTestSchema schema, ProbeClient probe, bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, n =>
        {
            n.MigrationsAssembly("Antiphon.Server");
            n.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512, AllowedRoots = ["C:\\", "/"],
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-claude-probe-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x", ClaudeAuthProbeEnabled = enabled,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(new ProbeDirectory(probe));
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-claude-probe-ws").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class ProbeDirectory(ProbeClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "server2"];
        public Guid? LiveStoreId => Guid.NewGuid();
        public ISessionRunnerClient Resolve(string? runnerId) => client;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("test"));
    }

    private sealed class ProbeClient(RunnerProviderAuthDto? answer) : ISessionRunnerClient
    {
        public int Requests { get; private set; }
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct)
        {
            provider.ShouldBe("claude");
            Requests++;
            return Task.FromResult(answer);
        }
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
