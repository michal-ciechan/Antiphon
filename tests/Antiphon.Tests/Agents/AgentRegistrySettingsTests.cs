using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentRegistrySettingsTests
{
    [Test]
    public void Binds_definitions_kind_exe_and_args_from_configuration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Agents:DefaultDefinition"] = "claude",
            ["Agents:Definitions:claude:Kind"] = "ClaudeCode",
            ["Agents:Definitions:claude:Exe"] = "cl.bat",
            ["Agents:Definitions:claude:ArgsTemplate:0"] = "--print",
            ["Agents:Definitions:claude:Env:FOO"] = "bar",
            ["Agents:Definitions:claude:Env:SERVICE_TOKEN"] = "synthetic-secret",
            ["Agents:Definitions:claude:NonSecretEnvironmentNames:0"] = "FOO",
            ["Agents:Definitions:claude:SecretEnvironmentNames:0"] = "SERVICE_TOKEN",
            ["Agents:Definitions:raw:Kind"] = "Raw",
            ["Agents:Definitions:raw:Exe"] = "pwsh.exe",
            ["Agents:Definitions:codex:Kind"] = "Codex",
            ["Agents:Definitions:codex:Exe"] = "codex.cmd",
            ["Agents:Definitions:codex:ArgsTemplate:0"] = "--no-alt-screen",
            ["Agents:Definitions:codex:ArgsTemplate:1"] = "--dangerously-bypass-approvals-and-sandbox",
            ["Agents:ClaudeReadyQuietPeriodMs"] = "1234",
            ["Agents:CodexReadyQuietPeriodMs"] = "4321",
            ["Agents:CodexDoneQuietPeriodMs"] = "3456",
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var settings = config.GetSection("Agents").Get<AgentRegistrySettings>()!;

        settings.DefaultDefinition.ShouldBe("claude");
        settings.Definitions.Count.ShouldBe(3);
        settings.Definitions["claude"].Kind.ShouldBe("ClaudeCode");
        settings.Definitions["claude"].Exe.ShouldBe("cl.bat");
        settings.Definitions["claude"].ArgsTemplate.ShouldBe(new[] { "--print" });
        settings.Definitions["claude"].Env["FOO"].ShouldBe("bar");
        settings.Definitions["claude"].NonSecretEnvironmentNames.ShouldBe(["FOO"]);
        settings.Definitions["claude"].SecretEnvironmentNames.ShouldBe(["SERVICE_TOKEN"]);
        settings.Definitions["raw"].Kind.ShouldBe("Raw");
        settings.Definitions["codex"].Kind.ShouldBe("Codex");
        settings.Definitions["codex"].Exe.ShouldBe("codex.cmd");
        settings.Definitions["codex"].ArgsTemplate.ShouldBe(
            new[] { "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox" });
        settings.ClaudeReadyQuietPeriodMs.ShouldBe(1234);
        settings.CodexReadyQuietPeriodMs.ShouldBe(4321);
        settings.CodexDoneQuietPeriodMs.ShouldBe(3456);
    }

    /// <summary>
    /// CARD-0099 S3 — the SHIPPED definition, read off the real appsettings.json rather than an
    /// in-memory sample. It named a <c>cx.ps1</c> wrapper that has never existed on this machine, so
    /// an interactive Codex launch died on CommandNotFound and every headed-Codex test skipped in
    /// silence for months (found by the CARD-0099 plan, confirmed by S2). The in-memory binding tests
    /// above cannot catch that: they assert the binder, not the file.
    /// </summary>
    [Test]
    public void The_shipped_codex_definition_no_longer_names_the_phantom_wrapper()
    {
        var codex = ShippedCodexDefinition(Path.Combine(ShippedSettingsRoot(), "server", "appsettings.json"));

        codex.Kind.ShouldBe("Codex");
        codex.Exe.ShouldBe("codex.cmd", "the npm shim, resolved off PATH by AgentExecutableResolver");
        codex.ArgsTemplate.ShouldContain("--no-alt-screen");
        codex.ArgsTemplate.ShouldContain("--dangerously-bypass-approvals-and-sandbox");
        // The whole point of the slice: nothing anywhere in the definition still reaches for the
        // wrapper, including through a pwsh -Command trampoline.
        codex.Exe.ShouldNotContain("cx.ps1");
        foreach (var arg in codex.ArgsTemplate)
            arg.ShouldNotContain("cx.ps1");
    }

    [Test]
    public void The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine()
    {
        // The assertion the binding tests structurally cannot make, and the one that was false for
        // months. Skipped rather than failed where Codex simply is not installed — the definition is
        // still correct there; it is "names something nobody has" that this is guarding against.
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("The npm shim layout is Windows-specific");

        var codex = ShippedCodexDefinition(Path.Combine(ShippedSettingsRoot(), "server", "appsettings.json"));
        var resolved = AgentExecutableResolver.Default.TryResolve(codex.Exe);
        if (resolved is null)
            throw new TUnit.Core.Exceptions.SkipTestException(
                $"'{codex.Exe}' is not on PATH; install codex-cli (npm i -g @openai/codex) to exercise this");

        File.Exists(resolved).ShouldBeTrue();
    }

    [Test]
    public void The_shipped_example_settings_agree_with_the_shipped_server_settings()
    {
        // appsettings.json.example is what a new checkout copies; the two drifting is how a machine
        // gets configured with the definition that was just proven broken.
        var root = ShippedSettingsRoot();
        var server = ShippedCodexDefinition(Path.Combine(root, "server", "appsettings.json"));
        var example = ShippedCodexDefinition(Path.Combine(root, "appsettings.json.example"));

        example.Kind.ShouldBe(server.Kind);
        example.Exe.ShouldBe(server.Exe);
        example.ArgsTemplate.ShouldBe(server.ArgsTemplate);
    }

    private static AgentDefinition ShippedCodexDefinition(string settingsPath) =>
        new ConfigurationBuilder().AddJsonFile(settingsPath).Build()
            .GetSection("Agents").Get<AgentRegistrySettings>()!.Definitions["codex"];

    private static string ShippedSettingsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }

    [Test]
    public void Validator_succeeds_for_well_formed_settings()
    {
        var settings = new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions =
            {
                ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" },
                ["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex.cmd" },
            }
        };

        var result = new AgentRegistrySettingsValidator().Validate(name: null, settings);

        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Validator_accepts_explicit_and_heuristic_environment_classification()
    {
        var settings = ValidSettings(new AgentDefinition
        {
            Kind = "ClaudeCode",
            Exe = "cl.bat",
            Env = new Dictionary<string, string>
            {
                ["NORMAL_SETTING"] = "ordinary",
                ["MISLEADING_TOKEN"] = "explicitly-ordinary",
                ["SERVICE_TOKEN"] = "heuristic-secret",
                ["NON_OBVIOUS_CREDENTIAL"] = "explicit-secret"
            },
            NonSecretEnvironmentNames = ["NORMAL_SETTING", "MISLEADING_TOKEN"],
            SecretEnvironmentNames = ["NON_OBVIOUS_CREDENTIAL"]
        });

        var result = new AgentRegistrySettingsValidator().Validate(name: null, settings);

        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Validator_rejects_unclassified_environment_without_exposing_value()
    {
        const string canary = "synthetic-unclassified-value-canary";
        var settings = ValidSettings(new AgentDefinition
        {
            Kind = "ClaudeCode",
            Exe = "cl.bat",
            Env = new Dictionary<string, string> { ["NORMAL_SETTING"] = canary }
        });

        var result = new AgentRegistrySettingsValidator().Validate(name: null, settings);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("NORMAL_SETTING");
        result.FailureMessage.ShouldNotContain(canary);
    }

    [Test]
    public void Validator_rejects_missing_overlap_and_host_equivalent_classifications()
    {
        var definition = new AgentDefinition
        {
            Kind = "ClaudeCode",
            Exe = "cl.bat",
            Env = new Dictionary<string, string>
            {
                ["NORMAL_SETTING"] = "ordinary",
                ["SERVICE_TOKEN"] = "secret"
            },
            NonSecretEnvironmentNames = ["NORMAL_SETTING", "SERVICE_TOKEN", "ABSENT_NAME"],
            SecretEnvironmentNames = ["SERVICE_TOKEN"]
        };
        if (OperatingSystem.IsWindows())
            definition.NonSecretEnvironmentNames.Add("normal_setting");
        else
            definition.NonSecretEnvironmentNames.Add("NORMAL_SETTING");

        var result = new AgentRegistrySettingsValidator().Validate(
            name: null,
            ValidSettings(definition));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ABSENT_NAME");
        result.FailureMessage.ShouldContain("both secret and ordinary");
        result.FailureMessage.ShouldContain("duplicate");
    }

    [Test]
    public void Validator_rejects_host_equivalent_environment_dictionary_keys()
    {
        var definition = new AgentDefinition
        {
            Kind = "ClaudeCode",
            Exe = "cl.bat",
            Env = new Dictionary<string, string>
            {
                ["DUPLICATE_NAME"] = "first",
                [OperatingSystem.IsWindows() ? "duplicate_name" : "DUPLICATE_NAME "] = "second"
            },
            NonSecretEnvironmentNames = OperatingSystem.IsWindows()
                ? ["DUPLICATE_NAME"]
                : ["DUPLICATE_NAME", "DUPLICATE_NAME "]
        };

        var result = new AgentRegistrySettingsValidator().Validate(
            name: null,
            ValidSettings(definition));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("environment name");
    }

    [Test]
    public void Validator_rejects_unbounded_import_fields()
    {
        var result = new AgentRegistrySettingsValidator().Validate(
            name: null,
            ValidSettings(new AgentDefinition
            {
                Kind = "ClaudeCode",
                Exe = new string('x', 2001),
                ArgsTemplate = [new string('a', 2001)],
                Env = new Dictionary<string, string>
                {
                    ["NORMAL_SETTING"] = new string('v', 4001)
                },
                NonSecretEnvironmentNames = ["NORMAL_SETTING"]
            }));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Exe");
        result.FailureMessage.ShouldContain("ArgsTemplate");
        result.FailureMessage.ShouldContain("NORMAL_SETTING");
    }

    [Test]
    [Arguments(200, true)]
    [Arguments(201, false)]
    public void Validator_enforces_bounded_definition_names(int length, bool expectedSuccess)
    {
        var definitionName = new string('n', length);
        var settings = new AgentRegistrySettings
        {
            DefaultDefinition = definitionName,
            Definitions =
            {
                [definitionName] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = "cl.bat"
                }
            }
        };

        var result = new AgentRegistrySettingsValidator().Validate(name: null, settings);

        result.Succeeded.ShouldBe(expectedSuccess);
        if (!expectedSuccess)
        {
            var failureMessage = result.FailureMessage!;
            failureMessage.ShouldContain("200");
            failureMessage.ShouldNotContain(definitionName);
        }
    }

    [Test]
    [Arguments(AgentTuiPlatform.Windows, true)]
    [Arguments(AgentTuiPlatform.Linux, false)]
    [Arguments(AgentTuiPlatform.MacOS, false)]
    public void Environment_name_comparer_matches_host_platform(
        AgentTuiPlatform platform,
        bool expectedEqual)
    {
        AgentEnvironmentVariableNames.ForPlatform(platform)
            .Equals("SERVICE_TOKEN", "service_token")
            .ShouldBe(expectedEqual);
    }

    [Test]
    public void Validator_rejects_empty_definitions()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "",
            Definitions = new Dictionary<string, AgentDefinition>()
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("at least one entry");
    }

    [Test]
    public void Validator_rejects_definition_with_empty_exe()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Exe must not be empty");
    }

    [Test]
    public void Validator_rejects_unknown_kind()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "weird",
            Definitions = { ["weird"] = new AgentDefinition { Kind = "Wat", Exe = "x" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("not a known AgentKind");
    }

    [Test]
    public void Validator_rejects_default_definition_not_in_dictionary()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "missing",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DefaultDefinition 'missing'");
    }

    [Test]
    public void Validator_rejects_non_positive_timing_settings()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } },
            ClaudeReadyQuietPeriodMs = 0,
            ClaudeDoneMaxWaitMs = -5,
            CodexReadyQuietPeriodMs = 0,
            CodexDoneQuietPeriodMs = -1,
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ClaudeReadyQuietPeriodMs must be positive");
        result.FailureMessage.ShouldContain("ClaudeDoneMaxWaitMs must be positive");
        result.FailureMessage.ShouldContain("CodexReadyQuietPeriodMs must be positive");
        result.FailureMessage.ShouldContain("CodexDoneQuietPeriodMs must be positive");
    }

    private static AgentRegistrySettings ValidSettings(AgentDefinition definition) => new()
    {
        DefaultDefinition = "claude",
        Definitions = { ["claude"] = definition }
    };
}
