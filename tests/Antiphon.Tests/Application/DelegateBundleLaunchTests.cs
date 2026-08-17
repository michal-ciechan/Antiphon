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

        var basics = InstructionBundles.Get(InstructionBundles.DelegateBasics);
        append.ShouldStartWith($"[bundle:delegate-basics v{basics.Version}]");
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
    public void a_check_task_launches_with_no_system_prompt_at_all()
    {
        // The standing check interpreter has no tools and a deny-all PreToolUse hook, and its own
        // contract is reconciled onto its agent row. "Commit and push each slice" is an instruction it
        // cannot obey — and it would arrive on the path nobody watches, because the dispatcher only
        // builds a launch spec for a pinned agent when that agent's session is NOT already up.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Check);

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

    // ---- harness -------------------------------------------------------------------------------

    private static string AppendedSystemPrompt(AgentTaskDispatcher dispatcher, AgentTask task)
    {
        var args = ArgsOf(dispatcher, task);
        var flag = args.IndexOf("--append-system-prompt");
        flag.ShouldBeGreaterThanOrEqualTo(0, $"args were [{string.Join(", ", args)}]");
        return args[flag + 1];
    }

    private static List<string> ArgsOf(AgentTaskDispatcher dispatcher, AgentTask task)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        return [.. dispatcher.BuildLaunchSpec(task, agent, session).Args];
    }

    private static AgentTask TaskFor(AgentTaskKind kind, AgentTaskRole role) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Role = role,
        // Check is haiku work; the others do not matter here beyond being a resolvable alias.
        ModelLevel = role == AgentTaskRole.Check ? AgentModelLevel.Low : AgentModelLevel.High,
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-bundle-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }
}
