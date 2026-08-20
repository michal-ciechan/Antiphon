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
