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
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0058 slice 2 — a delegate's role decides which instruction bundles ride its launch.
///
/// <para>Driven through the REAL <c>AgentTaskDispatcher.BuildLaunchSpec</c>, which is the only place
/// a delegate's <c>--append-system-prompt</c> is composed, so these are the actual launch arguments
/// and not a copy of the composition. Nothing is spawned: building the spec is a pure step of the
/// dispatch path, deliberately separated from the launch queue that executes it.</para>
/// </summary>
[Category("Integration")]
public class DelegateBundleLaunchTests
{
    [Test]
    public void a_worker_launches_with_the_delegate_basics_bundle_under_its_versioned_header()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Code);

        var append = AppendedSystemPrompt(dispatcher, task);

        var stage = InstructionBundles.Get(InstructionBundles.StageCode);
        var basics = InstructionBundles.Get(InstructionBundles.DelegateBasics);
        append.ShouldStartWith($"[bundle:stage-code v{stage.Version}]");
        append.ShouldContain($"[bundle:delegate-basics v{basics.Version}]");
        append.ShouldContain("RUN EVERY COMMAND IN THE FOREGROUND");
        append.ShouldContain("FORWARD slash");
        append.ShouldNotContain("[bundle:orchestrator", customMessage:
            "a worker is not an orchestrator — the role map is what decides, not the launch path");
    }

    [Test]
    public void a_sub_orchestrator_launches_with_its_own_contract_first_then_the_basics_each_once()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Orchestrator, AgentTaskRole.Plan);

        var append = AppendedSystemPrompt(dispatcher, task);

        var orchestrator = append.IndexOf("[bundle:orchestrator", StringComparison.Ordinal);
        var basics = append.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal);
        orchestrator.ShouldBe(0);
        basics.ShouldBeGreaterThan(orchestrator);
        // The whole moved contract, not a paraphrase: this is what says the CARD-0058 forward is
        // faithful on the path that actually uses it. Before the move this text was a const inlined
        // here; the launch must still carry it verbatim.
        append.ShouldContain(DelegationReportFormatter.OrchestratorContract);
        append.ShouldContain(InstructionBundles.TextOf(InstructionBundles.DelegateBasics));
        append.IndexOf("[bundle:delegate-basics", basics + 1, StringComparison.Ordinal)
            .ShouldBe(-1, "each bundle exactly once");
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Distill)]
    [Arguments(AgentTaskRole.Diagnose)]
    public void a_specialist_task_launches_with_no_system_prompt_at_all(AgentTaskRole role)
    {
        // A standing specialist has no tools and a deny-all PreToolUse hook, and its own
        // contract is reconciled onto its agent row. "Commit and push each slice" is an instruction it
        // cannot obey — and it would arrive on the path nobody watches, because the dispatcher only
        // builds a launch spec for a pinned agent when that agent's session is NOT already up.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, role);

        var args = ArgsOf(dispatcher, task);

        args.ShouldNotContain("--append-system-prompt");
        args.ShouldContain("--name", customMessage: "the rest of the launch is untouched");
        args.ShouldContain("haiku");
    }

    [Test]
    public void an_over_budget_composition_fails_the_launch_and_names_the_task()
    {
        // Throws rather than truncates, and throws BEFORE the spec exists — so a delegate never runs
        // under half a contract. The budget is squeezed here; in production the measured worst case
        // uses under a third of it (InstructionBundleTests).
        var (dispatcher, _) = CreateHarness(budgetChars: 200);
        var task = TaskFor(AgentTaskKind.Orchestrator, AgentTaskRole.Plan);

        var ex = Should.Throw<InvalidOperationException>(() => ArgsOf(dispatcher, task));

        ex.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
        ex.Message.ShouldContain("Orchestrator/Plan");
        ex.Message.ShouldContain("Nothing was truncated");
    }

    [Test]
    public void bundles_ride_the_launch_and_never_the_brief()
    {
        // Which is why a warm-pool reuse composes nothing: reuse delivers a brief into a session that
        // is already up (AgentTaskPoolTests pins "the LIVE session — no launch happened"), so there is
        // no launch for a bundle to ride. A bundle leaking into the brief would be typed into the pty,
        // where it would blow every CARD-0027/0037 ceiling the brief is measured against.
        var task = TaskFor(AgentTaskKind.Orchestrator, AgentTaskRole.Plan);
        var settings = new DelegationSettings();

        var brief = DelegationReportFormatter.BuildBrief(task, settings);

        brief.ShouldNotContain("[bundle:");
        brief.ShouldNotContain(InstructionBundles.TextOf(InstructionBundles.DelegateBasics));
        brief.ShouldContain(task.Goal, customMessage: "the brief carries the goal and the ephemeral state");
    }

    // ---- per-agent attachments (CARD-0058 slice 6) -----------------------------------------------

    [Test]
    public void a_pinned_agents_attachments_ride_its_launch_on_top_of_its_role()
    {
        // The card, stated as a launch: board-api is on NO role, so a delegate working the card API
        // never received it and widening the role map would have handed it to every delegate of that
        // role. An attachment reaches exactly the one agent — and the role's own bundle still comes
        // first, because the role says what this shape of work always needs.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Code);

        var append = AppendedSystemPrompt(dispatcher, task, [InstructionBundles.BoardApi]);

        append.ShouldStartWith("[bundle:stage-code v");
        append.ShouldContain("[bundle:delegate-basics v");
        append.ShouldContain($"[bundle:board-api v{InstructionBundles.Get(InstructionBundles.BoardApi).Version}]");
        append.ShouldContain(InstructionBundles.TextOf(InstructionBundles.BoardApi));
        append.IndexOf("[bundle:stage-code", StringComparison.Ordinal)
            .ShouldBeLessThan(append.IndexOf("[bundle:board-api", StringComparison.Ordinal));
    }

    [Test]
    public void attaching_a_bundle_the_role_already_grants_composes_it_once()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Code);

        var append = AppendedSystemPrompt(dispatcher, task, [InstructionBundles.DelegateBasics]);

        append.ShouldStartWith("[bundle:stage-code v");
        var first = append.IndexOf("[bundle:delegate-basics", StringComparison.Ordinal);
        first.ShouldBeGreaterThan(0);
        append.IndexOf("[bundle:delegate-basics", first + 1, StringComparison.Ordinal)
            .ShouldBe(-1, "the composer dedupes by key, so attaching a role default is harmless");
    }

    [Test]
    public void a_worker_launch_sets_task_kind_worker_alongside_task_id()
    {
        // CARD-0247 S2: the investigation hook unarms on ANTIPHON_TASK_ID (rule 3) for
        // every worker; Kind is still exported so a future discriminator can key on it.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Code);

        var env = EnvOf(dispatcher, task);

        env["ANTIPHON_TASK_ID"].ShouldBe(task.Id.ToString("D"));
        env["ANTIPHON_TASK_KIND"].ShouldBe(nameof(AgentTaskKind.Worker));
        env.ShouldNotContainKey("ANTIPHON_ORCHESTRATOR");
    }

    [Test]
    public void an_orchestrator_task_sets_task_kind_orchestrator_so_the_hook_arms()
    {
        // Plan §3.1 rule 2: ANTIPHON_TASK_KIND=Orchestrator arms the hook even though
        // ANTIPHON_TASK_ID is also set (rule 3 would otherwise unarm a worker).
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Orchestrator, AgentTaskRole.Plan);

        var env = EnvOf(dispatcher, task);

        env["ANTIPHON_TASK_KIND"].ShouldBe(nameof(AgentTaskKind.Orchestrator));
        env.ShouldContainKey("ANTIPHON_TASK_ID");
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Distill)]
    [Arguments(AgentTaskRole.Diagnose)]
    public void a_specialist_task_still_launches_with_nothing_even_when_its_agent_carries_attachments(AgentTaskRole role)
    {
        // A standing specialist is the agent most likely to be PINNED, and therefore the one most
        // likely to have an attachment. The carve-out is about what it can obey — no tools, a
        // deny-all PreToolUse hook — not about which map the instruction came through.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, role);

        ArgsOf(dispatcher, task, [InstructionBundles.BoardApi]).ShouldNotContain("--append-system-prompt");
    }

    // ---- CARD-0146 S3: stage bundles reach Grok --rules and Codex -c -----------------------------

    [Test]
    public void a_grok_investigate_launch_carries_the_stage_bundle_on_rules()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Investigate);

        var args = ArgsOf(dispatcher, task, kind: AgentKind.Grok);

        args.ShouldContain("--rules");
        args.ShouldNotContain("--append-system-prompt", customMessage:
            "Grok's system-prompt channel is --rules; the bundle would be dropped in silence");
        args.ShouldNotContain("-c");
        var rules = args[args.IndexOf("--rules") + 1];
        var bundle = InstructionBundles.Get(InstructionBundles.StageInvestigate);
        rules.ShouldContain($"[bundle:stage-investigate v{bundle.Version}]");
        rules.ShouldContain(bundle.Text);
        rules.ShouldContain("[bundle:delegate-basics v");
    }

    [Test]
    public void a_codex_investigate_launch_carries_the_stage_bundle_on_developer_instructions()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Investigate);

        var args = ArgsOf(dispatcher, task, kind: AgentKind.Codex);

        args.ShouldNotContain("--append-system-prompt");
        args.ShouldNotContain("--rules", customMessage: "--rules is Grok's flag");
        var value = ConfigValue(args, "developer_instructions").ShouldNotBeNull();
        var bundle = InstructionBundles.Get(InstructionBundles.StageInvestigate);
        value.ShouldContain($"[bundle:stage-investigate v{bundle.Version}]");
        value.ShouldContain(bundle.Text);
        value.ShouldContain("[bundle:delegate-basics v");
    }

    // ---- harness -------------------------------------------------------------------------------

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

    private static string AppendedSystemPrompt(
        AgentTaskDispatcher dispatcher, AgentTask task, IReadOnlyList<string>? attached = null)
    {
        var args = ArgsOf(dispatcher, task, attached);
        var flag = args.IndexOf("--append-system-prompt");
        flag.ShouldBeGreaterThanOrEqualTo(0, $"args were [{string.Join(", ", args)}]");
        return args[flag + 1];
    }

    private static List<string> ArgsOf(
        AgentTaskDispatcher dispatcher, AgentTask task, IReadOnlyList<string>? attached = null,
        AgentKind kind = AgentKind.ClaudeCode)
        => [.. SpecOf(dispatcher, task, attached, kind).Args];

    private static IReadOnlyDictionary<string, string> EnvOf(
        AgentTaskDispatcher dispatcher, AgentTask task, IReadOnlyList<string>? attached = null)
        => SpecOf(dispatcher, task, attached).Env;

    private static AgentLaunchSpec SpecOf(
        AgentTaskDispatcher dispatcher, AgentTask task, IReadOnlyList<string>? attached = null,
        AgentKind kind = AgentKind.ClaudeCode)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = task.WorkingDirectory,
            Kind = kind,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = kind switch
            {
                AgentKind.Grok => "grok",
                AgentKind.Codex => "codex",
                _ => "claude",
            },
            AgentKind = kind,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        return dispatcher.BuildLaunchSpec(task, agent, session, attached);
    }

    private static AgentTask TaskFor(AgentTaskKind kind, AgentTaskRole role) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Role = role,
        // Check is haiku work; the others do not matter here beyond being a resolvable alias.
        ModelLevel = AgentTaskRoles.IsSpecialist(role) ? AgentModelLevel.Low : AgentModelLevel.High,
        Status = AgentTaskStatus.Queued,
        Goal = "make the composed launch arguments observable",
        WorkingDirectory = Path.GetTempPath(),
        Workspace = WorkspaceMode.Shared,
        CreatedAt = DateTime.UtcNow,
    };

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness(
        int? budgetChars = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        var delegation = new DelegationSettings();
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
            s.Definitions["codex"] = new AgentDefinition
            {
                Kind = "Codex",
                Exe = "codex",
                ArgsTemplate = ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-bundle-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }
}
