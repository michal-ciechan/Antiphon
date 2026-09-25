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

/// <summary>
/// CARD-0647. A runner-bound Grok task is refused from the runner's auth probe, which runs before
/// the claim. A present desktop <c>auth.json</c> does not admit it, and an absent desktop store
/// does not refuse a runner that is signed in.
/// </summary>
[Category("Integration")]
public sealed class RunnerGrokCredentialProbeDispatcherTests
{
    private const string Sentinel = "desktop-secret-sentinel";

    [Test]
    public async Task Logged_out_runner_fails_before_a_worktree_even_when_the_desktop_store_is_present()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempWorkspace();
        await File.WriteAllTextAsync(Path.Combine(grokHome.Path, "auth.json"),
            "{\"default\":{\"key\":\"" + Sentinel + "\",\"refresh_token\":\"desktop-refresh-sentinel\"}}");
        var (parentId, taskId) = await SeedAsync(schema, workspace.Path, "server2");
        var probe = new ProbeClient(new RunnerProviderAuthDto("grok", false, null, null, DateTimeOffset.UtcNow, null));

        await CreateDispatcher(schema, probe, enabled: true, grokHome.Path).TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.AuthenticationRequired);
        task.FailureReason.ShouldNotBeNull();
        task.FailureReason.ShouldContain("provider_sign_in_required");
        task.FailureReason.ShouldContain("grok login");
        task.FailureReason.ShouldContain("server2");
        task.FailureReason.ShouldNotContain(Sentinel);
        task.AgentSessionId.ShouldBeNull();
        task.WorktreePath.ShouldBeNull();
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.ProviderSignInRequired
            && i.FailureReason == "grok-home:server2:/state/grok")).ShouldBe(1);
        var notes = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentId).Select(m => m.Body).ToListAsync();
        notes.ShouldContain(n => n.Contains("grok login", StringComparison.Ordinal));
        probe.Providers.ShouldBe(["grok"]);
    }

    [Test]
    public async Task Logged_in_runner_is_not_refused_when_the_desktop_store_is_absent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempWorkspace();
        var (_, taskId) = await SeedAsync(schema, workspace.Path, "server2");
        var probe = new ProbeClient(new RunnerProviderAuthDto("grok", true, "auth_file", null, DateTimeOffset.UtcNow, null));

        await CreateDispatcher(schema, probe, enabled: true, grokHome.Path).TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.FailureCode.ShouldNotBe(AgentTaskFailureCode.AuthenticationRequired);
        (task.FailureReason ?? "").ShouldNotContain("provider_sign_in_required");
        probe.Providers.ShouldBe(["grok"]);
    }

    [Test]
    public async Task A_local_grok_task_still_uses_the_desktop_store()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempWorkspace();
        var (_, taskId) = await SeedAsync(schema, workspace.Path, runnerId: null);
        var probe = new ProbeClient(new RunnerProviderAuthDto("grok", true, "auth_file", null, DateTimeOffset.UtcNow, null));

        await CreateDispatcher(schema, probe, enabled: true, grokHome.Path).TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.AuthenticationRequired);
        task.FailureReason.ShouldNotBeNull().ShouldContain(grokHome.Path);
        probe.Providers.ShouldBeEmpty();
    }

    private static async Task<(Guid ParentId, Guid TaskId)> SeedAsync(
        IsolatedTestSchema schema, string path, string? runnerId)
    {
        var now = DateTime.UtcNow;
        var parentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentId, DefinitionName = "grok", AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running, Cwd = path, Cols = 120, Rows = 30,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "remote Grok", Goal = "reply",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, RunnerId = runnerId, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = parentId,
            CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return (parentId, taskId);
    }

    private static AgentTaskDispatcher CreateDispatcher(
        IsolatedTestSchema schema, ProbeClient probe, bool enabled, string grokHome)
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
            s.DefaultDefinition = "grok";
            s.GrokCredentialProbeEnabled = enabled;
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok", Exe = "grok.exe",
                Env = new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-grok-probe-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x", ChildGrokHome = "/state/grok",
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
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-probe-ws").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class ProbeDirectory(ProbeClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "server2"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? null : Guid.NewGuid();
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
        public List<string> Providers { get; } = [];
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct)
        {
            Providers.Add(provider);
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
