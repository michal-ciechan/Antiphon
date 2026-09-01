using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0305 S2: a DATED pin holds its work at the dispatcher, not at create. Its own counter and
/// its own Held sentence, kept apart from CARD-0022's model-hold skip on purpose — a task waiting
/// on a date and a task waiting on a cap are two different problems for the operator.
///
/// <para>Isolated schema per test: the stage-wide pin index is unique on role alone.</para>
/// </summary>
[Category("Integration")]
public class RoutingPinDispatcherTests
{
    [Test]
    public async Task A_pin_that_is_not_due_yet_holds_its_task_and_dispatches_the_others()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(schema);
        var (heldAgent, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var (freeAgent, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var card = await SeedCardAsync(schema, "CARD-0301");
        await SeedPinAsync(schema, card.Id, AgentTaskRole.Plan, DateTime.UtcNow.AddHours(6));
        var held = await SeedQueuedTaskAsync(
            schema, workspace.Path, heldAgent, AgentTaskRole.Plan, card.Id, "dated plan");
        var free = await SeedQueuedTaskAsync(
            schema, workspace.Path, freeAgent, AgentTaskRole.Docs, null, "undated docs");

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedRoutingPin.ShouldBe(1);
        result.SkippedModelAvailability.ShouldBe(0, "a date is not a model hold");
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == held.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued);
        (await verify.AgentTasks.SingleAsync(t => t.Id == free.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
        var events = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync();
        events.ShouldContain(e => e.Detail.Contains("routing pin not before"));
    }

    [Test]
    public async Task Once_the_date_has_passed_the_same_task_dispatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(schema);
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var card = await SeedCardAsync(schema, "CARD-0301");
        await SeedPinAsync(schema, card.Id, AgentTaskRole.Plan, DateTime.UtcNow.AddSeconds(-2));
        var task = await SeedQueuedTaskAsync(
            schema, workspace.Path, agentId, AgentTaskRole.Plan, card.Id, "due plan");

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedRoutingPin.ShouldBe(0);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task A_stage_wide_dated_pin_holds_every_task_in_that_stage()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(schema);
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        await SeedPinAsync(schema, null, AgentTaskRole.Plan, DateTime.UtcNow.AddHours(3));
        var task = await SeedQueuedTaskAsync(
            schema, workspace.Path, agentId, AgentTaskRole.Plan, null, "cardless plan");

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedRoutingPin.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued);
    }

    private static AgentTaskDispatcher CreateDispatcher(IsolatedTestSchema schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
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
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-routing-pin-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    private static async Task SeedPinAsync(
        IsolatedTestSchema schema, Guid? cardId, AgentTaskRole role, DateTime notBefore)
    {
        await using var db = CreateContext(schema);
        db.RoutingPins.Add(new RoutingPin
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            Role = role,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            NotBefore = notBefore,
            Reason = "operator: plan on fable after the weekly cap",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Card> SeedCardAsync(IsolatedTestSchema schema, string identifier)
    {
        await using var db = CreateContext(schema);
        return await RoutingPinServiceTests.SeedCardAsync(db, identifier);
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        IsolatedTestSchema schema,
        string directory,
        Guid pinnedAgentId,
        AgentTaskRole role,
        Guid? cardId,
        string title)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = role,
            CardId = cardId,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            AgentId = pinnedAgentId,
            Ephemeral = false,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(
        IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-routing-pin-tick").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
