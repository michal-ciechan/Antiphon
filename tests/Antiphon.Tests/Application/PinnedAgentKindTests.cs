using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0140 S1 — a bare pin to a standing agent settles <see cref="AgentTask.AgentKind"/> at
/// creation the same way a follow-up already does. Probe 2's loud refusal and probe 1's silent
/// wrong-program launch both start here: omitting <c>agentKind</c> used to default to
/// <see cref="AgentKind.ClaudeCode"/> even when the named agent was Codex.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class PinnedAgentKindTests
{
    [Test]
    public async Task T1_a_task_pinned_to_a_stopped_standing_Codex_agent_with_no_kind_stores_Codex()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedStandingAgentAsync(workspace.Path, AgentKind.Codex, name: "Codex standing");

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "run this on my Codex agent") { AgentId = agentId },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Codex);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        row.AgentKind.ShouldBe(AgentKind.Codex);
        row.AgentId.ShouldBe(agentId);
        (await CreateService(verify).GetSummaryAsync(row, [row])).AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task T2_an_explicit_kind_mismatch_is_refused_and_an_agreeing_kind_is_accepted()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedStandingAgentAsync(workspace.Path, AgentKind.Codex, name: "Codex standing");

        await using var db = CreateContext();
        var service = CreateService(db);

        var mismatch = await Should.ThrowAsync<ConflictException>(
            () => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "please run as Claude")
                {
                    AgentId = agentId,
                    AgentKind = AgentKind.ClaudeCode,
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));
        mismatch.Message.ShouldContain("Codex standing");
        mismatch.Message.ShouldContain("Codex");
        mismatch.Message.ShouldContain("ClaudeCode");

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "run as Codex, which it is")
            {
                AgentId = agentId,
                AgentKind = AgentKind.Codex,
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);
        created.AgentKind.ShouldBe(AgentKind.Codex);
        (await CreateContext().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .AgentId.ShouldBe(agentId);
    }

    [Test]
    public async Task T3_a_bare_pin_to_a_pool_delegate_is_untouched_and_kind_mismatch_still_relaunches()
    {
        using var workspace = new TempWorkspace();
        var poolId = await SeedPoolAgentAsync(workspace.Path, AgentKind.Grok);

        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            new CreateAgentTaskRequest(Goal: "pin the long way to a pool row") { AgentId = poolId },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(
            AgentKind.ClaudeCode,
            "pool pins do not inherit Kind — the role policy (ClaudeCode, unset) still answers");
        (await CreateContext().AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id))
            .AgentId.ShouldBe(poolId);

        var grokSessionId = await SeedLiveSessionOnAsync(poolId, workspace.Path, AgentKind.Grok);
        var (dispatcher, _) = CreateDispatchHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentId.ShouldBe(poolId, "the pin is honoured — the row is relaunched, not replaced");
        dispatched.AgentSessionId.ShouldNotBe(
            grokSessionId,
            "TryReuseWarmAgentAsync relaunches on kind mismatch rather than typing Claude into Grok");
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == poolId))
            .Kind.ShouldBe(AgentKind.ClaudeCode, "ResolveAgentAsync restamps a pool row to the task's kind");
    }

    [Test]
    public async Task T4_an_orchestrator_pinned_to_a_Codex_agent_is_refused_not_clamped()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedStandingAgentAsync(workspace.Path, AgentKind.Codex, name: "Codex standing");
        var goal = $"own this chunk {Guid.NewGuid():N}";

        await using var db = CreateContext();
        var ex = await Should.ThrowAsync<ValidationException>(
            () => CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(Goal: goal, Kind: AgentTaskKind.Orchestrator)
                {
                    AgentId = agentId,
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));

        var message = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        message.ShouldContain("orchestrator");
        message.ShouldContain("Codex");
        message.ShouldContain("WORKER");

        await using var verify = CreateContext();
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal))
            .ShouldBe(0, "a clamped orchestrator would have left a ClaudeCode row behind");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static async Task<Guid> SeedStandingAgentAsync(string directory, AgentKind kind, string name)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = $"pin-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = directory,
            Details = "A standing agent for CARD-0140 S1.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            Kind = kind,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedPoolAgentAsync(string directory, AgentKind kind)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            Kind = kind,
            IsPoolDelegate = true,
            PoolIdleSince = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedLiveSessionOnAsync(Guid agentId, string directory, AgentKind kind)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = kind == AgentKind.Grok ? "grok" : "claude",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        agent.PersistentSessionId = sessionId.ToString("D");
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = now.AddMinutes(-5);
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static AgentTaskService CreateService(AppDbContext db)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateDispatchHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            PoolIdleRetireMinutes = 525_600,
            PoolMaxIdlePerDirectory = int.MaxValue,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
            s.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-pin-kind-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pin-kind").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
