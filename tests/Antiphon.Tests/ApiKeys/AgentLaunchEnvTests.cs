using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S2 — the per-agent launch environment, and the MERGE ORDER it lives in.
///
/// <para>The ordering is the load-bearing part: definition/profile env, then the agent's own launch
/// env, then <c>ExtraEnv</c> last. <c>ExtraEnv</c> is Antiphon's own orchestration block, so a
/// per-agent override of <c>ANTIPHON_SESSION_ID</c> would be a self-inflicted CARD-0006 — an agent
/// bound to another session's identity, tailing a transcript that is not its own.</para>
/// </summary>
[Category("Unit")]
public class AgentLaunchEnvTests
{
    // ---- the merge order --------------------------------------------------------------------------

    [Test]
    public void the_agent_env_beats_the_definition_env_and_loses_to_the_orchestration_block()
    {
        var registry = NewRegistry(definitionEnv: new Dictionary<string, string>
        {
            ["FROM_DEFINITION"] = "definition",
            ["CONTESTED"] = "definition",
        });

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            AgentEnv: new Dictionary<string, string>
            {
                ["CONTESTED"] = "agent",
                ["FROM_AGENT"] = "agent",
                ["ANTIPHON_SESSION_ID"] = "hijacked",
            },
            ExtraEnv: new Dictionary<string, string>
            {
                ["ANTIPHON_SESSION_ID"] = "the-real-session",
            }));

        spec.Env["FROM_DEFINITION"].ShouldBe("definition");
        spec.Env["FROM_AGENT"].ShouldBe("agent");
        spec.Env["CONTESTED"].ShouldBe(
            "agent", "the agent's own setting is more specific than a shared definition");
        spec.Env["ANTIPHON_SESSION_ID"].ShouldBe(
            "the-real-session",
            "ExtraEnv is Antiphon's orchestration identity and must outrank anything an operator "
            + "typed into an agent's settings");
    }

    [Test]
    public void an_agent_env_entry_is_present_when_nothing_contests_it()
    {
        var registry = NewRegistry();

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            AgentEnv: new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-from-agent" }));

        spec.Env["ANTHROPIC_API_KEY"].ShouldBe("sk-from-agent");
    }

    [Test]
    public void no_agent_env_changes_nothing_about_a_launch()
    {
        // Every agent row that existed before this column carries "{}", so this is the shape of
        // every launch in the installation on the day it ships.
        var registry = NewRegistry();

        var withNull = registry.Resolve("test", new AgentLaunchOptions(Cwd: "C:\\tmp"));
        var withEmpty = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp", AgentEnv: AgentLaunchEnv.Empty));

        withEmpty.Env.OrderBy(e => e.Key).ShouldBe(withNull.Env.OrderBy(e => e.Key));
    }

    // ---- the stored field -------------------------------------------------------------------------

    [Test]
    public void a_round_trip_through_the_stored_json_preserves_the_entries()
    {
        var env = new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = "{{key:anthropic-maven}}",
            ["FOO"] = "literal",
        };

        var parsed = AgentLaunchEnv.Parse(AgentLaunchEnv.Serialize(env));

        parsed["ANTHROPIC_API_KEY"].ShouldBe("{{key:anthropic-maven}}");
        parsed["FOO"].ShouldBe("literal");
    }

    [Test]
    [Arguments("")]
    [Arguments("{}")]
    [Arguments("not json at all")]
    [Arguments("[1,2,3]")]
    [Arguments(null)]
    public void unreadable_stored_json_reads_as_no_environment_rather_than_killing_the_launch(string? json)
    {
        // Deliberate: failing every launch of an agent whose column got corrupted would take a
        // working agent permanently offline over a field it may not even use, and the terminal that
        // could fix it is the one that just died. Writes are validated instead.
        AgentLaunchEnv.Parse(json).ShouldBeEmpty();
    }

    [Test]
    public void the_agents_own_row_is_the_default_source_of_its_launch_env()
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            LaunchEnvJson = """{"ANTHROPIC_API_KEY":"{{key:anthropic-maven}}"}""",
        };

        AgentLaunchEnv.ParseForAgent(agent)["ANTHROPIC_API_KEY"].ShouldBe("{{key:anthropic-maven}}");
        AgentLaunchEnv.ParseForAgent(null).ShouldBeEmpty();
    }

    // ---- write-time validation --------------------------------------------------------------------

    [Test]
    public void a_placeholder_in_a_VARIABLE_NAME_is_refused()
    {
        // Only values are a resolution surface. A variable literally called {{key:X}} would be
        // exported as-is and then refused by the launch tripwire, which is a worse place to find out.
        var ex = Should.Throw<ValidationException>(() => AgentLaunchEnv.Validate(
            new Dictionary<string, string> { ["{{key:x}}"] = "value" }));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("VALUE"));
    }

    [Test]
    public void a_placeholder_in_a_VALUE_is_accepted_unresolved()
    {
        // Stored as typed. Resolution happens at LAUNCH, so a key added after the agent was
        // configured starts working with no edit to the agent.
        var validated = AgentLaunchEnv.Validate(
            new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "{{key:anthropic-maven}}" });

        validated["ANTHROPIC_API_KEY"].ShouldBe("{{key:anthropic-maven}}");
    }

    [Test]
    public void a_malformed_placeholder_is_refused_at_write_time_naming_the_variable()
    {
        var ex = Should.Throw<ValidationException>(() => AgentLaunchEnv.Validate(
            new Dictionary<string, string> { ["K"] = "{{key:has space}}" }));

        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("K"));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("HAS=EQUALS")]
    public void an_unusable_variable_name_is_refused(string name)
    {
        Should.Throw<ValidationException>(() => AgentLaunchEnv.Validate(
            new Dictionary<string, string> { [name] = "value" }));
    }

    [Test]
    public void an_oversize_value_is_refused_without_echoing_it()
    {
        var ex = Should.Throw<ValidationException>(() => AgentLaunchEnv.Validate(
            new Dictionary<string, string> { ["K"] = new('x', ApiKeyNaming.MaxValueLength + 1) }));

        ex.Errors.Values.SelectMany(e => e).ShouldNotContain(e => e.Contains("xxxxxxxxxx"));
    }

    [Test]
    public void names_are_trimmed_and_an_empty_environment_is_the_shared_empty()
    {
        AgentLaunchEnv.Validate(new Dictionary<string, string> { ["  SPACED  "] = "v" })
            .ShouldContainKey("SPACED");
        AgentLaunchEnv.Validate(null).ShouldBeEmpty();
        AgentLaunchEnv.Validate(new Dictionary<string, string>()).ShouldBeEmpty();
    }

    // ---- CARD-0106 gap 1/2: launch-time override + project default merge order -----------------

    [Test]
    public void the_launch_override_beats_the_agent_env_and_loses_to_the_orchestration_block()
    {
        var registry = NewRegistry();

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            AgentEnv: new Dictionary<string, string>
            {
                ["CONTESTED"] = "agent",
                ["FROM_AGENT"] = "agent",
            },
            ExtraEnv: new Dictionary<string, string>
            {
                ["ANTIPHON_SESSION_ID"] = "the-real-session",
            },
            LaunchEnvOverride: new Dictionary<string, string>
            {
                ["CONTESTED"] = "override",
                ["FROM_OVERRIDE"] = "override",
                ["ANTIPHON_SESSION_ID"] = "hijacked",
            }));

        spec.Env["FROM_AGENT"].ShouldBe("agent");
        spec.Env["FROM_OVERRIDE"].ShouldBe("override");
        spec.Env["CONTESTED"].ShouldBe(
            "override", "the launch-time overlay is more specific than the agent's stored env");
        spec.Env["ANTIPHON_SESSION_ID"].ShouldBe(
            "the-real-session",
            "ExtraEnv is Antiphon's orchestration identity and must outrank a launch-time override, "
            + "even when the override maliciously names ANTIPHON_SESSION_ID");
    }

    [Test]
    public void the_agents_own_launch_env_beats_a_project_default()
    {
        var registry = NewRegistry(definitionEnv: new Dictionary<string, string>
        {
            ["FROM_DEFINITION"] = "definition",
            ["CONTESTED_DEF_PROJ"] = "definition",
        });

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            AgentEnv: new Dictionary<string, string>
            {
                ["CONTESTED_PROJ_AGENT"] = "agent",
                ["FROM_AGENT"] = "agent",
            },
            ProjectDefaultEnv: new Dictionary<string, string>
            {
                ["CONTESTED_DEF_PROJ"] = "project",
                ["CONTESTED_PROJ_AGENT"] = "project",
                ["FROM_PROJECT"] = "project",
            }));

        spec.Env["FROM_DEFINITION"].ShouldBe("definition");
        spec.Env["FROM_PROJECT"].ShouldBe("project");
        spec.Env["FROM_AGENT"].ShouldBe("agent");
        spec.Env["CONTESTED_DEF_PROJ"].ShouldBe(
            "project", "a project default is more specific than a shared definition");
        spec.Env["CONTESTED_PROJ_AGENT"].ShouldBe(
            "agent", "the agent's own setting is more specific than a project default");
    }

    [Test]
    public void five_layers_contest_pairwise_and_the_more_specific_one_wins_each_time()
    {
        // The single test that fails loudest if a future edit reorders a merge loop.
        // definition < project default < agent < override < ExtraEnv.
        var registry = NewRegistry(definitionEnv: new Dictionary<string, string>
        {
            ["A"] = "definition",
            ["B"] = "definition",
        });

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            AgentEnv: new Dictionary<string, string>
            {
                ["C"] = "agent",
                ["D"] = "agent",
            },
            ExtraEnv: new Dictionary<string, string>
            {
                ["E"] = "extra",
            },
            LaunchEnvOverride: new Dictionary<string, string>
            {
                ["D"] = "override",
                ["E"] = "override",
            },
            ProjectDefaultEnv: new Dictionary<string, string>
            {
                ["B"] = "project",
                ["C"] = "project",
            }));

        spec.Env["A"].ShouldBe("definition");
        spec.Env["B"].ShouldBe("project");
        spec.Env["C"].ShouldBe("agent");
        spec.Env["D"].ShouldBe("override");
        spec.Env["E"].ShouldBe("extra");
    }

    [Test]
    public void inherited_env_beats_a_project_default_and_loses_to_the_agent()
    {
        var registry = NewRegistry();

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            ProjectDefaultEnv: new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "project",
                ["FROM_PROJECT"] = "project",
            },
            InheritedEnv: new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "inherited",
                ["FROM_INHERITED"] = "inherited",
                ["CONTESTED_INH_AGENT"] = "inherited",
            },
            AgentEnv: new Dictionary<string, string>
            {
                ["CONTESTED_INH_AGENT"] = "agent",
                ["FROM_AGENT"] = "agent",
            }));

        spec.Env["FROM_PROJECT"].ShouldBe("project");
        spec.Env["FROM_INHERITED"].ShouldBe("inherited");
        spec.Env["X_LLM_PROJECT"].ShouldBe("inherited");
        spec.Env["CONTESTED_INH_AGENT"].ShouldBe("agent");
        spec.Env["FROM_AGENT"].ShouldBe("agent");
    }

    [Test]
    public void six_layers_contest_and_inherited_sits_between_project_and_agent()
    {
        // definition < project default < inherited < agent < override < ExtraEnv.
        var registry = NewRegistry(definitionEnv: new Dictionary<string, string>
        {
            ["A"] = "definition",
            ["B"] = "definition",
        });

        var spec = registry.Resolve("test", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            ProjectDefaultEnv: new Dictionary<string, string>
            {
                ["B"] = "project",
                ["C"] = "project",
            },
            InheritedEnv: new Dictionary<string, string>
            {
                ["C"] = "inherited",
                ["D"] = "inherited",
            },
            AgentEnv: new Dictionary<string, string>
            {
                ["D"] = "agent",
                ["E"] = "agent",
            },
            LaunchEnvOverride: new Dictionary<string, string>
            {
                ["E"] = "override",
                ["F"] = "override",
            },
            ExtraEnv: new Dictionary<string, string>
            {
                ["F"] = "extra",
            }));

        spec.Env["A"].ShouldBe("definition");
        spec.Env["B"].ShouldBe("project");
        spec.Env["C"].ShouldBe("inherited");
        spec.Env["D"].ShouldBe("agent");
        spec.Env["E"].ShouldBe("override");
        spec.Env["F"].ShouldBe("extra");
    }

    [Test]
    public void FilterTo_keeps_only_allowlisted_names_and_ProjectionsEqual_is_ordinal()
    {
        var names = new LlmEnvInheritanceSettings().Names;
        var env = new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["ANTHROPIC_BASE_URL"] = "http://localhost:10746",
            ["UNRELATED"] = "drop-me",
            ["GROK_CLI_CHAT_PROXY_BASE_URL"] = "http://localhost:10746/v1",
        };

        var filtered = AgentLaunchEnv.FilterTo(env, names);
        filtered.ShouldContainKey("X_LLM_PROJECT");
        filtered.ShouldContainKey("ANTHROPIC_BASE_URL");
        filtered.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        filtered.ShouldNotContainKey("UNRELATED");

        AgentLaunchEnv.ProjectionsEqual(env, filtered, names).ShouldBeTrue();
        AgentLaunchEnv.ProjectionsEqual(
            env,
            new Dictionary<string, string> { ["X_LLM_PROJECT"] = "Other" },
            names).ShouldBeFalse();
        AgentLaunchEnv.ProjectionsEqual(null, new Dictionary<string, string>(), names).ShouldBeTrue();

        AgentLaunchEnv.DifferingNames(
            env,
            new Dictionary<string, string> { ["X_LLM_PROJECT"] = "Other", ["ANTHROPIC_BASE_URL"] = "http://localhost:10746" },
            names).ShouldBe(["GROK_CLI_CHAT_PROXY_BASE_URL", "X_LLM_PROJECT"]);
    }

    [Test]
    public void an_override_can_deliberately_set_a_kind_default_that_would_otherwise_be_gap_filled()
    {
        var registry = NewRegistry();
        // The test definition is Raw, which has no kind defaults. Use ClaudeCode so the
        // DISABLE_AUTOUPDATER gap-fill is in play.
        var claude = new AgentDefinition { Kind = nameof(AgentKind.ClaudeCode), Exe = "claude.exe" };
        var claudeRegistry = new AgentRegistry(new OptionsMonitorStub(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = claude },
        }));

        var spec = claudeRegistry.Resolve("claude", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            LaunchEnvOverride: new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "0" }));

        spec.Env["DISABLE_AUTOUPDATER"].ShouldBe("0");
    }

    [Test]
    public void ValidateOverride_refuses_ANTIPHON_prefixed_names_including_lowercase()
    {
        var upper = Should.Throw<ValidationException>(() => AgentLaunchEnv.ValidateOverride(
            new Dictionary<string, string> { ["ANTIPHON_TASK_TOKEN"] = "stolen" }));
        upper.StatusCode.ShouldBe(422);
        upper.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("ANTIPHON_TASK_TOKEN"));
        upper.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("orchestration plumbing"));

        var lower = Should.Throw<ValidationException>(() => AgentLaunchEnv.ValidateOverride(
            new Dictionary<string, string> { ["antiphon_x"] = "nope" }));
        lower.StatusCode.ShouldBe(422);
        lower.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("antiphon_x"));
    }

    [Test]
    public void ValidateOverride_accepts_ordinary_names_and_placeholder_values()
    {
        var validated = AgentLaunchEnv.ValidateOverride(
            new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "http://proxy:8080",
                ["ANTHROPIC_API_KEY"] = "{{key:proxy-key}}",
            });

        validated["ANTHROPIC_BASE_URL"].ShouldBe("http://proxy:8080");
        validated["ANTHROPIC_API_KEY"].ShouldBe("{{key:proxy-key}}");
    }

    [Test]
    public void Validate_still_accepts_ANTIPHON_names_the_existing_agent_PATCH_must_not_422()
    {
        // Verdict 2: the shipped PATCH launchEnv surface keeps accepted-but-inert reserved names.
        // Refusing them here would 422 an already-stored config on its next unrelated save.
        var validated = AgentLaunchEnv.Validate(
            new Dictionary<string, string> { ["ANTIPHON_SESSION_ID"] = "stored-and-inert" });

        validated["ANTIPHON_SESSION_ID"].ShouldBe("stored-and-inert");
    }

    // ---- CARD-0167 S3: provider endpoint ranks 3b and 5b on the legacy registry path -----------

    private const string C0167Url = "https://proxy-0167.local";
    private const string C0167KeyName = "c0167-proxy-key";
    private const string C0167Placeholder = "{{key:" + C0167KeyName + "}}";

    [Test]
    public void C0167_registry_applies_project_and_agent_endpoints_in_rank_order()
    {
        var registry = KindRegistry("claude", AgentKind.ClaudeCode, "claude.exe",
            definitionEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-definition" });
        var map = new Dictionary<AgentKind, ProviderEndpoint>
        {
            [AgentKind.ClaudeCode] = new(C0167Url + "/", C0167KeyName),
        };
        var box = new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-box" };

        // No setting: byte-identical to the definition plus the kind defaults, no argument.
        var baseline = registry.Resolve("claude", new AgentLaunchOptions(Cwd: "C:\\tmp", ProjectProviderEndpoints: new Dictionary<AgentKind, ProviderEndpoint>()));
        Lines(baseline.Env).ShouldBe(
        [
            "ANTHROPIC_BASE_URL=http://from-definition",
            "CLAUDECODE=",
            "CLAUDE_CODE_BRIDGE_SESSION_ID=",
            "CLAUDE_CODE_CHILD_SESSION=",
            "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1",
            "CLAUDE_CODE_ENTRYPOINT=",
            "CLAUDE_CODE_SESSION_ID=",
            "DISABLE_AUTOUPDATER=1",
        ]);
        baseline.Args.ShouldBeEmpty();

        // Rank 3b beats the definition and the generic project box.
        var project = registry.Resolve("claude", new AgentLaunchOptions(
            Cwd: "C:\\tmp", ProjectDefaultEnv: box, ProjectProviderEndpoints: map));
        project.Env["ANTHROPIC_BASE_URL"].ShouldBe(C0167Url, "trailing slash trimmed");
        project.Env["ANTHROPIC_API_KEY"].ShouldBe(C0167Placeholder);
        project.Env["DISABLE_AUTOUPDATER"].ShouldBe("1", "kind defaults still gap-fill around the projection");
        project.Args.ShouldBeEmpty();

        // Rank 4 beats 3b; rank 5 beats 4.
        registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp",
                ProjectProviderEndpoints: map,
                InheritedEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-inherited" }))
            .Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-inherited");
        registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp",
                ProjectProviderEndpoints: map,
                InheritedEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-inherited" },
                AgentEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-agent" }))
            .Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-agent");

        // Rank 5b beats 5, and the project's key does not follow the agent's URL-only endpoint.
        var own = registry.Resolve("claude", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            ProjectProviderEndpoints: map,
            AgentEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://from-agent" },
            AgentProviderEndpointMode: ProviderEndpointMode.Endpoint,
            AgentProviderEndpoint: new ProviderEndpoint("https://agent-0167.local", null)));
        own.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://agent-0167.local");
        own.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // Rank 6 beats 5b; rank 7 beats 6.
        registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp",
                ProjectProviderEndpoints: map,
                AgentProviderEndpointMode: ProviderEndpointMode.Endpoint,
                AgentProviderEndpoint: new ProviderEndpoint("https://agent-0167.local", null),
                LaunchEnvOverride: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://override" }))
            .Env["ANTHROPIC_BASE_URL"].ShouldBe("http://override");
        registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp",
                ProjectProviderEndpoints: map,
                AgentProviderEndpointMode: ProviderEndpointMode.Endpoint,
                AgentProviderEndpoint: new ProviderEndpoint("https://agent-0167.local", null),
                LaunchEnvOverride: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://override" },
                ExtraEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://extra" }))
            .Env["ANTHROPIC_BASE_URL"].ShouldBe("http://extra");

        // Direct suppresses 3b and removes nothing.
        var direct = registry.Resolve("claude", new AgentLaunchOptions(
            Cwd: "C:\\tmp", ProjectDefaultEnv: box, ProjectProviderEndpoints: map,
            AgentProviderEndpointMode: ProviderEndpointMode.Direct));
        direct.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-box");
        direct.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");

        // Endpoint mode with nothing to project is refused rather than silently inherited.
        Should.Throw<ConflictException>(() => registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp", ProjectProviderEndpoints: map, AgentProviderEndpointMode: ProviderEndpointMode.Endpoint)))
            .Code.ShouldBe("provider_endpoint_required");

        // The Raw test definition with a map that has no Raw entry: nothing projected, no throw.
        NewRegistry().Resolve("test", new AgentLaunchOptions(Cwd: "C:\\tmp", ProjectProviderEndpoints: map))
            .Env.ShouldNotContainKey("ANTHROPIC_BASE_URL");

        // Codex: the five -c pairs go after ExtraArgs and before the tier's --model, and the tripwire
        // passes over every argument.
        var codex = KindRegistry("codex", AgentKind.Codex, "codex", argsTemplate: ["exec"]);
        var spec = codex.Resolve("codex", new AgentLaunchOptions(
            Cwd: "C:\\tmp",
            ExtraArgs: ["-c", "x=1"],
            TierModelAlias: "gpt-5",
            ProjectProviderEndpoints: new Dictionary<AgentKind, ProviderEndpoint>
            {
                [AgentKind.Codex] = new("https://codex-0167.local", C0167KeyName),
            }));
        spec.Args.ShouldBe(
        [
            "exec",
            "-c", "x=1",
            "-c", "model_providers.antiphon.name=\"Antiphon endpoint\"",
            "-c", "model_providers.antiphon.base_url=\"https://codex-0167.local/v1\"",
            "-c", "model_providers.antiphon.env_key=\"OPENAI_API_KEY\"",
            "-c", "model_providers.antiphon.wire_api=\"responses\"",
            "-c", "model_provider=antiphon",
            "--model", "gpt-5",
        ]);
        spec.Env["OPENAI_API_KEY"].ShouldBe(C0167Placeholder);
        for (var i = 0; i < spec.Args.Count; i++)
            ApiKeyPlaceholder.EnsureAbsent(spec.Args[i], $"registry argument {i}");
    }

    [Test]
    public void C0167_pool_match_includes_the_agent_projection()
    {
        // CARD-0260's warm-pool predicate compares inherit-list names. With a project endpoint the
        // process was launched with projected names its LaunchEnvJson never carried, so both sides
        // of the comparison include their projection: rank 3b, then the stored env, then rank 5b.
        var names = new LlmEnvInheritanceSettings().Names;
        var map = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = new(C0167Url, C0167KeyName) };
        var task = new AgentTask { AgentKind = AgentKind.ClaudeCode, InheritedLaunchEnvJson = "{}" };

        // 1. A delegate LAUNCHED under the project endpoint and a task on the same project: match.
        //    (Repair of Review e4b591eb D1: the match is against the stamp the cold launch wrote, not a
        //    re-read of the project map, so a delegate with no stamp is one launched with no proxy.)
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: Stamp(map)), task, map, names).ShouldBeTrue();

        // 2. A delegate launched with its own endpoint is not what this task would launch with.
        var own = new ProviderEndpoint("https://agent-0167.local", null);
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(ProviderEndpointMode.Endpoint, own, launchedWith: Stamp(map, ProviderEndpointMode.Endpoint, own)),
                task, map, names)
            .ShouldBeFalse();

        // 3. A Direct delegate carries no proxy (its stamp is null); the task expects the project's.
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(ProviderEndpointMode.Direct, launchedWith: Stamp(map, ProviderEndpointMode.Direct)), task, map, names)
            .ShouldBeFalse();

        // 4. The task's inherited routing points elsewhere (rank 4 beats 3b on both sides), and a
        //    delegate whose stamped snapshot points to the same elsewhere matches it.
        var elsewhere = new AgentTask
        {
            AgentKind = AgentKind.ClaudeCode,
            InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://elsewhere" }),
        };
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: Stamp(map)), elsewhere, map, names).ShouldBeFalse();
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(
                    launchEnv: new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://elsewhere" },
                    launchedWith: Stamp(map)),
                elsewhere, map, names)
            .ShouldBeTrue();

        // 5. No endpoint anywhere: two empties still match (CARD-0260 unchanged); a Grok-only map is
        //    nothing for a Claude pair.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(), task, null, names).ShouldBeTrue();
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(), task, new Dictionary<AgentKind, ProviderEndpoint>(), names).ShouldBeTrue();
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(), task,
                new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.Grok] = new("https://grok-0167.local", null) },
                names)
            .ShouldBeTrue();

        // 6. Codex: the key placeholder compares as a NAME on both sides; nothing is resolved here.
        var codexMap = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.Codex] = new("https://codex-0167.local", C0167KeyName) };
        var codexTask = new AgentTask { AgentKind = AgentKind.Codex, InheritedLaunchEnvJson = "{}" };
        var codexStamp = Stamp(codexMap, kind: AgentKind.Codex);
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(kind: AgentKind.Codex, launchedWith: codexStamp), codexTask, codexMap, names)
            .ShouldBeTrue();
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(
                    kind: AgentKind.Codex,
                    launchEnv: new Dictionary<string, string> { ["OPENAI_API_KEY"] = "{{key:other-0167}}" },
                    launchedWith: codexStamp),
                codexTask, codexMap, names)
            .ShouldBeFalse();

        // 7. An unsupported-kind entry written around the API projects nothing in the predicate; the
        //    launch is where it is refused.
        AgentTaskDispatcher.InheritedEnvMatchesPool(
                Delegate(kind: AgentKind.OpenCode),
                new AgentTask { AgentKind = AgentKind.OpenCode, InheritedLaunchEnvJson = "{}" },
                new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.OpenCode] = new(C0167Url, C0167KeyName) },
                names)
            .ShouldBeTrue();

    }

    [Test]
    public void C0167_pool_match_compares_the_launch_time_projection_not_the_current_map()
    {
        // Review e4b591eb D1: a warm process keeps the routing it launched with (the dispatcher's own
        // rule at its cold-launch stamp), so the predicate must compare the projection the process GOT
        // with the one this task would get NOW. Computing both sides from the current map matched a
        // proxy-less process to a task that expects the proxy, and a process still holding the
        // resolved key to a task on a project whose endpoint was cleared or changed.
        var names = new LlmEnvInheritanceSettings().Names;
        var task = new AgentTask { AgentKind = AgentKind.ClaudeCode, InheritedLaunchEnvJson = "{}" };
        var f = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = new(C0167Url, C0167KeyName) };
        var fPrime = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = new("https://proxy-prime-0167.local", C0167KeyName) };
        var fOtherKey = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = new(C0167Url, "c0167-rotated-name") };
        var launchedWithF = Stamp(f);
        launchedWithF.ShouldNotBeNull();

        // The stamp is names, the URL and the placeholder: the key NAME, never a value, and nothing
        // the resolver would substitute later.
        launchedWithF.ShouldContain("ANTHROPIC_BASE_URL");
        launchedWithF.ShouldContain(C0167Placeholder);
        launchedWithF.ShouldNotContain("sentinel");

        // (a) Launched while the project had NO endpoint; an operator then set F. The process runs
        //     direct to the provider, so it is not what this task would launch with.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: null), task, f, names).ShouldBeFalse();

        // (b) Launched with F (its environment holds the resolved key); the endpoint was then cleared.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: launchedWithF), task, ProviderEndpoint.NoEndpoints, names).ShouldBeFalse();
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: launchedWithF), task, null, names).ShouldBeFalse();

        // (c) Launched with F; the endpoint is now F' (another host), or the same host under another key NAME.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: launchedWithF), task, fPrime, names).ShouldBeFalse();
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: launchedWithF), task, fOtherKey, names).ShouldBeFalse();

        // (d) Launched with F; the endpoint is still F: match.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: launchedWithF), task, f, names).ShouldBeTrue();

        // Codex: F and F' differ only in the -c argv tail (same key NAME, same env names), which the
        // inherit-list env view cannot see; the stamp carries the tail, so this is still no match.
        var codexTask = new AgentTask { AgentKind = AgentKind.Codex, InheritedLaunchEnvJson = "{}" };
        var codexF = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.Codex] = new("https://codex-0167.local", C0167KeyName) };
        var codexFPrime = new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.Codex] = new("https://codex-prime-0167.local", C0167KeyName) };
        var launchedWithCodexF = Stamp(codexF, kind: AgentKind.Codex);
        launchedWithCodexF.ShouldNotBeNull();
        launchedWithCodexF.ShouldContain("model_providers.antiphon.base_url");
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(kind: AgentKind.Codex, launchedWith: launchedWithCodexF), codexTask, codexFPrime, names).ShouldBeFalse();
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(kind: AgentKind.Codex, launchedWith: launchedWithCodexF), codexTask, codexF, names).ShouldBeTrue();

        // A row that predates the stamp (null) is a process launched with no projection: it still
        // serves a task on a project with no endpoint, and nothing else.
        AgentTaskDispatcher.InheritedEnvMatchesPool(Delegate(launchedWith: null), task, ProviderEndpoint.NoEndpoints, names).ShouldBeTrue();
    }

    /// <summary>What a cold launch stamps on the pool row for these inputs (the predicate's agent side).</summary>
    private static string? Stamp(
        IReadOnlyDictionary<AgentKind, ProviderEndpoint>? map,
        ProviderEndpointMode mode = ProviderEndpointMode.Inherit,
        ProviderEndpoint? own = null,
        AgentKind kind = AgentKind.ClaudeCode) =>
        ProviderEndpointLayers.PoolFingerprint(kind, map, mode, own);

    private static Agent Delegate(
        ProviderEndpointMode mode = ProviderEndpointMode.Inherit,
        ProviderEndpoint? own = null,
        IReadOnlyDictionary<string, string>? launchEnv = null,
        AgentKind kind = AgentKind.ClaudeCode,
        string? launchedWith = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        IsPoolDelegate = true,
        ProviderEndpointMode = mode,
        ProviderEndpointJson = ProviderEndpoint.Serialize(own),
        LaunchEnvJson = AgentLaunchEnv.Serialize(launchEnv),
        PoolProviderProjectionJson = launchedWith,
    };

    private static string[] Lines(IReadOnlyDictionary<string, string> env) =>
        env.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}").ToArray();

    private static AgentRegistry KindRegistry(
        string name, AgentKind kind, string exe,
        IDictionary<string, string>? definitionEnv = null, string[]? argsTemplate = null)
    {
        var definition = new AgentDefinition { Kind = kind.ToString(), Exe = exe };
        if (argsTemplate is not null)
            definition.ArgsTemplate.AddRange(argsTemplate);
        if (definitionEnv is not null)
        {
            foreach (var (key, value) in definitionEnv)
                definition.Env[key] = value;
        }

        return new AgentRegistry(new OptionsMonitorStub(new AgentRegistrySettings
        {
            DefaultDefinition = name,
            Definitions = { [name] = definition },
        }));
    }

    private static AgentRegistry NewRegistry(IDictionary<string, string>? definitionEnv = null)
    {
        var definition = new AgentDefinition
        {
            Kind = nameof(AgentKind.Raw),
            Exe = "cmd.exe",
        };
        if (definitionEnv is not null)
        {
            foreach (var (key, value) in definitionEnv)
                definition.Env[key] = value;
        }

        return new AgentRegistry(new OptionsMonitorStub(new AgentRegistrySettings
        {
            DefaultDefinition = "test",
            Definitions = { ["test"] = definition },
        }));
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<AgentRegistrySettings>
    {
        public OptionsMonitorStub(AgentRegistrySettings value) => CurrentValue = value;

        public AgentRegistrySettings CurrentValue { get; }

        public AgentRegistrySettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AgentRegistrySettings, string?> listener) => null;
    }
}
