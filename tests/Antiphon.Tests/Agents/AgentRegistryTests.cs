using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentRegistryTests
{
    private static AgentRegistry BuildRegistry(AgentRegistrySettings settings)
    {
        var monitor = new TestOptionsMonitor<AgentRegistrySettings>(settings);
        return new AgentRegistry(monitor);
    }

    private static AgentRegistrySettings WithClaudeAndRaw() => new()
    {
        DefaultDefinition = "claude",
        Definitions =
        {
            ["claude"] = new AgentDefinition
            {
                Kind = "ClaudeCode",
                Exe = "cl.bat",
                ArgsTemplate = new() { "--print" },
                Env = new() { ["BASE_ENV"] = "v1" },
            },
            ["raw"] = new AgentDefinition
            {
                Kind = "Raw",
                Exe = "pwsh.exe",
                ArgsTemplate = new() { "-NoLogo" },
            },
        },
    };

    [Test]
    public void LookupByName_returns_definition_when_present()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());
        var def = registry.LookupByName("claude");
        def.Exe.ShouldBe("cl.bat");
    }

    [Test]
    public void LookupByName_throws_NotFoundException_on_unknown_name()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());
        Should.Throw<NotFoundException>(() => registry.LookupByName("nope"));
    }

    [Test]
    public void LookupByName_throws_on_blank_name()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());
        Should.Throw<ArgumentException>(() => registry.LookupByName("  "));
    }

    [Test]
    public void Resolve_builds_launch_spec_from_definition_and_options()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(
            Cwd: "C:/work",
            Cols: 100,
            Rows: 40,
            ExtraArgs: new[] { "--debug" },
            ExtraEnv: new Dictionary<string, string> { ["EXTRA"] = "yes" }));

        spec.DefinitionName.ShouldBe("claude");
        spec.Kind.ShouldBe(AgentKind.ClaudeCode);
        spec.Exe.ShouldBe("cl.bat");
        spec.Args.ShouldBe(new[] { "--print", "--debug" });
        spec.Env["BASE_ENV"].ShouldBe("v1");
        spec.Env["EXTRA"].ShouldBe("yes");
        spec.Cwd.ShouldBe("C:/work");
        spec.Cols.ShouldBe(100);
        spec.Rows.ShouldBe(40);
    }

    [Test]
    public void Resolve_defaults_blank_cwd_to_current_directory()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(Cwd: null));

        spec.Cwd.ShouldBe(Environment.CurrentDirectory);
    }

    [Test]
    public void Resolve_extra_env_overrides_base_env()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(
            ExtraEnv: new Dictionary<string, string> { ["BASE_ENV"] = "overridden" }));

        spec.Env["BASE_ENV"].ShouldBe("overridden");
    }

    [Test]
    public void Resolve_disables_claude_auto_updater_by_default()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions());

        spec.Env["DISABLE_AUTOUPDATER"].ShouldBe("1");
    }

    [Test]
    public void Resolve_does_not_set_auto_updater_env_for_non_claude_kinds()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("raw", new AgentLaunchOptions());

        spec.Env.ContainsKey("DISABLE_AUTOUPDATER").ShouldBeFalse();
    }

    [Test]
    public void Resolve_lets_config_override_the_auto_updater_default()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(
            ExtraEnv: new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "0" }));

        spec.Env["DISABLE_AUTOUPDATER"].ShouldBe("0");
    }

    [Test]
    public void Resolve_forces_classic_renderer_for_claude_by_default()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions());

        spec.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("1");
    }

    [Test]
    public void Resolve_does_not_set_alternate_screen_env_for_non_claude_kinds()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("raw", new AgentLaunchOptions());

        spec.Env.ContainsKey("CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN").ShouldBeFalse();
    }

    [Test]
    public void Resolve_lets_config_override_the_alternate_screen_default()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(
            ExtraEnv: new Dictionary<string, string> { ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "0" }));

        spec.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("0");
    }

    // A spawned Claude agent must not inherit the launcher's nesting markers, or Claude treats it
    // as a child session: it ignores --session-id (forks to a self-chosen id) and the transcript
    // tailer — which follows <session-id>.jsonl — loses turn-end detection and reply routing.
    [Test]
    public void Resolve_scrubs_claude_nesting_markers_to_empty()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions());

        foreach (var marker in new[]
        {
            "CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID",
            "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDE_CODE_ENTRYPOINT",
        })
        {
            spec.Env.ContainsKey(marker).ShouldBeTrue($"{marker} must be present (as an empty override)");
            spec.Env[marker].ShouldBe(string.Empty);
        }
    }

    [Test]
    public void Resolve_does_not_scrub_nesting_markers_for_non_claude_kinds()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("raw", new AgentLaunchOptions());

        spec.Env.ContainsKey("CLAUDE_CODE_SESSION_ID").ShouldBeFalse();
    }

    [Test]
    public void Resolve_lets_config_override_a_nesting_marker()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        var spec = registry.Resolve("claude", new AgentLaunchOptions(
            ExtraEnv: new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "cli" }));

        spec.Env["CLAUDE_CODE_ENTRYPOINT"].ShouldBe("cli");
    }

    [Test]
    public void Resolve_rejects_non_positive_dimensions()
    {
        var registry = BuildRegistry(WithClaudeAndRaw());

        Should.Throw<ArgumentException>(() => registry.Resolve("claude", new AgentLaunchOptions(Cols: 0)));
        Should.Throw<ArgumentException>(() => registry.Resolve("claude", new AgentLaunchOptions(Rows: -1)));
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
