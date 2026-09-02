using Antiphon.Server.Application.Dtos;
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
/// CARD-0099 S3 — the mirror of <see cref="GrokDelegateDispatchTests"/> for Codex, asserting the
/// three decisions that are Codex's own and the compatibility half beside each of them.
///
/// <para>Codex's command line shares nothing with Claude's but the word <c>--model</c>. It has no
/// <c>--name</c> (verified against <c>codex --help</c>, cli 0.147.0), no unversioned model aliases
/// (<c>-m luna</c> is a 400 from the service), no <c>--append-system-prompt</c> or <c>--rules</c>,
/// and a per-model default reasoning effort of <c>low</c> on the FRONTIER slug. Every one of those
/// is a way for a Codex delegate to launch looking healthy and be wrong: nameless is fine, but a
/// bare alias never starts, a dropped bundle runs the delegate under no contract at all, and a
/// Frontier delegate reasoning at <c>low</c> is the tier silently not happening.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class CodexDelegateDispatchTests
{
    // ---- launch spec ---------------------------------------------------------------------------

    [Test]
    public void a_codex_delegate_launches_the_codex_definition_with_a_slug_an_effort_and_developer_instructions()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.Codex, AgentModelLevel.High);

        var spec = SpecOf(dispatcher, task);
        var args = spec.Args.ToList();

        spec.DefinitionName.ShouldBe("codex", "resolved by KIND, not the default definition");
        spec.Kind.ShouldBe(AgentKind.Codex);
        // The definition's own template survives — the registry composes it ahead of the extras.
        args.ShouldContain("--no-alt-screen");

        args.ShouldNotContain("--name", customMessage:
            "Codex has no --name flag at all; the launch would die on an unknown argument");
        args[args.IndexOf("--model") + 1].ShouldBe("gpt-5.6-terra");
        args.ShouldNotContain("--append-system-prompt", customMessage:
            "Codex's standing-instruction channel is a -c config override, not Claude's flag");
        args.ShouldNotContain("--rules", customMessage: "--rules is Grok's flag");

        // All -c overrides are present, each as the TWO argv elements Codex expects.
        ConfigValue(args, "model_reasoning_effort").ShouldBe("high");
        ConfigValue(args, "disable_paste_burst").ShouldBe("true");
        ConfigValue(args, "developer_instructions").ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void the_bundle_rides_developer_instructions_as_one_unquoted_argv_element()
    {
        // Two failure modes in one assertion. If the bundle were split across argv elements Codex
        // would take only the first as the config value and drop the rest without a word; and if we
        // added quotes of our own they would land INSIDE the instructions, because this is an argv
        // array and there is no shell between here and the process.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.Codex, AgentModelLevel.High, AgentTaskRole.Code);

        var args = SpecOf(dispatcher, task).Args.ToList();
        var value = ConfigValue(args, "developer_instructions").ShouldNotBeNull();

        value.ShouldNotStartWith("\"");
        value.ShouldNotEndWith("\"");
        // The same text a Claude delegate of this role would receive through --append-system-prompt:
        // the CHANNEL is provider-specific, the contract is not.
        var claude = SpecOf(dispatcher, TaskFor(AgentKind.ClaudeCode, AgentModelLevel.High, AgentTaskRole.Code))
            .Args.ToList();
        value.ShouldBe(claude[claude.IndexOf("--append-system-prompt") + 1]);
    }

    [Test]
    [Arguments(AgentModelLevel.Frontier, "gpt-5.6-sol", "xhigh")]
    [Arguments(AgentModelLevel.High, "gpt-5.6-terra", "high")]
    [Arguments(AgentModelLevel.Medium, "gpt-5.6-luna", "medium")]
    [Arguments(AgentModelLevel.Low, "gpt-5.6-luna", "low")]
    public void every_tier_pins_a_full_slug_and_names_its_own_reasoning_effort(
        AgentModelLevel level, string expectedSlug, string expectedEffort)
    {
        // The slug half: there are no unversioned aliases in Codex's catalog — a bare `-m luna` is
        // rejected locally AND with an HTTP 400, so a "family alias" here never starts a session.
        // The effort half: gpt-5.6-sol's own default is `low`, and the operator's ~/.codex/config.toml
        // says xhigh globally, so leaving it unset makes the tier mean whatever a file nothing in
        // this repo owns happens to say.
        var (dispatcher, _) = CreateHarness();
        var args = SpecOf(dispatcher, TaskFor(AgentKind.Codex, level)).Args.ToList();

        var slug = args[args.IndexOf("--model") + 1];
        slug.ShouldBe(expectedSlug);
        slug.ShouldStartWith("gpt-5.6-");
        ConfigValue(args, "model_reasoning_effort").ShouldBe(expectedEffort);
        ConfigValue(args, "disable_paste_burst").ShouldBe("true");
    }

    [Test]
    public void a_claude_delegate_is_launched_exactly_as_it_was_before_codex_existed()
    {
        // The compatibility half, asserted on the argument list itself: --name, --model and
        // --append-system-prompt off the default definition, with no -c anywhere.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.ClaudeCode, AgentModelLevel.Medium, AgentTaskRole.Code);

        var spec = SpecOf(dispatcher, task);
        var args = spec.Args.ToList();

        spec.DefinitionName.ShouldBe("claude");
        args.IndexOf("--name").ShouldBeGreaterThanOrEqualTo(0);
        args[args.IndexOf("--model") + 1].ShouldBe("sonnet");
        args.ShouldContain("--append-system-prompt");
        args.ShouldNotContain("-c", customMessage:
            "Codex's config overrides must never leak onto a Claude command line");
        args.ShouldNotContain("gpt-5.6-terra");
    }

    [Test]
    public void a_grok_delegate_is_untouched_by_the_codex_branch()
    {
        var (dispatcher, _) = CreateHarness();
        var args = SpecOf(dispatcher, TaskFor(AgentKind.Grok, AgentModelLevel.High)).Args.ToList();

        args[args.IndexOf("--model") + 1].ShouldBe("grok-4.6");
        args.ShouldContain("--rules");
        args.ShouldNotContain("-c");
        args.ShouldNotContain("--name");
    }

    [Test]
    public void a_codex_dispatch_with_no_configured_definition_fails_the_launch_and_names_the_gap()
    {
        // Never a fallback to the default definition: a caller who asked for Codex and quietly got a
        // Claude has no way to tell from the report.
        var (dispatcher, _) = CreateHarness(withCodexDefinition: false);

        var ex = Should.Throw<InvalidOperationException>(
            () => SpecOf(dispatcher, TaskFor(AgentKind.Codex, AgentModelLevel.High)));

        ex.Message.ShouldContain("Codex");
        ex.Message.ShouldContain("Agents:Definitions");
    }

    [Test]
    public void the_command_line_budget_guards_a_codex_launch_the_same_way()
    {
        // The guard runs BEFORE anything is added and is provider-neutral — and it matters MORE for
        // Codex, whose bundle rides inside a single developer_instructions=&lt;text&gt; argv element.
        var (dispatcher, _) = CreateHarness(budgetChars: 200);
        var task = TaskFor(AgentKind.Codex, AgentModelLevel.High, AgentTaskRole.Code);

        var ex = Should.Throw<InvalidOperationException>(() => SpecOf(dispatcher, task));

        ex.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
        ex.Message.ShouldContain("Nothing was truncated");
    }

    [Test]
    public void a_codex_launch_carries_the_same_provider_neutral_antiphon_environment()
    {
        // Correlation, reply routing and depth accounting all ride the env, and none of it is
        // Claude-shaped — a Codex delegate that could not call home would settle nothing.
        var (dispatcher, _) = CreateHarness();

        var codex = SpecOf(dispatcher, TaskFor(AgentKind.Codex, AgentModelLevel.High));

        foreach (var key in new[] { "ANTIPHON_API", "ANTIPHON_SESSION_ID", "ANTIPHON_AGENT_ID", "ANTIPHON_TASK_ID" })
            codex.Env.ShouldContainKey(key);
        codex.Env.ShouldNotContainKey("CLAUDECODE", customMessage:
            "Claude's nesting markers mean nothing to Codex and are cleared per kind by the registry");
    }

    // ---- escalation (plan section 4 S3's last bullet, verified rather than assumed) -------------

    [Test]
    public async Task escalating_a_codex_task_between_real_models_makes_no_disclosure()
    {
        // Unlike Grok, Codex's top three rungs are all real model changes, so the note that Grok
        // needs must NOT fire here. Nothing special-cases the kind: SameModelEscalationNote compares
        // ALIASES, which is why this needed no code change at all — only proof.
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentKind.Codex, AgentModelLevel.Medium);

        await using var db = CreateContext();
        await CreateService(db).EscalateAsync(task.Id, to: null, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        // The Debug role's policy escalates straight to Frontier, so this is luna -> sol: still a
        // real model change, which is the whole point of the assertion.
        detail.ShouldStartWith("Escalated gpt-5.6-luna -> gpt-5.6-sol.");
        detail.ShouldNotContain("FRESH CONTEXT");
        detail.ShouldNotContain("opus", customMessage: "a Codex task never runs a Claude model");
        detail.ShouldNotContain("grok");
    }

    [Test]
    public async Task escalating_a_codex_task_off_its_shared_bottom_rung_says_so()
    {
        // Codex's ONE short rung: Low and Medium are both gpt-5.6-luna. The generic alias comparison
        // catches it, so the promise the operator reads stays honest at the bottom of the ladder too.
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentKind.Codex, AgentModelLevel.Low);

        await using var db = CreateContext();
        // Explicit target: the Debug role policy would otherwise jump the shared rung entirely and
        // land on Frontier, which is a different (and already-covered) case.
        await CreateService(db).EscalateAsync(task.Id, to: AgentModelLevel.Medium, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        detail.ShouldContain("gpt-5.6-luna");
        detail.ShouldContain("FRESH CONTEXT at the same model");
        detail.ShouldContain("Codex");
    }

    // ---- the dispatch itself -------------------------------------------------------------------

    [Test]
    public async Task a_spawn_on_a_standing_agent_leaves_Kind_alone_and_a_pool_delegate_is_restamped()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatchHarness();
        var standingId = await SeedPinnedAgentAsync(
            workspace.Path, isPoolDelegate: false, kind: AgentKind.Codex);
        var poolId = await SeedPinnedAgentAsync(
            workspace.Path, isPoolDelegate: true, kind: AgentKind.Grok);
        var standingTask = await SeedQueuedTaskAsync(
            workspace.Path, AgentKind.ClaudeCode, pinnedAgentId: standingId);
        var poolTask = await SeedQueuedTaskAsync(
            workspace.Path, AgentKind.ClaudeCode, pinnedAgentId: poolId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == standingId))
            .Kind.ShouldBe(AgentKind.Codex);
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == poolId))
            .Kind.ShouldBe(AgentKind.ClaudeCode);
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == standingTask.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == poolTask.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task dispatching_a_codex_task_writes_a_codex_session_and_a_codex_pool_row()
    {
        // The task's kind has to reach the SESSION row (which the brief's spill gate, the tailer and
        // delivery all read) and the pool ROW (which the next task's claim reads) — a warm row with
        // the wrong kind would hand the next Claude task a live Codex process.
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Codex);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.Dispatched.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);

        var session = await verify.AgentSessions.AsNoTracking()
            .SingleAsync(s => s.Id == dispatched.AgentSessionId!.Value);
        session.AgentKind.ShouldBe(AgentKind.Codex);
        session.DefinitionName.ShouldBe("codex");

        var agent = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == dispatched.AgentId!.Value);
        agent.Kind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task a_codex_pool_reuse_of_unrelated_work_types_no_compact()
    {
        // CARD-0117 S3: the Codex reuse shape end to end. One queued message, marked, carrying
        // the refocus line; the rollout must never see a slash command.
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatchHarness();
        var (agentId, sessionId) = await SeedWarmCodexAsync(workspace.Path);
        await SeedSettledOnAsync(agentId, sessionId, workspace.Path);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Codex);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentSessionId.ShouldBe(sessionId);

        var messages = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        messages.Count.ShouldBe(1);
        messages[0].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        messages.ShouldNotContain(m => m.Body.StartsWith("/"));
        if (messages[0].Body.Contains("YOUR BRIEF IS NOT IN THIS MESSAGE", StringComparison.Ordinal))
        {
            var spill = Path.Combine(
                workspace.Path, ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            File.Exists(spill).ShouldBeTrue();
            File.ReadAllText(spill).ShouldContain(DelegationReportFormatter.UnrelatedWorkRefocusLine);
        }
        else
        {
            messages[0].Body.ShouldContain(DelegationReportFormatter.UnrelatedWorkRefocusLine);
        }
    }

    [Test]
    public async Task the_dispatch_event_names_the_codex_model_the_task_actually_runs()
    {
        // The event is what the operator and the check interpreter read. Before ModelLevelAliases
        // grew its Codex arm this line said "sonnet" about a gpt-5.6-luna session.
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Codex);

        await dispatcher.TickAsync(CancellationToken.None);

        var detail = await LatestDispatchDetailAsync(task.Id);
        detail.ShouldContain("gpt-5.6-luna");
        detail.ShouldNotContain("sonnet");
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// The value of a <c>-c key=value</c> pair, or null. Reads the pair as Codex does — the flag and
    /// its <c>key=value</c> as two separate argv elements — so a test cannot pass by finding the key
    /// spliced into some other argument.
    /// </summary>
    private static string? ConfigValue(IReadOnlyList<string> args, string key)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] != "-c") continue;
            if (args[i + 1].StartsWith(key + "=", StringComparison.Ordinal))
                return args[i + 1][(key.Length + 1)..];
        }

        return null;
    }

    private static async Task<string> LatestDispatchDetailAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Dispatched)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstOrDefaultAsync();
        return detail.ShouldNotBeNull();
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
            DefinitionName = task.AgentKind switch
            {
                AgentKind.Codex => "codex",
                AgentKind.Grok => "grok",
                _ => "claude",
            },
            AgentKind = task.AgentKind,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };
        return dispatcher.BuildLaunchSpec(task, agent, session);
    }

    private static AgentTask TaskFor(
        AgentKind agentKind,
        AgentModelLevel level,
        AgentTaskRole role = AgentTaskRole.Docs) => new()
        {
            Id = Guid.NewGuid(),
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = agentKind,
            ModelLevel = level,
            Status = AgentTaskStatus.Queued,
            Goal = "make the composed launch arguments observable",
            WorkingDirectory = Path.GetTempPath(),
            Workspace = WorkspaceMode.Shared,
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task<AgentTask> SeedSettledTaskAsync(
        string directory, AgentKind agentKind, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "escalate me",
            Goal = "escalate me",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Debug,
            AgentKind = agentKind,
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

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmCodexAsync(string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "codex",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now.AddHours(-1),
            StartedAt = now.AddHours(-1),
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm Codex pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task SeedSettledOnAsync(Guid agentId, Guid sessionId, string directory)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "the previous work",
            Goal = "the previous work",
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            DispatchedAt = DateTime.UtcNow.AddMinutes(-19),
            CompletedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPinnedAgentAsync(
        string directory, bool isPoolDelegate, AgentKind kind)
    {
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = isPoolDelegate ? $"task-{agentId:N}"[..13] : $"standing-{agentId:N}"[..20],
            Slug = isPoolDelegate ? $"task-{agentId:N}"[..13] : $"standing-{agentId:N}"[..20],
            WorkingDirectory = directory,
            Details = isPoolDelegate ? "Pool delegate for restamp." : "Standing agent for restamp.",
            Status = AgentStatus.Idle,
            Kind = kind,
            IsPoolDelegate = isPoolDelegate,
            AlwaysOn = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return agentId;
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory, AgentKind agentKind, Guid? pinnedAgentId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "do the next piece of work",
            Goal = "do the next piece of work",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = agentKind,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = pinnedAgentId,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness(
        int? budgetChars = null, bool withCodexDefinition = true)
        => BuildHarness(budgetChars, withCodexDefinition);

    private static AgentTaskDispatcher CreateDispatchHarness()
        => BuildHarness(null, withCodexDefinition: true).Dispatcher;

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) BuildHarness(
        int? budgetChars, bool withCodexDefinition)
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
            // The fixture database is shared across suites; leftover Dispatched/Working rows from
            // other tests must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
            // These tests are about which PROGRAM a task launches, and several of them dispatch two
            // Shared tasks into one directory in a single tick to compare the two stamps. CARD-0063
            // D3 holds the second of those by design (two shared writers share one working tree);
            // the lease has its own tests, so it is turned off here rather than reshaping these.
            SerialiseSharedWriters = false,
        };
        if (budgetChars is int budget)
            delegation.CommandLineBudgetChars = budget;
        services.AddSingleton(Options.Create(delegation));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = "grok",
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
            };
            if (withCodexDefinition)
            {
                s.Definitions["codex"] = new AgentDefinition
                {
                    Kind = "Codex",
                    // Bare and unresolvable, so the background launch queue can never actually spawn
                    // a real Codex from these tests; the argv is the whole subject here.
                    Exe = "codex",
                    ArgsTemplate = ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
                };
            }
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-codex-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (
            provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            provider);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the resolver verifies existence, so a fake path won't do.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-codex-dispatch").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}
