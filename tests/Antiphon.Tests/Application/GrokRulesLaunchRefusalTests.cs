using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Raw argv refusal remains independent of the composed rules file transport.</summary>
[Category("Integration")]
[NotInParallel("AgentControl")]
public sealed class GrokRulesLaunchRefusalTests
{
    [Test]
    public async Task Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists()
    {
        await AssertNamedGrokRefused(SessionBackend.Herdr);
    }

    [Test]
    public async Task Named_grok_pty_host_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists()
    {
        await AssertNamedGrokRefused(SessionBackend.PtyHost);
    }

    [Test]
    public void Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload()
    {
        var (dispatcher, _) = CreateDelegateHarness();
        var task = TaskFor(AgentTaskKind.Worker, AgentTaskRole.Investigate);
        var attached = new[] { InstructionBundles.Orchestrator };
        var spec = SpecOf(dispatcher, task, AgentKind.Grok, attached);
        spec.GrokRulesPayload.ShouldNotBeNull().Content.ShouldContain("[bundle:orchestrator");
        spec.GrokRulesPayload.Content.ShouldContain("[bundle:stage-investigate");
        spec.Args.ShouldNotContain("--rules");
        spec.Args.ShouldAllBe(a => !a.Contains("[bundle:"));

        Should.NotThrow(() => SpecOf(dispatcher, task, AgentKind.ClaudeCode, attached));
        Should.NotThrow(() => SpecOf(dispatcher, task, AgentKind.Codex, attached));
    }

    [Test]
    public async Task Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical()
    {
        await using var db = CreateContext();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "grok-safe-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], includeLaunchResolver: true);
            var grok = await SeedGrokProfileAsync(db);
            var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
            var append = "Reply tersely; sign as {agentName}. " + sentinel;
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Grok Safe Rules",
                    workspace,
                    TuiProfileId: grok.Id,
                    ReplyStyle: AgentReplyStyle.Normal,
                    SystemPromptAppend: append),
                CancellationToken.None);

            var composition = await ComposeAsync(db, agent.Id);
            composition.GrokRulesPayload.ShouldNotBeNull().Content.ShouldBe(ChannelPreamble.Render(append, agent.Name, []));
            composition.ExtraArgs.ShouldNotContain("--rules");
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public void Over_budget_single_line_composition_still_throws_invalid_operation()
    {
        var composed = new ComposedInstructions(new string('x', 31_000), []);
        var ex = Should.Throw<InvalidOperationException>(() =>
            InstructionBundleComposer.EnsureWithinCommandLineBudget(
                composed, [], 30_000, "budget-pin"));
        ex.Message.ShouldContain("Nothing was truncated");
        ex.ShouldNotBeOfType<ConflictException>();
    }

    private static async Task AssertNamedGrokRefused(SessionBackend backend)
    {
        await using var db = CreateContext();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, $"grok-refuse-{backend}");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], includeLaunchResolver: true);
            var grok = await SeedGrokProfileAsync(db);
            var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
            var append = UnsafeAppend(sentinel);
            var revision = await db.AgentTuiProfileRevisions.SingleAsync(r => r.Id == grok.ActiveRevisionId);
            revision.ArgumentsJson = JsonSerializer.Serialize(new[] { "--rules", append });
            await db.SaveChangesAsync();
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Grok Rules Refuse",
                    workspace,
                    TuiProfileId: grok.Id,
                    SessionBackend: backend,
                    ReplyStyle: AgentReplyStyle.Terse,
                    BundleKeys: [InstructionBundles.Orchestrator],
                    SystemPromptAppend: append),
                CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.StartAsync(
                    agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None));
            ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
            ex.Message.ShouldContain(agent.Name);
            ex.Message.ShouldContain("--rules");
            ex.Message.ShouldContain(GrokRulesArgvPolicy.ReasonLineBreak);
            ex.Message.ShouldNotContain(sentinel);
            ex.Message.ShouldNotContain("[bundle:orchestrator");
            ex.Message.ShouldNotContain("override");
            var exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            ex.Message.ShouldNotContain(exe);

            adapter.Started.ShouldBeFalse();
            adapter.Prompts.ShouldBeEmpty();

            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.Cwd == workspace)).ShouldBe(0);
            (await verify.Agents.SingleAsync(a => a.Id == agent.Id)).Status.ShouldBe(AgentStatus.Idle);
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static string UnsafeAppend(string? sentinel = null) =>
        "Custom line one with spaces\r\nline \"two\" with `backticks`\nline three "
        + (sentinel ?? "card0382-sentinel-" + Guid.NewGuid().ToString("N"));

    private static async Task<AgentLaunchComposition> ComposeAsync(AppDbContext db, Guid id)
    {
        var (_, provider) = CreateDelegateHarness();
        await using var owned = provider;
        var composer = new AgentSessionLaunchComposer(db, Options.Create(new DelegationSettings()),
            provider.GetRequiredService<AgentRegistry>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentSessionLaunchComposer>.Instance);
        return await composer.ComposeForAgentAsync(await db.Agents.SingleAsync(a => a.Id == id), CancellationToken.None);
    }

    private static async Task<AgentTuiProfile> SeedGrokProfileAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"grok-rules-{Guid.NewGuid():N}"[..40],
            Kind = AgentKind.Grok,
            IsEnabled = true,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ArgumentsJson = JsonSerializer.Serialize(new[] { "--always-approve" }),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = null,
            Guidance = "CARD-0382",
            CreatedAt = now,
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return profile;
    }

    private static AppDbContext CreateContext() =>
        new(TestDbFixture.CreateDbContextOptions());

    private static AgentLaunchSpec SpecOf(
        AgentTaskDispatcher dispatcher,
        AgentTask task,
        AgentKind kind,
        IReadOnlyList<string>? attached)
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
        ModelLevel = AgentModelLevel.High,
        Status = AgentTaskStatus.Queued,
        Goal = "make the composed launch arguments observable",
        WorkingDirectory = Path.GetTempPath(),
        Workspace = WorkspaceMode.Shared,
        CreatedAt = DateTime.UtcNow,
    };

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateDelegateHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-grok-rules-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }
}
