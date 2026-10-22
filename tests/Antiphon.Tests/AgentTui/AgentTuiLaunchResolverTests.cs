using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[Category("Integration")]
[Category("Slow")]
public sealed class AgentTuiLaunchResolverTests
{
    [Test]
    public async Task Resolve_omits_model_argument_when_agent_has_no_exact_model()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(provider, AgentKind.OpenCode);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Atlas",
            TuiProfileId = profile.Id,
            ModelId = null
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Args.ShouldNotContain("--model");
        resolved.EffectiveModelId.ShouldBeNull();
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.None);
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
        resolved.ActivityMode.ShouldBe(AgentTuiLaunchActivityMode.QuietTime);
    }

    [Test]
    public async Task Resolve_appends_exact_model_as_separate_arguments()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            models: ["llmgateway/grok-4-5"]);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Atlas",
            TuiProfileId = profile.Id,
            ModelId = "llmgateway/grok-4-5"
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Args.TakeLast(2).ShouldBe(new[] { "--model", "llmgateway/grok-4-5" });
        resolved.EffectiveModelId.ShouldBe("llmgateway/grok-4-5");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Exact);
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
    }

    [Test]
    public async Task Grok_resolve_appends_model_and_uses_structured_activity()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            models: ["grok-4.6", "grok-4.5"]);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Grok-Agent",
            TuiProfileId = profile.Id,
            ModelId = "grok-4.6"
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Kind.ShouldBe(AgentKind.Grok);
        resolved.Spec.Args.TakeLast(2).ShouldBe(new[] { "--model", "grok-4.6" });
        resolved.EffectiveModelId.ShouldBe("grok-4.6");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Exact);
        resolved.ActivityMode.ShouldBe(AgentTuiLaunchActivityMode.Structured);
        resolved.Spec.Env["GROK_TELEMETRY_ENABLED"].ShouldBe("0");
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
    }

    [Test]
    public async Task Resolve_injects_managed_secrets_and_keeps_wrapper_profiles_secret_free()
    {
        var protector = new RecordingLaunchSecretProtector();
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString, protector);
        var (managed, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            authenticationMode: AgentTuiAuthenticationMode.ManagedEnvironment,
            secretNames: ["OPENAI_API_KEY"],
            canary: "canary-secret");
        var managedAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = managed.Id
        };
        var managedLaunch = await ResolveAsync(provider, managedAgent);
        managedLaunch.Spec.Env["OPENAI_API_KEY"].ShouldBe("canary-secret");
        protector.UnprotectCalls.ShouldBe(1);

        var (wrapper, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            displayName: "Wrapper profile",
            authenticationMode: AgentTuiAuthenticationMode.WrapperManaged);
        var wrapperAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Wrapper",
            TuiProfileId = wrapper.Id
        };
        var wrapperLaunch = await ResolveAsync(provider, wrapperAgent);
        wrapperLaunch.Spec.Env.ContainsKey("OPENAI_API_KEY").ShouldBeFalse();
        protector.UnprotectCalls.ShouldBe(1);
    }

    [Test]
    public async Task Resolve_fails_closed_for_disabled_profile_and_unknown_model()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            enabled: false,
            models: ["llmgateway/grok-4-5"]);
        var disabled = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            TuiProfileId = profile.Id
        };
        var disabledError = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, disabled));
        disabledError.Code.ShouldBe("profile_disabled");

        var (enabled, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            models: ["llmgateway/grok-4-5"]);
        var unknownModel = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Unknown model",
            TuiProfileId = enabled.Id,
            ModelId = "provider/not-in-catalogue"
        };
        var modelError = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, unknownModel));
        modelError.Code.ShouldBe("model_not_in_profile");
    }

    [Test]
    public async Task T1_blank_model_argument_name_suppresses_the_tier_alias()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            modelArgumentName: null);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "GKP",
            TuiProfileId = profile.Id,
            ModelId = null,
            ModelLevel = AgentModelLevel.High
        };

        var resolved = await ResolveAsync(
            provider,
            agent,
            new AgentLaunchOptions(Cols: 120, Rows: 30, TierModelAlias: "grok-4.7"));

        resolved.Spec.Args.ShouldNotContain("--model");
        resolved.Spec.Args.ShouldNotContain("grok-4.7");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.ProfileOwned);
        resolved.EffectiveModelId.ShouldBeNull();
    }

    [Test]
    public async Task T2_declared_model_argument_appends_the_tier_alias_once()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.Grok);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Grok-Tier",
            TuiProfileId = profile.Id,
            ModelId = null
        };

        var resolved = await ResolveAsync(
            provider,
            agent,
            new AgentLaunchOptions(Cols: 120, Rows: 30, TierModelAlias: "grok-4.7"));

        resolved.Spec.Args.Count(a => a == "--model").ShouldBe(1);
        resolved.Spec.Args[resolved.Spec.Args.ToList().IndexOf("--model") + 1].ShouldBe("grok-4.7");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Tier);
        resolved.EffectiveModelId.ShouldBeNull();
    }

    [Test]
    public async Task ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (grok, _) = await SeedProfileAsync(provider, AgentKind.Grok);
        var (codex, _) = await SeedProfileAsync(provider, AgentKind.Codex);
        var (raw, _) = await SeedProfileAsync(provider, AgentKind.Raw);

        var grokLaunch = await ResolveAsync(provider, new Agent { Id = Guid.NewGuid(), Name = "Grok", TuiProfileId = grok.Id },
            new AgentLaunchOptions(ModelTier: AgentModelLevel.High));
        var codexLaunch = await ResolveAsync(provider, new Agent { Id = Guid.NewGuid(), Name = "Codex", TuiProfileId = codex.Id },
            new AgentLaunchOptions(ModelTier: AgentModelLevel.High));
        var rawLaunch = await ResolveAsync(provider, new Agent { Id = Guid.NewGuid(), Name = "Raw", TuiProfileId = raw.Id },
            new AgentLaunchOptions(ModelTier: AgentModelLevel.High));

        grokLaunch.Spec.Args.TakeLast(2).ShouldBe(["--model", "grok-4.7"]);
        codexLaunch.Spec.Args.TakeLast(2).ShouldBe(["--model", "gpt-6.1-sol"]);
        rawLaunch.Spec.Args.ShouldNotContain("--model");
        rawLaunch.ModelArgument.ShouldBe(LaunchModelArgument.None);
    }

    [Test]
    public async Task Explicit_TierModelAlias_wins_over_ModelTier()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.Grok);

        var resolved = await ResolveAsync(provider, new Agent { Id = Guid.NewGuid(), Name = "Grok", TuiProfileId = profile.Id },
            new AgentLaunchOptions(TierModelAlias: "explicit-alias", ModelTier: AgentModelLevel.High));

        resolved.Spec.Args.TakeLast(2).ShouldBe(["--model", "explicit-alias"]);
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Tier);
    }

    [Test]
    public async Task T4_exact_model_on_a_blank_field_profile_is_model_argument_unsupported()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            modelArgumentName: null);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Pinned-GKP",
            TuiProfileId = profile.Id,
            ModelId = "maven-grok"
        };

        var error = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, agent));

        error.Code.ShouldBe("model_argument_unsupported");
        error.StatusCode.ShouldBe(409);
        error.Message.ShouldContain("passes no model argument");
    }

    [Test]
    public async Task Resolve_uses_installation_default_when_agent_has_no_profile()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        await AssertNoProfilesAsync(provider);
        var defaultName = $"Default OpenCode {Guid.NewGuid():N}";
        await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            displayName: defaultName,
            isDefault: true);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Legacy",
            TuiProfileId = null
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Kind.ShouldBe(AgentKind.OpenCode);
        resolved.Spec.DefinitionName.ShouldBe(defaultName);
    }

    [Test]
    public async Task Resolver_test_schemas_start_profile_empty_and_do_not_share_profiles()
    {
        await using var firstSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var firstProvider = BuildProvider(firstSchema.ConnectionString);
        await using var secondSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var secondProvider = BuildProvider(secondSchema.ConnectionString);

        await AssertNoProfilesAsync(firstProvider);
        await AssertNoProfilesAsync(secondProvider);
        await SeedProfileAsync(firstProvider, AgentKind.OpenCode, isDefault: true);

        await AssertNoProfilesAsync(secondProvider);
    }

    [Test]
    public async Task Legacy_resolution_rejects_selected_profile_when_resolver_is_absent()
    {
        var registry = new AgentRegistry(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "legacy",
            Definitions =
            {
                ["legacy"] = new AgentDefinition
                {
                    Kind = "Raw",
                    Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe")
                }
            }
        }));
        var selectedAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Selected profile",
            TuiProfileId = Guid.NewGuid()
        };

        var exception = await Should.ThrowAsync<ConflictException>(() =>
            AgentLaunchResolution.ResolveForAgentAsync(
                selectedAgent,
                registry,
                launchResolver: null,
                new AgentLaunchOptions(Cols: 120, Rows: 30),
                CancellationToken.None));

        exception.Code.ShouldBe("profile_resolution_unavailable");
    }

    [Test]
    public async Task Resolve_expands_dollar_env_on_profile_args_and_leaves_extra_args_literal()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            modelArgumentName: null,
            arguments: ["--project", "$env:X_LLM_PROJECT"]);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Gkp-Project",
            TuiProfileId = profile.Id
        };

        var resolved = await ResolveAsync(
            provider,
            agent,
            new AgentLaunchOptions(
                Cols: 120,
                Rows: 30,
                AgentEnv: new Dictionary<string, string> { ["X_LLM_PROJECT"] = "PredictionMarkets" },
                ExtraArgs: ["$env:X_LLM_PROJECT"]));

        resolved.Spec.Args.ShouldBe(["--project", "PredictionMarkets", "$env:X_LLM_PROJECT"]);
        resolved.Spec.Env["X_LLM_PROJECT"].ShouldBe("PredictionMarkets");
    }

    // ---- CARD-0167 S3: provider endpoint merge ranks 3b (project) and 5b (agent) ----------------
    //
    // Custody: these tests hold key NAMES and one sentinel VALUE that a fake key store returns. The
    // sentinel must reach the child environment and nothing else: not a log line, not an exception
    // message, not a DTO, not the Codex argv. Nothing here reads auth.json or any CLI home.

    private const string C0167Url = "https://proxy-0167.local:8443/tenant-path-0167";
    private const string C0167Host = "proxy-0167.local";
    private const string C0167Path = "tenant-path-0167";
    private const string C0167KeyName = "c0167-proxy-key";
    private const string C0167Placeholder = "{{key:" + C0167KeyName + "}}";
    private const string C0167Sentinel = "sentinel-0167-never-printed";

    private static Dictionary<AgentKind, ProviderEndpoint> C0167Map(
        params (AgentKind Kind, ProviderEndpoint Endpoint)[] entries)
    {
        var map = new Dictionary<AgentKind, ProviderEndpoint>();
        foreach (var (kind, endpoint) in entries)
            map[kind] = endpoint;
        return map;
    }

    private static string[] Lines(IReadOnlyDictionary<string, string> env) =>
        env.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}").ToArray();

    private static string[] Sorted(IEnumerable<string> lines) =>
        lines.OrderBy(l => l, StringComparer.Ordinal).ToArray();

    private static void AssertNothingLeaked(IEnumerable<RecordingLogEntry> log)
    {
        foreach (var entry in log)
        {
            entry.Message.ShouldNotContain(C0167Sentinel);
            entry.Message.ShouldNotContain(C0167Path, Case.Sensitive, "a log line carries the host, never the full URL");
            entry.Message.ShouldNotContain("{{key:");
            foreach (var value in entry.State.Values)
            {
                var text = value?.ToString() ?? string.Empty;
                text.ShouldNotContain(C0167Sentinel);
                text.ShouldNotContain(C0167Path);
            }
            (entry.Exception?.ToString() ?? string.Empty).ShouldNotContain(C0167Sentinel);
        }
    }

    [Test]
    public async Task C0167_no_endpoint_leaves_env_and_args_byte_identical()
    {
        // The captured pre-S3 baseline for a Claude launch with every generic layer populated and no
        // provider endpoint anywhere: exactly these names and values, exactly these arguments. An
        // exact list rather than the resolver compared with itself, so a wiring that projected
        // anything without a setting (a default, an empty value, an argument) fails it.
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode, arguments: ["--auto", "--mini"]);

        var expectedEnv = Sorted(
        [
            "ANTIPHON_SESSION_ID=the-real-session",
            "CLAUDECODE=",
            "CLAUDE_CODE_BRIDGE_SESSION_ID=",
            "CLAUDE_CODE_CHILD_SESSION=",
            "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1",
            "CLAUDE_CODE_ENTRYPOINT=",
            "CLAUDE_CODE_SESSION_ID=",
            "DISABLE_AUTOUPDATER=1",
            "FROM_AGENT=agent",
            "FROM_INHERITED=inherited",
            "FROM_OVERRIDE=override",
            "FROM_PROJECT=project",
        ]);
        string[] expectedArgs = ["--auto", "--mini", "--extra"];

        // Inherit with no map, Inherit with an empty map, Direct with no map, and Inherit on a row
        // whose own endpoint column is set: the mode governs, so Inherit ignores the agent's column.
        var cases = new (ProviderEndpointMode Mode, string? OwnJson, Dictionary<AgentKind, ProviderEndpoint>? Map)[]
        {
            (ProviderEndpointMode.Inherit, null, null),
            (ProviderEndpointMode.Inherit, null, C0167Map()),
            (ProviderEndpointMode.Direct, null, null),
            (ProviderEndpointMode.Inherit,
                ProviderEndpoint.Serialize(new ProviderEndpoint("https://ignored-0167.local", C0167KeyName)), null),
        };
        foreach (var (mode, ownJson, map) in cases)
        {
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = "Baseline",
                TuiProfileId = profile.Id,
                ProviderEndpointMode = mode,
                ProviderEndpointJson = ownJson,
            };

            var resolved = await ResolveAsync(provider, agent, new AgentLaunchOptions(
                Cols: 120,
                Rows: 30,
                ExtraArgs: ["--extra"],
                ExtraEnv: new Dictionary<string, string> { ["ANTIPHON_SESSION_ID"] = "the-real-session" },
                AgentEnv: new Dictionary<string, string> { ["FROM_AGENT"] = "agent" },
                LaunchEnvOverride: new Dictionary<string, string> { ["FROM_OVERRIDE"] = "override" },
                ProjectDefaultEnv: new Dictionary<string, string> { ["FROM_PROJECT"] = "project" },
                InheritedEnv: new Dictionary<string, string> { ["FROM_INHERITED"] = "inherited" },
                ProjectProviderEndpoints: map));

            var label = $"mode={mode} own={(ownJson is null ? "null" : "set")} map={(map is null ? "null" : "empty")}";
            Lines(resolved.Spec.Env).ShouldBe(expectedEnv, label);
            resolved.Spec.Args.ShouldBe(expectedArgs, label);
        }
    }

    [Test]
    public async Task C0167_project_endpoint_outranks_project_default_env_on_the_same_name()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode, arguments: ["--auto"]);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Project-endpoint", TuiProfileId = profile.Id };

        var resolved = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectDefaultEnv: new Dictionary<string, string>
            {
                // A stale hand-typed proxy in the generic project box loses to the typed setting
                // the operator set on purpose (rank 3b beats rank 3).
                ["ANTHROPIC_BASE_URL"] = "http://from-box",
                ["ANTHROPIC_API_KEY"] = "{{key:box-key}}",
                ["X_LLM_PROJECT"] = "FromBox",
            },
            ProjectProviderEndpoints: C0167Map(
                (AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url + "/", C0167KeyName)),
                // Another kind's entry is not projected for a Claude launch.
                (AgentKind.Grok, new ProviderEndpoint("https://grok-0167.local", null)))));

        resolved.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe(C0167Url, "the trailing slash is trimmed");
        resolved.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Placeholder);
        resolved.Spec.Env["X_LLM_PROJECT"].ShouldBe("FromBox", "an uncontested box entry stays");
        resolved.Spec.Env.ShouldNotContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        resolved.Spec.Env.ShouldNotContainKey("GROK_XAI_API_BASE_URL");
        resolved.Spec.Args.ShouldBe(["--auto"], "a Claude endpoint adds no argument");
    }

    [Test]
    public async Task C0167_agent_launch_env_outranks_project_endpoint()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Agent-env", TuiProfileId = profile.Id };
        var map = C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, C0167KeyName)));

        // Rank 4: the caller's inherited routing beats the project's blanket.
        var inherited = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            InheritedEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-inherited" },
            ProjectProviderEndpoints: map));
        inherited.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-inherited");
        inherited.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Placeholder, "the key name is not contested, so it still projects");

        // Rank 5: the agent's own launch env beats both.
        var agentEnv = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            InheritedEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-inherited" },
            AgentEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-agent-box" },
            ProjectProviderEndpoints: map));
        agentEnv.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-agent-box");

        // The agent row itself is the default source of rank 5.
        agent.LaunchEnvJson = AgentLaunchEnv.Serialize(
            new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-agent-row" });
        var agentRow = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120, Rows: 30, ProjectProviderEndpoints: map));
        agentRow.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-agent-row");
        agentRow.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Placeholder);
    }

    [Test]
    public async Task C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode);
        var map = C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, C0167KeyName)));
        // A URL-only endpoint: a key-injecting proxy of the agent's own.
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Own-endpoint",
            TuiProfileId = profile.Id,
            ProviderEndpointMode = ProviderEndpointMode.Endpoint,
            ProviderEndpointJson = ProviderEndpoint.Serialize(new ProviderEndpoint("https://agent-0167.local/", null)),
        };

        // Rank 5b beats rank 5, and the project's key does not follow the agent's URL-only endpoint:
        // the agent endpoint replaces the project endpoint, it does not overlay it.
        var own = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            AgentEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-agent-box" },
            ProjectProviderEndpoints: map));
        own.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://agent-0167.local");
        own.Spec.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // A key the agent typed into its own box for a name its endpoint does not set stays (5b sets
        // only what it projects; it removes nothing).
        var boxKey = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            AgentEnv: new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "http://from-agent-box",
                ["ANTHROPIC_API_KEY"] = "{{key:box-key}}",
            },
            ProjectProviderEndpoints: map));
        boxKey.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://agent-0167.local");
        boxKey.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe("{{key:box-key}}");

        // Rank 6 beats 5b; rank 7 beats 6.
        var overridden = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            LaunchEnvOverride: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://override" },
            ProjectProviderEndpoints: map));
        overridden.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://override");
        var extra = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            LaunchEnvOverride: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://override" },
            ExtraEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://extra" },
            ProjectProviderEndpoints: map));
        extra.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://extra");

        // An explicit caller overlay for the agent's endpoint outranks the row, like AgentEnv does.
        var inheritRow = new Agent { Id = Guid.NewGuid(), Name = "Explicit", TuiProfileId = profile.Id };
        var explicitEndpoint = await ResolveAsync(provider, inheritRow, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            AgentProviderEndpointMode: ProviderEndpointMode.Endpoint,
            AgentProviderEndpoint: new ProviderEndpoint("https://explicit-0167.local", C0167KeyName),
            ProjectProviderEndpoints: map));
        explicitEndpoint.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://explicit-0167.local");
        explicitEndpoint.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Placeholder);
    }

    [Test]
    public async Task C0167_direct_mode_suppresses_the_project_endpoint()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode);
        var map = C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, C0167KeyName)));
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Direct",
            TuiProfileId = profile.Id,
            ProviderEndpointMode = ProviderEndpointMode.Direct,
        };
        var box = new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-box" };

        // Direct suppresses 3b and removes nothing: a hand-typed box value is still there.
        var direct = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120, Rows: 30, ProjectDefaultEnv: box, ProjectProviderEndpoints: map));
        direct.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-box");
        direct.Spec.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // Direct with its own stored endpoint still projects nothing: the mode governs.
        agent.ProviderEndpointJson = ProviderEndpoint.Serialize(new ProviderEndpoint("https://agent-0167.local", C0167KeyName));
        var directWithOwn = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120, Rows: 30, ProjectDefaultEnv: box, ProjectProviderEndpoints: map));
        directWithOwn.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-box");
        directWithOwn.Spec.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // Inherit on a project whose map has no entry for this launch's kind projects nothing either.
        var inherit = new Agent { Id = Guid.NewGuid(), Name = "Inherit", TuiProfileId = profile.Id };
        var grokOnly = await ResolveAsync(provider, inherit, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectDefaultEnv: box,
            ProjectProviderEndpoints: C0167Map((AgentKind.Grok, new ProviderEndpoint("https://grok-0167.local", null)))));
        grokOnly.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-box");
        grokOnly.Spec.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");
        grokOnly.Spec.Env.ShouldNotContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
    }

    [Test]
    public async Task C0167_codex_endpoint_appends_five_args_after_extra_args_and_tripwire_passes()
    {
        var log = new List<RecordingLogEntry>();
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString, withApiKeys: true, log: log);
        await SeedApiKeyAsync(provider, C0167KeyName, C0167Sentinel);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.Codex, arguments: ["--profile-arg"]);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Codex-endpoint", TuiProfileId = profile.Id };
        var map = C0167Map((AgentKind.Codex, new ProviderEndpoint("https://codex-0167.local/openai/", C0167KeyName)));

        var resolved = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ExtraArgs: [CodexLaunchArgs.ConfigFlag, "developer_instructions=hello"],
            TierModelAlias: "gpt-5-codex",
            ProjectProviderEndpoints: map));

        // Profile args, then the caller's ExtraArgs, then the five projected pairs, then the tier.
        resolved.Spec.Args.ShouldBe(
        [
            "--profile-arg",
            "-c", "developer_instructions=hello",
            "-c", "model_providers.antiphon.name=\"Antiphon endpoint\"",
            "-c", "model_providers.antiphon.base_url=\"https://codex-0167.local/openai/v1\"",
            "-c", "model_providers.antiphon.env_key=\"OPENAI_API_KEY\"",
            "-c", "model_providers.antiphon.wire_api=\"responses\"",
            "-c", "model_provider=antiphon",
            "--model", "gpt-5-codex",
        ]);

        // The key is an env VALUE, resolved last. The argv carries neither the placeholder nor the
        // value: the resolver's tripwire ran over every argument, the projected ones included.
        resolved.Spec.Env["OPENAI_API_KEY"].ShouldBe(C0167Sentinel);
        var argv = string.Join("\n", resolved.Spec.Args);
        argv.ShouldNotContain(C0167Sentinel);
        argv.ShouldNotContain("{{key:");
        resolved.Spec.Env.Values.ShouldAllBe(v => !v.Contains("{{key:", StringComparison.Ordinal));
        resolved.Spec.Env.ShouldNotContainKey("OPENAI_BASE_URL");
        resolved.Spec.Env.ShouldNotContainKey("CODEX_HOME");

        // An argument that smuggles a placeholder is still refused by the tripwire, projection or not,
        // and the refusal never carries the value.
        var smuggled = await Should.ThrowAsync<InvalidOperationException>(() => ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ExtraArgs: [CodexLaunchArgs.ConfigFlag, "x=" + C0167Placeholder],
            ProjectProviderEndpoints: map)));
        smuggled.Message.ShouldNotContain(C0167Sentinel);
        AssertNothingLeaked(log);
    }

    [Test]
    public async Task C0167_sentinel_key_value_reaches_env_and_nothing_else()
    {
        var log = new List<RecordingLogEntry>();
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString, withApiKeys: true, log: log);
        await SeedApiKeyAsync(provider, C0167KeyName, C0167Sentinel);
        var (claude, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode, arguments: ["--auto"]);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Sentinel-agent", TuiProfileId = claude.Id };
        var endpoint = new ProviderEndpoint(C0167Url, C0167KeyName);
        var map = C0167Map((AgentKind.ClaudeCode, endpoint));

        var resolved = await ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120, Rows: 30, ProjectProviderEndpoints: map));

        // The child gets the value and the full URL: it needs both.
        resolved.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Sentinel);
        resolved.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe(C0167Url);

        // The one host-only log line: kind, host and key NAME. Never the path, never the value.
        var projectedLines = log.Where(e => e.Message.Contains("provider endpoint projected", StringComparison.Ordinal)).ToList();
        projectedLines.Count.ShouldBe(1);
        projectedLines[0].Message.ShouldContain("Sentinel-agent");
        projectedLines[0].Message.ShouldContain("kind=ClaudeCode");
        projectedLines[0].Message.ShouldContain("host=" + C0167Host);
        projectedLines[0].Message.ShouldContain("key=" + C0167KeyName);
        projectedLines[0].Message.ShouldNotContain("8443", Case.Sensitive, "host only, not the authority");
        AssertNothingLeaked(log);

        // Refusals name the rule, the kind or the key NAME: never the URL, never the value.
        var (codex, _) = await SeedProfileAsync(provider, AgentKind.Codex, displayName: "Codex sentinel");
        var codexAgent = new Agent { Id = Guid.NewGuid(), Name = "Codex-sentinel", TuiProfileId = codex.Id };
        var withoutKey = await Should.ThrowAsync<ConflictException>(() => ResolveAsync(provider, codexAgent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.Codex, new ProviderEndpoint("https://codex-0167.local/secret-path-0167", null))))));
        withoutKey.Code.ShouldBe("provider_endpoint_key_required");
        withoutKey.Message.ShouldNotContain("secret-path-0167");
        withoutKey.Message.ShouldNotContain(C0167Sentinel);

        var missing = await Should.ThrowAsync<ConflictException>(() => ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, "c0167-missing-key"))))));
        missing.Code.ShouldBe("api_key_not_found");
        missing.Message.ShouldContain("c0167-missing-key");
        missing.Message.ShouldNotContain(C0167Sentinel);
        missing.Message.ShouldNotContain(C0167Path);

        var userinfo = await Should.ThrowAsync<ValidationException>(() => ResolveAsync(provider, agent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint("https://user:pw-0167@proxy-0167.local/", C0167KeyName))))));
        userinfo.Code.ShouldBe("provider_endpoint_base_url_userinfo");
        userinfo.Message.ShouldNotContain("pw-0167");
        string.Join(" ", userinfo.Errors.Values.SelectMany(v => v)).ShouldNotContain("pw-0167");

        // The DTO echoes the URL and the name (it has no value field); the record's prose form is
        // host and name only, so an interpolated or structured log of the setting cannot leak a path.
        var dto = ProviderEndpointMapping.ToDto(endpoint);
        var json = JsonSerializer.Serialize(dto);
        json.ShouldContain(C0167Url);
        json.ShouldContain(C0167KeyName);
        json.ShouldNotContain(C0167Sentinel);
        ProviderEndpointMapping.ToRecord(dto).ShouldBe(endpoint);
        var prose = endpoint.ToString();
        prose.ShouldContain(C0167Host);
        prose.ShouldContain(C0167KeyName);
        prose.ShouldNotContain(C0167Path);
        prose.ShouldNotContain(C0167Sentinel);

        // The Codex argv on the same store: the value is in the env and nowhere in the arguments.
        var codexResolved = await ResolveAsync(provider, codexAgent, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.Codex, new ProviderEndpoint(C0167Url, C0167KeyName)))));
        codexResolved.Spec.Env["OPENAI_API_KEY"].ShouldBe(C0167Sentinel);
        string.Join("\n", codexResolved.Spec.Args).ShouldNotContain(C0167Sentinel);
        string.Join("\n", codexResolved.Spec.Args).ShouldNotContain("{{key:");

        AssertNothingLeaked(log);
    }

    [Test]
    public async Task C0167_unsupported_kind_refuses_at_resolve()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (openCode, _) = await SeedProfileAsync(provider, AgentKind.OpenCode);
        var endpointJson = ProviderEndpoint.Serialize(new ProviderEndpoint(C0167Url, C0167KeyName));

        // Endpoint mode on an OpenCode agent: 409 naming the kind, before any environment is built.
        var own = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "OpenCode-own",
            TuiProfileId = openCode.Id,
            ProviderEndpointMode = ProviderEndpointMode.Endpoint,
            ProviderEndpointJson = endpointJson,
        };
        var ownRefused = await Should.ThrowAsync<ConflictException>(() => ResolveAsync(provider, own));
        ownRefused.Code.ShouldBe("provider_endpoint_unsupported_kind");
        ownRefused.Message.ShouldContain("OpenCode");
        ownRefused.Message.ShouldNotContain(C0167Path);

        // A project entry for OpenCode written around the API (ParseMap keeps any enum name) refuses
        // the same way on the Inherit path.
        var inherit = new Agent { Id = Guid.NewGuid(), Name = "OpenCode-inherit", TuiProfileId = openCode.Id };
        var projectRefused = await Should.ThrowAsync<ConflictException>(() => ResolveAsync(provider, inherit, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.OpenCode, new ProviderEndpoint(C0167Url, C0167KeyName))))));
        projectRefused.Code.ShouldBe("provider_endpoint_unsupported_kind");

        // A project's Claude endpoint never breaks its OpenCode agents.
        var untouched = await ResolveAsync(provider, inherit, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, C0167KeyName)))));
        untouched.Spec.Env.ShouldNotContainKey("ANTHROPIC_BASE_URL");
        untouched.Spec.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // Endpoint mode with nothing stored (a hand-edited row) fails loud rather than routing
        // silently through the project or directly, whatever the kind.
        var (claude, _) = await SeedProfileAsync(provider, AgentKind.ClaudeCode, displayName: "Claude for hand-edited row");
        var halfSet = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Half-set",
            TuiProfileId = claude.Id,
            ProviderEndpointMode = ProviderEndpointMode.Endpoint,
            ProviderEndpointJson = null,
        };
        var halfSetRefused = await Should.ThrowAsync<ConflictException>(() => ResolveAsync(provider, halfSet, new AgentLaunchOptions(
            Cols: 120,
            Rows: 30,
            ProjectProviderEndpoints: C0167Map((AgentKind.ClaudeCode, new ProviderEndpoint(C0167Url, C0167KeyName))))));
        halfSetRefused.Code.ShouldBe("provider_endpoint_required");
    }

    private static async Task<ResolvedAgentTuiLaunch> ResolveAsync(
        ServiceProvider provider,
        Agent agent,
        AgentLaunchOptions? options = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>();
        return await resolver.ResolveForAgentAsync(
            agent,
            options ?? new AgentLaunchOptions(Cols: 120, Rows: 30),
            CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        RecordingLaunchSecretProtector? protector = null,
        bool withApiKeys = false,
        List<RecordingLogEntry>? log = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            connectionString));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(protector ?? new RecordingLaunchSecretProtector());
        services.AddSingleton<IAgentTuiSecretProtector>(sp =>
            sp.GetRequiredService<RecordingLaunchSecretProtector>());
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<AgentTuiLaunchResolver>();
        if (withApiKeys)
        {
            // CARD-0167 S3: a fake Data-Protection store so {{key:NAME}} resolves to a sentinel
            // VALUE the tests can prove reaches the child env and nothing else.
            services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
            services.AddScoped<ApiKeyEnvResolver>();
            services.AddSingleton<ILogger<ApiKeyEnvResolver>>(new RecordingLogger<ApiKeyEnvResolver>(log ?? []));
        }
        if (log is not null)
            services.AddSingleton<ILogger<AgentTuiLaunchResolver>>(new RecordingLogger<AgentTuiLaunchResolver>(log));
        return services.BuildServiceProvider();
    }

    private static async Task SeedApiKeyAsync(ServiceProvider provider, string name, string value, Guid? projectId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = id,
            Name = name,
            ProjectId = projectId,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(id, value),
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AssertNoProfilesAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTuiProfiles.AnyAsync()).ShouldBeFalse(
            "each resolver test must use a new schema, so profile seeds cannot leak to another test");
    }

    private static async Task<(AgentTuiProfile Profile, AgentTuiProfileRevision Revision)> SeedProfileAsync(
        ServiceProvider provider,
        AgentKind kind,
        string? displayName = null,
        bool enabled = true,
        bool isDefault = false,
        AgentTuiAuthenticationMode authenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
        string[]? secretNames = null,
        string[]? models = null,
        string canary = "canary-secret",
        string? modelArgumentName = "--model",
        string[]? arguments = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<RecordingLaunchSecretProtector>();
        var now = DateTime.UtcNow;
        displayName ??= $"Launch profile {Guid.NewGuid():N}";
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Kind = kind,
            IsEnabled = enabled,
            IsDefault = isDefault,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = "pwsh.exe",
            ArgumentsJson = JsonSerializer.Serialize(arguments ?? ["--auto", "--mini"]),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            WorkingDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            AuthenticationMode = authenticationMode,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = JsonSerializer.Serialize(secretNames ?? []),
            ModelArgumentName = modelArgumentName,
            Guidance = "Launch test",
            CreatedAt = now
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();

        profile.ActiveRevisionId = revision.Id;
        foreach (var secretName in secretNames ?? [])
        {
            db.AgentTuiSecrets.Add(new AgentTuiSecret
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Name = secretName,
                Ciphertext = protector.Protect(profile.Id, secretName, canary),
                ProtectionVersion = "v1",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        foreach (var model in models ?? [])
        {
            db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = model,
                DisplayName = model,
                Source = AgentTuiModelSource.Operator,
                Availability = AgentTuiModelAvailability.Verified,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return (profile, revision);
    }
}

public sealed class RecordingLaunchSecretProtector : IAgentTuiSecretProtector
{
    private int _unprotectCalls;
    public int UnprotectCalls => Volatile.Read(ref _unprotectCalls);

    public string Protect(Guid profileId, string environmentName, string plaintext) =>
        $"cipher:{profileId:N}:{environmentName}:{plaintext}";

    public string Unprotect(Guid profileId, string environmentName, string protectedValue)
    {
        Interlocked.Increment(ref _unprotectCalls);
        var prefix = $"cipher:{profileId:N}:{environmentName}:";
        if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
            throw new CryptographicException("Purpose mismatch.");
        return protectedValue[prefix.Length..];
    }
}

internal sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
{
    public OptionsMonitorStub(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
