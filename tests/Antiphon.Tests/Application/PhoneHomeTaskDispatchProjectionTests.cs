using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PhoneHomeTaskDispatchProjectionTests
{
    [Test]
    public Task Runner_bound_claude_task_reaches_the_queue_projected() =>
        VerifyProjectionAsync(AgentKind.ClaudeCode);

    [Test]
    public Task Runner_bound_grok_task_reaches_the_queue_projected() =>
        VerifyProjectionAsync(AgentKind.Grok);

    private static async Task VerifyProjectionAsync(AgentKind kind)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        const string mirror = "/work/worktrees/task-remote";
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse(mirror)),
            (PhoneHomeOperation)16 => Result(frame,
                new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, kind);
        var sink = new RecordingLaunchSink();
        var dispatcher = CreateDispatcher(schema, host, sink);

        await dispatcher.TickAsync(CancellationToken.None);

        var launch = sink.Specs.Single();
        launch.Cwd.ShouldBe(mirror);
        launch.Exe.ShouldBe(kind == AgentKind.Grok ? "grok" : "claude");
        launch.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        launch.Env["GROK_HOME"].ShouldBe("/state/grok");
        launch.Env["CLAUDE_CONFIG_DIR"].ShouldBe("/state/claude");
        if (kind == AgentKind.ClaudeCode)
        {
            launch.Env["DISABLE_AUTOUPDATER"].ShouldBe("1");
            launch.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("1");
        }
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.AgentSessionId.ShouldNotBeNull();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerCwd.ShouldBe(mirror);
        session.Cwd.ShouldBe(workspace.Path);
    }

    private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private static async Task<Guid> SeedAsync(
        IsolatedTestSchema schema, string path, string runnerId, AgentKind kind)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "remote task", Goal = "reply",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, WorktreePath = path, WorktreeBranch = "feat/test-remote",
            RunnerId = runnerId, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AgentTaskDispatcher CreateDispatcher(
        IsolatedTestSchema schema, PhoneHomeTestHost host, RecordingLaunchSink sink)
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
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok", Exe = "grok.exe", ArgsTemplate = ["--always-approve", "--no-alt-screen"],
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-remote-dispatch-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = host.AllowedRunnerId, AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x", ClaudeAuthProbeEnabled = true,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
        services.AddSingleton<ILandingGit, PushGit>();
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-remote-dispatch-ws").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class PushGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                "push" => new LandingGitResult(0, "", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }
}
