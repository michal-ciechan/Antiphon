using System.Text.Json;
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
/// CARD-0640. A boot-wedge relaunch must finish its launch spec the same way the first dispatch
/// does: a SourceLanding task carries a verification binding for the new session, and the Windows
/// Grok argv guard runs on that spec. On Linux the guard is inert (it returns before inspecting
/// argv); the binding is the observable half of that shared finish.
/// </summary>
[Category("Integration")]
public sealed class BootWedgeRelaunchParityTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Sourced_grok_relaunch_reserves_the_new_sessions_binding()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sink = new RecordingLaunchSink();
        var seeded = await SeedAsync(schema, workspace.Path, unresolvedCustody: false);
        await using var provider = BuildHarness(schema.ConnectionString, sink);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.RelaunchWedgedAsync(seeded.TaskId, seeded.OldSessionId, CancellationToken.None);

        var spec = sink.Specs.ShouldHaveSingleItem();
        spec.Kind.ShouldBe(AgentKind.Grok);
        // The desktop spec resolves "grok.exe" on Windows and "grok" elsewhere.
        Path.GetFileNameWithoutExtension(spec.Exe).ShouldBe("grok", StringCompareShould.IgnoreCase);
        spec.Args.ShouldContain(GrokLaunchArgs.ReasoningEffortFlag);
        spec.Args.ShouldContain("xhigh");
        spec.VerificationBinding.ShouldNotBeNull();
        spec.VerificationBinding.Source.TaskId.ShouldBe(seeded.TaskId);
        spec.VerificationBinding.Source.SourceOperationId.ShouldBe(seeded.OperationId);
        spec.VerificationBinding.Source.LandedSha.ShouldBe(seeded.Sha);
        spec.VerificationBinding.Generation.SessionId.ShouldNotBe(seeded.OldSessionId);
        GrokRulesArgvPolicy.ValidateArgv(spec.Args, isWindows: true, isGrok: true).ShouldBeNull();

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.AgentSessionId.ShouldBe(spec.VerificationBinding.Generation.SessionId);
        task.BootWedgeRelaunchCount.ShouldBe(1);
        var execution = await db.VerificationExecutions.SingleAsync(e => e.TaskId == seeded.TaskId);
        execution.SessionId.ShouldBe(spec.VerificationBinding.Generation.SessionId);
        execution.Id.ShouldBe(spec.VerificationBinding.ExecutionId);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Sourced_grok_relaunch_does_not_launch_unbound_when_custody_is_unresolved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sink = new RecordingLaunchSink();
        var seeded = await SeedAsync(schema, workspace.Path, unresolvedCustody: true);
        await using var provider = BuildHarness(schema.ConnectionString, sink);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.RelaunchWedgedAsync(seeded.TaskId, seeded.OldSessionId, CancellationToken.None);

        sink.Specs.ShouldBeEmpty();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
        task.AgentSessionId.ShouldBe(seeded.OldSessionId);
        task.BootWedgeRelaunchCount.ShouldBe(0);
        var events = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == seeded.TaskId)
            .Select(e => e.Detail)
            .ToListAsync();
        events.ShouldHaveSingleItem().ShouldStartWith("boot-wedge relaunch refused: ");
    }

    private static async Task<Seed> SeedAsync(IsolatedTestSchema schema, string workspace, bool unresolvedCustody)
    {
        var now = DateTime.UtcNow;
        var taskId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var sha = new string('a', 40);
        var creation = new VerificationCreationCoordinates(
            workspace, workspace, workspace, workspace, "feat/card-task-relaunch", Guid.NewGuid());
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourceId,
            RootTaskId = sourceId,
            Title = "landed source",
            Goal = "published",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = workspace,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Running,
            Cwd = workspace,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "relaunch-grok",
            Slug = "relaunch-grok",
            WorkingDirectory = workspace,
            Details = "CARD-0640 relaunch parity.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Frontier,
            Kind = AgentKind.Grok,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            SessionBackend = SessionBackend.PtyHost,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = operationId,
            TaskId = sourceId,
            CreatedAt = now,
            UpdatedAt = now,
            RepositoryPath = workspace,
            CommonDirectory = workspace,
            WorktreePath = workspace,
            GitDirectory = workspace,
            SourceFullRef = "refs/heads/feat/card-task-source",
            OriginalSourceSha = sha,
            VerifiedSourceSha = sha,
            TargetFullRef = "refs/heads/master",
            TargetBeforeSha = sha,
        });
        await db.SaveChangesAsync();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "sourced grok",
            Goal = "relaunch with the dispatch spec",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = workspace,
            WorktreePath = workspace,
            Status = AgentTaskStatus.Dispatched,
            AgentId = agentId,
            AgentSessionId = sessionId,
            SourceLandingOperationId = operationId,
            SourceLandingSha = sha,
            VerificationCustodyContractVersion = 1,
            VerificationCreationJson = JsonSerializer.Serialize(creation),
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now,
            DispatchedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        });
        if (unresolvedCustody)
        {
            db.VerificationExecutions.Add(new VerificationExecution
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                SourceLandingOperationId = operationId,
                SessionId = sessionId,
                AcceptedStartedAt = now,
                BindingJson = "{}",
                CreatedAt = now,
                CustodyReason = VerificationCustodyState.Starting.ToString(),
            });
        }

        await db.SaveChangesAsync();
        return new Seed(taskId, operationId, sessionId, sha);
    }

    private static ServiceProvider BuildHarness(string connectionString, RecordingLaunchSink sink)
    {
        var storeId = Guid.NewGuid();
        var runner = new FakeSessionRunnerClient { VerificationStoreId = storeId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, n =>
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
            MaxConcurrentTasks = 512,
            AllowedRoots = ["C:\\", "/"],
            BootWedgeRelaunchLimit = 1,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "grok";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = "grok",
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c640-wt"),
        });
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<ISessionRunnerDirectory>(new SingleRunnerDirectory(runner));
        services.AddScoped<SourceLandingAdmission>();
        services.AddScoped<VerificationExecutionService>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed record Seed(Guid TaskId, Guid OperationId, Guid OldSessionId, string Sha);

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c640-relaunch").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
