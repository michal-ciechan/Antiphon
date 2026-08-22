using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0099 S3 — a NAMED Codex agent (not a delegate task) launches with its tier and its standing
/// instructions, through the real AgentControlService → AgentSessionLaunchQueue → AgentSessionService
/// chain.
///
/// <para>This closes a defect that predates the delegate work and is independent of it. The whole
/// argument-composing block in <c>AgentControlService</c> was gated on
/// <c>isClaudeCode || isGrok</c>, so a Codex agent fell straight past it: <b>no <c>--model</c> at
/// all</b> — its <c>ModelLevel</c> was decorative and the process ran on whatever
/// <c>~/.codex/config.toml</c> said — and no bundles, no reply style and no
/// <c>SystemPromptAppend</c>, dropped without a log line. Nothing on any screen distinguishes that
/// from a healthy launch, which is why it survived until a second launch path needed the same
/// branch.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class NamedCodexAgentLaunchTests
{
    [Test]
    public async Task A_codex_agent_launches_with_its_tier_slug_and_reasoning_effort()
    {
        await using var h = await CreateHarnessAsync();
        await SetAgentAsync(h, AgentModelLevel.Frontier, systemPrompt: null);
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs.ToList();
        args[args.IndexOf("--model") + 1].ShouldBe("gpt-5.6-sol");
        ConfigValue(args, "model_reasoning_effort").ShouldBe(
            "xhigh", "sol's OWN default is low — a Frontier agent must not inherit it");
        args.ShouldNotContain("--name", customMessage: "Codex has no --name flag");
        args.ShouldNotContain("--append-system-prompt");
    }

    [Test]
    public async Task A_codex_agents_standing_instructions_ride_developer_instructions()
    {
        await using var h = await CreateHarnessAsync();
        await SetAgentAsync(h, AgentModelLevel.Medium, systemPrompt: "You are {agentName}. Answer briefly.");
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs.ToList();
        var instructions = ConfigValue(args, "developer_instructions").ShouldNotBeNull();
        // Rendered, not the raw template: {agentName} expands the same way it does for Claude.
        instructions.ShouldContain("You are BridgeQueue.");
        instructions.ShouldNotContain("{agentName}");
        args[args.IndexOf("--model") + 1].ShouldBe("gpt-5.6-luna");
        ConfigValue(args, "model_reasoning_effort").ShouldBe("medium");
    }

    [Test]
    public async Task A_null_profile_agent_keeps_default_Kind_and_still_launches_from_the_registry()
    {
        // CARD-0138 T3: Kind is the entity default (ClaudeCode) because no profile is attached,
        // and launch still composes from the AgentRegistry — PeekProfileKindAsync does not
        // consult Agent.Kind. If a later change started branching on the column, this would
        // grow Claude flags (--name, --append-system-prompt) on a Codex definition.
        await using var h = await CreateHarnessAsync();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var stored = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            stored.Kind.ShouldBe(AgentKind.ClaudeCode);
            if (stored.TuiProfileId is not null)
            {
                await db.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.TuiProfileId, (Guid?)null));
            }
        }

        await EndSessionAsync(h, SessionStatus.Failed);
        await StartAsync(h);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs.ToList();
        args[args.IndexOf("--model") + 1].ShouldBe("gpt-5.6-terra");
        ConfigValue(args, "model_reasoning_effort").ShouldBe("high");
        args.ShouldNotContain("--append-system-prompt");
        args.ShouldNotContain("--name");
    }

    [Test]
    public async Task A_Kind_PATCH_changes_the_column_and_not_the_composed_launch()
    {
        // CARD-0139 T4. Agent.Kind is not a launch input — PeekProfileKindAsync reads the profile,
        // then the registry. A Kind write on a no-profile agent must leave argv identical (session
        // ids aside) to the same start without the PATCH. The card asked for the opposite and
        // that test cannot be written truthfully.
        await using var h = await CreateHarnessAsync();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.TuiProfileId, (Guid?)null));
        }

        await EndSessionAsync(h, SessionStatus.Failed);
        await StartAsync(h);
        var before = NormalizeLaunchArgs(Factory(h).Created.ShouldHaveSingleItem().StartedArgs);

        using (var scope = h.Provider.CreateScope())
        {
            var agents = scope.ServiceProvider.GetRequiredService<AgentService>();
            var agent = await agents.GetByIdAsync(h.AgentId, CancellationToken.None);
            var updated = await agents.UpdateAsync(
                h.AgentId,
                new UpdateAgentRequest(
                    agent.Name,
                    agent.WorkingDirectory,
                    agent.Details,
                    agent.DefaultWorkflowTemplateId,
                    agent.AssignmentPolicy,
                    Kind: AgentKind.Grok),
                CancellationToken.None);
            updated.Kind.ShouldBe(AgentKind.Grok);
            updated.TuiProfileId.ShouldBeNull();
        }

        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var liveId = (await db.Agents.SingleAsync(a => a.Id == h.AgentId)).PersistentSessionId;
            liveId.ShouldNotBeNull();
            await db.AgentSessions.Where(s => s.Id == Guid.Parse(liveId))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Failed));
        }

        await StartAsync(h);
        Factory(h).Created.Count.ShouldBe(2);
        var after = NormalizeLaunchArgs(Factory(h).Created[1].StartedArgs);
        after.ShouldBe(before);
        after.ShouldContain("--model");
        after.ShouldNotContain("--name");
        after.ShouldNotContain("--append-system-prompt");
    }

    [Test]
    public async Task A_codex_agent_with_an_exact_model_id_keeps_it_and_still_sets_effort()
    {
        // The exact-ModelId branch is unchanged and still wins over the tier ladder; the effort
        // override is orthogonal to it, because a pinned model has the same low default.
        await using var h = await CreateHarnessAsync();
        await SetAgentAsync(h, AgentModelLevel.High, systemPrompt: null, modelId: "gpt-5.6-luna");
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs.ToList();
        args.ShouldNotContain("--model", customMessage:
            "an exact ModelId is applied by the launch resolver, not by the legacy alias fallback");
        ConfigValue(args, "model_reasoning_effort").ShouldBe("high");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static List<string> NormalizeLaunchArgs(IReadOnlyList<string> args) =>
        [.. args.Select(a => Guid.TryParse(a, out _) ? "<id>" : a)];

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

    private static RegisteringAdapterFactory Factory(BridgeQueueHarness h) =>
        (RegisteringAdapterFactory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = services =>
            {
                // A Codex-kind DEFAULT definition, which is what PeekProfileKindAsync reads when an
                // agent has no TUI profile. cmd.exe stays the spawn-check target so nothing real is
                // launched; the argv the chain composes is the whole subject.
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "fake",
                        Definitions =
                        {
                            ["fake"] = new AgentDefinition
                            {
                                Kind = "Codex",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                        },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                    new RegisteringAdapterFactory(sp.GetRequiredService<AgentSessionRuntime>()));
            },
        });

    private static async Task SetAgentAsync(
        BridgeQueueHarness h, AgentModelLevel level, string? systemPrompt, string? modelId = null)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        await db.Agents.Where(a => a.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.ModelLevel, level)
                .SetProperty(a => a.ModelId, modelId)
                .SetProperty(a => a.SystemPromptAppend, systemPrompt));
    }

    private static async Task EndSessionAsync(BridgeQueueHarness h, SessionStatus status)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
    }

    private static async Task StartAsync(BridgeQueueHarness h)
    {
        using var scope = h.Provider.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        await control.StartAsync(
            h.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);
        await h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private sealed class RegisteringAdapterFactory(AgentSessionRuntime runtime) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            Created.Add(adapter);
            return adapter;
        }
    }
}
