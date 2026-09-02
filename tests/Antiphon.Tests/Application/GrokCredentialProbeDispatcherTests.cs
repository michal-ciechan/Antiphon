using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0324 S3: registry-path Grok with an Absent store fails before
/// <c>EnqueueInteractiveSession</c>. A profile-path / API-key launch is not probed.
/// </summary>
[Category("Integration")]
public class GrokCredentialProbeDispatcherTests
{
    [Test]
    public async Task Registry_Grok_with_an_Absent_store_fails_before_spawn()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempHome();
        var (parentSessionId, taskId) = await SeedQueuedGrokAsync(
            schema, workspace.Path, grokHome.Path);

        var dispatcher = CreateDispatcher(schema, grokHome.Path, probeEnabled: true);
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.AuthenticationRequired);
        task.FailureReason.ShouldContain("grok login");
        task.AgentSessionId.ShouldBeNull("EnqueueInteractiveSession must not have run");
        task.WorktreePath.ShouldBeNull("the probe runs before a worktree is cut");

        (await verify.AgentIncidents.CountAsync(i =>
            i.Kind == AgentIncidentKind.ProviderSignInRequired
            && i.FailureReason == GrokSignInIncident.EpisodeKey(grokHome.Path)))
            .ShouldBe(1);

        var notes = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentSessionId)
            .Select(m => m.Body)
            .ToListAsync();
        notes.ShouldContain(b => b.Contains("grok login"));
    }

    [Test]
    public async Task Api_key_auth_is_not_probed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempHome();
        var (_, taskId) = await SeedQueuedGrokAsync(
            schema, workspace.Path, grokHome.Path,
            launchEnvOverrideJson: """{"XAI_API_KEY":"xai-test"}""");

        var dispatcher = CreateDispatcher(schema, grokHome.Path, probeEnabled: true);
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.AgentSessionId.ShouldNotBeNull();
        task.FailureCode.ShouldBeNull();
    }

    [Test]
    public async Task Probe_disabled_does_not_fail_an_Absent_store()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var grokHome = new TempHome();
        var (_, taskId) = await SeedQueuedGrokAsync(schema, workspace.Path, grokHome.Path);

        var dispatcher = CreateDispatcher(schema, grokHome.Path, probeEnabled: false);
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.AgentSessionId.ShouldNotBeNull();
        task.FailureCode.ShouldBeNull();
    }

    private static async Task<(Guid ParentSessionId, Guid TaskId)> SeedQueuedGrokAsync(
        IsolatedTestSchema schema,
        string directory,
        string grokHome,
        string? launchEnvOverrideJson = null)
    {
        var now = DateTime.UtcNow;
        var parentSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "grok pool worker",
            Goal = "do the grok work",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = parentSessionId,
            LaunchEnvOverrideJson = launchEnvOverrideJson,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return (parentSessionId, taskId);
    }

    private static AgentTaskDispatcher CreateDispatcher(
        IsolatedTestSchema schema, string grokHome, bool probeEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            AllowedRoots = ["C:\\", "/"],
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = probeEnabled;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = "grok-not-launched-by-this-test.exe",
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
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
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-probe-ws").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class TempHome : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-probe-home").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
