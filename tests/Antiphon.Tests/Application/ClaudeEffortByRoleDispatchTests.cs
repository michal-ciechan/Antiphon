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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Task 8333e231 — <c>Delegation:ClaudeEffortByRole</c> at the three places a task's effort is
/// observable: the composed launch argv (<see cref="AgentTaskDispatcher.BuildLaunchSpec"/>), the
/// warm-pool reuse decision (a reused process cannot change its <c>--effort</c>), and the
/// escalation event text. The empty-map cases are the compatibility pins: with no override every
/// argv element and every event string is exactly what CARD-0289 shipped.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class ClaudeEffortByRoleDispatchTests
{
    private static readonly Dictionary<string, string> CodeAtXhigh = new(StringComparer.OrdinalIgnoreCase) { ["Code"] = "xhigh" };
    private static readonly Dictionary<string, string> Empty = new(StringComparer.OrdinalIgnoreCase);

    // ---- launch argv ---------------------------------------------------------------------------

    [Test]
    public void a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier()
    {
        var dispatcher = CreateDispatcher(CodeAtXhigh);

        var code = SpecOf(dispatcher, TaskFor(AgentModelLevel.High, AgentTaskRole.Code)).Args.ToList();
        var review = SpecOf(dispatcher, TaskFor(AgentModelLevel.High, AgentTaskRole.Review)).Args.ToList();

        code[code.IndexOf(ClaudeLaunchArgs.EffortFlag) + 1].ShouldBe("xhigh");
        code[code.IndexOf("--model") + 1].ShouldBe("opus", "the override changes the effort, never the model");
        review[review.IndexOf(ClaudeLaunchArgs.EffortFlag) + 1].ShouldBe("high");
        review[review.IndexOf("--model") + 1].ShouldBe("opus");
    }

    [Test]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Review)]
    public void an_empty_map_launches_todays_argv(AgentTaskRole role)
    {
        // The CARD-0289 shape, pinned as literals: --name, --effort high, --append-system-prompt,
        // then --model opus LAST (CARD-0182 D2). Nothing else is on the dispatcher's Claude argv.
        var task = TaskFor(AgentModelLevel.High, role);
        var args = SpecOf(CreateDispatcher(Empty), task).Args.ToList();

        var name = args.IndexOf("--name");
        name.ShouldBeGreaterThanOrEqualTo(0);
        args[name + 1].ShouldBe($"task-{DelegationReportFormatter.Short(task.Id)}");
        args[name + 2].ShouldBe("--effort");
        args[name + 3].ShouldBe("high");
        args[name + 4].ShouldBe("--append-system-prompt");
        args[name + 6].ShouldBe("--model");
        args[name + 7].ShouldBe("opus");
        args.Count.ShouldBe(name + 8, "no extra element appears on the Claude argv");
        args.Count(a => a == ClaudeLaunchArgs.EffortFlag).ShouldBe(1);
    }

    [Test]
    public void an_override_changes_only_its_own_roles_effort_element()
    {
        var code = TaskFor(AgentModelLevel.High, AgentTaskRole.Code);
        var review = TaskFor(AgentModelLevel.High, AgentTaskRole.Review);
        var before = CreateDispatcher(Empty);
        var after = CreateDispatcher(CodeAtXhigh);

        var reviewBefore = SpecOf(before, review).Args.ToList();
        var reviewAfter = SpecOf(after, review).Args.ToList();
        var codeBefore = SpecOf(before, code).Args.ToList();
        var codeAfter = SpecOf(after, code).Args.ToList();

        reviewAfter.ShouldBe(reviewBefore, "a role absent from the map is byte-for-byte today's argv");
        codeAfter.Count.ShouldBe(codeBefore.Count);
        var effort = codeBefore.IndexOf(ClaudeLaunchArgs.EffortFlag) + 1;
        codeBefore[effort].ShouldBe("high");
        codeAfter[effort].ShouldBe("xhigh");
        for (var i = 0; i < codeBefore.Count; i++)
        {
            if (i != effort)
                codeAfter[i].ShouldBe(codeBefore[i], $"argv[{i}] must not change under an effort override");
        }
    }

    // ---- warm pool -----------------------------------------------------------------------------

    [Test]
    public async Task a_role_override_that_differs_from_the_tier_declines_warm_reuse()
    {
        using var workspace = new TempWorkspace();
        var (warm, _) = await SeedWarmAgentAsync(workspace.Path, AgentModelLevel.High);

        // Override active and different from High's own effort: the warm process launched at the
        // tier's `high` cannot serve a Code task that must run at `xhigh`.
        var overridden = await SeedQueuedTaskAsync(workspace.Path, AgentTaskRole.Code, AgentModelLevel.High);
        await CreateDispatcher(CodeAtXhigh).TickAsync(CancellationToken.None);
        await using var db = CreateContext();
        var declined = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == overridden.Id);
        declined.AgentId.ShouldNotBe(warm, "an overridden task needs a fresh process to carry its --effort");
        var retained = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == warm);
        retained.Status.ShouldBe(AgentStatus.Idle, "the warm delegate is left for a task it can serve");

        // Control: the same task shape with no override reuses the warm delegate exactly as today.
        var plain = await SeedQueuedTaskAsync(workspace.Path, AgentTaskRole.Code, AgentModelLevel.High);
        await CreateDispatcher(Empty).TickAsync(CancellationToken.None);
        var reused = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == plain.Id);
        reused.AgentId.ShouldBe(warm);
    }

    // ---- escalation text -----------------------------------------------------------------------

    [Test]
    public async Task escalating_an_overridden_claude_task_names_the_override()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentTaskRole.Debug, AgentModelLevel.Medium);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Debug"] = "xhigh" };

        await using var db = CreateContext();
        await CreateService(db, map).EscalateAsync(task.Id, to: AgentModelLevel.High, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        detail.ShouldBe("Escalated sonnet -> opus. opus launches at xhigh by role override"
            + " (Delegation:ClaudeEffortByRole:Debug), not the tier-wired high.");
    }

    [Test]
    public async Task escalation_text_is_unchanged_without_an_override_or_when_the_override_equals_the_tier()
    {
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();

        var plain = await SeedSettledTaskAsync(workspace.Path, AgentTaskRole.Debug, AgentModelLevel.Medium);
        await CreateService(db, Empty).EscalateAsync(plain.Id, to: AgentModelLevel.High, CancellationToken.None);
        (await LatestEscalationDetailAsync(plain.Id)).ShouldBe("Escalated sonnet -> opus.");

        // Frontier's own effort is xhigh, so the override adds nothing worth saying.
        var code = await SeedSettledTaskAsync(workspace.Path, AgentTaskRole.Code, AgentModelLevel.High);
        await CreateService(db, CodeAtXhigh).EscalateAsync(code.Id, to: null, CancellationToken.None);
        (await LatestEscalationDetailAsync(code.Id)).ShouldBe("Escalated opus -> fable.");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static AgentTaskDispatcher CreateDispatcher(Dictionary<string, string> claudeEffortByRole)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        var delegation = new DelegationSettings
        {
            PoolReservedForCallerMinutes = 0,
            PoolIdleRetireMinutes = 600,
            PoolMaxIdlePerDirectory = 8,
            // The fixture database is shared across suites; leftover Dispatched/Working rows from
            // other tests must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
        };
        foreach (var (role, effort) in claudeEffortByRole)
            delegation.ClaudeEffortByRole[role] = effort;
        services.AddSingleton(Options.Create(delegation));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            // Bare and unresolvable so nothing can be spawned by the background launch queue.
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper { StopsSessionsIn = TestDbFixture.ConnectionString });
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-effort-role-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    private static AgentTaskService CreateService(AppDbContext db, Dictionary<string, string> claudeEffortByRole)
    {
        var settings = new DelegationSettings { AllowedRoots = [] };
        foreach (var (role, effort) in claudeEffortByRole)
            settings.ClaudeEffortByRole[role] = effort;
        return new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AgentLaunchSpec SpecOf(AgentTaskDispatcher dispatcher, AgentTask task)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = task.WorkingDirectory,
            Kind = task.AgentKind,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = task.AgentKind,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };
        return dispatcher.BuildLaunchSpec(task, agent, session);
    }

    private static AgentTask TaskFor(AgentModelLevel level, AgentTaskRole role) => new()
    {
        Id = Guid.NewGuid(),
        Kind = AgentTaskKind.Worker,
        Role = role,
        AgentKind = AgentKind.ClaudeCode,
        ModelLevel = level,
        Status = AgentTaskStatus.Queued,
        Goal = "make the composed launch arguments observable",
        WorkingDirectory = Path.GetTempPath(),
        Workspace = WorkspaceMode.Shared,
        CreatedAt = DateTime.UtcNow,
    };

    private static async Task<AgentTask> SeedQueuedTaskAsync(string directory, AgentTaskRole role, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "do the next piece of work",
            Goal = "do the next piece of work",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            ReplyTo = AgentTaskReplyTo.None,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedSettledTaskAsync(string directory, AgentTaskRole role, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "escalate me",
            Goal = "escalate me",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            // Queued, so the escalation needs no session to stop — the tier bump and its event are
            // the whole subject here.
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    /// <summary>A warm Claude pool delegate at <paramref name="level"/> with a live Running session.</summary>
    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(string directory, AgentModelLevel level)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            GrokRulesState = GrokRulesState.None,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = level,
            Kind = AgentKind.ClaudeCode,
            IsPoolDelegate = true,
            // Well outside PoolReservedForCallerMinutes, so the reservation window never decides.
            PoolIdleSince = now.AddMinutes(-5),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<string> LatestEscalationDetailAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Escalated)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstOrDefaultAsync();
        return detail.ShouldNotBeNull();
    }

    /// <summary>A real directory on disk — the resolver verifies existence, so a fake path won't do.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-effort-role").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}
